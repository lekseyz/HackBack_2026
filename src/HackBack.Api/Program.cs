using HackBack.Api;
using HackBack.Domain;
using HackBack.Infrastructure;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Scalar.AspNetCore;
using System.Text;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi(options => options.AddDocumentTransformer((document, context, cancellationToken) =>
{
    document.Info.Title = "HackBack 2026 API";
    document.Info.Version = "v1";
    return Task.CompletedTask;
}));

builder.Services.AddCors(options => options.AddDefaultPolicy(policy =>
    policy.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader()));

builder.Services.AddSqlite<AppDbContext>(builder.Configuration.GetConnectionString("Sqlite"));
builder.Services.AddScoped<UserRepository>();
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
    });

builder.Services.AddAuthorization();

var app = builder.Build();

app.UseCors();
app.UseAuthentication();
app.UseAuthorization();

app.MapOpenApi();
app.MapScalarApiReference();

app.MapAuthEndpoints();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await db.Database.MigrateAsync();

    if (app.Environment.IsDevelopment())
    {
        var users = scope.ServiceProvider.GetRequiredService<UserRepository>();
        var login = app.Configuration["SeedUser:Login"];
        if (!string.IsNullOrEmpty(login) && await users.GetByLoginAsync(login) is null)
        {
            var (hash, salt) = PasswordHasher.Hash(app.Configuration["SeedUser:Password"]!);
            await users.CreateAsync(new User
            {
                Id = Guid.NewGuid(),
                Email = app.Configuration["SeedUser:Email"]!,
                Login = login,
                PasswordHash = hash,
                PasswordSalt = salt,
            });
        }
    }
}

app.Run();
