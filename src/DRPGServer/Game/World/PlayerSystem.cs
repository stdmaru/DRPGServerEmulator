using DRPGServer.Configuration;
using DRPGServer.Data.Persistence;
using DRPGServer.Data.Repositories;
using DRPGServer.Game.Battle;
using DRPGServer.Game.Entities;
using DRPGServer.Network;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DRPGServer.Game.World;

/// <summary>Quem está online no mapa: entrada, saída e salvamento.</summary>
public sealed class PlayerSystem : IWorldSystem
{
    private readonly Dictionary<long, Player> _bySession = [];
    private readonly ZoneSystem _zones;
    private readonly BattleSystem _battles;
    private readonly PersistenceQueue _persistence;
    private readonly CharacterRepository _characters;
    private readonly GameOptions _options;
    private readonly ILogger<PlayerSystem> _log;
    private TimeSpan _autosaveTimer;

    public PlayerSystem(GameWorld world, ZoneSystem zones, BattleSystem battles, PersistenceQueue persistence,
        CharacterRepository characters, IOptions<GameOptions> options, ILogger<PlayerSystem> log)
    {
        _zones = zones;
        _battles = battles;
        _persistence = persistence;
        _characters = characters;
        _options = options.Value;
        _log = log;
        world.AddSystem(this);
    }

    public IEnumerable<Player> Online => _bySession.Values;
    public int Count => _bySession.Count;

    /// <summary>Coloca o personagem no mundo. Derruba outra sessão de mapa da mesma conta.</summary>
    public Player? Enter(Session session, AuthenticatedAccount account, Character character)
    {
        if (session.IsClosed) return null;
        foreach (var other in _bySession.Values.Where(p => p.Account.Uid == account.Uid).ToList())
        {
            Leave(other.Session);
            other.Session.Close("a conta entrou no mapa por outra conexão");
        }

        var zone = _zones.Find(character.MapId);
        if (zone is null)
        {
            _log.LogWarning("{Name}: mapa {Map} não carregado, indo para o inicial", character.Name, character.MapId);
            zone = _zones.Find(_options.StartMapId)
                ?? throw new InvalidOperationException($"mapa inicial {_options.StartMapId} não carregado");
            character.MapId = zone.MapId;
            character.X = _options.StartPosX;
            character.Y = _options.StartPosY;
        }

        var player = new Player(session, account, character);
        session.Player = player;
        _bySession[session.Id] = player;
        zone.Enter(player);
        _log.LogInformation("{Player} entrou no mundo em {Zone} ({Count} online)", player, zone, _bySession.Count);
        return player;
    }

    public void Leave(Session session)
    {
        if (!_bySession.Remove(session.Id, out var player)) return;
        _battles.PlayerLeft(player);
        player.Zone?.Leave(player);
        session.Player = null;
        Save(player);
        _log.LogInformation("{Player} saiu do mundo ({Count} online)", player, _bySession.Count);
    }

    public Player? FindByName(string name) =>
        _bySession.Values.FirstOrDefault(p => string.Equals(p.Character.Name, name, StringComparison.OrdinalIgnoreCase));

    private void Save(Player player) => _persistence.SaveCharacter(_characters, CharacterSnapshot.Of(player.Character));

    public void Update(TimeSpan now, TimeSpan dt)
    {
        _autosaveTimer += dt;
        if (_autosaveTimer < TimeSpan.FromSeconds(_options.AutosaveSeconds)) return;
        _autosaveTimer = TimeSpan.Zero;
        foreach (var player in _bySession.Values)
            Save(player);
    }

    public void Shutdown()
    {
        foreach (var player in _bySession.Values.ToList())
            Leave(player.Session);
    }
}
