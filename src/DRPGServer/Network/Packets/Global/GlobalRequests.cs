using DRPG.Shared.Protocol;

namespace DRPGServer.Network.Packets.Global;

/// <summary>✅ 0x64 (C2S 114 B, v322): primeiro pacote no global. Usuário[21], personagem[21] e 72 B finais.</summary>
public sealed record GlobalUserAuthRequest(string Username, string CharacterName) : IClientPacket<GlobalUserAuthRequest>
{
    public static PacketLength Length => PacketLength.Exact(114);

    public static GlobalUserAuthRequest Read(PacketReader r)
    {
        var username = r.ReadFixedString(21);
        var character = r.ReadFixedString(21);
        r.Skip(72);
        return new(username, character);
    }
}

/// <summary>
/// ✅ 0x7B (C2S 1120 B, v322): dados cifrados em AES-128-ECB (u32 uid do digimon + u8 owned[1100]; ❓ o que sobra
/// até 1120 B). Só o primeiro bloco de 16 B é guardado: ele leva o uid que o servidor ecoa. A decifragem é do handler
/// (a chave é da sessão).
/// </summary>
public sealed record EncyclopediaDataRequest(byte[] FirstBlock) : IClientPacket<EncyclopediaDataRequest>
{
    public const int PayloadLength = 1120;

    public static PacketLength Length => PacketLength.Exact(PayloadLength);

    public static EncyclopediaDataRequest Read(PacketReader r)
    {
        var first = r.ReadBytes(GlobalSessionCipher.BlockSize);
        r.Skip(PayloadLength - GlobalSessionCipher.BlockSize);   // resto dos dados cifrados
        return new(first);
    }
}

/// <summary>✅ 0x7C (C2S 80 B, v322): pedir o time e as evoluções para a enciclopédia. Nada é lido.</summary>
public sealed record EncyclopediaCollectionRequest : IClientPacket<EncyclopediaCollectionRequest>
{
    public static PacketLength Length => PacketLength.Exact(80);

    public static EncyclopediaCollectionRequest Read(PacketReader r)
    {
        r.Skip(80);
        return new();
    }
}
