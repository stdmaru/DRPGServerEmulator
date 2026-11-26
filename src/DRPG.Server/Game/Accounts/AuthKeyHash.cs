namespace DRPG.Server.Game.Accounts;

/// <summary>
/// Hash of the <c>auth_key</c> (<see cref="Pbkdf2Hash"/> format). Whoever issues the key (launcher login, web site)
/// may write this format straight into <c>account.auth_key_hash</c>; see docs/emulador/seguranca.md.
/// </summary>
public static class AuthKeyHash
{
    public const string Scheme = Pbkdf2Hash.Scheme;

    /// <summary>
    /// The key is a random token issued at each login, not a chosen password: 100 000 iterations are enough and cost a
    /// few ms, which matters because every login attempt pays that cost.
    /// </summary>
    public const int Iterations = 100_000;

    /// <summary>Reference hash for unknown users: refusing one costs the same time as a wrong key.</summary>
    private static readonly string Dummy = Hash("dummy-auth-key");

    public static string Hash(string key) => Pbkdf2Hash.Hash(key, Iterations);

    /// <summary>Checks the key in constant time. A malformed stored value is a refusal, not an exception.</summary>
    public static bool Verify(string key, string stored) => Pbkdf2Hash.Verify(key, stored);

    /// <summary>Spends the same time as a <see cref="Verify"/>, so a refusal does not tell why it refused.</summary>
    public static void SpendVerifyTime(string key) => Verify(key, Dummy);

    /// <summary>Constant-time comparison of a plain-text key (accounts not converted yet).</summary>
    public static bool PlainEquals(string key, string stored) => Pbkdf2Hash.PlainEquals(key, stored);
}
