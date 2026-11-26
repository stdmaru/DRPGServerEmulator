namespace DRPG.Server.Game.Stats;

/// <summary>
/// O que um modificador altera. Cada tipo declara como acumula e em que formas existe
/// (<see cref="StatModifierRules"/>). Tipo novo: um valor aqui, a regra e o uso no cálculo.
/// </summary>
public enum StatModifierKind : byte
{
    Str,
    Agi,
    Con,
    Int,
    Hp,
    Vp,
    Atk,
    Def,
    Br,
    /// <summary>Teto de Card Slashes por batalha (vale o maior).</summary>
    CardSlashLimit,
    ExtraCardSlashes,
    DigimonExp,
    TamerExp,
    /// <summary>Recuperação automática no mapa, somada à base de cada tick.</summary>
    HpRecovery,
    VpRecovery,
    EvpRecovery,
    /// <summary>Os Digimons não voltam à forma-base no fim da batalha nem ao desequipar (Aura).</summary>
    MaintainEvolution,
    /// <summary>Parceiros vivos invocados já na abertura da batalha.</summary>
    AutoSummonPartners,
    Damage,
    SingleSkillDamage,
    MultiSkillDamage,
    MeleeSkillDamage,
    RangedSkillDamage,
    /// <summary>HP recuperado a cada golpe que acerta.</summary>
    OnHitHealHp,
    OnHitHealVp,
    /// <summary>Bônus percentual de velocidade de movimento no mapa (ex.: Cycle Costume +200%).</summary>
    MoveSpeed,
}

public enum StatModifierForm : byte
{
    Flat,
    Percent,
}

/// <summary>Como as fontes de um mesmo tipo se juntam.</summary>
public enum StatStacking : byte
{
    Sum,
    Max,
    /// <summary>Basta uma fonte (valor 1).</summary>
    Any,
}

/// <summary>Um bônus de equipamento, título, card ou buff.</summary>
public readonly record struct StatModifier(StatModifierKind Kind, int Value, StatModifierForm Form = StatModifierForm.Flat)
{
    public override string ToString() => Form == StatModifierForm.Percent ? $"{Kind} {Value}%" : $"{Kind} {Value}";
}

public static class StatModifierRules
{
    public static StatStacking Stacking(StatModifierKind kind) => kind switch
    {
        StatModifierKind.CardSlashLimit => StatStacking.Max,
        StatModifierKind.MoveSpeed => StatStacking.Max,
        StatModifierKind.MaintainEvolution => StatStacking.Any,
        _ => StatStacking.Sum,
    };

    /// <summary>Se o tipo existe na forma: ATK, DEF e BR nas duas; EXP de Digimon, MoveSpeed e dano só em %; o resto só plano.</summary>
    public static bool Allows(StatModifierKind kind, StatModifierForm form) => kind switch
    {
        StatModifierKind.Atk or StatModifierKind.Def or StatModifierKind.Br => true,
        StatModifierKind.DigimonExp or StatModifierKind.MoveSpeed or StatModifierKind.Damage
            or StatModifierKind.SingleSkillDamage or StatModifierKind.MultiSkillDamage
            or StatModifierKind.MeleeSkillDamage or StatModifierKind.RangedSkillDamage => form == StatModifierForm.Percent,
        _ => form == StatModifierForm.Flat,
    };
}

/// <summary>
/// Modificadores agregados de uma ou mais fontes, imutável. A junção segue a regra de cada tipo
/// (<see cref="StatModifierRules.Stacking"/>), sem lista de campos escrita à mão.
/// </summary>
public sealed class StatModifiers
{
    private static readonly int KindCount = Enum.GetValues<StatModifierKind>().Length;

    public static readonly StatModifiers None = new(new int[KindCount], new int[KindCount]);

    private readonly int[] _flat;
    private readonly int[] _percent;

    private StatModifiers(int[] flat, int[] percent)
    {
        _flat = flat;
        _percent = percent;
        IsEmpty = !flat.Any(v => v != 0) && !percent.Any(v => v != 0);
    }

    public bool IsEmpty { get; }

    public int Flat(StatModifierKind kind) => _flat[(int)kind];
    public int Percent(StatModifierKind kind) => _percent[(int)kind];
    public bool Has(StatModifierKind kind) => _flat[(int)kind] > 0 || _percent[(int)kind] > 0;

    public Attributes Attributes => new(Flat(StatModifierKind.Str), Flat(StatModifierKind.Agi), Flat(StatModifierKind.Con),
        Flat(StatModifierKind.Int));

    /// <summary>Os modificadores não nulos, na ordem do enum (planos antes dos percentuais de cada tipo).</summary>
    public IEnumerable<StatModifier> Modifiers
    {
        get
        {
            for (int k = 0; k < KindCount; k++)
            {
                if (_flat[k] != 0) yield return new((StatModifierKind)k, _flat[k]);
                if (_percent[k] != 0) yield return new((StatModifierKind)k, _percent[k], StatModifierForm.Percent);
            }
        }
    }

    /// <summary>Junta modificadores soltos (de uma fonte). Forma que o tipo não aceita é erro.</summary>
    public static StatModifiers Of(IEnumerable<StatModifier> modifiers)
    {
        int[] flat = new int[KindCount], percent = new int[KindCount];
        foreach (var m in modifiers)
        {
            if (!StatModifierRules.Allows(m.Kind, m.Form))
                throw new ArgumentException($"{m.Kind} não existe como {m.Form}", nameof(modifiers));
            var target = m.Form == StatModifierForm.Percent ? percent : flat;
            target[(int)m.Kind] = Stack(m.Kind, target[(int)m.Kind], m.Value);
        }
        return Create(flat, percent);
    }

    /// <summary>Junta fontes já agregadas (equipamentos do tamer, título, buffs).</summary>
    public static StatModifiers Combine(IEnumerable<StatModifiers> sources)
    {
        int[] flat = new int[KindCount], percent = new int[KindCount];
        foreach (var s in sources)
        {
            if (s.IsEmpty) continue;
            for (int k = 0; k < KindCount; k++)
            {
                flat[k] = Stack((StatModifierKind)k, flat[k], s._flat[k]);
                percent[k] = Stack((StatModifierKind)k, percent[k], s._percent[k]);
            }
        }
        return Create(flat, percent);
    }

    public static StatModifiers Combine(params StatModifiers[] sources) => Combine((IEnumerable<StatModifiers>)sources);

    private static int Stack(StatModifierKind kind, int current, int value) => StatModifierRules.Stacking(kind) switch
    {
        StatStacking.Max => Math.Max(current, value),
        StatStacking.Any => current > 0 || value > 0 ? 1 : 0,
        _ => current + value,
    };

    private static StatModifiers Create(int[] flat, int[] percent)
    {
        var result = new StatModifiers(flat, percent);
        return result.IsEmpty ? None : result;
    }

    public override string ToString() => IsEmpty ? "(nenhum)" : string.Join(", ", Modifiers);
}
