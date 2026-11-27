using DRPG.Server.Game;
using DRPG.Shared.Protocol;

namespace DRPG.Server.Network.Packets.Map;

/// <summary>✅ 0x49 (C2S 18 B, v322): primeiro pedido no mapa. Nada é lido.</summary>
public sealed record JoinHandshakeRequest : IClientPacket<JoinHandshakeRequest>
{
    public static PacketLength Length => PacketLength.Exact(18);

    public static JoinHandshakeRequest Read(PacketReader r)
    {
        r.Skip(18);
        return new();
    }
}

/// <summary>✅ 0x04 (C2S 30 B, v322): pedir os dados do personagem. Serial do personagem (16 B) e 14 B finais.</summary>
public sealed record RefreshCharacterInfoRequest(Serial Character) : IClientPacket<RefreshCharacterInfoRequest>
{
    public static PacketLength Length => PacketLength.Exact(Serial.Length + 14);

    public static RefreshCharacterInfoRequest Read(PacketReader r)
    {
        var serial = Serial.Read(r);
        r.Skip(14);
        return new(serial);
    }
}

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

/// <summary>✅ 0x121 (C2S 14 B, v322): o cliente pede; o servidor responde com o conteúdo capturado. Nada é lido.</summary>
public sealed record Unknown121Request : IClientPacket<Unknown121Request>
{
    public static PacketLength Length => PacketLength.Exact(14);

    public static Unknown121Request Read(PacketReader r)
    {
        r.Skip(14);
        return new();
    }
}
