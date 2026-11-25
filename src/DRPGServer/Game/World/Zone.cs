using DRPGServer.Data.GameData;
using DRPGServer.Game.Entities;
using DRPGServer.Network;
using DRPGServer.Network.Packets.Map;
using DRPGServer.Network.Protocol;

namespace DRPGServer.Game.World;

/// <summary>Um mapa: jogadores, digimons selvagens, itens no chão e portais. Só na thread do mundo.</summary>
public sealed class Zone
{
    // ✅ v322: o oficial manda o 0x11 de cada selvagem a cada ~344 ms (mediana de 72 mil pacotes), parado ou não
    public static readonly TimeSpan WildTickInterval = TimeSpan.FromMilliseconds(344);
    // ✅ derrotado (8) por ~10,4 s antes de sair do mapa (12); ❓ só 2 amostras
    public static readonly TimeSpan DeadDisplayTime = TimeSpan.FromSeconds(10.4);
    // ✅ v322 (sessões 52–56): o oficial manda 0xC4 e a recuperação no mesmo tick, mediana 5,2 s (p10 5,1, p90 5,3)
    public static readonly TimeSpan RecoveryInterval = TimeSpan.FromMilliseconds(5200);
    /// <summary>✅ Primeira recuperação depois da batalha: mediana 0,2 s depois do 0x6D (41 batalhas, 0,04–0,34 s).</summary>
    public static readonly TimeSpan RecoveryAfterBattle = TimeSpan.FromMilliseconds(200);
    /// <summary>✅ Itens no chão expiram em ~60 s nas capturas v322 (sessões 56 e 61: 60,3–60,5 s).</summary>
    public static readonly TimeSpan DropCleanInterval = TimeSpan.FromSeconds(1);
    public static readonly TimeSpan DropLifetime = TimeSpan.FromSeconds(60);
    /// <summary>✅ Chance por tick de um selvagem parado começar a andar: 3276 de 65 462 ticks parados (5 %).</summary>
    public const double WildMoveChance = 0.05;
    /// <summary>
    /// ✅ Janela de visibilidade (AOI) dos selvagens nas capturas v322 (~100 mil pacotes 0x11 com jogador parado):
    /// |dx| ≤ 12 e |dy| ≤ 23 (grade isométrica onde X vale o dobro de Y na tela; usamos 24 em Y por margem).
    /// Quando o jogador se afasta, o servidor simplesmente deixa de enviar o 0x11 (não manda estado 12).
    /// </summary>
    public const int WildViewRangeX = 12;
    public const int WildViewRangeY = 24;
    private const int HomeAttempts = 32;

    private readonly GameData _data;
    private readonly GameWorld _world;
    private readonly IGameClock _clock;
    private readonly List<Player> _players = [];
    private readonly Dictionary<Serial, WildDigimon> _wild = [];
    private readonly Dictionary<uint, ItemDrop> _drops = [];
    private TimeSpan _wildTimer, _recoveryTimer, _cleanTimer;
    private uint _nextDropUid = 10_000;

    public Zone(byte mapId, GameData data, GameWorld world, IGameClock clock)
    {
        MapId = mapId;
        _data = data;
        _world = world;
        _clock = clock;
        Portals = data.PortalsOf(mapId);
        Grid = data.GridOf(mapId);
        SpawnAll();
    }

    public byte MapId { get; }
    /// <summary>Grade do mapa; null = sem grade extraída (tudo andável).</summary>
    public MapGrid? Grid { get; }

    public bool IsWalkable(int x, int y) => Grid?.IsWalkable(x, y) ?? true;
    public bool IsNearWalkable(int x, int y) => Grid?.IsNearWalkable(x, y) ?? true;
    public IReadOnlyList<PortalInfo> Portals { get; }
    public IReadOnlyList<Player> Players => _players;
    public IEnumerable<WildDigimon> WildDigimons => _wild.Values;

    public override string ToString() => Enum.IsDefined((MapId)MapId) ? $"{(MapId)MapId}({MapId})" : MapId.ToString();

    public void Update(TimeSpan now, TimeSpan dt)
    {
        if (Every(ref _wildTimer, dt, WildTickInterval))
            UpdateWild(now);

        if (Every(ref _recoveryTimer, dt, RecoveryInterval))
            foreach (var player in _players)
                player.Send(new KeepAlivePacket((ushort)MapOpcode.KeepAlive));
        AutoRecovery(now);

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
        player.NextRecoveryAt = _world.Now + RecoveryInterval;
        foreach (var wild in _wild.Values)
            if (wild.State != WildState.Gone && IsInView(player, wild.X, wild.Y))
                player.Send(new WildDigimonUpdatePacket(wild));
        foreach (var drop in _drops.Values)
            player.Send(new ItemDropPacket(drop));
    }

