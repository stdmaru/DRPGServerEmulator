using DRPGServer.Data.GameData;
using DRPGServer.Game.Entities;
using DRPGServer.Network.Packets.Map;
using DRPGServer.Network.Protocol;

namespace DRPGServer.Game.World;

/// <summary>Um mapa: jogadores, digimons selvagens, itens no chão e portais. Só na thread do mundo.</summary>
public sealed class Zone
{
    private static readonly TimeSpan DropResendInterval = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan WildTickInterval = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan RecoveryInterval = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan DropCleanInterval = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan DropLifetime = TimeSpan.FromSeconds(60);
    private const int WildMoveChancePercent = 50;

    private readonly GameData _data;
    private readonly GameWorld _world;
    private readonly List<Player> _players = [];
    private readonly Dictionary<Serial, WildDigimon> _wild = [];
    private readonly Dictionary<uint, ItemDrop> _drops = [];
    private TimeSpan _dropTimer, _wildTimer, _recoveryTimer, _cleanTimer;
    private uint _nextDropUid = 10_000;

    public Zone(byte mapId, GameData data, GameWorld world)
    {
        MapId = mapId;
        _data = data;
        _world = world;
        Portals = data.PortalsOf(mapId);
        SpawnAll();
    }

    public byte MapId { get; }
    public IReadOnlyList<PortalInfo> Portals { get; }
    public IReadOnlyList<Player> Players => _players;
    public IEnumerable<WildDigimon> WildDigimons => _wild.Values;

    public override string ToString() => Enum.IsDefined((MapId)MapId) ? $"{(MapId)MapId}({MapId})" : MapId.ToString();

    public void Update(TimeSpan now, TimeSpan dt)
    {
        if (Every(ref _dropTimer, dt, DropResendInterval))
            foreach (var drop in _drops.Values)
                Broadcast(new ItemDropPacket(drop));

        if (Every(ref _wildTimer, dt, WildTickInterval))
            UpdateWild(now);

        if (Every(ref _recoveryTimer, dt, RecoveryInterval))
            AutoRecovery();

        if (Every(ref _cleanTimer, dt, DropCleanInterval))
            foreach (var drop in _drops.Values.Where(d => now - d.DroppedAt >= DropLifetime).ToList())
                RemoveDrop(drop);
    }

    private static bool Every(ref TimeSpan timer, TimeSpan dt, TimeSpan interval)
    {
        timer += dt;
        if (timer < interval) return false;
        timer = TimeSpan.Zero;
        return true;
    }

    // ---------- jogadores ----------

    public void Enter(Player player)
    {
        _world.AssertWorldThread();
        _players.Add(player);
        player.Zone = this;
        foreach (var wild in _wild.Values)
            player.Send(new WildDigimonUpdatePacket(wild));
    }

    public void Leave(Player player)
    {
        _world.AssertWorldThread();
        _players.Remove(player);
        if (player.Zone == this) player.Zone = null;
    }

    public void Broadcast(ServerPacket packet, Player? except = null)
    {
        if (_players.Count == 0) return;
        var frame = packet.Encode();
        foreach (var player in _players)
            if (player != except)
                player.Session.SendFrame(frame);
    }

    private void AutoRecovery()
    {
        foreach (var player in _players)
        {
            if (player.InBattle) continue;
            var leader = player.Leader;
            var before = (leader.Hp, leader.Vp, leader.Evp);
            leader.Heal(5, 3, 0);
            if ((leader.Hp, leader.Vp, leader.Evp) != before)
                player.Send(new RefreshDigimonStatusPacket(leader));
        }
    }

    // ---------- digimons selvagens ----------

    public WildDigimon? FindWild(Serial serial) => _wild.GetValueOrDefault(serial);

    private void SpawnAll()
    {
        foreach (var area in _data.SpawnsOf(MapId))
            for (int i = 0; i < area.MaxCount; i++)
                AddWild(new WildDigimon(_data, area, _world.Random));
    }

    private void AddWild(WildDigimon wild) => _wild.Add(wild.Serial, wild);

    private void UpdateWild(TimeSpan now)
    {
        var random = _world.Random;
        foreach (var wild in _wild.Values.ToList())
        {
            switch (wild.State)
            {
                case WildState.Dead when now >= wild.RespawnAt:
                    _wild.Remove(wild.Serial);
                    var fresh = new WildDigimon(_data, wild.Spawn, random);
                    AddWild(fresh);
                    Broadcast(new WildDigimonUpdatePacket(fresh));
                    continue;
                case WildState.Dead:
                    continue;
                case WildState.Roaming when random.Next(100) < WildMoveChancePercent:
                    var area = wild.Spawn;
                    short x = (short)Math.Clamp(wild.X + random.Next(-2, 3), area.XMin, area.XMax);
                    short y = (short)Math.Clamp(wild.Y + random.Next(-2, 3), area.YMin, area.YMax);
                    if (x != wild.X || y != wild.Y) wild.MoveTo(x, y);
                    break;
            }
            Broadcast(new WildDigimonUpdatePacket(wild));
        }
    }

    /// <summary>Marca o grupo como derrotado: some do mapa e volta depois do tempo de respawn.</summary>
    public void Kill(WildDigimon wild)
    {
        wild.State = WildState.Dead;
        wild.RespawnAt = _world.Now + wild.Spawn.RespawnTime;
        Broadcast(new WildDigimonUpdatePacket(wild));
    }

    public void ReloadSpawns()
    {
        foreach (var wild in _wild.Values)
        {
            wild.State = WildState.Dead;
            Broadcast(new WildDigimonUpdatePacket(wild));
        }
        _wild.Clear();
        SpawnAll();
        foreach (var wild in _wild.Values)
            Broadcast(new WildDigimonUpdatePacket(wild));
    }

    // ---------- itens no chão ----------

    public ItemDrop Drop(uint itemId, short x, short y, uint? ownerCharacterUid = null)
    {
        var random = _world.Random;
        var drop = new ItemDrop(_nextDropUid++, itemId, (short)(x + random.Next(-1, 2)), (short)(y + random.Next(-1, 2)),
            _world.Now, ownerCharacterUid);
        _drops.Add(drop.Uid, drop);
        Broadcast(new ItemDropPacket(drop));
        return drop;
    }

    public ItemDrop? FindDrop(uint uid) => _drops.GetValueOrDefault(uid);

    public void RemoveDrop(ItemDrop drop)
    {
        if (_drops.Remove(drop.Uid))
            Broadcast(new ItemDropPacket(drop, removed: true));
    }
}
