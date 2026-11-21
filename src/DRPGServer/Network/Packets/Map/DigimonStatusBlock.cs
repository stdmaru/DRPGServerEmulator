using DRPGServer.Game.Entities;
using DRPGServer.Network.Protocol;

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

    /// <summary>Bloco vazio (posição sem digimon na batalha).</summary>
    public static void WriteEmpty(PacketWriter w) => w.WriteZeros(Length);

    /// <summary>0..63: identidade, nível e atributos.</summary>
    public static void WriteHead(PacketWriter w, Digimon d)
    {
        w.WriteU32(d.Uid);                      // 0
        d.Serial.Write(w);                      // 4
        w.WriteU16(d.SpeciesId);                // 20
        w.WriteU16(0);                          // 22 ? sempre 0
        w.WriteU8(1);                           // 24 ? sempre 1
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
        // e o u16 em 162 (❓ 1, 2, 0x80, 0x200 nas capturas; varia por espécie)
        w.WriteSparse(128, (96, [d.Species.Stage, d.Species.Attribute, 0x01, 0x00]));
        WriteCombat(w, d);
        WriteProgress(w, d);
        WriteSkills(w, d);
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

    /// <summary>276..327: skills (só a primeira é conhecida) e bytes capturados.</summary>
    public static void WriteSkills(PacketWriter w, Digimon d)
    {
        w.WriteU32(12);                         // 276 skill 1 (id 12 nas capturas)
        w.WriteU16(6);                          // 280 ?
        w.WriteI16(1);                          // 282 nível da skill 1 ❓
        w.WriteU32(0);                          // 284 skill 2
        w.WriteU16(0);                          // 288 ?
        w.WriteI16(0);                          // 290 nível da skill 2
        w.WriteU32(0);                          // 292
        w.WriteU32(0x2A);                       // 296 ? (0x2A, 0x4C, 0x69 nas capturas)
        w.WriteU8(d.IsWild ? (byte)2 : (byte)0); // 300 ✅ 0 no digimon do jogador, 2 no selvagem
        w.WriteZeros(11);
        w.WriteU8(8);                           // 312 ? (7 ou 8 nas capturas)
        w.WriteZeros(15);                       // até 327
    }
}
