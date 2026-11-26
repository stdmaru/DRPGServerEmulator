using DRPGServer.Configuration;
using DRPGServer.Data.Persistence;
using DRPGServer.Data.Repositories;
using DRPGServer.Game.Battle;
using DRPGServer.Game.Entities;
using DRPGServer.Network;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DRPGServer.Game.World;

/// <summary>Resultado de <see cref="PlayerSystem.BeginEntry"/>.</summary>
/// <param name="Accepted">Falso: outra entrada da mesma conta está em andamento.</param>
/// <param name="Ready">Completa quando o último save da conta chegou ao banco: só então carregar.</param>
public readonly record struct EntryStart(bool Accepted, Task Ready);

/// <summary>
/// Quem está online no mapa: entrada, saída e salvamento. Cada conta tem um estado
/// (<c>Offline → Loading → Online → Saving → Offline</c>, docs/emulador/seguranca.md#entrada-e-saída-do-mundo), para que
/// nunca se carregue do banco um personagem cujo último estado ainda está só em memória ou na fila de gravação.
/// O fluxo completo da entrada (com a carga do banco) é do <see cref="WorldEntry"/>.
/// </summary>
public sealed class PlayerSystem : IWorldSystem
{
    private enum PresenceState { Loading, Online, Saving }

    /// <summary>Estado de uma conta fora de <c>Offline</c>. <see cref="Saved"/> é o último save que ela espera.</summary>
    private sealed class Presence(PresenceState state, Session? owner, Task saved)
    {
        public PresenceState State { get; set; } = state;
        public Session? Owner { get; } = owner;
        public Task Saved { get; } = saved;
    }

    private readonly Dictionary<long, Player> _bySession = [];
    private readonly Dictionary<uint, Presence> _presence = [];
    private readonly GameWorld _world;
    private readonly ZoneSystem _zones;
    private readonly BattleSystem _battles;
    private readonly PersistenceQueue _persistence;
    private readonly ICharacterSnapshotStore _characters;
    private readonly GameOptions _options;
    private readonly ILogger<PlayerSystem> _log;

    public PlayerSystem(GameWorld world, ZoneSystem zones, BattleSystem battles, PersistenceQueue persistence,
        ICharacterSnapshotStore characters, IOptions<GameOptions> options, ILogger<PlayerSystem> log)
    {
        _world = world;
        _zones = zones;
        _battles = battles;
        _persistence = persistence;
        _characters = characters;
        _options = options.Value;
        _log = log;
        // fail-fast: o mapa inicial (GameOptions) precisa existir nas tabelas carregadas
        var start = zones.Find(_options.StartMapId)
            ?? throw new InvalidOperationException($"{GameOptions.Section}:StartMapId = {_options.StartMapId}: mapa não carregado");
        if (!start.IsWalkable(_options.StartPosX, _options.StartPosY))
            throw new InvalidOperationException(
                $"{GameOptions.Section}:StartPosX/StartPosY = ({_options.StartPosX}, {_options.StartPosY}): célula não andável no mapa {start.MapId}");
        world.AddSystem(this);
    }

    public IEnumerable<Player> Online => _bySession.Values;
    public int Count => _bySession.Count;

    /// <summary>
    /// Primeiro passo da entrada no mapa (thread do mundo). Tira do mundo um jogador online da mesma conta (o save dele
    /// vai para a fila) e devolve o save que a entrada precisa esperar antes de carregar. Recusa se outra entrada da
    /// conta, de uma sessão ainda aberta, está em andamento.
    /// </summary>
    public EntryStart BeginEntry(Session session, AuthenticatedAccount account)
    {
        _world.AssertWorldThread();
        if (session.IsClosed || session.Player is not null) return new(false, Task.CompletedTask);

        var ready = Task.CompletedTask;
        if (_presence.TryGetValue(account.Uid, out var current))
        {
            switch (current.State)
            {
                case PresenceState.Loading when current.Owner is { IsClosed: false }:
                    return new(false, Task.CompletedTask);
                case PresenceState.Online:
                    var previous = current.Owner!;
                    Leave(previous);
                    previous.Close("a conta entrou no mapa por outra conexão");
                    ready = _presence[account.Uid].Saved;
                    break;
                default:
                    // Saving, ou Loading de uma sessão que já fechou (a carga dela é descartada em CompleteEntry)
                    ready = current.Saved;
                    break;
            }
        }
        _presence[account.Uid] = new Presence(PresenceState.Loading, session, ready);
        return new(true, ready);
    }

