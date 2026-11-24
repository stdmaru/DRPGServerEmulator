namespace DRPGServer.Game.Battle.Cards;

/// <summary>Modificadores acumulados pelos cards de Ataque (col 23 = 1) ativados no turno.</summary>
public sealed class CardAttackContext
{
    public double DamageBonusPercent { get; set; }
    public double BrBonusPercent { get; set; }
    public bool ForceAllTargets { get; set; }
    public bool GuaranteedHit { get; set; }
    public byte? OverrideAttackerAttribute { get; set; }
    public byte? OverrideTargetAttribute { get; set; }
    public int HealHpFlat { get; set; }
    public int HealVpFlat { get; set; }
    public double HealHpPercent { get; set; }
    public double HealVpPercent { get; set; }
    public double HealEvpPercent { get; set; }
    public int PartyHealHpFlat { get; set; }
    public double PartyHealHpPercent { get; set; }
    public double PartyHealVpPercent { get; set; }
    public double PartyHealEvpPercent { get; set; }
    public double SelfHpCostPercent { get; set; }
    public double LifestealFraction { get; set; }
    public bool HalveEnemyHpChance { get; set; }
    public double BonusCurrentHpDamageFraction { get; set; }
    public bool LeaveOneHp { get; set; }
    public double KeepGaugeFraction { get; set; }
    public bool EvolveNext { get; set; }
    public bool EvolveMax { get; set; }

    public double DamageMultiplier => Math.Max(0.0, 1.0 + DamageBonusPercent / 100.0);
    public double BrMultiplier => Math.Max(0.1, 1.0 + BrBonusPercent / 100.0);
}

/// <summary>Modificadores acumulados pelos cards de Defesa (col 23 = 2) ativados quando o jogador é atacado.</summary>
public sealed class CardDefenseContext
{
    public double DefenseBonusPercent { get; set; }
    public double DamageReductionPercent { get; set; }
    public double BrBonusPercent { get; set; }
    public bool ReflectAttack { get; set; }

    public double DefenseMultiplier => Math.Max(0.1, 1.0 + DefenseBonusPercent / 100.0);
    public double IncomingDamageMultiplier => Math.Max(0.0, 1.0 - Math.Min(100.0, DamageReductionPercent) / 100.0);
    public double BrMultiplier => Math.Max(0.1, 1.0 + BrBonusPercent / 100.0);
}

/// <summary>
/// Registro extensível de efeitos dos cards padrão de <c>card_info_us.dat</c> (PvE/loja/tutorial).
/// Cards especiais de evento/PvP (<c>SubType = 3</c>) sem efeito mapeado aqui ainda ativam e consomem
/// normalmente sem quebrar o turno, podendo ser adicionados incrementalmente neste registro.
/// </summary>
public static class CardEffectRegistry
{
    public const uint SlotReturnCardId = 41;

    private const byte AttrVaccine = 1;
    private const byte AttrData = 2;
    private const byte AttrVirus = 3;

    public static bool IsHasteCard(uint cardId) => cardId is 81 or 119 or 10002;

    public static CardAttackContext EvaluateAttack(ActivatedCards activated)
    {
        var ctx = new CardAttackContext();
        foreach (uint cardId in activated.CardIds())
            ApplyAttackCard(cardId, ctx);
        return ctx;
    }

    public static CardDefenseContext EvaluateDefense(ActivatedCards activated)
    {
        var ctx = new CardDefenseContext();
        foreach (uint cardId in activated.CardIds())
            ApplyDefenseCard(cardId, ctx);
        return ctx;
    }

