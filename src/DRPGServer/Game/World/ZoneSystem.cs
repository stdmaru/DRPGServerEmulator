using DRPGServer.Data.GameData;
using DRPGServer.Game.Entities;
using DRPGServer.Network.Packets.Map;
using Microsoft.Extensions.Logging;

namespace DRPGServer.Game.World;

/// <summary>Todas as zonas e a troca de mapa.</summary>
public sealed class ZoneSystem : IWorldSystem
{
    private readonly Dictionary<byte, Zone> _zones;
    private readonly ILogger<ZoneSystem> _log;
    private readonly GameWorld _world;

    public ZoneSystem(GameData data, GameWorld world, IGameClock clock, ILogger<ZoneSystem> log)
    {
        _log = log;
        _world = world;
        _zones = data.ZoneIds.ToDictionary(id => id, id => new Zone(id, data, world, clock));
        _log.LogInformation("{Count} zonas, {Wild} grupos selvagens", _zones.Count, _zones.Values.Sum(z => z.WildDigimons.Count()));
        world.AddSystem(this);
    }

    public IEnumerable<Zone> Zones => _zones.Values;

    public Zone? Find(byte mapId) => _zones.GetValueOrDefault(mapId);

    public void Update(TimeSpan now, TimeSpan dt)
    {
        foreach (var zone in _zones.Values)
            zone.Update(now, dt);
    }

    /// <summary>Move o jogador (mesmo mapa ou outro) e avisa o cliente com 0xB0.</summary>
    public bool Teleport(Player player, byte mapId, short x, short y)
    {
        bool changedMap = player.Zone?.MapId != mapId;
        if (changedMap)
        {
            if (Find(mapId) is not { } target)
            {
                _log.LogWarning("{Player}: mapa {Map} não existe", player, mapId);
                return false;
            }
            player.Zone?.Leave(player);
            target.Enter(player);
            _log.LogDebug("{Player} entrou em {Zone}", player, target);
        }
        player.Character.MapId = mapId;
        player.Character.X = x;
        player.Character.Y = y;
        player.LastMoveAt = _world.Now;
        if (changedMap) player.RequestImmediateSave();
        else player.MarkDirty();
        player.Send(new TeleportCharacterPacket(mapId, x, y));
        return true;
    }

    /// <summary>
    /// Aplica um 0x20. Passo inválido (célula bloqueada ou rápido demais) não é aceito: o jogador volta para a
    /// última posição válida com 0xB0 (❓ o oficial não foi observado recusando passo).
    /// </summary>
    public void Move(Player player, short x, short y)
    {
        if (player.Zone is not { } zone) return;
        var c = player.Character;
        var verdict = Movement.Check(zone, c.X, c.Y, x, y, _world.Now - player.LastMoveAt);
        if (verdict != Movement.Verdict.Ok)
        {
            _log.LogInformation("{Player}: passo recusado ({Verdict}) de ({X0}, {Y0}) para ({X}, {Y}) em {Zone}",
                player, verdict, c.X, c.Y, x, y, zone);
            Teleport(player, zone.MapId, c.X, c.Y);
            return;
        }
        c.X = x;
        c.Y = y;
        player.LastMoveAt = _world.Now;
        player.MarkDirty();
        if (zone.Portals.FirstOrDefault(p => p.Contains(x, y)) is { } portal)
            Teleport(player, portal.DestMapId, portal.DestX, portal.DestY);
    }
}
