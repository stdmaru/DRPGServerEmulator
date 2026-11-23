using DRPGServer.Game.Entities;
using DRPGServer.Network.Packets.Map;
using DRPGServer.Network.Protocol;

namespace DRPGServer.Game.Battle;

public enum BattleSide : byte { A, B }

/// <summary>Resultado no 0x6D. <see cref="Cancel"/> é interno (desconexão): não vai para o cliente.</summary>
public enum BattleResult : byte
{
    Win = 1,
    Lose = 2,
    Cancel = 3,
}

/// <summary>Um digimon dentro da batalha, com o estado que só existe nela.</summary>
public sealed class Combatant(Digimon digimon, BattleSide side, Player? owner, WildMember? wild)
{
    public Digimon Digimon { get; } = digimon;
    public BattleSide Side { get; } = side;
    public Player? Owner { get; } = owner;
    public WildMember? Wild { get; } = wild;
    public bool IsWild => Owner is null;
    public int Gauge { get; set; }
    /// <summary>✅ Ações feitas na batalha (u16 no offset 134 da entrada do 0x67).</summary>
    public int Actions { get; set; }
    /// <summary>Ação marcada pelo jogador (0x35); acontece quando a barra enche.</summary>
    public BattleIcon Choice { get; set; }
    public Combatant? ChoiceTarget { get; set; }
    /// <summary>Digimon do time escolhido para invocar (<see cref="BattleIcon.Summon"/>).</summary>
    public Digimon? ChoiceSummon { get; set; }
    public bool GaugeFull => Gauge >= BattleFormulas.MaxGauge;

    public void ClearChoice()
    {
        Choice = BattleIcon.None;
        ChoiceTarget = null;
        ChoiceSummon = null;
    }
}

/// <summary>
/// Batalha PvE, refeita sobre as capturas v322 (docs/mecanicas/batalha.md). Roda só na thread do mundo.
///
/// Abertura: 0x61 (C2S) → 0x62 (S2C serial) → 0x62 (C2S eco) → 0x63 (times) → 0x69 (C2S pronto) →
/// 0x10 (tamer em batalha) → 0x69. Depois as barras enchem (0x6B de todos a cada passo). O jogador marca a
/// ação com 0x35 (2 → 1 marcada, de novo → 4 desmarcada); com a barra cheia ela acontece:
/// ataque/skill 0x35 (3) → 0x68 → [0x6E/0x70/0x80 se derrubou] → 0x67; invocação 0x68 → 0x64 → 0x35 (3);
/// evolução 0x68 → 0x81 → 0x35 (3). O inimigo age sozinho: 0x68 → 0x67. As barras param até o cliente mandar
/// 0x69 no fim da animação. Vitória: … 0x67 → 0xB1 → 0x10 → 0x84 → 0x6D 1 (0xB1, 0x84 e 0x6D um por digimon).
/// Fuga: 0x35 (3) → 0x65 → 0x6A → 0x6D 2.
/// </summary>
public sealed class Battle
{
    /// <summary>✅ Passo das barras: 200 ms (6 passos visuais de ~33,3 ms do cliente a 60 FPS).</summary>
    public static readonly TimeSpan TickInterval = TimeSpan.FromMilliseconds(200);
    /// <summary>✅ Atraso após S2C 0x69 na abertura da batalha enquanto a animação de entrada bloqueia a barra no cliente (mediana 536 ms em 76 batalhas).</summary>
    public static readonly TimeSpan OpeningDelay = TimeSpan.FromMilliseconds(540);
    /// <summary>✅ Atraso após S2C 0x69 na retomada pós-animação antes da barra voltar a subir (mediana 196 ms em 272 turnos).</summary>
    public static readonly TimeSpan ResumeDelay = TimeSpan.FromMilliseconds(200);
    public const int MaxPerSide = 5;

    private enum Phase { Opening, Running, Animating, Over }

    private readonly BattleSystem _system;
    private readonly List<Player> _participants = [];
    private readonly List<Combatant> _teamA = [];
    private readonly List<Combatant> _teamB = [];
    private readonly Dictionary<Player, int> _captureAttempts = [];
    private Phase _phase = Phase.Opening;
    private TimeSpan _timer;

