using System.Buffers.Binary;
using DRPGServer.Data.Repositories;
using DRPGServer.Network.Protocol;

namespace DRPGServer.Network.Packets.Map;

/// <summary>
/// ✅ Map 0xE6 (S2C, 32 bytes cifrados em AES-128-ECB com a chave derivada do Map 0x0E):
/// badge de evento de EXP no canto superior esquerdo (<c>DigimonRPG.bin @ 0x00AC0093 / 0x00AEF8B0 / 0x00B8AF52</c>).
/// Plaintext (32 B): u8 spriteIndex (passos de +25%; 0 desliga), u8[3] 0, u32 year, u32 month, u32 day,
/// u8 isHotTime, u8 mode (0 = data 0xE63, 1 = expirado 0xE62), u8[14] 0.
/// </summary>
public sealed class ExpEventPacket(byte[]? sessionAesKey, ActiveExpEvent? activeEvent) : ServerPacket
{
    public const int PlaintextLength = 32;
    private const uint IgnoredClientYear = 2038; // 0x7F6 ignorado em DigimonRPG.bin @ 0x00AC00B6

    public override ushort Opcode => (ushort)MapOpcode.ExpEvent;

    protected override void Write(PacketWriter w)
    {
        Span<byte> plain = stackalloc byte[PlaintextLength];
        plain.Clear();

        if (activeEvent is { SpriteIndex: > 0 })
        {
            uint year = (uint)activeEvent.EndsAt.Year;
            if (year == IgnoredClientYear) year = IgnoredClientYear - 1;

            plain[0] = activeEvent.SpriteIndex;
            BinaryPrimitives.WriteUInt32LittleEndian(plain[4..8], year);
            BinaryPrimitives.WriteUInt32LittleEndian(plain[8..12], (uint)activeEvent.EndsAt.Month);
            BinaryPrimitives.WriteUInt32LittleEndian(plain[12..16], (uint)activeEvent.EndsAt.Day);
            plain[16] = activeEvent.IsHotTime ? (byte)1 : (byte)0;
            plain[17] = 0;
        }

        if (sessionAesKey is { Length: GlobalSessionCipher.BlockSize })
            w.WriteBytes(GlobalSessionCipher.EncryptBlocks(sessionAesKey, plain));
        else
            w.WriteBytes(plain);
    }
}
