using System.Security.Cryptography;

namespace DRPG.Shared.Protocol;

/// <summary>
/// ✅ AES-128-ECB cipher of the global session (encyclopedia packets 0x7B and 0x7C).
/// Confirmed in <c>DigimonRPG.bin</c> (0x00869CC0 / 0x00869810 / 0x00ACF629) and 31/31 connections in <c>captures.db</c>:
/// <c>K = AES_C(hello XOR W)</c>, where <c>hello</c> is the 16 bytes of the global <c>0x0E</c> packet.
/// </summary>
public static class GlobalSessionCipher
{
    public const int BlockSize = 16;

    /// <summary>✅ Constant W at <c>DigimonRPG.bin @ 0x010078A8</c>.</summary>
    private static readonly byte[] ConstantW =
    [
        0x47, 0x95, 0xF1, 0xC8, 0x01, 0x02, 0x18, 0x49,
        0x77, 0xD6, 0x26, 0xE1, 0x3E, 0x4B, 0xC6, 0xD2,
    ];

    /// <summary>✅ Constant C at <c>DigimonRPG.bin @ 0x010078B8</c>.</summary>
    private static readonly byte[] ConstantC =
    [
        0x1A, 0x05, 0x71, 0xCD, 0xC1, 0x92, 0x88, 0xF9,
        0x77, 0xFF, 0x2D, 0x11, 0x3D, 0x4B, 0xCA, 0xDD,
    ];

    /// <summary>Derives the session AES-128 key from the 16 bytes of the global <c>0x0E</c>.</summary>
    public static byte[] DeriveKey(ReadOnlySpan<byte> helloSeed)
    {
        if (helloSeed.Length != BlockSize)
            throw new ArgumentException($"The 0x0E seed must be {BlockSize} bytes.", nameof(helloSeed));

        Span<byte> xored = stackalloc byte[BlockSize];
        for (int i = 0; i < BlockSize; i++)
            xored[i] = (byte)(helloSeed[i] ^ ConstantW[i]);

        using var aes = Aes.Create();
        aes.Key = ConstantC;
        return aes.EncryptEcb(xored, PaddingMode.None);
    }

    /// <summary>Encrypts whole 16-byte blocks with AES-128-ECB, no padding.</summary>
    public static byte[] EncryptBlocks(ReadOnlySpan<byte> sessionKey, ReadOnlySpan<byte> plaintext)
    {
        if (sessionKey.Length != BlockSize)
            throw new ArgumentException($"The AES key must be {BlockSize} bytes.", nameof(sessionKey));
        if (plaintext.Length % BlockSize != 0)
            throw new ArgumentException($"The payload must be a multiple of {BlockSize} bytes.", nameof(plaintext));

        using var aes = Aes.Create();
        aes.Key = sessionKey.ToArray();
        return aes.EncryptEcb(plaintext, PaddingMode.None);
    }

    /// <summary>Decrypts whole 16-byte blocks with AES-128-ECB, no padding.</summary>
    public static byte[] DecryptBlocks(ReadOnlySpan<byte> sessionKey, ReadOnlySpan<byte> ciphertext)
    {
        if (sessionKey.Length != BlockSize)
            throw new ArgumentException($"The AES key must be {BlockSize} bytes.", nameof(sessionKey));
        if (ciphertext.Length % BlockSize != 0)
            throw new ArgumentException($"The payload must be a multiple of {BlockSize} bytes.", nameof(ciphertext));

        using var aes = Aes.Create();
        aes.Key = sessionKey.ToArray();
        return aes.DecryptEcb(ciphertext, PaddingMode.None);
    }
}
