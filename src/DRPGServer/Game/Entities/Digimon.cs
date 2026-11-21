using DRPGServer.Data.GameData;
using DRPGServer.Game.Stats;

namespace DRPGServer.Game.Entities;

/// <summary>Um digimon (do jogador ou selvagem). Estado de batalha fica em Battle.Combatant, não aqui.</summary>
public sealed class Digimon
{
    private readonly GameData _data;

    public Digimon(GameData data, DigimonSpecies species, ushort level, Attributes? fixedAttributes = null)
    {
        _data = data;
        Species = species;
        Name = species.Name;
        Level = Math.Max((ushort)1, level);
        FixedAttributes = fixedAttributes;
        RestoreAll();
    }

    /// <summary>Chave no banco (0 = selvagem).</summary>
    public uint Uid { get; init; }
    public Serial Serial { get; } = Serial.New();
    public DigimonSpecies Species { get; }
    public ushort SpeciesId => Species.Id;
    public string Name { get; set; }
    public ushort Level { get; private set; }
    public long Exp { get; private set; }

    /// <summary>Pontos que o jogador distribuiu (persistidos).</summary>
    public Attributes Allocated { get; private set; }

    /// <summary>Atributos fixos (digimon selvagem com valores na tabela de spawn).</summary>
    public Attributes? FixedAttributes { get; }

    public ushort AbilityPoints { get; private set; }
    public ushort SkillPoints { get; private set; }
    public int TotalWins { get; set; }
    public int TotalBattles { get; set; }

    public int Hp { get; private set; }
    public int Vp { get; private set; }
    public int Evp { get; private set; }

    public Attributes Attributes =>
        (FixedAttributes is { } f && (f.Str | f.Agi | f.Con | f.Int) != 0 ? f : StatFormulas.BaseAttributes(Species, Level)) + Allocated;

    public CombatStats Stats => StatFormulas.Combat(Attributes, Level, Species.ActionGauge);
    public long ExpToNextLevel => _data.DigimonExpToNext(Level);
    public bool IsKnockedOut => Hp <= 0;

    /// <summary>Restaura o estado salvo no banco.</summary>
    public void Load(long exp, Attributes allocated, ushort abilityPoints, ushort skillPoints, int wins, int battles)
    {
        Exp = Math.Max(0, exp);
        Allocated = allocated;
        AbilityPoints = abilityPoints;
        SkillPoints = skillPoints;
        TotalWins = wins;
        TotalBattles = battles;
        RestoreAll();
    }

    public void RestoreAll()
    {
        var s = Stats;
        Hp = s.MaxHp;
        Vp = s.MaxVp;
        Evp = s.MaxEvp;
    }

    public void Heal(int hp, int vp, int evp)
    {
        var s = Stats;
        Hp = Math.Clamp(Hp + hp, 0, s.MaxHp);
        Vp = Math.Clamp(Vp + vp, 0, s.MaxVp);
        Evp = Math.Clamp(Evp + evp, 0, s.MaxEvp);
    }

    public void TakeDamage(int damage) => Hp = Math.Max(0, Hp - Math.Max(0, damage));

    /// <summary>Soma EXP e sobe quantos níveis couberem. Devolve quantos níveis subiu.</summary>
    public int GainExp(long amount, int maxLevel)
    {
        if (amount <= 0) return 0;
        Exp += amount;
        int gained = 0;
        while (Level < maxLevel && Exp >= ExpToNextLevel)
        {
            Exp -= ExpToNextLevel;
            LevelUp();
            gained++;
        }
        if (Level >= maxLevel) Exp = Math.Min(Exp, ExpToNextLevel);
        return gained;
    }

    public void LevelUp()
    {
        Level++;
        AbilityPoints += 2;
        SkillPoints += 1;
        RestoreAll();
    }

    public bool AllocatePoint(StatKind stat)
    {
        if (AbilityPoints == 0) return false;
        Allocated = stat switch
        {
            StatKind.Str => Allocated with { Str = Allocated.Str + 1 },
            StatKind.Agi => Allocated with { Agi = Allocated.Agi + 1 },
            StatKind.Con => Allocated with { Con = Allocated.Con + 1 },
            StatKind.Int => Allocated with { Int = Allocated.Int + 1 },
            _ => throw new ArgumentOutOfRangeException(nameof(stat)),
        };
        AbilityPoints--;
        return true;
    }
}