    internal Battle(BattleSystem system, Player player, WildDigimon wild, Func<WildMember, Digimon> enemyOf, Random random)
    {
        _system = system;
        Wild = wild;
        Zone = player.Zone;
        _participants.Add(player);
        _teamA.Add(new Combatant(player.Leader, BattleSide.A, player, null));
        for (int i = 0; i < wild.Members.Count && _teamB.Count < MaxPerSide; i++)
        {
            var member = wild.Members[i];
            if (i == 0 || random.Next(100) < member.Option.AppearanceRate)
                _teamB.Add(new Combatant(enemyOf(member), BattleSide.B, null, member));
        }
    }

    public Serial Serial { get; } = Serial.New();
    public WildDigimon Wild { get; }
    public World.Zone? Zone { get; }
    public IReadOnlyList<Combatant> TeamA => _teamA;
    public IReadOnlyList<Combatant> TeamB => _teamB;
    public IReadOnlyList<Player> Participants => _participants;
    public bool IsOver => _phase == Phase.Over;
    /// <summary>Barras enchendo (fora da abertura e das animações).</summary>
    public bool IsRunning => _phase == Phase.Running;

    private IEnumerable<Combatant> All => _teamA.Concat(_teamB);
    private Combatant? Find(Serial serial) => All.FirstOrDefault(c => c.Digimon.Serial == serial);
    public bool Contains(Digimon digimon) => All.Any(c => c.Digimon == digimon);

    internal void Start()
    {
        foreach (var player in _participants)
            player.Battle = this;
        Broadcast(new StartBattlePacket(Serial));
    }

    /// <summary>0x62 confirmado pelo cliente: manda os dois times.</summary>
    public void SendTeams(Player player)
    {
        if (_phase == Phase.Opening) player.Send(new BattleCreatePacket(this));
    }

    /// <summary>0x69: o cliente terminou de carregar (abertura) ou de animar (depois de cada ação).</summary>
    public void ClientReady(Player player)
    {
        TimeSpan delay;
        switch (_phase)
        {
            case Phase.Opening:
                player.Zone?.Broadcast(new RefreshCharacterInfoPacket(player.Character, TamerState.InBattle));
                delay = OpeningDelay;
                break;
            case Phase.Animating:
                ClearDeadChoices();
                delay = ResumeDelay;
                break;
            default:
                return;
        }
        player.Send(new BattleBeginPacket());
        _phase = Phase.Running;
        _timer = -delay;
    }

    internal void Update(TimeSpan dt)
    {
        if (_phase != Phase.Running) return;
        _timer += dt;
        if (_timer < TickInterval) return;
        _timer -= TickInterval;
        Step();
    }

    private void Step()
    {
        var alive = All.Where(c => !c.Digimon.IsKnockedOut).ToList();
        foreach (var c in alive)
        {
            if (c.GaugeFull) continue;
            c.Gauge = Math.Min(BattleFormulas.MaxGauge, c.Gauge + BattleFormulas.GaugeGain(c.Digimon, c.IsWild));
            Broadcast(new ActionCooldownPacket(c.Digimon.Serial, c.Gauge));
        }

        // ✅ quem tem a barra cheia e uma ação age; o selvagem age sozinho, o jogador espera a escolha
        if (alive.FirstOrDefault(c => c.GaugeFull && (c.IsWild || c.Choice != BattleIcon.None)) is { } actor)
            Act(actor);
    }