    private static void ApplyAttackCard(uint cardId, CardAttackContext ctx)
    {
        switch (cardId)
        {
            // ---- Modificadores de dano (+X% Damage) ----
            case 3 or 128:                              // Offense Plug-In A / (E)
                ctx.DamageBonusPercent += 20;
                break;
            case 12:                                    // Iron Claw
                ctx.DamageBonusPercent += 30;
                break;
            case 16:                                    // Small Spear
                ctx.DamageBonusPercent += 40;
                break;
            case 7 or 39 or 50 or 68:                   // Boost Chip, Spiked Club, Power Charger, Tutorial Card
                ctx.DamageBonusPercent += 50;
                break;
            case 4 or 43:                               // Offense Plug-In N, Offense Plug-In K
                ctx.DamageBonusPercent += 60;
                break;
            case 71:                                    // Bao Chui X
                ctx.DamageBonusPercent += 70;
                break;
            case 44 or 54 or 72 or 98:                  // Offense Plug-In V, Thor Hammer X, Taegeukgi, Thor Hammer
                ctx.DamageBonusPercent += 80;
                break;
            case 18 or 55 or 135:                       // Berserk Sword, Battle Tomahawk, Berserk Sword X
                ctx.DamageBonusPercent += 100;
                break;
            case 60 or 66 or 79 or 121:                 // Shishioumaru, Excalibur, CSAT Fork, Excalibur X
                ctx.DamageBonusPercent += 120;
                break;

            // ---- Sacrifício de HP + dano ----
            case 31 or 134 or 136:                      // Data Explosion / V2 / V2 X: -50% HP, +110% dano
                ctx.SelfHpCostPercent = Math.Max(ctx.SelfHpCostPercent, 50);
                ctx.DamageBonusPercent += 110;
                break;
            case 302:                                   // Data Corruption: -5% HP
                ctx.SelfHpCostPercent = Math.Max(ctx.SelfHpCostPercent, 5);
                break;

            // ---- AoE (ataca todos os inimigos com 100% de dano) ----
            case 42 or 58 or 59:                        // The Wicked God's Advent, Obelisk, Vajra
                ctx.ForceAllTargets = true;
                break;
            case 64 or 73 or 74 or 82 or 83 or 129:
                // Golden Drill, Steel Drill, Golden Drill X (✅ 77/77 acertos em captures.db), Golden Drill D, Fenrir Sword / (E)
                ctx.ForceAllTargets = true;
                ctx.GuaranteedHit = true;
                break;
            case 124:                                   // Fenrir Sword S (AoE + 30% dano + acerto garantido)
                ctx.ForceAllTargets = true;
                ctx.GuaranteedHit = true;
                ctx.DamageBonusPercent += 30;
                break;
            case 125:                                   // Fenrir Sword SS (AoE + 50% dano + acerto garantido)
                ctx.ForceAllTargets = true;
                ctx.GuaranteedHit = true;
                ctx.DamageBonusPercent += 50;
                break;

            // ---- Recuperação individual (HP / VP / EVP) ----
            case 19: ctx.HealHpFlat += 50; break;       // Recovery Floppy L
            case 20: ctx.HealHpFlat += 100; break;      // Recovery Floppy M
            case 21: ctx.HealHpFlat += 150; break;      // Recovery Floppy S
            case 22: ctx.HealHpFlat += 200; break;      // Recovery Floppy D
            case 89: ctx.HealHpFlat += 300; break;      // Recovery Floppy R
            case 27: ctx.HealVpFlat += 50; break;       // Vitality Floppy L
            case 28: ctx.HealVpFlat += 100; break;      // Vitality Floppy M
            case 29: ctx.HealVpFlat += 150; break;      // Vitality Floppy S
            case 30: ctx.HealVpFlat += 200; break;      // Vitality Floppy D
            case 69:                                    // Restoration Program M
                ctx.HealHpFlat += 100;
                ctx.HealVpFlat += 30;
                break;
            case 70:                                    // Restoration Program D
                ctx.HealHpFlat += 200;
                ctx.HealVpFlat += 100;
                break;
            case 51:                                    // Max Charger
                ctx.HealEvpPercent += 100;
                break;
            case 65 or 80 or 120:                       // Accelerator / CSAT Mirror / Accelerator X
                ctx.HealHpPercent += 100;
                ctx.HealVpPercent += 100;
                ctx.HealEvpPercent += 100;
                break;

            // ---- Recuperação em grupo ----
            case 23: ctx.PartyHealHpFlat += 50; break;  // Recovery Floppy LX
            case 24: ctx.PartyHealHpFlat += 100; break; // Recovery Floppy MX
            case 25: ctx.PartyHealHpFlat += 150; break; // Recovery Floppy SX
            case 26: ctx.PartyHealHpFlat += 200; break; // Recovery Floppy DX
            case 34: ctx.PartyHealEvpPercent += 30; break; // Golden Banana
            case 126:                                   // Ulforce Saber
                ctx.PartyHealHpPercent += 20;
                ctx.PartyHealVpPercent += 20;
                break;

            // ---- Mudança de atributo ----
            case 8: ctx.OverrideAttackerAttribute = AttrData; break;     // Supreme Training Manual
            case 9: ctx.OverrideAttackerAttribute = AttrVaccine; break;  // Light Training Manual
            case 10: ctx.OverrideAttackerAttribute = AttrVirus; break;   // Dark Training Manual
            case 13: ctx.OverrideTargetAttribute = AttrVirus; break;     // Evil Spiral

            // ---- BR / Velocidade / Barra de Ação (TP) ----
            case 2: ctx.BrBonusPercent += 20; break;    // High Speed Plug-In B
            case 47: ctx.BrBonusPercent += 60; break;   // High Speed Plug-In H
            case 48: ctx.BrBonusPercent += 80; break;   // High Speed Plug-In T
            case 11 or 57: ctx.BrBonusPercent += 100; break; // Speed Sevens, The Bone That Calls Death
            case 52:                                    // Jumper ROM (consome só 50% do TP)
                ctx.KeepGaugeFraction = Math.Max(ctx.KeepGaugeFraction, 0.5);
                break;
            case 81 or 119 or 10002:                    // Haste / Haste X / Haste (for Ladder)
                ctx.KeepGaugeFraction = 1.0;
                break;

            // ---- Utilitários de combate ----
            case 6: ctx.EvolveNext = true; break;       // Evolution Plug-in E
            case 62: ctx.EvolveMax = true; break;       // Evolution Plug-In E2
            case 33: ctx.LifestealFraction = Math.Max(ctx.LifestealFraction, 0.5); break; // Miracle Ruby
            case 37: ctx.HalveEnemyHpChance = true; break; // Digimon Graveyard
            case 77: ctx.LeaveOneHp = true; break;      // Power Control
            case 95: ctx.BonusCurrentHpDamageFraction += 0.10; break; // Darkness Zone
        }
    }

