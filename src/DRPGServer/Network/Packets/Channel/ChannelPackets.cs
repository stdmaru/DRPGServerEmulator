using DRPGServer.Game.Entities;
using DRPGServer.Network.Protocol;

namespace DRPGServer.Network.Packets.Channel;

/// <summary>Código de resultado no começo das respostas do channel: 2 sucesso, 3 erro.</summary>
internal static class Result
{
    public static void Write(PacketWriter w, int errorCode)
    {
        w.WriteI32(errorCode == 0 ? 2 : 3);
        w.WriteI32(errorCode);
    }
}

public static class ChannelError
{
    public const int None = 0;
    public const int InvalidCredentials = 101;
    public const int NameTaken = 102;
}

/// <summary>0x03: seleção de canal aceita.</summary>
public sealed class ChannelSelectPacket(int fourthStarter) : ServerPacket
{
    public override ushort Opcode => (ushort)ChannelOpcode.ChannelSelect;

    protected override void Write(PacketWriter w)
    {
        Result.Write(w, 0);
        w.WriteI32(fourthStarter);
        w.WriteFixedString("MoveInteractive_Digimon_035", 40);
    }
}

/// <summary>
/// 0x04: resultado da autenticação. Em erro, o oficial ecoa o pedido (usuário, chave e MACs);
/// em sucesso, só o usuário.
/// </summary>
public sealed class AuthenticationPacket(int errorCode, string username, string authKey = "", string mac1 = "", string mac2 = "", string mac3 = "") : ServerPacket
{
    public override ushort Opcode => (ushort)ChannelOpcode.Authenticate;

    protected override void Write(PacketWriter w)
    {
        Result.Write(w, errorCode);
        w.WriteFixedString(username, 21);
        w.WriteFixedString(authKey, 40);
        w.WriteZeros(60);
        w.WriteFixedString(mac1, 21);
        w.WriteFixedString(mac2, 21);
        w.WriteFixedString(mac3, 21);
        w.WriteZeros(256);
    }
}

/// <summary>0x10: resultado da criação de personagem.</summary>
public sealed class CharacterCreatePacket(int errorCode, byte tamerId, string name, ushort digimonId, string digimonName) : ServerPacket
{
    public override ushort Opcode => (ushort)ChannelOpcode.CharacterCreate;

    protected override void Write(PacketWriter w)
    {
        Result.Write(w, errorCode);
        w.WriteU16(0x4F01);                     // ?
        w.WriteU16(0x003D);                     // ? (varia 3B..3E)
        w.WriteU16(0x0038);                     // ? (varia 37..3A)
        w.WriteU8(tamerId);
        w.WriteFixedString(name, 21);
        w.WriteU16(digimonId);
        w.WriteFixedString(digimonName, 26);
    }
}

/// <summary>0x11: personagem apagado.</summary>
public sealed class CharacterDeletePacket(uint characterUid) : ServerPacket
{
    public override ushort Opcode => (ushort)ChannelOpcode.CharacterDelete;

    protected override void Write(PacketWriter w)
    {
        Result.Write(w, 0);
        w.WriteU32(characterUid);
        // ❓ o pedido traz o MD5 da senha; o protótipo respondia o MD5 de "" e o cliente aceitava
        w.WriteFixedString("d41d8cd98f00b204e9800998ecf8427e", 40);
    }
}

/// <summary>0x12: os quatro slots de personagem.</summary>
public sealed class CharacterListPacket(IReadOnlyList<CharacterSummary?> slots, Random random) : ServerPacket
{
    public const int Slots = 4;

    public override ushort Opcode => (ushort)ChannelOpcode.CharacterList;

    protected override void Write(PacketWriter w)
    {
        w.WriteI32(1);                          // ?
        for (byte slot = 0; slot < Slots; slot++)
            WriteSlot(w, slot, slot < slots.Count ? slots[slot] : null);
    }

    private void WriteSlot(PacketWriter w, byte slot, CharacterSummary? c)
    {
        int offset = random.Next(0, 255);
        w.WriteU32(c?.Uid ?? 0);
        w.WriteU8(c?.TamerId ?? 0);
        w.WriteFixedString(c?.Name, 21);
        w.WriteU16(c?.Level ?? 0);
        w.WriteI16(c?.X ?? 0);
        w.WriteI16(c?.Y ?? 0);
        w.WriteZeros(44);                       // equipamento (11 × u32: ?, pés, pernas, mãos, peito, peito2, cabeça, ...)
        w.WriteU16(c?.LeaderSpeciesId ?? 0);
        w.WriteU16(c?.LeaderLevel ?? 0);
        w.WriteFixedString(c?.LeaderName, 24);
        w.WriteI32(c?.TotalBattles ?? 0);
        w.WriteI32(c?.TotalWins ?? 0);
        w.WriteU8(slot);
        w.WriteU8(c?.MapId ?? 0);
        w.WriteI16(0);
        // ❓ verificação: deslocamento aleatório + mapa + nível, seguido do deslocamento
        w.WriteI32(offset + (c?.MapId ?? 0) + (c?.Level ?? 0));
        w.WriteI32(offset);
    }
}

/// <summary>0x14: confirma o usuário.</summary>
public sealed class UserCheckPacket(string username) : ServerPacket
{
    public override ushort Opcode => (ushort)ChannelOpcode.UserCheck;
    protected override void Write(PacketWriter w) => w.WriteFixedString(username, 22);
}
