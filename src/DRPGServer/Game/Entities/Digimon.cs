using DRPGServer.Data.GameData;
using DRPGServer.Game.Stats;

namespace DRPGServer.Game.Entities;

/// <summary>
/// Uma forma do digimon (espécie da linha evolutiva). ✅ Cada forma tem atributos distribuídos e skills próprios;
/// nível, EXP e pontos de skill são do digimon (docs/mecanicas/evolucao.md).
/// </summary>
public sealed class DigimonForm
{
    public const int SkillSlots = 2;

    public DigimonForm(DigimonSpecies species) => Species = species;

    public DigimonSpecies Species { get; }
    public Attributes Allocated { get; set; }
    /// <summary>Nível de cada skill (0 = ainda não aprendida). ✅ Vai a 1 quando o digimon chega no nível exigido.</summary>
    public int[] SkillLevels { get; } = new int[SkillSlots];

    public ushort SkillId(int slot) => slot switch { 0 => Species.Skill1, 1 => Species.Skill2, _ => 0 };

    public int AllocatedTotal => Allocated.Str + Allocated.Agi + Allocated.Con + Allocated.Int;
}

/// <summary>Um digimon (do jogador ou selvagem). Estado de batalha fica em Battle.Combatant, não aqui.</summary>
public sealed class Digimon
{
    private readonly GameData _data;
    private readonly Dictionary<ushort, DigimonForm> _forms = [];
    private readonly List<uint> _titles = [];

    /// <summary>✅ Limite de títulos na lista do pacote 0x97 (200 entradas × 8 bytes = 1600 B).</summary>
    public const int MaxTitles = 200;

    /// <param name="species">Forma-base: in-training até o nível 11, depois o rookie (✅ não volta).</param>
    public Digimon(GameData data, DigimonSpecies species, ushort level, CombatStats? wildStats = null)
    {
        _data = data;
        Name = species.Name;
        Level = Math.Max((ushort)1, level);
        WildStats = wildStats;
        BaseForm = Current = FormOf(species);
        UnlockedStage = Math.Max(species.Stage, StageUnlockedAt(Level));
        LearnSkills();
        RestoreAll();
    }

    /// <summary>✅ Nome de todo digimon obtido pelo jogo (captura, ovo), até o jogador renomear.</summary>
    public const string DefaultName = "InputName";

    /// <summary>✅ Pode renomear uma vez de graça (0xF0 responde 1); obtido pelo jogo nasce com ela.</summary>
    public bool FreeRename { get; set; }

    /// <summary>Chave no banco (0 = selvagem ou ainda não salvo).</summary>
    public uint Uid { get; init; }
    /// <summary>Selvagem = tem status fixos. Não usa o uid: ele só é atribuído depois do construtor.</summary>
    public bool IsWild => WildStats is not null;
    /// <summary>✅ Grau do selvagem (2 = Beginner, 3 = Veteran — byte 300 do bloco de status); 0 no digimon do jogador.</summary>
    public byte WildGrade { get; init; }
    public Serial Serial { get; init; } = Serial.New();
    public string Name { get; set; }
    public ushort Level { get; private set; }
    public long Exp { get; private set; }

    /// <summary>Forma-base (in-training ou rookie) e forma atual (diferente só durante a batalha).</summary>
    public DigimonForm BaseForm { get; private set; }
    public DigimonForm Current { get; private set; }
    public bool IsEvolved => Current != BaseForm;
    public IReadOnlyCollection<DigimonForm> Forms => _forms.Values;

    /// <summary>✅ Maior estágio liberado (byte 24 do bloco): sobe ao chegar nos níveis 21, 31, 41.</summary>
    public byte UnlockedStage { get; private set; }

    public DigimonSpecies Species => Current.Species;
    public ushort SpeciesId => Species.Id;

    /// <summary>Status fixos de selvagem (<see cref="WildFormulas"/> ou o XML); null no digimon do jogador.</summary>
    public CombatStats? WildStats { get; }

    public int TotalWins { get; set; }
    public int TotalBattles { get; set; }

    /// <summary>✅ Título equipado do Digimon (offset 320 do bloco de status; offset 100 do 0x10; 0 = nenhum).</summary>
    public uint TitleId { get; private set; }
    /// <summary>✅ Títulos desbloqueados deste Digimon (enviados no 0x97, até <see cref="MaxTitles"/>).</summary>
    public IReadOnlyList<uint> Titles => _titles;
    public DigimonTitleInfo? EquippedTitle => TitleId == 0 ? null : _data.FindDigimonTitle(TitleId);
    public Attributes TitleAttributeBonus => EquippedTitle?.AttributeBonus ?? default;

