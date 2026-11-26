namespace DRPGServer.Network.Protocol;

/// <summary>Tamanho aceito do payload de um C2S (sem o cabeçalho de 14 bytes).</summary>
public readonly record struct PacketLength(int Min, int Max)
{
    /// <summary>O cliente sempre manda este tamanho (✅ nas capturas ou no binário).</summary>
    public static PacketLength Exact(int length) => new(length, length);

    /// <summary>O tamanho varia de verdade; o resto é lido ou pulado pelo próprio request.</summary>
    public static PacketLength AtLeast(int length) => new(length, PacketHeader.MaxPacketSize - PacketHeader.Length);

    public bool Accepts(int length) => length >= Min && length <= Max;

    public override string ToString() => Min == Max ? $"{Min} B" : $"{Min}..{Max} B";
}

/// <summary>
/// Request de um C2S: descreve o tamanho e o layout do payload. Só o <see cref="Read"/> lê bytes; o handler recebe o
/// tipo pronto (ver docs/emulador/handlers.md).
/// </summary>
public interface IClientPacket<TSelf> where TSelf : IClientPacket<TSelf>
{
    static abstract PacketLength Length { get; }

    /// <summary>Lê o payload inteiro: campos não usados são pulados aqui, com um comentário do que são.</summary>
    static abstract TSelf Read(PacketReader reader);
}

public static class ClientPacket
{
    /// <summary>
    /// Confere o tamanho, lê o request e exige que o layout consuma o payload inteiro.
    /// Qualquer divergência é <see cref="PacketFormatException"/>.
    /// </summary>
    public static T Parse<T>(ReadOnlyMemory<byte> payload) where T : IClientPacket<T>
    {
        if (!T.Length.Accepts(payload.Length))
            throw new PacketFormatException($"{typeof(T).Name}: {payload.Length} B, esperado {T.Length}");
        var reader = new PacketReader(payload);
        var request = T.Read(reader);
        if (reader.Remaining != 0)
            throw new PacketFormatException($"{typeof(T).Name}: {reader.Remaining} B sobrando depois do layout");
        return request;
    }
}

/// <summary>Payload de um C2S que o servidor não lê (eco, ping, informações sem uso). Qualquer tamanho.</summary>
public sealed record IgnoredPayload : IClientPacket<IgnoredPayload>
{
    private static readonly IgnoredPayload Instance = new();

    public static PacketLength Length => PacketLength.AtLeast(0);

    public static IgnoredPayload Read(PacketReader reader)
    {
        reader.Skip(reader.Remaining);
        return Instance;
    }
}