    private void Act(Combatant actor)
    {
        if (actor.IsWild)
        {
            var targets = _teamA.Where(c => !c.Digimon.IsKnockedOut).ToList();
            if (targets.Count == 0) return;
            Attack(actor, targets[_system.Random.Next(targets.Count)], null);
            return;
        }

        var choice = actor.Choice;
        var target = actor.ChoiceTarget;
        var summon = actor.ChoiceSummon;
        actor.ClearChoice();
        var executed = new ActionRequestPacket(BattleAction.Executed, BattleIcon.None, actor.Digimon.Serial, Serial.Empty);

        switch (choice)
        {
            case BattleIcon.Flee:
                Broadcast(executed);
                Flee(actor);
                return;
            case BattleIcon.Summon:
                if (summon is null || !CanSummon(actor, summon)) break;
                Broadcast(new LockActionBarPacket());
                Summon(actor, summon);
                Broadcast(executed);
                return;
            case >= BattleIcon.Evolve1 and <= BattleIcon.Evolve3:
                var options = actor.Digimon.EvolutionOptions();
                int index = choice - BattleIcon.Evolve1;
                if (index >= options.Count) break;
                Broadcast(new LockActionBarPacket());
                actor.Digimon.EvolveTo(options[index]);
                actor.Owner?.Character.RegisterEncyclopedia(actor.Digimon);
                actor.Gauge = 0;
                actor.Actions++;
                Broadcast(new BattleEvolvePacket(actor.Digimon));
                Broadcast(executed);
                _phase = Phase.Animating;
                return;
            case BattleIcon.Capture:
                if (target is null || !CanCapture(actor, target)) break;
                Capture(actor, target);
                return;
            case BattleIcon.Attack or BattleIcon.Skill1 or BattleIcon.Skill2:
                if (target is null || target.Digimon.IsKnockedOut) break;
                Broadcast(executed);
                Attack(actor, target, choice == BattleIcon.Attack ? null : choice - BattleIcon.Skill1);
                return;
        }
        Broadcast(executed);                    // ação que deixou de valer (alvo caiu, VP acabou): perde a vez
        actor.Gauge = 0;
    }

    /// <param name="skillSlot">null = ataque básico.</param>
    private void Attack(Combatant actor, Combatant target, int? skillSlot)
    {
        Broadcast(new LockActionBarPacket());
        var digimon = actor.Digimon;
        Hit hit;
        ushort skillId = 0;
        if (skillSlot is { } slot && digimon.SkillInfoOf(slot) is { } skill && digimon.SpendVp(skill.VpCost))
        {
            skillId = skill.Id;
            hit = BattleFormulas.Skill(digimon, target.Digimon, skill, digimon.Current.SkillLevels[slot], _system.Random);
        }
        else
        {
            hit = BattleFormulas.Attack(digimon, target.Digimon, _system.Random);
        }
        target.Digimon.TakeDamage(hit.Damage);
        actor.Actions++;
        actor.Gauge = 0;

        if (target.Digimon.IsKnockedOut && actor.Owner is { } owner && target.Wild is { } member)
            _system.GrantRewards(owner, actor, _teamA.Where(c => c.Owner == owner && !c.Digimon.IsKnockedOut).ToList(), member);

        var (acting, other) = actor.Side == BattleSide.A ? (_teamA, _teamB) : (_teamB, _teamA);
        Broadcast(new ActionResultsPacket(acting, other, actor, target, hit.Landed, skillId));

        if (_teamB.All(c => c.Digimon.IsKnockedOut)) End(BattleResult.Win);
        else if (_teamA.All(c => c.Digimon.IsKnockedOut)) End(BattleResult.Lose);
        else _phase = Phase.Animating;
    }

    public int CaptureAttempts(Player player) => _captureAttempts.GetValueOrDefault(player);

    private bool CanCapture(Combatant actor, Combatant target) =>
        actor.Owner is { } owner && target.IsWild && !target.Digimon.IsKnockedOut
        && CaptureAttempts(owner) < World.Taming.AttemptsPerBattle && World.Taming.CanBattleCapture(owner.Character, target.Digimon);

