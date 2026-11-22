using DRPGServer.Data.GameData;
using DRPGServer.Game.Entities;
using DRPGServer.Network.Protocol;

namespace DRPGServer.Network.Packets.Map;

/// <summary>0xB4: informações de um digimon (enviado na entrada do mapa e por comandos).</summary>
public sealed class SingleDigimonInfoPacket(Digimon digimon) : ServerPacket
{
    public override ushort Opcode => (ushort)MapOpcode.SingleDigimonInfo;
    protected override void Write(PacketWriter w) => DigimonStatusBlock.Write(w, digimon);
}

/// <summary>0xB2: status atualizado (recuperação automática no mapa).</summary>
public sealed class RefreshDigimonStatusPacket(Digimon digimon) : ServerPacket
{
    public override ushort Opcode => (ushort)MapOpcode.RefreshDigimonStatus;
    protected override void Write(PacketWriter w) => DigimonStatusBlock.Write(w, digimon);
}

/// <summary>0x71 (704 bytes): ✅ ponto distribuído: bloco da forma do estágio pedido + eco do u8 estágio e do u8 atributo + 6 zeros.</summary>
public sealed class AddAbilityPointPacket(Digimon digimon, DigimonForm form, byte stage, byte stat) : ServerPacket
{
    public override ushort Opcode => (ushort)MapOpcode.DigimonAddAbilityPoint;

    protected override void Write(PacketWriter w)
    {
        DigimonStatusBlock.Write(w, digimon, form);
        w.WriteU8(stage);
        w.WriteU8(stat);
        w.WriteZeros(6);
    }
}

/// <summary>0x80: ✅ o digimon mudou de forma ao subir de nível (rookie no 11; champion no 21 em batalha). Bloco da forma nova.</summary>
public sealed class DigimonEvolvedPacket(Digimon digimon) : ServerPacket
{
    public override ushort Opcode => (ushort)MapOpcode.DigimonEvolved;
    protected override void Write(PacketWriter w) => DigimonStatusBlock.Write(w, digimon);
}

/// <summary>0x81 (720 bytes): ✅ evolução do menu acontecendo (barra cheia): u32 0, u32 0, bloco da forma nova, 16 zeros.</summary>
public sealed class BattleEvolvePacket(Digimon digimon) : ServerPacket
{
    public override ushort Opcode => (ushort)MapOpcode.BattleEvolve;

    protected override void Write(PacketWriter w)
    {
        w.WriteU64(0);
        DigimonStatusBlock.Write(w, digimon);
        w.WriteZeros(16);
    }
}

/// <summary>0x82 (720 bytes): ✅ janela de evolução, bloco da forma pedida: u32 estágio (0 = atual), u32 0, bloco, 16 zeros.</summary>
public sealed class DigimonFormInfoPacket(uint stage, Digimon digimon, DigimonForm form) : ServerPacket
{
    public override ushort Opcode => (ushort)MapOpcode.DigimonFormInfo;

    protected override void Write(PacketWriter w)
    {
        w.WriteU32(stage);
        w.WriteU32(0);
        DigimonStatusBlock.Write(w, digimon, form);
        w.WriteZeros(16);
    }
}

/// <summary>0x72 (704 bytes): ✅ skill subiu: bloco da forma + eco do u8 estágio e do u8 skill (1/2) do pedido + 6 zeros.</summary>
public sealed class SkillUpgradePacket(Digimon digimon, DigimonForm form, byte stage, byte skill) : ServerPacket
{
    public override ushort Opcode => (ushort)MapOpcode.DigimonSkillUpgrade;

    protected override void Write(PacketWriter w)
    {
        DigimonStatusBlock.Write(w, digimon, form);
        w.WriteU8(stage);
        w.WriteU8(skill);
        w.WriteZeros(6);
    }
}

/// <summary>0x9E (8 bytes): ✅ formas liberadas para evoluir agora: u8 1, u8 0, u16 × 3 (champion, ultimate, mega; 0 = nenhuma).</summary>
public sealed class EvolutionListPacket(IReadOnlyList<DigimonSpecies> options) : ServerPacket
{
    public override ushort Opcode => (ushort)MapOpcode.EvolutionList;

    protected override void Write(PacketWriter w)
    {
        w.WriteU8(1);
        w.WriteU8(0);
        for (int i = 0; i < 3; i++)
            w.WriteU16(i < options.Count ? options[i].Id : (ushort)0);
    }
}

/// <summary>0xF1 (12 bytes): ✅ líder trocado: u32 1, u32 personagem, u32 digimon.</summary>
public sealed class SetLeaderPacket(uint characterUid, uint digimonUid) : ServerPacket
{
    public override ushort Opcode => (ushort)MapOpcode.SetLeader;

    protected override void Write(PacketWriter w)
    {
        w.WriteU32(1);
        w.WriteU32(characterUid);
        w.WriteU32(digimonUid);
    }
}

/// <summary>0x173 (8 bytes): ✅ posições trocadas: u16 1, u16 botão, u16 botão, u16 botão + 1 (sessão 56).</summary>
public sealed class PartySwapPacket(ushort button) : ServerPacket
{
    public override ushort Opcode => (ushort)MapOpcode.PartySwap;

    protected override void Write(PacketWriter w)
    {
        w.WriteU16(1);
        w.WriteU16(button);
        w.WriteU16(button);
        w.WriteU16((ushort)(button + 1));
    }
}

/// <summary>0xEF (696 bytes): ✅ digimon novo no time (captura, ovo): o bloco dele. Vem antes da resposta do 0x41.</summary>
public sealed class DigimonReceivedPacket(Digimon digimon) : ServerPacket
{
    public override ushort Opcode => (ushort)MapOpcode.DigimonReceived;
    protected override void Write(PacketWriter w) => DigimonStatusBlock.Write(w, digimon);
}

/// <summary>
/// 0xF0 (28 bytes): ✅ renomeação. u8 resultado, u8 tipo (1 tamer, 2 digimon), u16 0, u32 uid, nome[20].
/// Digimon: consulta sem nome → 1 (tem a grátis) ou 2 (não tem); com nome → 3 (trocado, eco do nome).
/// Tamer: o oficial sempre responde 2 na entrada do mapa.
/// </summary>
public sealed class RenamePacket(RenameResult result, RenameKind kind, uint uid, string name) : ServerPacket
{
    public const int NameLength = 20;

    public override ushort Opcode => (ushort)MapOpcode.Rename;

    protected override void Write(PacketWriter w)
    {
        w.WriteU8((byte)result);
        w.WriteU8((byte)kind);
        w.WriteU16(0);
        w.WriteU32(uid);
        w.WriteFixedString(name, NameLength);
    }
}

public enum RenameKind : byte { Tamer = 1, Digimon = 2 }

public enum RenameResult : byte
{
    /// <summary>Consulta: a renomeação grátis está disponível.</summary>
    Free = 1,
    /// <summary>Consulta ou pedido recusado: não tem a grátis (✅ sessão 44, depois de renomear).</summary>
    NotAvailable = 2,
    Renamed = 3,
}
