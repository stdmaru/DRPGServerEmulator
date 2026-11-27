using DRPG.Server.Game.Entities;
using DRPG.Shared.Packets.Map;
using DRPG.Shared.Protocol;

namespace DRPG.Server.Network.Packets.Map;

/// <summary>
/// Monta o bloco de status de digimon (<see cref="DigimonStatus"/>, no <c>DRPG.Shared</c>), comum a 0x02, 0x63, 0x70,
/// 0x71, 0xB2 e 0xB4. ✅ v322: 696 bytes (era 688 na v242). Layout em docs/protocolo/structs/status-digimon.md; os
/// bytes "?" vêm das capturas. Cada forma de evolução tem bloco próprio (docs/mecanicas/evolucao.md).
/// </summary>
public static class DigimonStatusBlock
{
    public const int Length = DigimonStatus.Length;

    /// <summary>O bloco do digimon na forma atual, ou o bloco vazio (posição sem digimon).</summary>
    public static DigimonStatus Of(Digimon? d) => d is null ? DigimonStatus.Empty : Of(d, ElementMask(d));

    /// <summary>
    /// O bloco com a máscara de elemento dada. O bloco do time no 0x02 manda sempre 1 em 162 (✅ capturas);
    /// os demais pacotes mandam <c>1 &lt;&lt; (elemento - 1)</c>.
    /// </summary>
    public static DigimonStatus Of(Digimon d, ushort elementMask)
    {
        var s = d.Stats;
        // ✅ o oficial manda zero nos atributos de digimon selvagem (0x63) e os reais nos do jogador
        var a = d.IsWild ? default : d.Attributes;
        var bonus = d.IsWild ? default : d.TitleAttributeBonus;
        var skill1 = Skill(d, 0);
        var skill2 = Skill(d, 1);
        return new DigimonStatus(
            d.Uid, d.Serial, d.SpeciesId,
            UnlockedStage: d.IsWild ? (byte)0 : d.UnlockedStage,    // ✅ maior estágio liberado (0 no selvagem)
            d.Name, d.Level, a.Str, a.Agi, a.Con, a.Int,
            d.Species.Stage, d.Species.Attribute, elementMask,      // 160 ✅ colunas 2 e 3 do digimon_us
            d.Hp, d.Vp, d.Evp, s.Atk, s.Def, s.Br,
            Unknown216: 0x8000_0000,                                // ? sempre 00 00 00 80
            d.Exp, s.MaxActionGauge, s.MaxHp, s.MaxVp, s.MaxEvp,
            Unknown252: 100_000,                                    // ? sempre 100000
            ExpToNextLevel: d.ExpToNextLevel == long.MaxValue ? 0 : d.ExpToNextLevel,
            d.TotalWins, d.TotalBattles, d.AbilityPoints, d.SkillPoints,
            skill1, skill2,
            GaugePerStep: (uint)Game.Battle.BattleFormulas.StatusGaugeSpeed(d, d.IsWild), // ✅ barra por passo (off296)
            Owner: d.IsWild ? (d.WildGrade > 0 ? d.WildGrade : Game.Stats.WildFormulas.GradeBeginner) : (byte)0, // ✅ 0 jogador, 2 Beginner, 3 Veteran
            Unknown312: 8,                                          // ? (7 ou 8 nas capturas)
            TitleId: d.IsWild ? 0u : d.TitleId,                     // ✅ título equipado do digimon (conn=76/112/141)
            TitleActive: !d.IsWild && d.TitleId != 0 ? 1u : 0u,     // ✅ 1 quando há título equipado
            bonus.Str, bonus.Agi, bonus.Con, bonus.Int);            // ✅ bônus do título
    }

    /// <summary>Bloco de outra forma do digimon (janela de evolução, 0x71 e 0x72 de uma forma que não é a atual).</summary>
    public static DigimonStatus Of(Digimon d, DigimonForm form) => d.ViewAs(form, () => Of(d));

    /// <summary>Escreve o bloco inteiro (<see cref="Length"/> bytes).</summary>
    public static void Write(PacketWriter w, Digimon d) => Of(d).Write(w);

    /// <summary>Bloco de outra forma do digimon.</summary>
    public static void Write(PacketWriter w, Digimon d, DigimonForm form) => Of(d, form).Write(w);

    /// <summary>Bloco vazio (posição sem digimon na batalha).</summary>
    public static void WriteEmpty(PacketWriter w) => DigimonStatus.Empty.Write(w);

    /// <summary>✅ 162: bitmask do elemento, <c>1 &lt;&lt; (elemento - 1)</c> (1 sem elemento).</summary>
    private static ushort ElementMask(Digimon d) =>
        d.Species.Element > 0 ? (ushort)(1 << (d.Species.Element - 1)) : (ushort)1;

    private static DigimonSkill Skill(Digimon d, int slot)
    {
        ushort id = d.Current.SkillId(slot);
        return new DigimonSkill(id, d.SkillInfoOf(slot)?.RequiredLevel ?? 0, (short)(id == 0 ? 0 : d.Current.SkillLevels[slot]));
    }
}
