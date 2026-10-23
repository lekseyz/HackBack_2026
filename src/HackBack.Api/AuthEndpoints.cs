using System.Security.Claims;
using HackBack.Domain;
using HackBack.Infrastructure;

namespace HackBack.Api;

public static class AuthEndpoints
{
    internal const string CookieName = "hackback_refresh";

    internal static CookieOptions CookieOptions() => new()
    {
        HttpOnly = true,
        SameSite = SameSiteMode.Strict,
        IsEssential = true,
        Path = "/api/v1",
    };

    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/auth").WithTags("Auth");

        // POST /api/v1/auth/login — вход по username и паролю, при успехе выдаёт JWT, срок действия, профиль и refresh-токен в HttpOnly-куке.
        group.MapPost("/login", async (LoginRequest request, HttpContext http, UserRepository users, RefreshTokenRepository refreshTokens, JwtService jwt, IConfiguration configuration) =>
        {
            var user = await users.GetByUsernameAsync(request.Username);

            if (user is null || !PasswordHasher.Verify(request.Password, user.PasswordHash, user.PasswordSalt))
                return Results.Unauthorized();

            var (token, expiresAt) = jwt.CreateToken(user);
            var refresh = await refreshTokens.CreateAsync(user.Id, DateTime.UtcNow.AddDays(configuration.GetValue("Refresh:ExpiresDays", 30)));
            http.Response.Cookies.Append(CookieName, refresh, CookieOptions());

            return Results.Ok(new AuthResponse(token, expiresAt, new UserDto(user.Id, user.Username)));
        })
        .WithSummary("Вход по логину и паролю")
        .WithDescription("Принимает username и password. При успехе возвращает JWT, expiresAt и данные пользователя, ставит refresh-куку hackback_refresh. Неверные данные — 401.")
        .Produces<AuthResponse>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status401Unauthorized);

        // GET /api/v1/auth/me — профиль текущего пользователя из JWT (заголовок "Authorization: Bearer <token>").
        group.MapGet("/me", (ClaimsPrincipal principal) =>
        {
            var id = principal.FindFirstValue(ClaimTypes.NameIdentifier);
            var username = principal.FindFirstValue(ClaimTypes.Name) ?? principal.Identity?.Name;

            return !Guid.TryParse(id, out var userId) || username is null
                ? Results.Unauthorized()
                : Results.Ok(new UserDto(userId, username));
        })
        .RequireAuthorization()
        .WithSummary("Получение текущего пользователя")
        .WithDescription("Возвращает профиль пользователя из JWT. 401 — нет или просрочен токен.")
        .Produces<UserDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status401Unauthorized);

        // POST /api/v1/auth/logout — отзывает refresh-токен из куки, удаляет куку и завершает сессию.
        group.MapPost("/logout", async (HttpContext http, RefreshTokenRepository refreshTokens) =>
        {
            if (http.Request.Cookies.TryGetValue(CookieName, out var refresh))
                await refreshTokens.RevokeAsync(refresh);

            http.Response.Cookies.Delete(CookieName, CookieOptions());
            return Results.Ok(new { success = true, message = "Сессия успешно завершена" });
        })
        .WithSummary("Завершение текущей сессии")
        .WithDescription("Отзывает refresh-токен из куки hackback_refresh и удаляет её. Всегда 200.")
        .Produces(StatusCodes.Status200OK);

        // POST /api/v1/refresh — обновление access-токена по refresh-куке: старый токен отзывается, выдаётся новый (ротация).
        group.MapPost("/", async (HttpContext http, RefreshTokenRepository refreshTokens, UserRepository users, JwtService jwt, IConfiguration configuration) =>
        {
            if (!http.Request.Cookies.TryGetValue(AuthEndpoints.CookieName, out var refresh))
                return Results.Unauthorized();

            var stored = await refreshTokens.GetValidAsync(refresh);
            if (stored is null)
                return Results.Unauthorized();

            var user = await users.GetByIdAsync(stored.UserId);
            if (user is null)
                return Results.Unauthorized();

            await refreshTokens.RevokeAsync(refresh);
            var newRefresh = await refreshTokens.CreateAsync(user.Id, DateTime.UtcNow.AddDays(configuration.GetValue("Refresh:ExpiresDays", 30)));
            http.Response.Cookies.Append(AuthEndpoints.CookieName, newRefresh, AuthEndpoints.CookieOptions());

            var (token, expiresAt) = jwt.CreateToken(user);
            return Results.Ok(new AuthResponse(token, expiresAt, new UserDto(user.Id, user.Username)));
        })
        .WithSummary("Обновление access-токена")
        .WithDescription("Читает refresh-токен из HttpOnly куки hackback_refresh, отзывает его и выдаёт новый (ротация). В ответе новый JWT и новая кука. Нет куки, токен отозван или просрочен — 401.")
        .Produces<AuthResponse>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status401Unauthorized);

        return app;
    }
}

public record LoginRequest(string Username, string Password);

public record UserDto(Guid Id, string Username);

public record AuthResponse(string Token, DateTime ExpiresAt, UserDto User);
