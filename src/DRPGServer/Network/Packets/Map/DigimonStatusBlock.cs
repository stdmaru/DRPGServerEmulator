using DRPGServer.Game.Entities;
using DRPG.Shared.Protocol;

namespace DRPGServer.Network.Packets.Map;

/// <summary>
/// Bloco de status de digimon, comum a 0x02, 0x63, 0x70, 0x71, 0xB2 e 0xB4.
/// ✅ v322: 696 bytes (era 688 na v242; 0xB4 e o 0x02 com 5 blocos na sessão 52).
/// Offsets 0..343 iguais à v242; depois de 343 é zero em todas as capturas, então a posição dos
/// 8 bytes novos não muda nenhum byte gerado. Cada forma de evolução tem bloco próprio (docs/mecanicas/evolucao.md).
/// Layout em docs/protocolo/structs/status-digimon.md. Os offsets nos comentários são relativos
/// ao início do bloco. No protótipo cada pacote tinha uma cópia; os bytes "?" vêm das capturas.
/// </summary>
public static class DigimonStatusBlock
{
    public const int Length = 696;

    /// <summary>Escreve o bloco inteiro (<see cref="Length"/> bytes).</summary>
    public static void Write(PacketWriter w, Digimon d)
    {
        int start = w.Position;
        WriteHead(w, d);
        WriteBody(w, d);
        w.WriteZeros(Length - (w.Position - start));
    }

    /// <summary>Bloco de outra forma do digimon (janela de evolução, 0x71 e 0x72 de uma forma que não é a atual).</summary>
    public static void Write(PacketWriter w, Digimon d, DigimonForm form) =>
        d.ViewAs(form, () => { Write(w, d); return 0; });

    /// <summary>Bloco vazio (posição sem digimon na batalha).</summary>
    public static void WriteEmpty(PacketWriter w) => w.WriteZeros(Length);

    /// <summary>0..63: identidade, nível e atributos.</summary>
    public static void WriteHead(PacketWriter w, Digimon d)
    {
        w.WriteU32(d.Uid);                      // 0
        d.Serial.Write(w);                      // 4
        w.WriteU16(d.SpeciesId);                // 20
        w.WriteU16(0);                          // 22 ? sempre 0
        w.WriteU8(d.IsWild ? (byte)0 : d.UnlockedStage); // 24 ✅ maior estágio liberado (0 no selvagem)
        w.WriteFixedString(d.Name, 21);         // 25
        w.WriteU16(d.Level);                    // 46
        // 48: ✅ o oficial manda zero nos atributos de digimon selvagem (0x63) e os reais nos do jogador
        var a = d.IsWild ? default : d.Attributes;
        w.WriteI32(a.Str);
        w.WriteI32(a.Agi);
        w.WriteI32(a.Con);
        w.WriteI32(a.Int);
    }

    /// <summary>64..327: estado, status de combate, EXP, contadores e skills.</summary>
    public static void WriteBody(PacketWriter w, Digimon d)
    {
        // 64..191: zero nas capturas, exceto 160 estágio, 161 atributo (✅ colunas 2 e 3 do digimon_us)
        // e o u16 em 162 (bitmask do elemento: 1 << (elem - 1))
        ushort elemMask = d.Species.Element > 0 ? (ushort)(1 << (d.Species.Element - 1)) : (ushort)1;
        w.WriteSparse(128, (96, [d.Species.Stage, d.Species.Attribute, (byte)(elemMask & 0xFF), (byte)(elemMask >> 8)]));
        WriteCombat(w, d);
        WriteProgress(w, d);
        WriteSkills(w, d);
        var bonus = d.IsWild ? default : d.TitleAttributeBonus;
        w.WriteI32(bonus.Str);                  // 328 ✅ bônus STR do título equipado
        w.WriteI32(bonus.Agi);                  // 332 ✅ bônus AGI
        w.WriteI32(bonus.Con);                  // 336 ✅ bônus CON
        w.WriteI32(bonus.Int);                  // 340 ✅ bônus INT
    }

    /// <summary>192..215: HP/VP/EVP atuais, ATK/DEF/BR.</summary>
    public static void WriteCombat(PacketWriter w, Digimon d)
    {
        var s = d.Stats;
        w.WriteI32(d.Hp);                       // 192
        w.WriteI32(d.Vp);
        w.WriteI32(d.Evp);
        w.WriteI32(s.Atk);                      // 204
        w.WriteI32(s.Def);
        w.WriteI32(s.Br);
    }

    /// <summary>216..275: EXP, máximos, contadores e pontos.</summary>
    public static void WriteProgress(PacketWriter w, Digimon d)
    {
        var s = d.Stats;
        w.WriteU32(0x8000_0000);                // 216 ? sempre 00 00 00 80
        w.WriteU32(0);                          // 220
        w.WriteI64(d.Exp);                      // 224
        w.WriteI32(s.MaxActionGauge);           // 232
        w.WriteI32(s.MaxHp);                    // 236
        w.WriteI32(s.MaxVp);
        w.WriteI32(s.MaxEvp);
        w.WriteU32(0);                          // 248 ?
        w.WriteU32(100_000);                    // 252 ? sempre 100000
        w.WriteI64(d.ExpToNextLevel == long.MaxValue ? 0 : d.ExpToNextLevel); // 256
        w.WriteI32(d.TotalWins);                // 264
        w.WriteI32(d.TotalBattles);             // 268
        w.WriteU16(d.AbilityPoints);            // 272
        w.WriteU16(d.SkillPoints);              // 274
    }

    /// <summary>
    /// 276..327: skills da forma atual. ✅ Por skill: u32 id, u16 nível exigido (coluna 1 do <c>digimon_skill_us</c>),
    /// i16 nível (0 = ainda não aprendida). Em 296, ✅ quanto a barra de ação enche por passo.
    /// </summary>
    public static void WriteSkills(PacketWriter w, Digimon d)
    {
        for (int slot = 0; slot < DigimonForm.SkillSlots; slot++)
        {
            ushort id = d.Current.SkillId(slot);
            w.WriteU32(id);                                         // 276 / 284
            w.WriteU16(d.SkillInfoOf(slot)?.RequiredLevel ?? 0);    // 280 / 288
            w.WriteI16((short)(id == 0 ? 0 : d.Current.SkillLevels[slot])); // 282 / 290
        }
        w.WriteU32(0);                          // 292
        w.WriteU32((uint)Game.Battle.BattleFormulas.StatusGaugeSpeed(d, d.IsWild)); // 296 ✅ barra por passo (off296)
        w.WriteU8(d.IsWild ? (d.WildGrade > 0 ? d.WildGrade : Game.Stats.WildFormulas.GradeBeginner) : (byte)0); // 300 ✅ 0 jogador, 2 Beginner, 3 Veteran
        w.WriteZeros(11);
        w.WriteU8(8);                           // 312 ? (7 ou 8 nas capturas)
        w.WriteZeros(7);                        // 313..319
        w.WriteU32(d.IsWild ? 0u : d.TitleId);  // 320 ✅ título equipado do digimon (conn=76/112/141)
        w.WriteU32(!d.IsWild && d.TitleId != 0 ? 1u : 0u); // 324 ✅ 1 quando há título equipado
    }
}