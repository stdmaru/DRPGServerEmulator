using DRPGServer.Game;
using DRPGServer.Network.Protocol;

namespace DRPGServer.Network.Packets.Map;

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

/// <summary>✅ 0x01 (C2S 57 B, v322): entrar no mapa. Personagem[21], usuário[21], u8 e 14 B finais.</summary>
public sealed record JoinCharacterRequest(string CharacterName, string Username) : IClientPacket<JoinCharacterRequest>
{
    public static PacketLength Length => PacketLength.Exact(57);

    public static JoinCharacterRequest Read(PacketReader r)
    {
        var character = r.ReadFixedString(21);
        var username = r.ReadFixedString(21);
        r.ReadU8();
        r.Skip(14);
        return new(character, username);
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

/// <summary>✅ 0x20 (C2S 20 B, v322): andar. i16 (sempre 5), i16 x, i16 y e 14 B zerados.</summary>
public sealed record MoveCharacterRequest(short X, short Y) : IClientPacket<MoveCharacterRequest>
{
    public static PacketLength Length => PacketLength.Exact(20);

    public static MoveCharacterRequest Read(PacketReader r)
    {
        r.ReadI16();                            // ✅ sempre 5
        var x = r.ReadI16();
        var y = r.ReadI16();
        r.Skip(14);
        return new(x, y);
    }
}

/// <summary>
/// ✅ 0x21 (C2S 277 B, v322): chat normal. Apelido[21] (o do servidor vale) e mensagem[256].
/// ❓ O servidor usa só os primeiros <see cref="MessageLength"/> bytes da mensagem.
/// </summary>
public sealed record ChatNormalRequest(string Message) : IClientPacket<ChatNormalRequest>
{
    public const int MessageLength = 44;
    private const int MessageField = 256;

    public static PacketLength Length => PacketLength.Exact(21 + MessageField);

    public static ChatNormalRequest Read(PacketReader r)
    {
        r.ReadFixedString(21);                  // apelido
        var message = r.ReadFixedString(MessageLength);
        r.Skip(MessageField - MessageLength);
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
