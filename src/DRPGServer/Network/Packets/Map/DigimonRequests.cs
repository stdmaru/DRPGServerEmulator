using DRPGServer.Game;
using DRPG.Shared.Protocol;

namespace DRPGServer.Network.Packets.Map;

/// <summary>
/// ✅ 0x71 (C2S 718 B, v322): distribuir ponto. Bloco de status ecoado (uid nos primeiros 4 B), u8 estágio da forma,
/// u8 atributo (1..4) e 20 B finais.
/// </summary>
public sealed record AddAbilityPointRequest(uint DigimonUid, byte Stage, byte Stat) : IClientPacket<AddAbilityPointRequest>
{
    public static PacketLength Length => PacketLength.Exact(DigimonStatusBlock.Length + 2 + 20);

    public static AddAbilityPointRequest Read(PacketReader r)
    {
        var uid = r.ReadU32();
        r.Skip(DigimonStatusBlock.Length - 4);
        var stage = r.ReadU8();
        var stat = r.ReadU8();
        r.Skip(20);
        return new(uid, stage, stat);
    }
}

/// <summary>✅ 0x72 (C2S 718 B, v322): subir skill. Bloco ecoado, u8 estágio da forma, u8 skill (1 ou 2) e 20 B finais.</summary>
public sealed record SkillUpgradeRequest(uint DigimonUid, byte Stage, byte Skill) : IClientPacket<SkillUpgradeRequest>
{
    public static PacketLength Length => PacketLength.Exact(DigimonStatusBlock.Length + 2 + 20);

    public static SkillUpgradeRequest Read(PacketReader r)
    {
        var uid = r.ReadU32();
        r.Skip(DigimonStatusBlock.Length - 4);
        var stage = r.ReadU8();
        var skill = r.ReadU8();
        r.Skip(20);
        return new(uid, stage, skill);
    }
}

/// <summary>
/// ✅ 0x82 (C2S 734 B, v322): janela de evolução. u32 estágio (0 = atual), u32 0, bloco ecoado (uid nos primeiros 4 B),
/// 16 B zerados e 14 B finais.
/// </summary>
public sealed record DigimonFormInfoRequest(uint Stage, uint DigimonUid) : IClientPacket<DigimonFormInfoRequest>
{
    public static PacketLength Length => PacketLength.Exact(8 + DigimonStatusBlock.Length + 16 + 14);

    public static DigimonFormInfoRequest Read(PacketReader r)
    {
        var stage = r.ReadU32();
        r.ReadU32();
        var uid = r.ReadU32();
        r.Skip(DigimonStatusBlock.Length - 4);
        r.Skip(16 + 14);
        return new(stage, uid);
    }
}

/// <summary>✅ 0x97 (C2S 1622 B, v322): abrir a janela de títulos. u32 ação (2), u32 uid do digimon e 1614 B não usados.</summary>
public sealed record DigimonTitleListRequest(uint Action, uint DigimonUid) : IClientPacket<DigimonTitleListRequest>
{
    public static PacketLength Length => PacketLength.Exact(1622);

    public static DigimonTitleListRequest Read(PacketReader r)
    {
        var action = r.ReadU32();
        var uid = r.ReadU32();
        r.Skip(1614);
        return new(action, uid);
    }
}

/// <summary>✅ 0x99 (C2S 26 B, v322): equipar título. u32 ação (2), u32 uid do digimon, u32 título (0 = remover) e 14 B finais.</summary>
public sealed record DigimonTitleEquipRequest(uint Action, uint DigimonUid, uint TitleId) : IClientPacket<DigimonTitleEquipRequest>
{
    public static PacketLength Length => PacketLength.Exact(26);

    public static DigimonTitleEquipRequest Read(PacketReader r)
    {
        var action = r.ReadU32();
        var uid = r.ReadU32();
        var titleId = r.ReadU32();
        r.Skip(14);
        return new(action, uid, titleId);
    }
}

/// <summary>✅ 0x9E (C2S 18 B, v322): formas para evoluir agora. u32 uid do digimon e 14 B finais.</summary>
public sealed record EvolutionListRequest(uint DigimonUid) : IClientPacket<EvolutionListRequest>
{
    public static PacketLength Length => PacketLength.Exact(18);

    public static EvolutionListRequest Read(PacketReader r)
    {
        var uid = r.ReadU32();
        r.Skip(14);
        return new(uid);
    }
}

/// <summary>✅ 0xF1 (C2S 26 B, v322): trocar o líder. u32 0, u32 personagem, u32 digimon e 14 B finais.</summary>
public sealed record SetLeaderRequest(uint CharacterUid, uint DigimonUid) : IClientPacket<SetLeaderRequest>
{
    public static PacketLength Length => PacketLength.Exact(26);

    public static SetLeaderRequest Read(PacketReader r)
    {
        r.ReadU32();
        var characterUid = r.ReadU32();
        var digimonUid = r.ReadU32();
        r.Skip(14);
        return new(characterUid, digimonUid);
    }
}

/// <summary>✅ 0x173 (C2S 22 B, v322): u16 0, u16 botão k (0..2): troca as posições k + 1 e k + 2 do time; 18 B finais.</summary>
public sealed record PartySwapRequest(ushort Button) : IClientPacket<PartySwapRequest>
{
    public static PacketLength Length => PacketLength.Exact(22);

    public static PartySwapRequest Read(PacketReader r)
    {
        r.ReadU16();
        var button = r.ReadU16();
        r.Skip(18);
        return new(button);
    }
}

/// <summary>
/// ✅ 0xF0 (C2S 42 B, v322): renomear. u8 0, u8 tipo (1 tamer, 2 digimon), u16 0, u32 uid, nome[20] e 14 B finais.
/// Sem nome é consulta.
/// </summary>
public sealed record RenameRequest(RenameKind Kind, uint Uid, string Name) : IClientPacket<RenameRequest>
{
    public static PacketLength Length => PacketLength.Exact(8 + RenamePacket.NameLength + 14);

    public static RenameRequest Read(PacketReader r)
    {
        r.ReadU8();
        var kind = (RenameKind)r.ReadU8();
        r.ReadU16();
        var uid = r.ReadU32();
        var name = r.ReadFixedString(RenamePacket.NameLength);
        r.Skip(14);
        return new(kind, uid, name);
    }
}

/// <summary>
/// ✅ 0xB4 (C2S 710 B, v322): o cliente saiu da tela de resultado. Bloco de status (u32 uid, serial nos 16 B seguintes)
/// e 14 B finais.
/// </summary>
public sealed record DigimonInfoRequest(Serial Digimon) : IClientPacket<DigimonInfoRequest>
{
    public static PacketLength Length => PacketLength.Exact(DigimonStatusBlock.Length + 14);

    public static DigimonInfoRequest Read(PacketReader r)
    {
        r.ReadU32();
        var serial = Serial.Read(r);
        r.Skip(DigimonStatusBlock.Length - 4 - Serial.Length);
        r.Skip(14);
        return new(serial);
    }
}