    public void Leave(Player player)
    {
        _world.AssertWorldThread();
        _players.Remove(player);
        if (player.Zone == this) player.Zone = null;
    }

    public static bool IsInView(Player player, short x, short y) =>
        Math.Abs(x - player.Character.X) <= WildViewRangeX
        && Math.Abs(y - player.Character.Y) <= WildViewRangeY;

    public void Broadcast(ServerPacket packet, Player? except = null)
    {
        if (_players.Count == 0) return;
        var frame = packet.Encode();
        foreach (var player in _players)
            if (player != except)
                player.Session.SendFrame(frame);
    }

    public void BroadcastNear(short x, short y, ServerPacket packet, Player? except = null)
    {
        if (_players.Count == 0) return;
        byte[]? frame = null;
        foreach (var player in _players)
        {
            if (player == except || !IsInView(player, x, y)) continue;
            frame ??= packet.Encode();
            player.Session.SendFrame(frame);
        }
    }

    /// <summary>
    /// Recuperação do time a cada ~5,2 s, por jogador. ✅ 349 ticks: HP +5, VP +3, EVP +10, até o máximo;
    /// um 0xB2 por digimon que mudou (rajadas de até 5 na sessão 56). ✅ Nenhum 0xB2 durante as 67 batalhas v322; no fim o relógio recomeça com
    /// <see cref="RecoveryAfterBattle"/> (ver <see cref="Player.NextRecoveryAt"/>). O ping 0xC4 segue no tick da zona.
    /// </summary>
    private void AutoRecovery(TimeSpan now)
    {
        long unixNow = _clock.UnixNow;
        foreach (var player in _players)
        {
            if (player.InBattle || now < player.NextRecoveryAt) continue;
            player.NextRecoveryAt = now + RecoveryInterval;
            ExpireTimedItems(player, unixNow);
            var equip = player.Character.TotalEquipmentBonus;
            int hpGain = 5 + equip.HpRecovery;
            int vpGain = 3 + equip.VpRecovery;
            int evpGain = 10 + equip.EvpRecovery;
            foreach (var digimon in player.Character.Digimons)
            {
                // ✅ conn=408: digimons nocauteados (HP = 0) não regeneram automaticamente no mapa
                if (digimon.IsKnockedOut) continue;
                var before = (digimon.Hp, digimon.Vp, digimon.Evp);
                digimon.Heal(hpGain, vpGain, evpGain);
                if ((digimon.Hp, digimon.Vp, digimon.Evp) != before)
                    player.Send(new RefreshDigimonStatusPacket(digimon));
            }
        }
    }

    /// <summary>Remove itens de período vencidos no inventário e nos equipamentos do jogador fora de batalha.</summary>
    private void ExpireTimedItems(Player player, long unixNow)
    {
        var c = player.Character;
        int expiredBag = c.Inventory.Main.RemoveExpired(unixNow);
        int expiredEquip = c.Inventory.Equipment.RemoveExpired(unixNow);
        if (expiredBag == 0 && expiredEquip == 0) return;

        player.RequestImmediateSave();
        if (expiredBag > 0)
            player.RefreshInventory();
        if (expiredEquip > 0)
        {
            c.SyncEquipmentBonuses();
            if (!c.TotalEquipmentBonus.MaintainEvolution)
                foreach (var d in c.Digimons)
                    if (d.IsEvolved) d.Devolve();
            player.Send(new TamerExpPacket(c));
            foreach (var d in c.Digimons)
                player.Send(new BattleDigimonStatusPacket(d));
            Broadcast(new RefreshCharacterInfoPacket(c));
        }
    }

    // ---------- digimons selvagens ----------

    public WildDigimon? FindWild(Serial serial) => _wild.GetValueOrDefault(serial);

    private void SpawnAll()
    {
        foreach (var area in _data.SpawnsOf(MapId))
            for (int i = 0; i < area.MaxCount; i++)
                if (Spawn(area, i) is { } wild)
                    AddWild(wild);
    }

    /// <summary>Novo grupo na casa <paramref name="slot"/> da área (ponto fixo, ou célula andável sorteada).</summary>
    private WildDigimon? Spawn(SpawnArea area, int slot)
    {
        if (area.Points.Count > 0)
        {
            var (px, py) = area.Points[slot % area.Points.Count];
            return new WildDigimon(_data, area, px, py) { Slot = slot };
        }
        var random = _world.Random;
        for (int i = 0; i < HomeAttempts; i++)
        {
            short x = (short)random.Next(area.XMin, area.XMax + 1), y = (short)random.Next(area.YMin, area.YMax + 1);
            if (IsWalkable(x, y)) return new WildDigimon(_data, area, x, y) { Slot = slot };
        }
        return null;
    }

