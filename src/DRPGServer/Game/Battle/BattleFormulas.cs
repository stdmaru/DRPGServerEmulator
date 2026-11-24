using DRPGServer.Game.Entities;

namespace DRPGServer.Game.Battle;

/// <summary>Resultado de um golpe.</summary>
public readonly record struct Hit(bool Landed, int Damage);

/// <summary>
/// Fórmulas de batalha ajustadas nas 67 batalhas v322 (sessões 53–58). Detalhes e resíduos em
/// docs/mecanicas/batalha.md.
/// </summary>
public static class BattleFormulas
{
    /// <summary>✅ Barra de ação cheia (500 em todas as amostras, igual ao MaxActionGauge do bloco de status).</summary>
    public const int MaxGauge = 500;

    /// <summary>❓ Chance de acerto: 313 golpes do jogador com 32 erros (10 %); dos selvagens, 22/25 sem o Otamamon L50.</summary>
    public const double HitChance = 0.90;

    /// <summary>
    /// ✅ Ataque básico (skill 0 no fim do 0x67): ⌊ATK² / (10·DEF) · mult⌋, ATK de quem ataca e DEF de quem apanha,
    /// com multiplicador de afinidade/atributo (0,90…1,05) e modificadores opcionais de Card Slash.
    /// </summary>
    public static Hit Attack(
        Digimon attacker, Digimon target, Random random,
        double damageMultiplier = 1.0, double defenseMultiplier = 1.0,
        byte? attackerAttrOverride = null, byte? targetAttrOverride = null)
    {
        if (random.NextDouble() >= HitChance) return new Hit(false, 0);
        byte atkAttr = attackerAttrOverride ?? attacker.Species.Attribute;
        byte tgtAttr = targetAttrOverride ?? target.Species.Attribute;
        double mult = AffinityMultiplier(atkAttr, attacker.Species.Element, tgtAttr, target.Species.Element);
        int effectiveDef = Math.Max(1, (int)Math.Floor(target.Stats.Def * defenseMultiplier));
        int damage = damageMultiplier <= 0.0
            ? 0
            : Math.Max(1, (int)Math.Floor(BaseDamage(attacker.Stats.Atk, effectiveDef) * mult * damageMultiplier));
        return new Hit(true, damage);
    }

    /// <summary>
    /// Skill: o mesmo ataque com multiplicador de skill, afinidade e modificadores opcionais de Card Slash.
    /// </summary>
    public static Hit Skill(
        Digimon attacker, Digimon target, Data.GameData.SkillInfo skill, int skillLevel, Random random,
        double damageMultiplier = 1.0, double defenseMultiplier = 1.0,
        byte? attackerAttrOverride = null, byte? targetAttrOverride = null)
    {
        if (random.NextDouble() >= HitChance) return new Hit(false, 0);
        byte atkAttr = attackerAttrOverride ?? attacker.Species.Attribute;
        byte tgtAttr = targetAttrOverride ?? target.Species.Attribute;
        double mult = SkillMultiplier(skill, skillLevel) * AffinityMultiplier(atkAttr, attacker.Species.Element, tgtAttr, target.Species.Element);
        int effectiveDef = Math.Max(1, (int)Math.Floor(target.Stats.Def * defenseMultiplier));
        int damage = damageMultiplier <= 0.0
            ? 0
            : Math.Max(1, (int)Math.Floor(BaseDamage(attacker.Stats.Atk, effectiveDef) * mult * damageMultiplier));
        return new Hit(true, damage);
    }

