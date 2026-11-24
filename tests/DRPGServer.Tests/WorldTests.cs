using System.Collections.Frozen;
using DRPGServer.Data;
using DRPGServer.Data.GameData;
using DRPGServer.Game.Entities;
using DRPGServer.Game.World;
using DRPGServer.Network;
using DRPGServer.Network.Handlers;
using Microsoft.Extensions.Logging.Abstractions;

namespace DRPGServer.Tests;

public class InfrastructureTests
{
    [Fact]
    public void Every_handler_is_unique_and_implements_a_handler_interface()
    {
        var found = PacketHandlerRegistration.Discover().ToList();
        Assert.NotEmpty(found);
        Assert.Equal(found.Count, found.Select(f => (f.Attribute.Role, f.Attribute.Opcode)).Distinct().Count());
        Assert.All(found, f => Assert.True(
            typeof(ISessionPacketHandler).IsAssignableFrom(f.Type) || typeof(IWorldPacketHandler).IsAssignableFrom(f.Type), f.Type.Name));
    }

    [Fact]
    public void Migrations_are_discovered_in_order()
    {
        var migrations = DatabaseMigrator.Discover(Path.Combine(AppContext.BaseDirectory, "sql", "upgrade"));
        Assert.True(migrations.Count >= 2);
        Assert.Equal(migrations.Select(m => m.Version).Order(), migrations.Select(m => m.Version));
        Assert.Equal(1, migrations[0].Version);
    }

    [Fact]
    public void Game_data_loads_and_cross_references_resolve()
    {
        var data = TestSupport.Data;
        Assert.NotEmpty(data.Species);
        Assert.Contains((byte)79, data.ZoneIds);
        Assert.All(data.Spawns.Values.SelectMany(s => s).SelectMany(s => s.Options),
            o => Assert.True(data.Species.ContainsKey(o.SpeciesId)));
    }
}

public class ZoneTickTests
{
    [Fact]
    public void Map_tick_pings_and_recovers_leader_like_official()
    {
        // ✅ v322: a cada ~5,2 s o oficial manda 0xC4 e, se algo está abaixo do máximo, 0xB2 com HP +5, VP +3, EVP +10
        var world = TestSupport.NewWorld();
        var zones = new ZoneSystem(TestSupport.Data, world, NullLogger<ZoneSystem>.Instance);
        var session = TestSupport.NewSession();
        // nível 10: MaxEVP = L × (L − 1) = 90 (no nível 1 é 0)
        var character = new Character(Entities.Digimon(level: 10), new Inventory(999))
        {
            Uid = 1, AccountUid = 1, Slot = 0, Name = "Tester", TamerId = 1, MapId = 79, X = 56, Y = 56,
        };
        var player = new Player(session, new AuthenticatedAccount(1, "u", Authority.None), character);
        var zone = zones.Find(79)!;
        zone.Enter(player);
        var leader = player.Leader;
        leader.Heal(-50, -50, -50);
        var (hp, vp, evp) = (leader.Hp, leader.Vp, leader.Evp);
        session.DrainOutgoing();

        var due = player.NextRecoveryAt;
        zone.Update(due - TimeSpan.FromMilliseconds(100), TimeSpan.FromMilliseconds(5100));
        Assert.DoesNotContain((ushort)0xC4, session.SentOpcodes());

        zone.Update(due, TimeSpan.FromMilliseconds(100));
        var sent = session.SentOpcodes();
        Assert.Contains((ushort)0xC4, sent);
        Assert.Contains((ushort)0xB2, sent);
        Assert.Equal((hp + 5, vp + 3, evp + 10), (leader.Hp, leader.Vp, leader.Evp));
    }