    public int Hp { get; private set; }
    public int Vp { get; private set; }
    public int Evp { get; private set; }

    public Attributes Allocated => Current.Allocated;
    public Attributes Attributes => StatFormulas.BaseAttributes(Species, Level) + Current.Allocated;

    public CombatStats Stats => WildStats ?? ComputePlayerStats();
    public long ExpToNextLevel => _data.DigimonExpToNext(Level);
    public bool IsKnockedOut => Hp <= 0;

    public bool HasTitle(uint titleId) => titleId != 0 && _titles.Contains(titleId);

    /// <summary>Desbloqueia um título novo para este Digimon. False se já possuía, inválido ou lista cheia.</summary>
    public bool UnlockTitle(uint titleId)
    {
        if (titleId == 0 || _titles.Count >= MaxTitles || _titles.Contains(titleId)) return false;
        _titles.Add(titleId);
        return true;
    }

    /// <summary>✅ 0x99: equipa um título desbloqueado (ou 0 para desequipar). Ajusta HP/VP/EVP se o máximo diminuir.</summary>
    public bool EquipTitle(uint titleId)
    {
        if (titleId != 0 && !HasTitle(titleId)) return false;
        TitleId = titleId;
        var s = Stats;
        Hp = Math.Min(Hp, s.MaxHp);
        Vp = Math.Min(Vp, s.MaxVp);
        Evp = Math.Min(Evp, s.MaxEvp);
        return true;
    }

    /// <summary>Carga do banco: restaura lista de títulos desbloqueados e o título equipado.</summary>
    public void LoadTitles(uint equippedTitleId, IEnumerable<uint> unlockedTitles)
    {
        _titles.Clear();
        foreach (uint tid in unlockedTitles)
            UnlockTitle(tid);
        TitleId = equippedTitleId != 0 && HasTitle(equippedTitleId) ? equippedTitleId : 0;
    }

    private CombatStats ComputePlayerStats()
    {
        var title = EquippedTitle;
        var effectiveAttrs = title is null ? Attributes : Attributes + title.AttributeBonus;
        var baseStats = StatFormulas.Combat(effectiveAttrs, Level);
        if (title is null) return baseStats;

        int maxHp = baseStats.MaxHp + title.BonusHp;
        int maxVp = baseStats.MaxVp + title.BonusVp;
        int atk = (int)Math.Floor((baseStats.Atk + title.BonusAtk) * (1.0 + title.BonusAtkPercent / 100.0));
        int def = (int)Math.Floor((baseStats.Def + title.BonusDef) * (1.0 + title.BonusDefPercent / 100.0));
        int br = (int)Math.Floor((baseStats.Br + title.BonusBr) * (1.0 + title.BonusBrPercent / 100.0));
        return new CombatStats(maxHp, maxVp, baseStats.MaxEvp, atk, def, br, baseStats.MaxActionGauge);
    }

    /// <summary>✅ Pontos de atributo livres da forma atual: os ganhos na janela do estágio menos os distribuídos.</summary>
    public ushort AbilityPoints => PointsOf(Current);

    /// <summary>
    /// ✅ Pontos de skill, compartilhados: 1 por nível menos os níveis comprados em todas as formas
    /// (V-mon L14: 13 → 4 depois de levar a skill 1 a 10; XV-mon L21 tem os mesmos 3 do V-mon).
    /// </summary>
    public ushort SkillPoints
    {
        get
        {
            int spent = _forms.Values.Sum(f => f.SkillLevels.Sum(l => Math.Max(0, l - 1)));
            return (ushort)Math.Max(0, Level - 1 - spent);
        }
    }

    public ushort PointsOf(DigimonForm form) =>
        (ushort)Math.Max(0, StatFormulas.AbilityPointsEarned(form.Species.Stage, Level) - form.AllocatedTotal);

    public static byte StageUnlockedAt(int level) => level switch
    {
        >= 41 => StatFormulas.Mega,
        >= 31 => StatFormulas.Ultimate,
        >= 21 => StatFormulas.Champion,
        _ => 0,
    };

    /// <summary>Forma existente ou nova (sem pontos, skills aprendidas pelo nível).</summary>
    public DigimonForm FormOf(DigimonSpecies species)
    {
        if (!_forms.TryGetValue(species.Id, out var form))
        {
            form = new DigimonForm(species);
            _forms[species.Id] = form;
            Learn(form);
        }
        return form;
    }

