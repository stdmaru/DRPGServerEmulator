using DRPGServer.Game;
using DRPG.Shared.Protocol;

namespace DRPGServer.Network.Packets.Map;

/// <summary>✅ 0x61 (C2S 34 B, v322): o jogador encostou num selvagem. i32, serial do selvagem e 14 B finais.</summary>
public sealed record BattleStartRequest(Serial Wild) : IClientPacket<BattleStartRequest>
{
    public static PacketLength Length => PacketLength.Exact(4 + Serial.Length + 14);

    public static BattleStartRequest Read(PacketReader r)
    {
        r.ReadI32();
        var wild = Serial.Read(r);
        r.Skip(14);
        return new(wild);
    }
}

/// <summary>✅ 0x62 (C2S 34 B, v322): o cliente confirma o serial da batalha. Serial e 18 B finais.</summary>
public sealed record BattleConfirmRequest(Serial Battle) : IClientPacket<BattleConfirmRequest>
{
    public static PacketLength Length => PacketLength.Exact(Serial.Length + 18);

    public static BattleConfirmRequest Read(PacketReader r)
    {
        var battle = Serial.Read(r);
        r.Skip(18);
        return new(battle);
    }
}

/// <summary>✅ 0x69 (C2S 46 B, v322): o cliente está pronto (no começo e depois de cada animação). Nada é lido.</summary>
public sealed record BattleReadyRequest : IClientPacket<BattleReadyRequest>
{
    public static PacketLength Length => PacketLength.Exact(46);

    public static BattleReadyRequest Read(PacketReader r)
    {
        r.Skip(46);
        return new();
    }
}

/// <summary>
/// ✅ 0x35 (C2S 50 B, v322): u8 ação (2 = escolher), u16 ícone (0x0A ataque, 0x04 fuga…), u8 0, serial de quem age,
/// serial do alvo e 14 B finais.
/// </summary>
public sealed record BattleActionRequest(BattleAction Action, BattleIcon Icon, Serial Requester, Serial Target)
    : IClientPacket<BattleActionRequest>
{
    public static PacketLength Length => PacketLength.Exact(4 + 2 * Serial.Length + 14);

    public static BattleActionRequest Read(PacketReader r)
    {
        var action = (BattleAction)r.ReadU8();
        var icon = (BattleIcon)r.ReadU16();
        r.ReadU8();
        var requester = Serial.Read(r);
        var target = Serial.Read(r);
        r.Skip(14);
        return new(action, icon, requester, target);
    }
}

/// <summary>
/// ✅ 0xA0 (C2S 98 B, v322): equipar card dos CardSlots no slot de batalha 0..2. u32 slot, registro do card
/// (uid e id nos primeiros 8 B) e 18 B finais.
/// </summary>
public sealed record BattleCardEquipRequest(uint Slot, uint Uid, uint CardId) : IClientPacket<BattleCardEquipRequest>
{
    public static PacketLength Length => PacketLength.Exact(4 + CardRecord.Length + 18);

    public static BattleCardEquipRequest Read(PacketReader r)
    {
        var slot = r.ReadU32();
        var uid = r.ReadU32();
        var cardId = r.ReadU32();
        r.Skip(CardRecord.Length - 8);          // resto do registro
        r.Skip(18);
        return new(slot, uid, cardId);
    }
}

/// <summary>✅ 0xA1 (C2S 98 B, v322): desequipar o card do slot de batalha 0..2. u32 slot e 94 B não usados.</summary>
public sealed record BattleCardUnequipRequest(uint Slot) : IClientPacket<BattleCardUnequipRequest>
{
    public static PacketLength Length => PacketLength.Exact(98);

    public static BattleCardUnequipRequest Read(PacketReader r)
    {
        var slot = r.ReadU32();
        r.Skip(94);
        return new(slot);
    }
}
