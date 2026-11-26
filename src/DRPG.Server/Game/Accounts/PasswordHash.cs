namespace DRPG.Server.Game.Accounts;

/// <summary>
/// Hash of account passwords (<see cref="Pbkdf2Hash"/> format) for the launcher login. Passwords are chosen by people,
/// so the iteration count follows the OWASP recommendation for PBKDF2-SHA256.
/// </summary>
public static class PasswordHash
{
    public const int Iterations = 600_000;

    private static readonly string Dummy = Hash("dummy-password");

    public static string Hash(string password) => Pbkdf2Hash.Hash(password, Iterations);

    public static bool Verify(string password, string stored) => Pbkdf2Hash.Verify(password, stored);

    /// <summary>True when the stored hash uses fewer iterations than today: rehash after a successful login.</summary>
    public static bool NeedsRehash(string stored) => Pbkdf2Hash.IterationsOf(stored) < Iterations;

    /// <summary>Spends the time of a <see cref="Verify"/>, so an unknown login costs the same as a wrong password.</summary>
    public static void SpendVerifyTime(string password) => Verify(password, Dummy);

    public static bool PlainEquals(string password, string stored) => Pbkdf2Hash.PlainEquals(password, stored);
}
