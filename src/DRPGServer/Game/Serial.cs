using System.Buffers.Binary;
using System.Security.Cryptography;
using DRPG.Shared.Protocol;

namespace DRPGServer.Game;

/// <summary>
/// Identificador de 16 bytes que o protocolo usa para personagens, digimons, batalhas e
/// digimons selvagens. Struct com igualdade por valor: serve de chave de dicionário.
/// </summary>
public readonly record struct Serial(ulong Low, ulong High)
{
    public const int Length = 16;
    public static readonly Serial Empty = default;

    public bool IsEmpty => Low == 0 && High == 0;

    public static Serial New()
    {
        Span<byte> bytes = stackalloc byte[Length];
        RandomNumberGenerator.Fill(bytes);
        return FromBytes(bytes);
    }

    public static Serial FromBytes(ReadOnlySpan<byte> bytes) => new(
        BinaryPrimitives.ReadUInt64LittleEndian(bytes),
        BinaryPrimitives.ReadUInt64LittleEndian(bytes[8..]));

    public static Serial Read(PacketReader reader) => FromBytes(reader.ReadBytes(Length));

    public void Write(PacketWriter writer)
    {
        writer.WriteU64(Low);
        writer.WriteU64(High);
    }

    public override string ToString() => $"{High:X16}{Low:X16}";
}
