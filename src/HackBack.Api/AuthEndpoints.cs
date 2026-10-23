using System.Security.Claims;
using HackBack.Domain;
using HackBack.Infrastructure;

namespace HackBack.Api;

public static class AuthEndpoints
{
    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/auth").WithTags("Auth");

        // POST /api/v1/auth/login — вход по логину и паролю (логином считается также email).
        group.MapPost("/login", async (LoginRequest request, UserRepository users, JwtService jwt) =>
        {
            var user = await users.GetByLoginAsync(request.Login);

            if (user is null || !PasswordHasher.Verify(request.Password, user.PasswordHash, user.PasswordSalt))
                return Results.Unauthorized();

            return Results.Ok(new AuthResponse(jwt.CreateToken(user), new UserDto(user.Id, user.Email, user.Login)));
        })
        .WithSummary("Вход по логину и паролю")
        .WithDescription("Принимает логин (или email) и пароль. При успехе возвращает JWT и данные пользователя. Неверные данные — 401.")
        .Produces<AuthResponse>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status401Unauthorized);

        // GET /api/v1/auth/me — получение текущего пользователя по JWT из заголовка "Authorization: Bearer <token>".
        group.MapGet("/me", async (ClaimsPrincipal principal, UserRepository users) =>
        {
            if (!Guid.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
                return Results.Unauthorized();

            var user = await users.GetByIdAsync(userId);
            return user is null
                ? Results.Unauthorized()
                : Results.Ok(new UserDto(user.Id, user.Email, user.Login));
        })
        .RequireAuthorization()
        .WithSummary("Получение текущего пользователя")
        .WithDescription("Возвращает пользователя из JWT. 401 — нет/просрочен токен или пользователь удалён.")
        .Produces<UserDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status401Unauthorized);

        // POST /api/v1/auth/logout — завершение текущей сессии (JWT stateless: клиент просто удаляет токен).
        group.MapPost("/logout", () => Results.Ok(new { message = "Logged out" }))
        .WithSummary("Завершение текущей сессии")
        .WithDescription("Сервер ничего не хранит (JWT stateless), клиенту достаточно удалить токен. Всегда 200.")
        .Produces(StatusCodes.Status200OK);

        return app;
    }
}

public record LoginRequest(string Login, string Password);

public record UserDto(Guid Id, string Email, string Login);

public record AuthResponse(string Token, UserDto User);
