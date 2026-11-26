using DRPGServer.Game.Entities;
using DRPG.Shared.Protocol;

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

/// <summary>
/// Cabeçalho do pedido de criação (0x10), ecoado pelo oficial na resposta.
/// ✅ sessões 3, 4 e 53: slot escolhido pelo cliente, mapa 0x4F (tutorial) e dois u16 que variam
/// (3B..3E e 37..3A). ❓ parecem coordenadas, mas o personagem criado aparece em outra posição.
/// </summary>
public readonly record struct CharacterCreateHeader(byte Slot, byte MapId, ushort A, ushort B);

/// <summary>0x10: resultado da criação de personagem.</summary>
public sealed class CharacterCreatePacket(int errorCode, CharacterCreateHeader header, byte tamerId, string name, ushort digimonId, string digimonName) : ServerPacket
{
    public override ushort Opcode => (ushort)ChannelOpcode.CharacterCreate;

    protected override void Write(PacketWriter w)
    {
        Result.Write(w, errorCode);
        w.WriteU8(header.Slot);
        w.WriteU8(header.MapId);
        w.WriteU16(header.A);
        w.WriteU16(header.B);
        w.WriteU8(tamerId);
        w.WriteFixedString(name, 21);
        w.WriteU16(digimonId);
        w.WriteFixedString(digimonName, 26);
    }
}

/// <summary>0x11: personagem apagado. ✅ sessão 53: o oficial ecoa o uid e o MD5 do pedido.</summary>
public sealed class CharacterDeletePacket(uint characterUid, string passwordMd5) : ServerPacket
{
    public override ushort Opcode => (ushort)ChannelOpcode.CharacterDelete;

    protected override void Write(PacketWriter w)
    {
        Result.Write(w, 0);
        w.WriteU32(characterUid);
        w.WriteFixedString(passwordMd5, 40);
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
