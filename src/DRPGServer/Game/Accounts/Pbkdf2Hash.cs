using System.Security.Cryptography;
using System.Text;

namespace DRPGServer.Game.Accounts;

/// <summary>
/// Stored secret hash in the format <c>pbkdf2-sha256$&lt;iterations&gt;$&lt;base64 salt&gt;$&lt;base64 hash&gt;</c>
/// (16-byte salt, 32-byte hash). Shared by <see cref="AuthKeyHash"/> and <see cref="PasswordHash"/>; see
/// docs/emulador/seguranca.md.
/// </summary>
internal static class Pbkdf2Hash
{
    public const string Scheme = "pbkdf2-sha256";

    private const int SaltBytes = 16;
    private const int HashBytes = 32;
    private const int MaxIterations = 10_000_000;

    public static string Hash(string secret, int iterations)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltBytes);
        var hash = Derive(secret, salt, iterations);
        return $"{Scheme}${iterations}${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}";
    }

    /// <summary>Constant-time check. A malformed stored value is a refusal, not an exception.</summary>
    public static bool Verify(string secret, string stored)
    {
        var parts = stored.Split('$');
        if (parts.Length != 4 || parts[0] != Scheme) return false;
        if (!int.TryParse(parts[1], out var iterations) || iterations is < 1 or > MaxIterations) return false;
        byte[] salt, expected;
        try
        {
            salt = Convert.FromBase64String(parts[2]);
            expected = Convert.FromBase64String(parts[3]);
        }
        catch (FormatException)
        {
            return false;
        }
        if (salt.Length == 0 || expected.Length != HashBytes) return false;
        return CryptographicOperations.FixedTimeEquals(Derive(secret, salt, iterations), expected);
    }

    /// <summary>Iteration count of a stored hash, or 0 when malformed (used to rehash old hashes).</summary>
    public static int IterationsOf(string stored)
    {
        var parts = stored.Split('$');
        return parts.Length == 4 && parts[0] == Scheme && int.TryParse(parts[1], out var n) ? n : 0;
    }

    public static bool PlainEquals(string secret, string stored) =>
        CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes(stored));

    private static byte[] Derive(string secret, byte[] salt, int iterations) =>
        Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(secret), salt, iterations, HashAlgorithmName.SHA256, HashBytes);
}