    /// <summary>
    /// ✅ Captura comum (sessão 56): 0x68 → 0xB3 → 0x35 3, depois a animação (0x69). ❓ No sucesso o selvagem sai da
    /// batalha sem recompensa, o digimon novo vai para o time (0xEF) e a batalha acaba se não sobrar inimigo.
    /// </summary>
    private void Capture(Combatant actor, Combatant target)
    {
        var owner = actor.Owner!;
        _captureAttempts[owner] = CaptureAttempts(owner) + 1;
        actor.Gauge = 0;
        actor.Actions++;
        Broadcast(new LockActionBarPacket());
        Digimon? captured = _system.Random.NextDouble() < _system.Taming.BattleChance
            ? _system.Taming.Tame(owner.Character, target.Digimon.Species) : null;
        Broadcast(new BattleCapturePacket(captured is not null, owner.Character.Serial, actor, target, captured));
        Broadcast(new ActionRequestPacket(BattleAction.Executed, BattleIcon.None, actor.Digimon.Serial, Serial.Empty));
        if (captured is not null)
        {
            target.Digimon.TakeDamage(target.Digimon.Hp);
            owner.Send(new DigimonReceivedPacket(captured));
            if (_teamB.All(c => c.Digimon.IsKnockedOut))
            {
                End(BattleResult.Win);
                return;
            }
        }
        _phase = Phase.Animating;
    }

    private bool CanSummon(Combatant actor, Digimon digimon) =>
        _teamA.Count < MaxPerSide && actor.Owner is { } owner && owner.Character.SlotOf(digimon) >= 0
        && !Contains(digimon) && !digimon.IsKnockedOut;

    /// <summary>✅ Invocação (sessão 56): o digimon entra no time A na próxima posição, com a barra zerada.</summary>
    private void Summon(Combatant actor, Digimon digimon)
    {
        var c = new Combatant(digimon, BattleSide.A, actor.Owner, null);
        _teamA.Add(c);
        actor.Gauge = 0;
        actor.Actions++;
        Broadcast(new BattleSummonPacket(_teamA.Count - 1, digimon));
        _phase = Phase.Animating;
    }

    private void Flee(Combatant actor)
    {
        Broadcast(new FleeDigimonPacket(actor.Digimon.Serial));
        if (actor.Owner is { } owner)
            Broadcast(new FleeTamerPacket(owner.Character.Serial));
        End(BattleResult.Lose);
    }

    /// <summary>✅ Escolha apontando para um inimigo derrubado: o oficial desmarca (0x35 4, ícone e alvo zerados).</summary>
    private void ClearDeadChoices()
    {
        foreach (var c in _teamA.Where(c => c.ChoiceTarget is { Digimon.IsKnockedOut: true }))
        {
            c.ClearChoice();
            Broadcast(new ActionRequestPacket(BattleAction.Cleared, BattleIcon.None, c.Digimon.Serial, Serial.Empty));
        }
    }

    /// <summary>
    /// 0x35: o jogador clicou numa ação. ✅ Alterna: marca (1) ou, se é a mesma, desmarca (4); o servidor
    /// devolve o ícone e o alvo. Vale também durante a animação (o cliente manda antes do 0x69).
    /// </summary>
    public void RequestAction(Player player, BattleIcon icon, Serial requester, Serial target)
    {
        if (_phase is Phase.Opening or Phase.Over) return;
        if (Find(requester) is not { } actor || actor.Owner != player || actor.Digimon.IsKnockedOut) return;

        Combatant? victim = null;
        Digimon? summon = null;
        switch (icon)
        {
            case BattleIcon.Attack:
                victim = Find(target);
                if (victim is null || victim.Side == actor.Side || victim.Digimon.IsKnockedOut) return;
                break;
            case BattleIcon.Skill1 or BattleIcon.Skill2:
                victim = Find(target);
                if (victim is null || victim.Side == actor.Side || victim.Digimon.IsKnockedOut) return;
                int slot = icon - BattleIcon.Skill1;
                // ✅ skill só depois de aprendida (nível 0 no bloco antes do nível exigido) e com VP para o custo
                if (actor.Digimon.Current.SkillLevels[slot] < 1 || actor.Digimon.SkillInfoOf(slot) is not { } skill
                    || actor.Digimon.Vp < skill.VpCost) return;
                break;
            case BattleIcon.Capture:
                victim = Find(target);
                if (victim is null || victim.Side == actor.Side || !CanCapture(actor, victim)) return;
                break;
            case BattleIcon.Summon:
                summon = player.Character.FindDigimon(target);
                if (summon is null || !CanSummon(actor, summon)) return;
                break;
            case BattleIcon.Flee:
                break;
            case >= BattleIcon.Evolve1 and <= BattleIcon.Evolve3:
                if (icon - BattleIcon.Evolve1 >= actor.Digimon.EvolutionOptions().Count) return;
                break;
            default:
                return;                             // ❓ itens ainda não
        }

        bool same = actor.Choice == icon && actor.ChoiceTarget == victim && actor.ChoiceSummon == summon;
        actor.ClearChoice();
        if (!same)
        {
            actor.Choice = icon;
            actor.ChoiceTarget = victim;
            actor.ChoiceSummon = summon;
        }
        var targetSerial = victim?.Digimon.Serial ?? summon?.Serial ?? Serial.Empty;
        Broadcast(new ActionRequestPacket(same ? BattleAction.Cleared : BattleAction.Selected, icon, requester, targetSerial));
    }

