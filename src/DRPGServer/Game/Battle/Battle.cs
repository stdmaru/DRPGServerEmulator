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
    public bool GaugeFull => Gauge >= BattleFormulas.MaxGauge;
}

/// <summary>
/// Batalha PvE, refeita sobre as capturas v322 (docs/mecanicas/batalha.md). Roda só na thread do mundo.
///
/// Abertura: 0x61 (C2S) → 0x62 (S2C serial) → 0x62 (C2S eco) → 0x63 (times) → 0x69 (C2S pronto) →
/// 0x10 (tamer em batalha) → 0x69. Depois as barras enchem (0x6B de todos a cada passo). O jogador marca a
/// ação com 0x35 (2 → 1 marcada, de novo → 4 desmarcada); com a barra cheia ela acontece:
/// 0x35 (3) → 0x68 → [0x6E/0x70 se derrubou] → 0x67. O inimigo age sozinho: 0x68 → 0x67. As barras param
/// até o cliente mandar 0x69 no fim da animação. Vitória: … 0x67 → 0xB1 → 0x10 → 0x84 → 0x6D 1.
/// Fuga: 0x35 (3) → 0x65 → 0x6A → 0x6D 2.
/// </summary>
public sealed class Battle
{
    /// <summary>✅ Passo das barras: mediana de 0,16 s entre 0x6B do mesmo digimon (2927 de 4300 intervalos).</summary>
    public static readonly TimeSpan TickInterval = TimeSpan.FromMilliseconds(165);
    public const int MaxPerSide = 5;

    private enum Phase { Opening, Running, Animating, Over }

    private readonly BattleSystem _system;
    private readonly List<Player> _participants = [];
    private readonly List<Combatant> _teamA = [];
    private readonly List<Combatant> _teamB = [];
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
        switch (_phase)
        {
            case Phase.Opening:
                player.Zone?.Broadcast(new RefreshCharacterInfoPacket(player.Character, TamerState.InBattle));
                break;
            case Phase.Animating:
                ClearDeadChoices();
                break;
            default:
                return;
        }
        player.Send(new BattleBeginPacket());
        _phase = Phase.Running;
        _timer = TimeSpan.Zero;
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
            c.Gauge = Math.Min(BattleFormulas.MaxGauge, c.Gauge + BattleFormulas.GaugeGain(c.Digimon, c.IsWild));
        foreach (var c in alive)
            Broadcast(new ActionCooldownPacket(c.Digimon.Serial, c.Gauge));

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
            Attack(actor, targets[_system.Random.Next(targets.Count)]);
            return;
        }

        var choice = actor.Choice;
        var target = actor.ChoiceTarget;
        actor.Choice = BattleIcon.None;
        actor.ChoiceTarget = null;
        Broadcast(new ActionRequestPacket(BattleAction.Executed, BattleIcon.None, actor.Digimon.Serial, Serial.Empty));

        if (choice == BattleIcon.Flee)
        {
            Flee(actor);
            return;
        }
        if (target is null || target.Digimon.IsKnockedOut) return;
        Attack(actor, target);
    }

    private void Attack(Combatant actor, Combatant target)
    {
        Broadcast(new LockActionBarPacket());
        var hit = BattleFormulas.Attack(actor.Digimon, target.Digimon, _system.Random);
        target.Digimon.TakeDamage(hit.Damage);
        actor.Actions++;
        actor.Gauge = 0;

        if (target.Digimon.IsKnockedOut && actor.Owner is { } owner && target.Wild is { } member)
            _system.GrantRewards(owner, actor.Digimon, member);

        var (acting, other) = actor.Side == BattleSide.A ? (_teamA, _teamB) : (_teamB, _teamA);
        Broadcast(new ActionResultsPacket(acting, other, actor, target, hit.Landed));

        if (_teamB.All(c => c.Digimon.IsKnockedOut)) End(BattleResult.Win);
        else if (_teamA.All(c => c.Digimon.IsKnockedOut)) End(BattleResult.Lose);
        else _phase = Phase.Animating;
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
            c.Choice = BattleIcon.None;
            c.ChoiceTarget = null;
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
        switch (icon)
        {
            case BattleIcon.Attack:
                victim = Find(target);
                if (victim is null || victim.Side == actor.Side || victim.Digimon.IsKnockedOut) return;
                break;
            case BattleIcon.Flee:
                break;
            default:
                return;                             // ❓ skills e itens ainda não
        }

        bool same = actor.Choice == icon && actor.ChoiceTarget == victim;
        actor.Choice = same ? BattleIcon.None : icon;
        actor.ChoiceTarget = same ? null : victim;
        var targetSerial = victim?.Digimon.Serial ?? Serial.Empty;
        Broadcast(new ActionRequestPacket(same ? BattleAction.Cleared : BattleAction.Selected, icon, requester, targetSerial));
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
            if (owner is not null && c.Wild is { } member)
                _system.GrantRewards(owner, owner.Leader, member);
        }
        End(side == BattleSide.B ? BattleResult.Win : BattleResult.Lose);
    }

    /// <summary>Jogador saiu (desconectou). Sem ninguém, a batalha acaba sem resultado.</summary>
    internal void Remove(Player player)
    {
        _participants.Remove(player);
        player.Battle = null;
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
                if (result == BattleResult.Win)
                    player.Send(new TamerExpPacket(player.Character));
                player.Zone?.Broadcast(new RefreshCharacterInfoPacket(player.Character, TamerState.Idle));
                foreach (var c in _teamA.Where(c => c.Owner == player))
                {
                    // ❓ derrota por nocaute não capturada; o digimon volta com 1 de HP para não ficar zerado no mapa
                    if (c.Digimon.IsKnockedOut) c.Digimon.Heal(1, 0, 0);
                    if (result == BattleResult.Win) player.Send(new BattleDigimonStatusPacket(c.Digimon));
                }
            }
            Broadcast(new BattleEndPacket(result));
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