    [Fact]
    public void Auto_recovery_skips_knocked_out_party_digimons()
    {
        var world = TestSupport.NewWorld();
        var zones = new ZoneSystem(TestSupport.Data, world, NullLogger<ZoneSystem>.Instance);
        var session = TestSupport.NewSession();
        var character = Entities.Character();
        var fallen = Entities.Digimon(level: 5);
        fallen.TakeDamage(fallen.Hp);
        character.Place(1, fallen);
        var player = new Player(session, new AuthenticatedAccount(1, "u", Authority.None), character);
        var zone = zones.Find(79)!;
        zone.Enter(player);
        session.DrainOutgoing();

        var due = player.NextRecoveryAt;
        zone.Update(due, Zone.RecoveryInterval);
        Assert.Equal(0, fallen.Hp);
        Assert.DoesNotContain((ushort)0xB2, session.SentOpcodes());
    }
}

internal static class Grids
{
    /// <summary>Grade de teste: tudo andável menos a coluna x = <paramref name="wallX"/>.</summary>
    public static MapGrid WithWall(int w, int h, int wallX)
    {
        var bits = new byte[(w * h + 7) / 8];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                if (x != wallX) bits[(y * w + x) >> 3] |= (byte)(1 << ((y * w + x) & 7));
        return new MapGrid(w, h, bits);
    }

    public static GameData DataWithGrid(byte map, MapGrid grid) =>
        GameDataLoader.Load(Path.Combine(AppContext.BaseDirectory, "Resources"), TestSupport.Pack,
            new Dictionary<byte, MapGrid> { [map] = grid }.ToFrozenDictionary());
}

public class MapGridTests
{
    [Fact]
    public void Reads_walk_bit_from_client_map_cells()
    {
        // u32 largura, u32 altura, u32 64, u32 32, células de 24 bytes; bit 0 do 6º u32 = andável
        var d = new byte[16 + 24 * 6];
        BitConverter.TryWriteBytes(d.AsSpan(0), 3);
        BitConverter.TryWriteBytes(d.AsSpan(4), 2);
        foreach (int i in new[] { 0, 4, 5 }) d[16 + 24 * i + 20] = 1;
        d[16 + 24 * 1 + 20] = 0x00; d[16 + 24 * 1 + 21] = 0x01;   // só o bit 8: não é andável
        var g = MapGrid.FromClientMap(d);
        Assert.Equal((3, 2), (g.Width, g.Height));
        Assert.True(g.IsWalkable(0, 0));
        Assert.False(g.IsWalkable(1, 0));
        Assert.True(g.IsWalkable(1, 1));     // célula 4 = (1, 1)
        Assert.False(g.IsWalkable(3, 0));    // fora
        Assert.True(g.IsNearWalkable(1, 0));
    }

    [Fact]
    public void Grids_file_round_trips()
    {
        var path = Path.GetTempFileName();
        MapGrid.Save(path, new Dictionary<byte, MapGrid> { [79] = Grids.WithWall(10, 7, 4) });
        var g = MapGrid.Load(path)[79];
        File.Delete(path);
        Assert.Equal((10, 7), (g.Width, g.Height));
        Assert.False(g.IsWalkable(4, 3));
        Assert.True(g.IsWalkable(5, 3));
    }
}

public class MovementTests
{
    private readonly GameWorld _world = TestSupport.NewWorld();
    private readonly ZoneSystem _zones;
    private readonly Session _session = TestSupport.NewSession();
    private readonly Player _player;

    public MovementTests()
    {
        // parede de 3 colunas em x = 59..61: x = 60 fica a 2 células da área livre
        var data = Grids.DataWithGrid(79, WallOf3(100, 100));
        _zones = new ZoneSystem(data, _world, NullLogger<ZoneSystem>.Instance);
        _player = new Player(_session, new AuthenticatedAccount(1, "u", Authority.None), Entities.Character());
        _zones.Find(79)!.Enter(_player);
        _session.DrainOutgoing();
    }

    private static MapGrid WallOf3(int w, int h)
    {
        var bits = new byte[(w * h + 7) / 8];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                if (x is < 59 or > 61) bits[(y * w + x) >> 3] |= (byte)(1 << ((y * w + x) & 7));
        return new MapGrid(w, h, bits);
    }

    [Fact]
    public void Normal_step_is_accepted()
    {
        _zones.Move(_player, 52, 50);                       // de (56, 56): 7,2 células
        Assert.Equal((52, 50), (_player.Character.X, _player.Character.Y));
        Assert.Empty(_session.SentOpcodes());
    }