    /// <summary>
    /// Multiplicador de afinidade e atributo entre atacante e alvo.
    /// - Atributos clássicos: Vacina (1), Dados (2), Vírus (3).
    ///   Vacina > Vírus > Dados > Vacina (vantagem ~1.05, desvantagem ~0.90..0.95).
    /// - Afinidade elemental (coluna 22 do digimon_us): Fogo (1) > Natureza (2, 9) > Água (8, 10) > Fogo (1).
    /// </summary>
    public static double AffinityMultiplier(byte attackerAttr, byte attackerElem, byte targetAttr, byte targetElem)
    {
        double mult = 1.0;

        // Triângulo clássico de atributos (1: Vacina, 2: Dados, 3: Vírus)
        if (attackerAttr != 0 && targetAttr != 0)
        {
            if ((attackerAttr == 1 && targetAttr == 3) ||
                (attackerAttr == 3 && targetAttr == 2) ||
                (attackerAttr == 2 && targetAttr == 1))
            {
                mult *= 1.05;
            }
            else if ((attackerAttr == 1 && targetAttr == 2) ||
                     (attackerAttr == 2 && targetAttr == 3) ||
                     (attackerAttr == 3 && targetAttr == 1))
            {
                mult *= (targetElem == 8 ? 0.95 : 0.90);
            }
        }

        // Afinidade elemental (1: Fogo, 2/9: Vento/Natureza/Planta, 8/10: Gelo/Água)
        if (attackerElem != 11 && targetElem != 11 && attackerElem != 0 && targetElem != 0)
        {
            if ((attackerElem == 1 && (targetElem == 2 || targetElem == 9)) ||
                ((attackerElem == 2 || attackerElem == 9) && (targetElem == 8 || targetElem == 10)) ||
                ((attackerElem == 8 || targetElem == 10) && targetElem == 1))
            {
                mult *= 1.05;
            }
            else if (((attackerElem == 2 || attackerElem == 9) && targetElem == 1) ||
                     ((attackerElem == 8 || attackerElem == 10) && (targetElem == 2 || targetElem == 9)) ||
                     (attackerElem == 1 && (targetElem == 8 || targetElem == 10)))
            {
                mult *= 0.90;
            }
        }

        return mult;
    }

    public static double SkillMultiplier(Data.GameData.SkillInfo skill, int skillLevel) =>
        1 + 0.0075 * (skill.RequiredLevel + 4) + 0.01 * (Math.Max(1, skillLevel) - 1);

    public static double BaseDamage(int atk, int def) => (double)atk * atk / (10.0 * Math.Max(1, def));

    /// <summary>
    /// Passo da barra visual do jogador no cliente (`DigimonRPG.bin @ 0x0085E0B8`): a cada ~33,3 ms (60 FPS,
    /// pois o cliente checa `elapsed / 30` e descarta o resto de `timeGetTime()`), soma este inteiro até 500.
    /// </summary>
    public static int PlayerClientStep(int level, int agi) =>
        Math.Max(1, Math.Min(50, (int)((4.0 - 2.5 / Math.Max(1, level)) * 4.0 + Math.Max(0, agi) / 25.0)));

    /// <summary>
    /// Quanto a barra enche por passo de <see cref="Battle.TickInterval"/> (200 ms).
    /// Jogador: ✅ sincronizado com os 6 passos de ~33,3 ms que o cliente executa em 200 ms (`6 × PlayerClientStep`).
    /// Selvagem: ✅ pelo BR, ⌊15,98·BR^0,2 − 30,29⌋ (13 de 16 exatas, 2 a ±1; ❓ o Yuramon L2 com BR 65 enche 8, não 6), BR 85 → 8, 165 → 14, 1040 → 33.
    /// </summary>
    public static int GaugeGain(Digimon digimon, bool wild) =>
        wild ? WildGaugeGain(digimon.Stats.Br) : 6 * PlayerClientStep(digimon.Level, digimon.Attributes.Agi);

    /// <summary>Valor do campo `off296` no bloco de status (fórmula contínua × 200/30, 0 erros nas 26 amostras L1–L160).</summary>
    public static int StatusGaugeSpeed(Digimon digimon, bool wild) =>
        wild ? WildGaugeGain(digimon.Stats.Br) : PlayerGaugeGain(digimon.Level, digimon.Attributes.Agi);

    public static int PlayerGaugeGain(int level, int agi)
    {
        double per30ms = Math.Min(50.0, (4.0 - 2.5 / Math.Max(1, level)) * 4.0 + Math.Max(0, agi) / 25.0);
        return Math.Max(1, (int)(per30ms * (200.0 / 30.0)));
    }

    public static int WildGaugeGain(int br) =>
        Math.Max(1, (int)(15.98 * Math.Pow(Math.Max(1, br), 0.2) - 30.29));
}
