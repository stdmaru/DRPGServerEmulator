using DRPGServer.Game.Entities;
using DRPGServer.Network.Packets.Map;
using DRPGServer.Network.Protocol;

namespace DRPGServer.Game.Battle;

public enum BattleSide : byte { A, B }

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
    public int Gauge { get; set; }
    public Combatant? AutoTarget { get; set; }
    public bool GaugeFull => Gauge >= Digimon.Stats.MaxActionGauge;
}

/// <summary>
/// Batalha PvE, portada do protótipo sem mudar a mecânica (a próxima tarefa refaz o sistema).
/// Roda só na thread do mundo, chamada pelo <see cref="BattleSystem"/>.
///
/// Fluxo: 0x61 (C2S pede) → 0x62 (S2C serial) → 0x62 (C2S confirma) → 0x63 (S2C times) →
/// 0x69 (C2S pronto) → 0x69 (S2C) → barras enchem (0x6B) → ataque (0x68, 0x67) →
/// o cliente manda 0x69 de novo quando termina a animação → … → 0x6D (fim).
/// </summary>
public sealed class Battle
{
    private static readonly TimeSpan StartDelay = TimeSpan.FromMilliseconds(700);
    private static readonly TimeSpan TickInterval = TimeSpan.FromMilliseconds(200);
    public const int MaxPerSide = 5;

    private readonly BattleSystem _system;
    private readonly List<Player> _participants = [];
    private readonly List<Combatant> _teamA = [];
    private readonly List<Combatant> _teamB = [];
    private readonly Queue<(Combatant Attacker, Combatant Target)> _pending = new();
    private TimeSpan _timer;
    private bool _started;

    /// <summary>O cliente terminou de animar e espera o próximo passo (0x69).</summary>
    private bool _clientReady;

    /// <summary>Barras enchendo; dá para pedir ação.</summary>
    private bool _idle;

    internal Battle(BattleSystem system, Player player, WildDigimon wild, Random random)
    {
        _system = system;
        Wild = wild;
        _participants.Add(player);
        _teamA.Add(new Combatant(player.Leader, BattleSide.A, player, null));
        for (int i = 0; i < wild.Members.Count && _teamB.Count < MaxPerSide; i++)
        {
            var member = wild.Members[i];
            if (i == 0 || random.Next(101) <= member.Option.AppearanceRate)
                _teamB.Add(new Combatant(member.Digimon, BattleSide.B, null, member));
        }
    }

    public Serial Serial { get; } = Serial.New();
    public WildDigimon Wild { get; }
    public IReadOnlyList<Combatant> TeamA => _teamA;
    public IReadOnlyList<Combatant> TeamB => _teamB;
    public IReadOnlyList<Player> Participants => _participants;
    public bool IsOver { get; private set; }
    public bool IsIdle => _idle;

    private IEnumerable<Combatant> All => _teamA.Concat(_teamB);

    internal void Start()
    {
        foreach (var player in _participants)
            player.Battle = this;
        foreach (var c in All)
            c.Gauge = 0;
        Wild.State = WildState.InBattle;
        Broadcast(new StartBattlePacket(Serial));
        _timer = -StartDelay;
    }

    /// <summary>0x62 confirmado pelo cliente: manda os dois times.</summary>
    public void SendTeams(Player player) => player.Send(new BattleCreatePacket(this));

    /// <summary>0x69: o cliente está pronto para continuar.</summary>
    public void ClientReady(Player player)
    {
        player.Send(new BattleBeginPacket());
        if (!_idle) _clientReady = true;
    }

    internal void Update(TimeSpan dt)
    {
        if (IsOver) return;
        _timer += dt;
        if (_timer < TickInterval) return;
        _timer = TimeSpan.Zero;
        _started = true;
        Step();
    }

