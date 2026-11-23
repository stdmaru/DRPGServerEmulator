using System.Security.Cryptography;

namespace DRPGServer.Network.Protocol;

/// <summary>
/// ✅ Cifra AES-128-ECB da sessão global (pacotes 0x7B e 0x7C da enciclopédia).
/// Confirmado em <c>DigimonRPG.bin</c> (0x00869CC0 / 0x00869810 / 0x00ACF629) e 31/31 conexões em <c>captures.db</c>:
/// <c>K = AES_C(hello XOR W)</c>, onde <c>hello</c> são os 16 bytes do pacote global <c>0x0E</c>.
/// </summary>
public static class GlobalSessionCipher
{
    public const int BlockSize = 16;

    /// <summary>✅ Constante W em <c>DigimonRPG.bin @ 0x010078A8</c>.</summary>
    private static readonly byte[] ConstantW =
    [
        0x47, 0x95, 0xF1, 0xC8, 0x01, 0x02, 0x18, 0x49,
        0x77, 0xD6, 0x26, 0xE1, 0x3E, 0x4B, 0xC6, 0xD2,
    ];

    /// <summary>✅ Constante C em <c>DigimonRPG.bin @ 0x010078B8</c>.</summary>
    private static readonly byte[] ConstantC =
    [
        0x1A, 0x05, 0x71, 0xCD, 0xC1, 0x92, 0x88, 0xF9,
        0x77, 0xFF, 0x2D, 0x11, 0x3D, 0x4B, 0xCA, 0xDD,
    ];

    /// <summary>Deriva a chave AES-128 da sessão a partir dos 16 bytes do <c>0x0E</c> global.</summary>
    public static byte[] DeriveKey(ReadOnlySpan<byte> helloSeed)
    {
        if (helloSeed.Length != BlockSize)
            throw new ArgumentException($"Semente 0x0E deve ter {BlockSize} bytes.", nameof(helloSeed));

        Span<byte> xored = stackalloc byte[BlockSize];
        for (int i = 0; i < BlockSize; i++)
            xored[i] = (byte)(helloSeed[i] ^ ConstantW[i]);

        using var aes = Aes.Create();
        aes.Key = ConstantC;
        return aes.EncryptEcb(xored, PaddingMode.None);
    }

    /// <summary>Cifra blocos completos de 16 bytes em AES-128-ECB sem padding.</summary>
    public static byte[] EncryptBlocks(ReadOnlySpan<byte> sessionKey, ReadOnlySpan<byte> plaintext)
    {
        if (sessionKey.Length != BlockSize)
            throw new ArgumentException($"Chave AES deve ter {BlockSize} bytes.", nameof(sessionKey));
        if (plaintext.Length % BlockSize != 0)
            throw new ArgumentException($"Payload deve ser múltiplo de {BlockSize} bytes.", nameof(plaintext));

        using var aes = Aes.Create();
        aes.Key = sessionKey.ToArray();
        return aes.EncryptEcb(plaintext, PaddingMode.None);
    }

    /// <summary>Decifra blocos completos de 16 bytes em AES-128-ECB sem padding.</summary>
    public static byte[] DecryptBlocks(ReadOnlySpan<byte> sessionKey, ReadOnlySpan<byte> ciphertext)
    {
        if (sessionKey.Length != BlockSize)
            throw new ArgumentException($"Chave AES deve ter {BlockSize} bytes.", nameof(sessionKey));
        if (ciphertext.Length % BlockSize != 0)
            throw new ArgumentException($"Payload deve ser múltiplo de {BlockSize} bytes.", nameof(ciphertext));

        using var aes = Aes.Create();
        aes.Key = sessionKey.ToArray();
        return aes.DecryptEcb(ciphertext, PaddingMode.None);
    }
}