    [Fact]
    public void Step_next_to_a_wall_is_accepted_like_official()
    {
        _zones.Move(_player, 59, 56);                       // bloqueada, mas encostada em x = 58
        Assert.Equal(59, _player.Character.X);
    }

    [Fact]
    public void Step_into_a_wall_snaps_back()
    {
        _zones.Move(_player, 60, 56);
        Assert.Equal((56, 56), (_player.Character.X, _player.Character.Y));
        Assert.Equal([(ushort)0xB0], _session.SentOpcodes());
    }

    [Fact]
    public void Too_far_in_one_report_snaps_back()
    {
        _zones.Move(_player, 56, 90);                       // 34 células de uma vez
        Assert.Equal(56, _player.Character.Y);
        Assert.Equal([(ushort)0xB0], _session.SentOpcodes());
    }

    [Fact]
    public void Allowed_distance_grows_with_time_but_not_while_idle()
    {
        Assert.Equal(Movement.MaxDistance(TimeSpan.Zero), Movement.MaxDistance(Movement.ReportInterval));
        Assert.True(Movement.MaxDistance(Movement.ReportInterval) >= 9.3);     // maior passo capturado: 9,2
        Assert.Equal(Movement.MaxDistance(TimeSpan.FromSeconds(2)), Movement.MaxDistance(TimeSpan.FromMinutes(5)));
    }
}

public class WildRoamingTests
{
    private static readonly MapGrid Grid = Grids.WithWall(100, 100, 30);   // corta as caixas das casas

    [Fact]
    public void Wild_roams_only_around_home_on_walkable_cells()
    {
        var world = TestSupport.NewWorld();
        var zone = new ZoneSystem(Grids.DataWithGrid(79, Grid), world, NullLogger<ZoneSystem>.Instance).Find(79)!;
        var wilds = zone.WildDigimons.ToList();
        Assert.Equal([(27, 65), (29, 66), (31, 67)], wilds.Select(w => ((int)w.HomeX, (int)w.HomeY)));
        var moved = false;
        for (int i = 0; i < 2000; i++)
        {
            zone.Update(TimeSpan.Zero, Zone.WildTickInterval);
            foreach (var w in wilds)
            {
                Assert.InRange(w.X, w.HomeX - WildDigimon.RoamRadius, w.HomeX + WildDigimon.RoamRadius);
                Assert.InRange(w.Y, w.HomeY - WildDigimon.RoamRadius, w.HomeY + WildDigimon.RoamRadius);
                Assert.True(Grid.IsWalkable(w.DestX, w.DestY));
                moved |= (w.X, w.Y) != (w.HomeX, w.HomeY);
            }
        }
        Assert.True(moved);
    }

    [Fact]
    public void Move_is_two_ticks_moving_then_idle_and_every_tick_is_sent()
    {
        var world = TestSupport.NewWorld();
        var zone = new ZoneSystem(TestSupport.Data, world, NullLogger<ZoneSystem>.Instance).Find(79)!;
        var session = TestSupport.NewSession();
        var character = Entities.Character();
        character.X = 29;
        character.Y = 66;
        zone.Enter(new Player(session, new AuthenticatedAccount(1, "u", Authority.None), character));
        session.DrainOutgoing();
        var w = zone.WildDigimons.First();

        w.StartMove((short)(w.X + 2), (short)(w.Y - 2));
        zone.Update(TimeSpan.Zero, Zone.WildTickInterval);   // chega, ainda "andando"
        Assert.Equal((WildState.Moving, w.DestX, w.DestY, Direction.NorthEast), (w.State, w.X, w.Y, w.Facing));
        zone.Update(TimeSpan.Zero, Zone.WildTickInterval);   // para
        Assert.Equal(WildState.Idle, w.State);
        Assert.Equal(2 * zone.WildDigimons.Count(), session.SentOpcodes().Count(op => op == 0x11));
    }

