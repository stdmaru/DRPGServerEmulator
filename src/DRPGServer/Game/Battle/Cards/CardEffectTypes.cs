using DRPGServer.Data.GameData;

namespace DRPGServer.Game.Battle.Cards;

/// <summary>Atributo de Digimon usado nos cards de troca de atributo (✅ 1 Vaccine, 2 Data, 3 Virus).</summary>
public enum DigimonAttribute : byte
{
    Vaccine = 1,
    Data = 2,
    Virus = 3,
}

/// <summary>
/// Tipos de efeito de card: o nome do <c>Type</c> no XML liga a uma fábrica que lê os parâmetros declarados.
/// Efeito novo = uma classe e uma linha aqui.
/// </summary>
public static class CardEffectTypes
{
    private static readonly Dictionary<string, Func<EffectParams, ICardEffect>> Factories = new()
    {
        ["Damage"] = p => new DamageEffect(p.PositiveInt("Percent")),
        ["SelfHpCost"] = p => new SelfHpCostEffect(p.PositiveInt("Percent")),
        ["AllTargets"] = _ => new AllTargetsEffect(),
        ["GuaranteedHit"] = _ => new GuaranteedHitEffect(),
        ["HealHp"] = p => HealAmountOrPercent(p, (amount, percent) => new HealHpEffect(amount, percent)),
        ["HealVp"] = p => HealAmountOrPercent(p, (amount, percent) => new HealVpEffect(amount, percent)),
        ["HealEvp"] = p => new HealEvpEffect(p.PositiveInt("Percent")),
        ["PartyHealHp"] = p => HealAmountOrPercent(p, (amount, percent) => new PartyHealHpEffect(amount, percent)),
        ["PartyHealVp"] = p => new PartyHealVpEffect(p.PositiveInt("Percent")),
        ["PartyHealEvp"] = p => new PartyHealEvpEffect(p.PositiveInt("Percent")),
        ["AttackerAttribute"] = p => new AttackerAttributeEffect(p.Enum<DigimonAttribute>("Attribute")),
        ["TargetAttribute"] = p => new TargetAttributeEffect(p.Enum<DigimonAttribute>("Attribute")),
        ["Br"] = p => new BrEffect(p.PositiveInt("Percent")),
        ["KeepGauge"] = p => new KeepGaugeEffect(Percent(p)),
        ["Haste"] = _ => new HasteEffect(),
        ["EvolveNext"] = _ => new EvolveNextEffect(),
        ["EvolveMax"] = _ => new EvolveMaxEffect(),
        ["Lifesteal"] = p => new LifestealEffect(Percent(p)),
        ["HalveEnemyHp"] = _ => new HalveEnemyHpEffect(),
        ["LeaveOneHp"] = _ => new LeaveOneHpEffect(),
        ["CurrentHpDamage"] = p => new CurrentHpDamageEffect(Percent(p)),
        ["SlotReturn"] = _ => new SlotReturnEffect(),
        ["Defense"] = p => new DefenseEffect(p.PositiveInt("Percent")),
        ["DamageReduction"] = p => new DamageReductionEffect(Percent(p)),
        ["Reflect"] = _ => new ReflectEffect(),
    };

    public static ICardEffect Create(EffectParams p)
    {
        if (!Factories.TryGetValue(p.Type, out var factory))
            throw p.Fail("desconhecido");
        var effect = factory(p);
        p.EnsureAllRead();
        return effect;
    }

    private static int Percent(EffectParams p)
    {
        int percent = p.PositiveInt("Percent");
        if (percent > 100) throw p.Fail($"Percent={percent} acima de 100");
        return percent;
    }

    private static ICardEffect HealAmountOrPercent(EffectParams p, Func<int, int, ICardEffect> create)
    {
        p.RequireOneOf("Amount", "Percent");
        return create(p.OptionalPositiveInt("Amount") ?? 0, p.OptionalPositiveInt("Percent") ?? 0);
    }
}

// ---- Ataque: dano e alvo ----

/// <summary>+N% de dano.</summary>
public sealed record DamageEffect(int Percent) : ICardAttackEffect
{
    public void Apply(CardAttackContext ctx) => ctx.DamageBonusPercent += Percent;
}

/// <summary>Custa N% do HP do atacante (o maior entre os cards do turno).</summary>
public sealed record SelfHpCostEffect(int Percent) : ICardAttackEffect
{
    public void Apply(CardAttackContext ctx) => ctx.SelfHpCostPercent = Math.Max(ctx.SelfHpCostPercent, Percent);
}

/// <summary>Ataca todos os inimigos.</summary>
public sealed record AllTargetsEffect : ICardAttackEffect
{
    public void Apply(CardAttackContext ctx) => ctx.ForceAllTargets = true;
}

/// <summary>O ataque sempre acerta.</summary>
public sealed record GuaranteedHitEffect : ICardAttackEffect
{
    public void Apply(CardAttackContext ctx) => ctx.GuaranteedHit = true;
}

/// <summary>Atacante passa a ter o atributo dado neste ataque.</summary>
public sealed record AttackerAttributeEffect(DigimonAttribute Attribute) : ICardAttackEffect
{
    public void Apply(CardAttackContext ctx) => ctx.OverrideAttackerAttribute = (byte)Attribute;
}

/// <summary>Alvo passa a ter o atributo dado neste ataque.</summary>
public sealed record TargetAttributeEffect(DigimonAttribute Attribute) : ICardAttackEffect
{
    public void Apply(CardAttackContext ctx) => ctx.OverrideTargetAttribute = (byte)Attribute;
}

