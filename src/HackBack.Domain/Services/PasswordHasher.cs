using System.Security.Cryptography;

namespace HackBack.Domain;

public static class PasswordHasher
{
    private const int SaltSize = 16;
    private const int HashSize = 32;
    private const int Iterations = 100_000;

    public static (string Hash, string Salt) Hash(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, HashSize);
        return (Convert.ToHexString(hash), Convert.ToHexString(salt));
    }

    public static bool Verify(string password, string hash, string salt)
    {
        var expected = Convert.FromHexString(hash);
        var actual = Rfc2898DeriveBytes.Pbkdf2(password, Convert.FromHexString(salt), Iterations, HashAlgorithmName.SHA256, expected.Length);
        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }
}
