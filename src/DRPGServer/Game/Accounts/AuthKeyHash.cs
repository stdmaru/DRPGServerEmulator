using System.Security.Cryptography;
using System.Text;

namespace DRPGServer.Game.Accounts;

/// <summary>
/// Hash da <c>auth_key</c> no formato <c>pbkdf2-sha256$&lt;iterações&gt;$&lt;sal base64&gt;$&lt;hash base64&gt;</c>
/// (sal de 16 bytes, hash de 32). Quem emite a chave (launcher, site) pode gravar este formato direto em
/// <c>account.auth_key_hash</c>; ver docs/emulador/seguranca.md.
/// </summary>
public static class AuthKeyHash
{
    public const string Scheme = "pbkdf2-sha256";

    /// <summary>
    /// A chave é um token aleatório emitido a cada login, não uma senha escolhida: 100 000 iterações bastam e custam
    /// poucos ms, o que importa porque cada tentativa de login paga esse custo.
    /// </summary>
    public const int Iterations = 100_000;

    private const int SaltBytes = 16;
    private const int HashBytes = 32;

    /// <summary>Hash de referência para usuário inexistente: a recusa custa o mesmo tempo que a de senha errada.</summary>
    private static readonly string Dummy = Hash("dummy-auth-key");

    public static string Hash(string key)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltBytes);
        var hash = Derive(key, salt, Iterations);
        return $"{Scheme}${Iterations}${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}";
    }

    /// <summary>Confere a chave em tempo constante. Formato inválido é recusa, não exceção.</summary>
    public static bool Verify(string key, string stored)
    {
        var parts = stored.Split('$');
        if (parts.Length != 4 || parts[0] != Scheme) return false;
        if (!int.TryParse(parts[1], out var iterations) || iterations is < 1 or > 10_000_000) return false;
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
        return CryptographicOperations.FixedTimeEquals(Derive(key, salt, iterations), expected);
    }

    /// <summary>Gasta o mesmo tempo de um <see cref="Verify"/>, para a recusa não revelar por que recusou.</summary>
    public static void SpendVerifyTime(string key) => Verify(key, Dummy);

    /// <summary>Comparação em tempo constante de chave em texto puro (contas ainda não convertidas).</summary>
    public static bool PlainEquals(string key, string stored) =>
        CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(key), Encoding.UTF8.GetBytes(stored));

    private static byte[] Derive(string key, byte[] salt, int iterations) =>
        Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(key), salt, iterations, HashAlgorithmName.SHA256, HashBytes);
}