/// <summary>Chance de levar o HP do inimigo à metade.</summary>
public sealed record HalveEnemyHpEffect : ICardAttackEffect
{
    public void Apply(CardAttackContext ctx) => ctx.HalveEnemyHpChance = true;
}

/// <summary>O golpe não derruba: deixa o alvo com 1 HP.</summary>
public sealed record LeaveOneHpEffect : ICardAttackEffect
{
    public void Apply(CardAttackContext ctx) => ctx.LeaveOneHp = true;
}

/// <summary>Dano extra de N% do HP atual do alvo.</summary>
public sealed record CurrentHpDamageEffect(int Percent) : ICardAttackEffect
{
    public void Apply(CardAttackContext ctx) => ctx.BonusCurrentHpDamageFraction += Percent / 100.0;
}

/// <summary>Recupera N% do dano causado como HP (o maior entre os cards do turno).</summary>
public sealed record LifestealEffect(int Percent) : ICardAttackEffect
{
    public void Apply(CardAttackContext ctx) => ctx.LifestealFraction = Math.Max(ctx.LifestealFraction, Percent / 100.0);
}

// ---- Ataque: cura ----

/// <summary>Cura HP do atacante: <c>Amount</c> plano ou <c>Percent</c> do máximo.</summary>
public sealed record HealHpEffect(int Amount, int Percent) : ICardAttackEffect
{
    public void Apply(CardAttackContext ctx)
    {
        ctx.HealHpFlat += Amount;
        ctx.HealHpPercent += Percent;
    }
}

/// <summary>Cura VP do atacante: <c>Amount</c> plano ou <c>Percent</c> do máximo.</summary>
public sealed record HealVpEffect(int Amount, int Percent) : ICardAttackEffect
{
    public void Apply(CardAttackContext ctx)
    {
        ctx.HealVpFlat += Amount;
        ctx.HealVpPercent += Percent;
    }
}

/// <summary>Recupera N% do EVP do atacante.</summary>
public sealed record HealEvpEffect(int Percent) : ICardAttackEffect
{
    public void Apply(CardAttackContext ctx) => ctx.HealEvpPercent += Percent;
}

/// <summary>Cura HP do grupo: <c>Amount</c> plano ou <c>Percent</c> do máximo.</summary>
public sealed record PartyHealHpEffect(int Amount, int Percent) : ICardAttackEffect
{
    public void Apply(CardAttackContext ctx)
    {
        ctx.PartyHealHpFlat += Amount;
        ctx.PartyHealHpPercent += Percent;
    }
}

/// <summary>Recupera N% do VP do grupo.</summary>
public sealed record PartyHealVpEffect(int Percent) : ICardAttackEffect
{
    public void Apply(CardAttackContext ctx) => ctx.PartyHealVpPercent += Percent;
}

/// <summary>Recupera N% do EVP do grupo.</summary>
public sealed record PartyHealEvpEffect(int Percent) : ICardAttackEffect
{
    public void Apply(CardAttackContext ctx) => ctx.PartyHealEvpPercent += Percent;
}

// ---- Ataque: barra de ação e evolução ----

/// <summary>Mantém N% da barra de ação depois do ataque (o maior entre os cards do turno).</summary>
public sealed record KeepGaugeEffect(int Percent) : ICardAttackEffect
{
    public void Apply(CardAttackContext ctx) => ctx.KeepGaugeFraction = Math.Max(ctx.KeepGaugeFraction, Percent / 100.0);
}

/// <summary>Haste: mantém a barra cheia, mas não ativa em ataques seguidos (<see cref="CardBattleState"/>).</summary>
public sealed record HasteEffect : ICardAttackEffect
{
    public void Apply(CardAttackContext ctx) => ctx.KeepGaugeFraction = 1.0;
}

/// <summary>Evolui para a próxima forma liberada.</summary>
public sealed record EvolveNextEffect : ICardAttackEffect
{
    public void Apply(CardAttackContext ctx) => ctx.EvolveNext = true;
}

/// <summary>Evolui para a maior forma liberada.</summary>
public sealed record EvolveMaxEffect : ICardAttackEffect
{
    public void Apply(CardAttackContext ctx) => ctx.EvolveMax = true;
}

/// <summary>Slot Return: os cards dos slots seguintes ativam sem ser consumidos (<see cref="CardBattleState"/>).</summary>
public sealed record SlotReturnEffect : ICardAttackEffect
{
    public void Apply(CardAttackContext ctx) { }
}

// ---- Ataque e defesa ----

/// <summary>+N% de BR, no ataque ou na defesa (vale o gatilho do card).</summary>
public sealed record BrEffect(int Percent) : ICardAttackEffect, ICardDefenseEffect
{
    public void Apply(CardAttackContext ctx) => ctx.BrBonusPercent += Percent;
    public void Apply(CardDefenseContext ctx) => ctx.BrBonusPercent += Percent;
}

// ---- Defesa ----

/// <summary>+N% de DEF.</summary>
public sealed record DefenseEffect(int Percent) : ICardDefenseEffect
{
    public void Apply(CardDefenseContext ctx) => ctx.DefenseBonusPercent += Percent;
}

/// <summary>-N% do dano recebido.</summary>
public sealed record DamageReductionEffect(int Percent) : ICardDefenseEffect
{
    public void Apply(CardDefenseContext ctx) => ctx.DamageReductionPercent += Percent;
}

/// <summary>Devolve o ataque ao atacante.</summary>
public sealed record ReflectEffect : ICardDefenseEffect
{
    public void Apply(CardDefenseContext ctx) => ctx.ReflectAttack = true;
}
