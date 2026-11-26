namespace DRPG.Server.Game.Battle.Cards;

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

/// <summary>Efeito tipado de card (<c>Resources/Card/CardEffects.xml</c>). Registrado pelo tipo em <see cref="CardEffectTypes"/>.</summary>
public interface ICardEffect;

/// <summary>Efeito que vale quando o card ativa no ataque (<see cref="Data.GameData.CardTrigger.Attack"/>).</summary>
public interface ICardAttackEffect : ICardEffect
{
    void Apply(CardAttackContext ctx);
}

/// <summary>Efeito que vale quando o card ativa na defesa (<see cref="Data.GameData.CardTrigger.Defense"/>).</summary>
public interface ICardDefenseEffect : ICardEffect
{
    void Apply(CardDefenseContext ctx);
}

/// <summary>Junta os efeitos dos cards ativados num turno.</summary>
public static class CardEffectEvaluator
{
    public static CardAttackContext EvaluateAttack(ActivatedCards activated)
    {
        var ctx = new CardAttackContext();
        foreach (var effect in activated.Effects().OfType<ICardAttackEffect>())
            effect.Apply(ctx);
        return ctx;
    }

    public static CardDefenseContext EvaluateDefense(ActivatedCards activated)
    {
        var ctx = new CardDefenseContext();
        foreach (var effect in activated.Effects().OfType<ICardDefenseEffect>())
            effect.Apply(ctx);
        return ctx;
    }
}