    private static void ApplyDefenseCard(uint cardId, CardDefenseContext ctx)
    {
        switch (cardId)
        {
            // ---- Bônus de Defesa (+X% DEF) ----
            case 5: ctx.DefenseBonusPercent += 20; break;  // Defense Plug-In C
            case 45: ctx.DefenseBonusPercent += 40; break; // Defense Plug-In F
            case 46: ctx.DefenseBonusPercent += 60; break; // Defense Plug-In G

            // ---- Redução direta de dano (-X% Damage) ----
            case 17: ctx.DamageReductionPercent += 20; break;              // Brave Shield
            case 10000: ctx.DamageReductionPercent += 35; break;           // Silver Robe
            case 15 or 10003: ctx.DamageReductionPercent += 50; break;     // Holy Orb / (For Ladder)
            case 56 or 114: ctx.DamageReductionPercent += 60; break;       // Aegis, Death-X Evolution
            case 67 or 122: ctx.DamageReductionPercent += 80; break;       // Holy Sevens / X
            case 63 or 78 or 84: ctx.DamageReductionPercent += 100; break; // White Wings / CSAT Toilet Paper / (for Providing)

            // ---- Bônus de BR na defesa ----
            case 1: ctx.BrBonusPercent += 20; break;    // Strength Plug-In P
            case 49: ctx.BrBonusPercent += 80; break;   // Strength Plug-In W
            case 53: ctx.BrBonusPercent += 100; break;  // Strength Plug-In O

            // ---- Reflexão de ataque ----
            case 38: ctx.ReflectAttack = true; break;   // Reverse Sevens
        }
    }
}
