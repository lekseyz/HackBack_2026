using System.Security.Cryptography;
using System.Text;
using HackBack.Domain;
using Microsoft.EntityFrameworkCore;

namespace HackBack.Infrastructure;

public class RefreshTokenRepository(AppDbContext db)
{
    public async Task<string> CreateAsync(Guid userId, DateTime expiresAt)
    {
        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(64));
        db.RefreshTokens.Add(new RefreshToken
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            TokenHash = Hash(token),
            CreatedAt = DateTime.UtcNow,
            ExpiresAt = expiresAt,
        });
        await db.SaveChangesAsync();
        return token;
    }

    public Task<RefreshToken?> GetValidAsync(string token) =>
        db.RefreshTokens.FirstOrDefaultAsync(r =>
            r.TokenHash == Hash(token) && r.RevokedAt == null && r.ExpiresAt > DateTime.UtcNow);

    public async Task RevokeAsync(string token)
    {
        var stored = await db.RefreshTokens.FirstOrDefaultAsync(r => r.TokenHash == Hash(token) && r.RevokedAt == null);
        if (stored is null)
            return;

        stored.RevokedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
    }

    private static string Hash(string token) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
}
