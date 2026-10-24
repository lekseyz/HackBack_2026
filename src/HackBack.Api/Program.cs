using HackBack.Api;
using HackBack.Domain;
using HackBack.Infrastructure;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Primitives;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;
using Scalar.AspNetCore;
using System.Text;
using System.Text.Json;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi(options => options.AddDocumentTransformer((document, context, cancellationToken) =>
{
    document.Info.Title = "HackBack 2026 API";
    document.Info.Version = "v1";

    document.Components ??= new OpenApiComponents();
    var components = document.Components;
    components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
    components.SecuritySchemes["bearerAuth"] = new OpenApiSecurityScheme
    {
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        Description = "JWT из POST /api/v1/auth/login",
    };

    foreach (var (pathKey, pathItem) in document.Paths)
    {
        if (pathKey is "/api/v1/auth/login" or "/api/v1/auth/logout" or "/api/v1/refresh" or "/api/v1/docs" or "/metrics")
            continue;

        if (pathItem.Operations is null)
            continue;

        foreach (var operation in pathItem.Operations.Values)
        {
            operation.Security ??= [];
            operation.Security.Add(new OpenApiSecurityRequirement
            {
                [new OpenApiSecuritySchemeReference("bearerAuth", document)] = new List<string>(),
            });
        }
    }

    return Task.CompletedTask;
}));

builder.Services.AddCors(options => options.AddDefaultPolicy(policy =>
    policy.SetIsOriginAllowed(_ => true).AllowAnyMethod().AllowAnyHeader().AllowCredentials()));

builder.Services.AddSqlite<AppDbContext>(builder.Configuration.GetConnectionString("Sqlite"));
builder.Services.AddScoped<UserRepository>();
builder.Services.AddScoped<RefreshTokenRepository>();
builder.Services.AddScoped<AccountRepository>();
builder.Services.AddScoped<ModelSettingsRepository>();
builder.Services.AddSingleton<JwtService>();

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        var jwt = builder.Configuration.GetSection("Jwt");
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwt["Issuer"],
            ValidateAudience = true,
            ValidAudience = jwt["Audience"],
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt["Key"] ?? string.Empty)),
        };
        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = context =>
            {
                var accessToken = context.Request.Query["access_token"];
                var path = context.HttpContext.Request.Path;
                if (!StringValues.IsNullOrEmpty(accessToken) && path.StartsWithSegments("/hubs/analytics"))
                    context.Token = accessToken;
                return Task.CompletedTask;
            }
        };
    });

builder.Services.AddAuthorization();

builder.Services.AddSignalR().AddJsonProtocol(o =>
    o.PayloadSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase);
builder.Services.AddSingleton<MetricsRecalculationService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<MetricsRecalculationService>());

var app = builder.Build();

app.UseCors();
app.UseAuthentication();
app.UseAuthorization();

app.MapOpenApi();
app.MapScalarApiReference("/api/v1/docs");

app.MapAuthEndpoints();
app.MapSnapshotEndpoints();
app.MapAccountEndpoints();
app.MapServiceEndpoints();
app.MapModelEndpoints();
app.MapAnalyticsEndpoints();
app.MapMetricsEndpoints();

// /hubs/analytics - SignalR-хаб живых обновлений аналитики (только server-to-client push).
app.MapHub<AnalyticsHub>("/hubs/analytics").RequireAuthorization();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await db.Database.MigrateAsync();

    if (app.Environment.IsDevelopment())
    {
        var users = scope.ServiceProvider.GetRequiredService<UserRepository>();
        var username = app.Configuration["SeedUser:Username"];
        if (!string.IsNullOrEmpty(username) && await users.GetByUsernameAsync(username) is null)
        {
            var (hash, salt) = PasswordHasher.Hash(app.Configuration["SeedUser:Password"]!);
            await users.CreateAsync(new User
            {
                Id = Guid.NewGuid(),
                Username = username,
                PasswordHash = hash,
                PasswordSalt = salt,
            });
        }
    }
}

app.Run();