    private void AddWild(WildDigimon wild) => _wild.Add(wild.Serial, wild);

    /// <summary>
    /// Tick dos selvagens (docs/mecanicas/selvagens.md): parado → 5 % de chance de escolher um destino andável na
    /// caixa da casa; andando → chega no tick seguinte e para no outro. Todos vão no 0x11 a cada tick.
    /// </summary>
    private void UpdateWild(TimeSpan now)
    {
        var random = _world.Random;
        foreach (var wild in _wild.Values.ToList())
        {
            switch (wild.State)
            {
                case WildState.Idle when random.NextDouble() < WildMoveChance:
                    if (PickDestination(wild, random) is var (x, y)) wild.StartMove(x, y);
                    break;
                case WildState.Moving when (wild.X, wild.Y) != (wild.DestX, wild.DestY):
                    wild.Arrive();
                    break;
                case WildState.Moving:
                    wild.Stop();
                    break;
                case WildState.Dead when now - wild.DiedAt >= DeadDisplayTime:
                    wild.State = WildState.Gone;
                    break;
                case WildState.Gone when now - wild.DiedAt >= DeadDisplayTime + wild.Spawn.RespawnTime:
                    _wild.Remove(wild.Serial);
                    if (Spawn(wild.Spawn, wild.Slot) is { } fresh)
                    {
                        AddWild(fresh);
                        BroadcastNear(fresh.X, fresh.Y, new WildDigimonUpdatePacket(fresh));
                    }
                    continue;
                case WildState.Gone:
                    continue;
            }
            BroadcastNear(wild.X, wild.Y, new WildDigimonUpdatePacket(wild));
        }
    }

    private (short, short)? PickDestination(WildDigimon wild, Random random)
    {
        const int r = WildDigimon.RoamRadius;
        for (int i = 0; i < 8; i++)
        {
            short x = (short)(wild.HomeX + random.Next(-r, r + 1)), y = (short)(wild.HomeY + random.Next(-r, r + 1));
            if ((x, y) != (wild.X, wild.Y) && IsWalkable(x, y)) return (x, y);
        }
        return null;
    }

    /// <summary>Marca o grupo como derrotado: some do mapa e volta depois do tempo de respawn.</summary>
    public void Kill(WildDigimon wild)
    {
        wild.State = WildState.Dead;
        wild.EnsureBattleDiagonal();
        wild.DiedAt = _world.Now;
        BroadcastNear(wild.X, wild.Y, new WildDigimonUpdatePacket(wild));
    }

    public void ReloadSpawns()
    {
        foreach (var wild in _wild.Values)
        {
            wild.State = WildState.Gone;
            Broadcast(new WildDigimonUpdatePacket(wild));
        }
        _wild.Clear();
        SpawnAll();
        foreach (var wild in _wild.Values)
            BroadcastNear(wild.X, wild.Y, new WildDigimonUpdatePacket(wild));
    }

    // ---------- itens no chão ----------

    public ItemDrop Drop(uint itemId, short x, short y, uint? ownerCharacterUid = null)
    {
        var (dx, dy) = PickDropCell(x, y);
        var drop = new ItemDrop(_nextDropUid++, itemId, dx, dy,
            _world.Now, ownerCharacterUid) { Info = _data.FindItem(itemId) };
        _drops.Add(drop.Uid, drop);
        Broadcast(new ItemDropPacket(drop));
        return drop;
    }

    /// <summary>
    /// Escolhe uma célula livre ao redor de (x, y) para evitar colisão de coordenadas — o cliente remove drops pelo
    /// par (X, Y) no 0x13, então dois itens na mesma célula se sobrescreveriam.
    /// </summary>
    private (short X, short Y) PickDropCell(short x, short y)
    {
        var random = _world.Random;
        for (int attempt = 0; attempt < 9; attempt++)
        {
            short cx = (short)(x + random.Next(-1, 2));
            short cy = (short)(y + random.Next(-1, 2));
            if (!_drops.Values.Any(d => d.X == cx && d.Y == cy))
                return (cx, cy);
        }
        for (int dy = -2; dy <= 2; dy++)
            for (int dx = -2; dx <= 2; dx++)
            {
                short cx = (short)(x + dx), cy = (short)(y + dy);
                if (!_drops.Values.Any(d => d.X == cx && d.Y == cy))
                    return (cx, cy);
            }
        return (x, y);
    }

    public ItemDrop? FindDrop(uint uid) => _drops.GetValueOrDefault(uid);
    public IEnumerable<ItemDrop> Drops => _drops.Values;

    public void RemoveDrop(ItemDrop drop)
    {
        if (_drops.Remove(drop.Uid))
            Broadcast(new ItemDropPacket(drop, removed: true));
    }
}
