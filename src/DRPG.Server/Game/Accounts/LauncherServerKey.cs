using System.Security.Cryptography;
using DRPG.Shared.Protocol.Launcher;
using DRPG.Server.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DRPG.Server.Game.Accounts;

/// <summary>
/// The LoginServer ECDH P-256 key for the launcher login (docs/protocolo/launcher.md). Loaded from
/// <see cref="LauncherLoginOptions.KeyPath"/>, or created there on first start (owner-only permissions on Unix).
/// The fingerprint is logged so it can be published in <c>DR_Configuration.json</c>.
/// </summary>
public sealed class LauncherServerKey : IDisposable
{
    public LauncherServerKey(IOptions<LauncherLoginOptions> options, ILogger<LauncherServerKey> log)
    {
        var path = Path.IsPathRooted(options.Value.KeyPath)
            ? options.Value.KeyPath
            : Path.Combine(AppContext.BaseDirectory, options.Value.KeyPath);
        Key = LauncherCrypto.CreateKey();
        if (File.Exists(path))
        {
            Key.ImportFromPem(File.ReadAllText(path));
            if (Key.KeySize != 256) throw new CryptographicException($"{path}: not a P-256 key");
        }
        else
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, Key.ExportPkcs8PrivateKeyPem());
            if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            log.LogWarning("Launcher login key created at {Path}", path);
        }
        PublicKey = Key.PublicKey.ExportSubjectPublicKeyInfo();
        Fingerprint = LauncherCrypto.Fingerprint(PublicKey);
        log.LogInformation("Launcher login key fingerprint (DR_Configuration.json LoginServer.KeyFingerprint): {Fingerprint}", Fingerprint);
    }

    public ECDiffieHellman Key { get; }

    /// <summary>SubjectPublicKeyInfo DER sent in the hello reply.</summary>
    public byte[] PublicKey { get; }

    public string Fingerprint { get; }

    public void Dispose() => Key.Dispose();
}