    private void Step()
    {
        if (_teamA.All(c => c.Digimon.IsKnockedOut)) { End(BattleResult.Lose); return; }
        if (_teamB.All(c => c.Digimon.IsKnockedOut)) { End(BattleResult.Win); return; }

        if (_pending.TryDequeue(out var action))
        {
            _clientReady = false;
            _idle = false;
            Attack(action.Attacker, action.Target);
            return;
        }

        if (_clientReady) _idle = true;
        if (!_idle) return;

        var cooldowns = new List<ServerPacket>();
        foreach (var c in All)
        {
            if (c.Digimon.IsKnockedOut) continue;
            c.Gauge = Math.Min(c.Gauge + BattleFormulas.GaugeGain(c), c.Digimon.Stats.MaxActionGauge);
            cooldowns.Add(new ActionCooldownPacket(c.Digimon.Serial, c.Gauge));

            if (c.Side == BattleSide.B && c.AutoTarget is null)
                QueueAutoAttack(c, PickTarget(_teamA), visual: false);

            if (c.GaugeFull && c.AutoTarget is { } target)
            {
                _pending.Enqueue((c, target));
                break;
            }
        }
        foreach (var packet in cooldowns)
            Broadcast(packet);
    }

    private Combatant PickTarget(List<Combatant> team)
    {
        var alive = team.Where(c => !c.Digimon.IsKnockedOut).ToList();
        return alive.Count > 0 ? alive[_system.Random.Next(alive.Count)] : team[0];
    }

    /// <summary>0x35: o jogador escolheu atacar um alvo.</summary>
    public void RequestAction(Player player, Serial requester, Serial target)
    {
        if (!_idle || IsOver) return;
        var attacker = All.FirstOrDefault(c => c.Digimon.Serial == requester);
        var victim = All.FirstOrDefault(c => c.Digimon.Serial == target);
        if (attacker is null || victim is null || attacker.Owner != player) return;

        if (attacker.AutoTarget is not null)
            ClearAutoAttack(attacker);
        QueueAutoAttack(attacker, victim, visual: !attacker.GaugeFull);
    }

    private void Attack(Combatant attacker, Combatant target)
    {
        Broadcast(new LockActionBarPacket());

        int damage = BattleFormulas.Damage(attacker.Digimon, target.Digimon);
        target.Digimon.TakeDamage(damage);
        Broadcast(ActionResults(attacker, target));

        if (target.Digimon.IsKnockedOut)
        {
            ClearAutoAttack(attacker);
            foreach (var c in All.Where(c => c.AutoTarget == target))
                c.AutoTarget = null;
            if (attacker.Owner is { } owner && target.Wild is { } member)
                _system.GrantRewards(owner, attacker.Digimon, member);
        }
        attacker.Gauge = 0;
    }

    private ActionResultsPacket ActionResults(Combatant attacker, Combatant target)
    {
        bool enemyTurn = attacker.Side == BattleSide.B;
        var attackers = (enemyTurn ? _teamB : _teamA).Select(c => c.Digimon).ToList();
        var defenders = (enemyTurn ? _teamA : _teamB).Select(c => c.Digimon).ToList();
        return new ActionResultsPacket(attackers, defenders, attacker.Digimon, target.Digimon, enemyTurn);
    }

    private void ClearAutoAttack(Combatant c)
    {
        c.AutoTarget = null;
        Broadcast(new ActionRequestPacket(0, c.Digimon.Serial, Serial.Empty));
    }

    private void QueueAutoAttack(Combatant c, Combatant target, bool visual)
    {
        c.AutoTarget = target;
        if (visual)
            Broadcast(new ActionRequestPacket(1, c.Digimon.Serial, target.Digimon.Serial));
    }

    /// <summary>Comando de GM: derruba um lado inteiro.</summary>
    public void KnockOut(BattleSide side)
    {
        if (IsOver) return;
        _idle = false;
        foreach (var c in side == BattleSide.A ? _teamA : _teamB)
        {
            c.Digimon.TakeDamage(c.Digimon.Hp);
            Broadcast(ActionResults(c, c));
        }
        _clientReady = true;
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
        IsOver = true;
        Broadcast(new BattleEndPacket(result));
        _system.Finish(this, result);
        foreach (var player in _participants)
            player.Battle = null;
        _participants.Clear();
    }

    private void Broadcast(ServerPacket packet)
    {
        if (_participants.Count == 0) return;
        var frame = packet.Encode();
        foreach (var player in _participants)
            player.Session.SendFrame(frame);
    }

    public override string ToString() => $"batalha {Serial} ({(_started ? "andando" : "começando")})";
}
