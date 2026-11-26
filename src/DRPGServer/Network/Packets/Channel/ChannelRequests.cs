using DRPG.Shared.Protocol;

namespace DRPGServer.Network.Packets.Channel;

/// <summary>✅ 0x03 (C2S 66 B, v322): o jogador clicou num canal. Nada é lido.</summary>
public sealed record ChannelSelectRequest : IClientPacket<ChannelSelectRequest>
{
    public static PacketLength Length => PacketLength.Exact(66);

    public static ChannelSelectRequest Read(PacketReader r)
    {
        r.Skip(66);
        return new();
    }
}

/// <summary>
/// ✅ 0x04 (C2S 462 B, v322): i32, i32, usuário[21], auth key[40], 60 B, MAC[21] × 3 e 270 B finais.
/// </summary>
public sealed record AuthenticateRequest(string Username, string AuthKey, string Mac1, string Mac2, string Mac3)
    : IClientPacket<AuthenticateRequest>
{
    public static PacketLength Length => PacketLength.Exact(462);

    public static AuthenticateRequest Read(PacketReader r)
    {
        r.ReadI32();
        r.ReadI32();
        var username = r.ReadFixedString(21);
        var authKey = r.ReadFixedString(40);
        r.Skip(60);
        var mac1 = r.ReadFixedString(21);
        var mac2 = r.ReadFixedString(21);
        var mac3 = r.ReadFixedString(21);
        r.Skip(270);
        return new(username, authKey, mac1, mac2, mac3);
    }

    /// <summary>Nunca registra a auth key.</summary>
    public override string ToString() => $"{nameof(AuthenticateRequest)}({Username})";
}

/// <summary>
/// ✅ 0x10 (C2S 78 B, v322): i32, i32, cabeçalho (u8 slot, u8 mapa, u16, u16), u8 tamer, nome[21], u16 digimon,
/// nome do digimon[26] e 14 B finais.
/// </summary>
public sealed record CharacterCreateRequest(CharacterCreateHeader Header, byte TamerId, string Name, ushort DigimonId, string DigimonName)
    : IClientPacket<CharacterCreateRequest>
{
    public static PacketLength Length => PacketLength.Exact(78);

    public static CharacterCreateRequest Read(PacketReader r)
    {
        r.ReadI32();
        r.ReadI32();
        var header = new CharacterCreateHeader(r.ReadU8(), r.ReadU8(), r.ReadU16(), r.ReadU16());
        var tamerId = r.ReadU8();
        var name = r.ReadFixedString(21);
        var digimonId = r.ReadU16();
        var digimonName = r.ReadFixedString(26);
        r.Skip(14);
        return new(header, tamerId, name, digimonId, digimonName);
    }
}

/// <summary>✅ 0x11 (C2S 66 B, v322): i32, i32, u32 uid do personagem, MD5 da senha[40] e 14 B finais.</summary>
public sealed record CharacterDeleteRequest(uint CharacterUid, string PasswordMd5) : IClientPacket<CharacterDeleteRequest>
{
    public static PacketLength Length => PacketLength.Exact(66);

    public static CharacterDeleteRequest Read(PacketReader r)
    {
        r.ReadI32();
        r.ReadI32();
        var uid = r.ReadU32();
        var passwordMd5 = r.ReadFixedString(40);   // ❓ o oficial confere; aqui não há senha local
        r.Skip(14);
        return new(uid, passwordMd5);
    }

    public override string ToString() => $"{nameof(CharacterDeleteRequest)}({CharacterUid})";
}

/// <summary>✅ 0x12 (C2S 14 B, v322): pedir a lista de personagens. Nada é lido.</summary>
public sealed record CharacterListRequest : IClientPacket<CharacterListRequest>
{
    public static PacketLength Length => PacketLength.Exact(14);

    public static CharacterListRequest Read(PacketReader r)
    {
        r.Skip(14);
        return new();
    }
}

/// <summary>✅ 0x14 (C2S 36 B, v322): confirmação do usuário. Nada é lido.</summary>
public sealed record UserCheckRequest : IClientPacket<UserCheckRequest>
{
    public static PacketLength Length => PacketLength.Exact(36);

    public static UserCheckRequest Read(PacketReader r)
    {
        r.Skip(36);
        return new();
    }
}