    [Fact]
    public void Wild_updates_are_filtered_by_player_view_range()
    {
        var world = TestSupport.NewWorld();
        var zone = new ZoneSystem(TestSupport.Data, world, NullLogger<ZoneSystem>.Instance).Find(79)!;

        // Jogador longe dos Yuramon em (27..31, 65..67): não recebe 0x11 na entrada nem no tick
        var farSession = TestSupport.NewSession();
        var farChar = Entities.Character();
        farChar.X = 56;
        farChar.Y = 56;
        var farPlayer = new Player(farSession, new AuthenticatedAccount(1, "far", Authority.None), farChar);
        zone.Enter(farPlayer);
        Assert.DoesNotContain((ushort)0x11, farSession.SentOpcodes());

        // Jogador perto dos Yuramon: recebe todos os 3 na entrada e a cada tick
        var nearSession = TestSupport.NewSession();
        var nearChar = Entities.Character();
        nearChar.X = 29;
        nearChar.Y = 66;
        zone.Enter(new Player(nearSession, new AuthenticatedAccount(2, "near", Authority.None), nearChar));
        Assert.Equal(3, nearSession.SentOpcodes().Count(op => op == 0x11));

        zone.Update(TimeSpan.Zero, Zone.WildTickInterval);
        Assert.DoesNotContain((ushort)0x11, farSession.SentOpcodes());
        Assert.Equal(3, nearSession.SentOpcodes().Count(op => op == 0x11));

        // Jogador distante se aproxima para dentro da janela (±12 em X, ±24 em Y): passa a receber no próximo tick
        farChar.X = 35;
        farChar.Y = 66;
        zone.Update(TimeSpan.Zero, Zone.WildTickInterval);
        Assert.Equal(3, farSession.SentOpcodes().Count(op => op == 0x11));
    }

    [Fact]
    public void Defeated_group_shows_dead_then_leaves_then_respawns_at_home()
    {
        var world = TestSupport.NewWorld();
        var zone = new ZoneSystem(TestSupport.Data, world, NullLogger<ZoneSystem>.Instance).Find(79)!;
        var w = zone.WildDigimons.First();
        Assert.Equal(Direction.South, w.Facing); // começa em direção cardeal (sem animação Dead no cliente)
        zone.Kill(w);
        Assert.Equal(8, w.StateCode);
        Assert.True(w.Facing is Direction.NorthWest or Direction.SouthEast);
        var ticks = (int)Math.Ceiling(Zone.DeadDisplayTime / Zone.WildTickInterval);
        for (int i = 0; i < ticks; i++) zone.Update(world.Now + Zone.WildTickInterval * (i + 1), Zone.WildTickInterval);
        Assert.Equal(12, w.StateCode);
        var later = world.Now + Zone.DeadDisplayTime + w.Spawn.RespawnTime + Zone.WildTickInterval * (ticks + 2);
        zone.Update(later, Zone.WildTickInterval);
        Assert.DoesNotContain(w, zone.WildDigimons);
        Assert.Contains(zone.WildDigimons, f => (f.HomeX, f.HomeY) == (w.HomeX, w.HomeY));
    }

    [Fact]
    public void Wild_battle_facing_snaps_to_diagonal_3_or_7()
    {
        var world = TestSupport.NewWorld();
        var zone = new ZoneSystem(TestSupport.Data, world, NullLogger<ZoneSystem>.Instance).Find(79)!;
        var w = zone.WildDigimons.First();

        // Jogador à esquerda/noroeste -> NorthWest (3)
        w.FaceBattleDiagonal((short)(w.X - 2), (short)(w.Y - 1));
        Assert.Equal(Direction.NorthWest, w.Facing);

        // Jogador à direita/sudeste -> SouthEast (7)
        w.FaceBattleDiagonal((short)(w.X + 2), (short)(w.Y + 1));
        Assert.Equal(Direction.SouthEast, w.Facing);

        // Movimento cardeal (East) -> corrigido para diagonal em EnsureBattleDiagonal
        w.StartMove((short)(w.X + 1), w.Y);
        w.Arrive();
        Assert.Equal(Direction.East, w.Facing);
        w.EnsureBattleDiagonal();
        Assert.True(w.Facing is Direction.NorthWest or Direction.SouthEast);
    }
}
