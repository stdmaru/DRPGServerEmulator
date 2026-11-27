using DRPG.Server.Game;
using DRPG.Shared.Protocol;

namespace DRPG.Server.Network.Packets.Map;

/// <summary>
/// ✅ 0x21 (C2S 291 B, v322; captures.db conn=108/158): chat normal. Apelido[21] (o do servidor vale), mensagem[256] e 14 B zerados.
/// ❓ O servidor usa só os primeiros <see cref="MessageLength"/> bytes da mensagem.
/// </summary>
public sealed record ChatNormalRequest(string Message) : IClientPacket<ChatNormalRequest>
{
    public const int MessageLength = 44;
    private const int MessageField = 256;
    private const int TrailerLength = 14;

    public static PacketLength Length => PacketLength.Exact(21 + MessageField + TrailerLength);

    public static ChatNormalRequest Read(PacketReader r)
    {
        r.ReadFixedString(21);                  // apelido
        var message = r.ReadFixedString(MessageLength);
        r.Skip(MessageField - MessageLength + TrailerLength);
        return new(message);
    }
}

/// <summary>✅ 0x2B (C2S 14 B, v322): sair do jogo. Nada é lido.</summary>
public sealed record ExitGameRequest : IClientPacket<ExitGameRequest>
{
    public static PacketLength Length => PacketLength.Exact(14);

    public static ExitGameRequest Read(PacketReader r)
    {
        r.Skip(14);
        return new();
    }
}