    /// <summary>
    /// Segundo passo (thread do mundo): coloca no mundo o personagem carregado. <paramref name="character"/> null encerra
    /// a entrada sem jogador (falha na carga). Devolve null se a entrada foi substituída ou a sessão fechou.
    /// </summary>
    public Player? CompleteEntry(Session session, AuthenticatedAccount account, Character? character)
    {
        _world.AssertWorldThread();
        if (!_presence.TryGetValue(account.Uid, out var presence) || presence.State != PresenceState.Loading ||
            presence.Owner != session)
            return null;
        if (character is null || session.IsClosed)
        {
            StartSaving(account.Uid, presence.Saved);
            return null;
        }

        var zone = _zones.Find(character.MapId);
        if (zone is null)
        {
            _log.LogWarning("{Name}: mapa {Map} não carregado, indo para o inicial", character.Name, character.MapId);
            zone = _zones.Find(_options.StartMapId)!;   // conferido na construção
            character.MapId = zone.MapId;
            character.X = _options.StartPosX;
            character.Y = _options.StartPosY;
        }

        var player = new Player(session, account, character);
        player.MarkSaved(_world.Now);
        session.Player = player;
        _bySession[session.Id] = player;
        presence.State = PresenceState.Online;
        zone.Enter(player);
        _log.LogInformation("{Player} entrou no mundo em {Zone} ({Count} online)", player, zone, _bySession.Count);
        return player;
    }

    /// <summary>
    /// Tira a sessão do mundo, ou encerra a entrada dela em andamento. A conta fica em <c>Saving</c> até o save chegar
    /// ao banco.
    /// </summary>
    public void Leave(Session session)
    {
        _world.AssertWorldThread();
        if (_bySession.Remove(session.Id, out var player))
        {
            _battles.PlayerLeft(player);
            player.Zone?.Leave(player);
            session.Player = null;
            if (player.PendingTransfer is { } transfer)
            {
                StartSaving(player.Account.Uid, SaveAfterAsync(player, transfer));
            }
            else
            {
                Save(player, _world.Now);
                StartSaving(player.Account.Uid, _persistence.FlushAsync());   // o flush vem depois do save na fila
            }
            _log.LogInformation("{Player} saiu do mundo ({Count} online)", player, _bySession.Count);
        }
        else if (session.Account is { } account && _presence.TryGetValue(account.Uid, out var presence) &&
                 presence.State == PresenceState.Loading && presence.Owner == session)
        {
            StartSaving(account.Uid, presence.Saved);   // continua esperando o save que a entrada esperava
        }
    }

    /// <summary>O personagem está no mundo ou com a saída ainda gravando (thread do mundo).</summary>
    public bool IsOnline(uint characterUid)
    {
        _world.AssertWorldThread();
        return _bySession.Values.Any(p => p.Character.Uid == characterUid);
    }

    public Player? FindByName(string name) =>
        _bySession.Values.FirstOrDefault(p => string.Equals(p.Character.Name, name, StringComparison.OrdinalIgnoreCase));

    /// <summary>A conta fica em <c>Saving</c> até <paramref name="saved"/>; depois volta para <c>Offline</c>.</summary>
    private void StartSaving(uint accountUid, Task saved)
    {
        var presence = new Presence(PresenceState.Saving, null, saved);
        _presence[accountUid] = presence;
        saved.ContinueWith(_ => _world.Post(() =>
        {
            if (_presence.TryGetValue(accountUid, out var current) && current == presence)
                _presence.Remove(accountUid);
        }), TaskScheduler.Default);
    }

    /// <summary>Snapshot final de quem saiu no meio de uma transferência: só depois do commit ou do rollback dela.</summary>
    private async Task SaveAfterAsync(Player player, Task transfer)
    {
        await transfer;
        var flushed = await _world.InvokeAsync(() =>
        {
            Save(player, _world.Now);
            return _persistence.FlushAsync();
        });
        await flushed;
    }

    private void Save(Player player, TimeSpan now)
    {
        player.MarkSaved(now);
        var snapshot = CharacterSnapshot.Of(player.Character);
        // a auditoria foi gravada junto com o snapshot: sai do personagem (um snapshot coalescido nunca roda, e as
        // movimentações dele seguem no próximo)
        _persistence.SaveCharacter(_characters, snapshot,
            committed: () => _world.Post(() => player.Character.Economy.Acknowledge(snapshot.Economy)));
    }

    public void Update(TimeSpan now, TimeSpan dt)
    {
        var dirtyInterval = TimeSpan.FromSeconds(Math.Max(1, _options.AutosaveSeconds));
        foreach (var player in _bySession.Values)
        {
            if (player.PendingTransfer is not null) continue;   // o snapshot da transferência vem antes
            if (player.NeedsImmediateSave)
            {
                Save(player, now);
            }
            else if (player.IsDirty && now - player.LastSavedAt >= dirtyInterval)
            {
                Save(player, now);
            }
        }
    }

    public void Shutdown()
    {
        foreach (var player in _bySession.Values.ToList())
            Leave(player.Session);
    }
}
