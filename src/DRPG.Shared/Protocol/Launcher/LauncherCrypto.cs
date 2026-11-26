using System.Security.Cryptography;
using System.Text;

namespace DRPG.Shared.Protocol.Launcher;

/// <summary>
/// Encryption of the launcher login (docs/protocolo/launcher.md).
/// <list type="bullet">
/// <item>The LoginServer has a static ECDH P-256 key pair. Its public key (SubjectPublicKeyInfo DER) travels in the
/// hello reply and is checked by the launcher against the SHA-256 fingerprint published in
/// <c>DR_Configuration.json</c>, which is downloaded over HTTPS. That check is what stops a man in the middle.</item>
/// <item>Request = sealed box: the launcher makes an ephemeral P-256 key, derives a key with
/// HKDF-SHA256(ECDH secret, salt = server nonce, info = <see cref="RequestInfo"/>) and encrypts with AES-256-GCM.</item>
/// <item>Reply = AES-256-GCM with the random response key the launcher put inside the request.</item>
/// </list>
/// P-256 instead of X25519: the .NET base library has no X25519, and P-256 works the same on Windows (CNG) and
/// Linux (OpenSSL) with NativeAOT.
/// </summary>
public static class LauncherCrypto
{
    public const int IvBytes = 12;
    public const int TagBytes = 16;
    public const int KeyBytes = 32;

    private static readonly byte[] RequestInfo = "drpg-launcher-login-request-v1"u8.ToArray();
    private static readonly byte[] RequestAad = "drpg-launcher-login-request"u8.ToArray();
    private static readonly byte[] ReplyAad = "drpg-launcher-login-reply"u8.ToArray();

    public static ECDiffieHellman CreateKey() => ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);

    /// <summary>Lowercase hex SHA-256 of the SubjectPublicKeyInfo: the value published in <c>DR_Configuration.json</c>.</summary>
    public static string Fingerprint(ReadOnlySpan<byte> publicKey) => Convert.ToHexStringLower(SHA256.HashData(publicKey));

    /// <summary>Constant-time comparison of a received key with the published fingerprint (case-insensitive hex).</summary>
    public static bool MatchesFingerprint(ReadOnlySpan<byte> publicKey, string fingerprint)
    {
        var expected = Encoding.ASCII.GetBytes(fingerprint.Trim().ToLowerInvariant());
        var actual = Encoding.ASCII.GetBytes(Fingerprint(publicKey));
        return CryptographicOperations.FixedTimeEquals(expected, actual);
    }

    /// <summary>Launcher side: seals <paramref name="plaintext"/> for the server key.</summary>
    public static LauncherLogin Seal(ReadOnlySpan<byte> serverPublicKey, ReadOnlySpan<byte> serverNonce, ReadOnlySpan<byte> plaintext)
    {
        using var server = ImportPublicKey(serverPublicKey);
        using var ephemeral = CreateKey();
        var key = DeriveRequestKey(ephemeral, server.PublicKey, serverNonce);
        var iv = RandomNumberGenerator.GetBytes(IvBytes);
        return new LauncherLogin(ephemeral.PublicKey.ExportSubjectPublicKeyInfo(), iv, Encrypt(key, iv, plaintext, RequestAad));
    }

    /// <summary>Server side: opens a request sealed for <paramref name="serverKey"/>. Throws <see cref="CryptographicException"/> on any failure.</summary>
    public static byte[] Open(ECDiffieHellman serverKey, ReadOnlySpan<byte> serverNonce, LauncherLogin request)
    {
        using var ephemeral = ImportPublicKey(request.EphemeralKey);
        var key = DeriveRequestKey(serverKey, ephemeral.PublicKey, serverNonce);
        return Decrypt(key, request.Iv, request.Sealed, RequestAad);
    }

    public static LauncherLoginReply SealReply(ReadOnlySpan<byte> responseKey, ReadOnlySpan<byte> plaintext)
    {
        var iv = RandomNumberGenerator.GetBytes(IvBytes);
        return new LauncherLoginReply(iv, Encrypt(responseKey, iv, plaintext, ReplyAad));
    }

    public static byte[] OpenReply(ReadOnlySpan<byte> responseKey, LauncherLoginReply reply) =>
        Decrypt(responseKey, reply.Iv, reply.Sealed, ReplyAad);

    private static ECDiffieHellman ImportPublicKey(ReadOnlySpan<byte> spki)
    {
        var key = CreateKey();
        try
        {
            key.ImportSubjectPublicKeyInfo(spki, out int read);
            if (read != spki.Length) throw new CryptographicException("trailing bytes after the public key");
            if (key.KeySize != 256) throw new CryptographicException("public key is not P-256");
            return key;
        }
        catch
        {
            key.Dispose();
            throw;
        }
    }

    private static byte[] DeriveRequestKey(ECDiffieHellman own, ECDiffieHellmanPublicKey other, ReadOnlySpan<byte> nonce)
    {
        if (nonce.Length != LauncherLimits.NonceBytes) throw new CryptographicException("bad nonce length");
        var secret = own.DeriveRawSecretAgreement(other);
        try
        {
            return HKDF.DeriveKey(HashAlgorithmName.SHA256, secret, KeyBytes, nonce.ToArray(), RequestInfo);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(secret);
        }
    }

    private static byte[] Encrypt(ReadOnlySpan<byte> key, ReadOnlySpan<byte> iv, ReadOnlySpan<byte> plaintext, byte[] aad)
    {
        if (key.Length != KeyBytes) throw new CryptographicException("bad key length");
        var result = new byte[plaintext.Length + TagBytes];
        using var aes = new AesGcm(key, TagBytes);
        aes.Encrypt(iv, plaintext, result.AsSpan(0, plaintext.Length), result.AsSpan(plaintext.Length), aad);
        return result;
    }

    private static byte[] Decrypt(ReadOnlySpan<byte> key, ReadOnlySpan<byte> iv, ReadOnlySpan<byte> sealedBytes, byte[] aad)
    {
        if (key.Length != KeyBytes) throw new CryptographicException("bad key length");
        if (iv.Length != IvBytes || sealedBytes.Length < TagBytes) throw new CryptographicException("sealed body too short");
        var plaintext = new byte[sealedBytes.Length - TagBytes];
        using var aes = new AesGcm(key, TagBytes);
        aes.Decrypt(iv, sealedBytes[..plaintext.Length], sealedBytes[plaintext.Length..], plaintext, aad);
        return plaintext;
    }
}