    public DigimonForm? FindForm(ushort speciesId) => _forms.GetValueOrDefault(speciesId);

    /// <summary>Espécie da linha no estágio dado, a partir da forma-base (null se a árvore não chega lá).</summary>
    public DigimonSpecies? LineSpecies(byte stage)
    {
        var s = BaseForm.Species;
        while (s.Stage < stage && _data.NextForm(s) is { } next) s = next;
        return s.Stage == stage ? s : null;
    }

    /// <summary>Formas para as quais o digimon pode evoluir agora (✅ resposta do 0x9E: até três, champion a mega).</summary>
    public IReadOnlyList<DigimonSpecies> EvolutionOptions()
    {
        var list = new List<DigimonSpecies>();
        for (byte stage = StatFormulas.Champion; stage <= UnlockedStage; stage++)
            if (LineSpecies(stage) is { } s && s.Id != SpeciesId)
                list.Add(s);
        return list;
    }

    /// <summary>Restaura o estado salvo no banco (antes das formas).</summary>
    public void Load(long exp, byte unlockedStage, int wins, int battles)
    {
        Exp = Math.Max(0, exp);
        UnlockedStage = Math.Max(UnlockedStage, unlockedStage);
        TotalWins = wins;
        TotalBattles = battles;
    }

    /// <summary>Restaura uma forma salva.</summary>
    public void LoadForm(DigimonSpecies species, Attributes allocated, int skill1, int skill2)
    {
        var form = FormOf(species);
        form.Allocated = allocated;
        form.SkillLevels[0] = Math.Clamp(skill1, 0, SkillInfo.MaxLevel);
        form.SkillLevels[1] = Math.Clamp(skill2, 0, SkillInfo.MaxLevel);
        Learn(form);
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

    /// <summary>✅ Perde EXP (ex.: −1 % de ExpToNext ao ser nocauteado em batalha); nunca fica negativa.</summary>
    public long LoseExp(long amount)
    {
        if (amount <= 0 || Exp <= 0) return 0;
        long lost = Math.Min(Exp, amount);
        Exp -= lost;
        return lost;
    }

    public bool SpendVp(int amount)
    {
        if (Vp < amount) return false;
        Vp -= amount;
        return true;
    }

    /// <summary>
    /// Soma EXP e sobe de nível. Com <paramref name="allowMultiLevelUp"/> = <c>true</c>, sobe quantos níveis
    /// couberem preservando o excedente; com <c>false</c> (oficial), sobe no máximo 1 nível e zera o excedente.
    /// </summary>
    public int GainExp(long amount, int maxLevel, bool allowMultiLevelUp = true)
    {
        if (amount <= 0) return 0;
        Exp += amount;
        int gained = 0;

        if (!allowMultiLevelUp)
        {
            if (Level < maxLevel && Exp >= ExpToNextLevel)
            {
                LevelUp();
                Exp = 0;
                return 1;
            }
            if (Level >= maxLevel) Exp = Math.Min(Exp, ExpToNextLevel);
            return 0;
        }

        while (Level < maxLevel && Exp >= ExpToNextLevel)
        {
            Exp -= ExpToNextLevel;
            LevelUp();
            gained++;
        }
        if (Level >= maxLevel) Exp = Math.Min(Exp, ExpToNextLevel);
        return gained;
    }

    /// <summary>✅ HP e VP enchem no level-up (0x70). Skills novas da linha passam a nível 1.</summary>
    public void LevelUp()
    {
        Level++;
        LearnSkills();
        RestoreAll();
    }

    /// <summary>✅ No nível 11 o in-training vira rookie (próxima forma da árvore) e não volta mais.</summary>
    public bool ShouldBecomeRookie => BaseForm.Species.Stage <= StatFormulas.InTraining && Level >= StatFormulas.StageLevel(StatFormulas.Rookie)
                                      && _data.NextForm(BaseForm.Species) is not null;

    /// <summary>Troca a forma-base de in-training para rookie. A forma in-training é descartada.</summary>
    public bool BecomeRookie()
    {
        if (!ShouldBecomeRookie || _data.NextForm(BaseForm.Species) is not { } rookie) return false;
        bool wasCurrent = Current == BaseForm;
        _forms.Remove(BaseForm.Species.Id);
        BaseForm = FormOf(rookie);
        if (wasCurrent) Current = BaseForm;
        UnlockedStage = Math.Max(UnlockedStage, rookie.Stage);
        RestoreAll();
        return true;
    }

    /// <summary>Libera o estágio do nível atual (21, 31, 41). Devolve o estágio novo, ou 0 se nada mudou.</summary>
    public byte UnlockStage()
    {
        if (BaseForm.Species.Stage < StatFormulas.Rookie) return 0;
        byte stage = StageUnlockedAt(Level);
        if (stage <= UnlockedStage) return 0;
        UnlockedStage = stage;
        return stage;
    }

    /// <summary>
    /// Evolui para uma forma da linha. ✅ A forma nova chega com HP e VP cheios (0x80/0x81); o EVP é do digimon.
    /// </summary>
    public bool EvolveTo(DigimonSpecies species)
    {
        if (species.Id == SpeciesId) return false;
        int evp = Evp;
        Current = FormOf(species);
        RestoreAll();
        Evp = Math.Min(evp, Stats.MaxEvp);
        return true;
    }

    /// <summary>
    /// ✅ Volta para a forma-base no fim da batalha (sem Aura). ❓ O HP volta na mesma proporção do máximo
    /// (V-mon 285/319 depois de lutar como XV-mon); o VP também.
    /// </summary>
    public bool Devolve()
    {
        if (!IsEvolved) return false;
        var before = Stats;
        double hp = before.MaxHp == 0 ? 1 : (double)Hp / before.MaxHp, vp = before.MaxVp == 0 ? 1 : (double)Vp / before.MaxVp;
        int evp = Evp;
        Current = BaseForm;
        var s = Stats;
        Hp = Math.Clamp((int)Math.Ceiling(s.MaxHp * hp), IsKnockedOut ? 0 : 1, s.MaxHp);
        Vp = Math.Clamp((int)Math.Ceiling(s.MaxVp * vp), 0, s.MaxVp);
        Evp = Math.Min(evp, s.MaxEvp);
        return true;
    }

    /// <summary>
    /// Forma do estágio pedido pelo cliente (u8 no 0x71, 0x72 e 0x82). ✅ 0 = a atual; senão a espécie da linha nesse
    /// estágio, desde que já liberado. Null para estágio sem forma (abaixo da base ou além do liberado).
    /// </summary>
    public DigimonForm? FormAtStage(byte stage)
    {
        if (stage == 0 || stage == Current.Species.Stage) return Current;
        if (stage == BaseForm.Species.Stage) return BaseForm;
        if (stage < BaseForm.Species.Stage || stage > UnlockedStage) return null;
        return LineSpecies(stage) is { } s ? FormOf(s) : null;
    }

    public bool AllocatePoint(StatKind stat) => AllocatePoint(Current, stat);

    /// <summary>✅ 0x71: um ponto num atributo da forma (cada forma tem os seus pontos).</summary>
    public bool AllocatePoint(DigimonForm form, StatKind stat)
    {
        if (PointsOf(form) == 0) return false;
        var a = form.Allocated;
        form.Allocated = stat switch
        {
            StatKind.Str => a with { Str = a.Str + 1 },
            StatKind.Agi => a with { Agi = a.Agi + 1 },
            StatKind.Con => a with { Con = a.Con + 1 },
            StatKind.Int => a with { Int = a.Int + 1 },
            _ => throw new ArgumentOutOfRangeException(nameof(stat)),
        };
        return true;
    }

    /// <summary>✅ 0x72: sobe um nível da skill (1 ponto de skill por nível, até 10). A skill precisa estar aprendida.</summary>
    public bool UpgradeSkill(DigimonForm form, int slot)
    {
        if (slot is < 0 or >= DigimonForm.SkillSlots || SkillPoints == 0) return false;
        int level = form.SkillLevels[slot];
        if (level < 1 || level >= SkillInfo.MaxLevel) return false;
        form.SkillLevels[slot] = level + 1;
        return true;
    }

    /// <summary>Executa <paramref name="view"/> com outra forma como atual (janela de evolução, 0x82), sem trocar de verdade.</summary>
    public T ViewAs<T>(DigimonForm form, Func<T> view)
    {
        var current = Current;
        Current = form;
        try { return view(); }
        finally { Current = current; }
    }

    public SkillInfo? SkillInfoOf(int slot) => _data.FindSkill(Current.SkillId(slot));

    private void LearnSkills()
    {
        foreach (var form in _forms.Values) Learn(form);
    }

    private void Learn(DigimonForm form)
    {
        for (int slot = 0; slot < DigimonForm.SkillSlots; slot++)
            if (form.SkillLevels[slot] == 0 && _data.FindSkill(form.SkillId(slot)) is { } skill && Level >= skill.RequiredLevel)
                form.SkillLevels[slot] = 1;
    }
}