    /// <summary>0x9E: formas para as quais o digimon pode evoluir agora (✅ só em batalha nas capturas).</summary>
    public IReadOnlyList<Data.GameData.DigimonSpecies> EvolutionOptions(Player player, uint digimonUid)
    {
        var c = _teamA.FirstOrDefault(c => c.Owner == player && c.Digimon.Uid == digimonUid);
        return c is null || c.Digimon.IsKnockedOut || _phase is Phase.Opening or Phase.Over ? [] : c.Digimon.EvolutionOptions();
    }

    /// <summary>Comando de GM: derruba um lado inteiro e encerra.</summary>
    public void KnockOut(BattleSide side)
    {
        if (IsOver) return;
        var owner = _participants.FirstOrDefault();
        foreach (var c in side == BattleSide.A ? _teamA : _teamB)
        {
            if (c.Digimon.IsKnockedOut) continue;
            c.Digimon.TakeDamage(c.Digimon.Hp);
            if (owner is not null && c.Wild is { } member && _teamA.FirstOrDefault(a => !a.Digimon.IsKnockedOut) is { } killer)
                _system.GrantRewards(owner, killer, _teamA.Where(a => a.Owner == owner && !a.Digimon.IsKnockedOut).ToList(), member);
        }
        End(side == BattleSide.B ? BattleResult.Win : BattleResult.Lose);
    }

    /// <summary>Jogador saiu (desconectou). Sem ninguém, a batalha acaba sem resultado.</summary>
    internal void Remove(Player player)
    {
        _participants.Remove(player);
        player.Battle = null;
        foreach (var c in _teamA.Where(c => c.Owner == player))
            c.Digimon.Devolve();
        if (_participants.Count == 0)
            End(BattleResult.Cancel);
    }

    private void End(BattleResult result)
    {
        if (IsOver) return;
        _phase = Phase.Over;

        if (result != BattleResult.Cancel)
        {
            foreach (var player in _participants)
            {
                var mine = _teamA.Where(c => c.Owner == player).ToList();
                if (result == BattleResult.Win)
                    foreach (var _ in mine)
                        player.Send(new TamerExpPacket(player.Character));
                player.Zone?.Broadcast(new RefreshCharacterInfoPacket(player.Character, TamerState.Idle));
                foreach (var c in mine)
                {
                    // ❓ derrota por nocaute não capturada; o digimon volta com 1 de HP para não ficar zerado no mapa
                    if (c.Digimon.IsKnockedOut) c.Digimon.Heal(1, 0, 0);
                    // ✅ evoluído volta para a forma-base no fim (sem Aura): o 0x84 já traz o bloco do rookie
                    c.Digimon.Devolve();
                    if (result == BattleResult.Win) player.Send(new BattleDigimonStatusPacket(c.Digimon));
                }
                foreach (var _ in mine)
                    player.Send(new BattleEndPacket(result));
            }
        }

        _system.Finish(this, result);
        foreach (var player in _participants)
        {
            player.Battle = null;
            player.NextRecoveryAt = _system.Now + World.Zone.RecoveryAfterBattle;
        }
        _participants.Clear();
    }

    private void Broadcast(ServerPacket packet)
    {
        if (_participants.Count == 0) return;
        var frame = packet.Encode();
        foreach (var player in _participants)
            player.Session.SendFrame(frame);
    }

    public override string ToString() => $"batalha {Serial} ({_phase})";
}
