using DRPGServer.Data;
using DRPGServer.Game.Battle;
using DRPGServer.Game.Entities;
using DRPGServer.Game.Stats;
using DRPGServer.Game.World;
using DRPGServer.Network;
using DRPGServer.Network.Handlers;
using Microsoft.Extensions.Logging.Abstractions;

namespace DRPGServer.Tests;

public class DigimonTests
{
    [Fact]
    public void MaxEvp_follows_confirmed_formula()
    {
        Assert.Equal(0, StatFormulas.MaxEvp(1));
        Assert.Equal(30, StatFormulas.MaxEvp(6));
        Assert.Equal(156, StatFormulas.MaxEvp(13));
        Assert.Equal(156, Entities.Digimon(level: 13).Stats.MaxEvp);
    }

    [Fact]
    public void Allocating_needs_points_and_raises_attribute()
    {
        var d = Entities.Digimon();
        Assert.False(d.AllocatePoint(StatKind.Str));
        d.LevelUp();
        var before = d.Attributes.Str;
        Assert.True(d.AllocatePoint(StatKind.Str));
        Assert.True(d.AllocatePoint(StatKind.Str));
        Assert.False(d.AllocatePoint(StatKind.Str));
        Assert.Equal(before + 2, d.Attributes.Str);
    }

    [Fact]
    public void Exp_carries_over_several_levels()
    {
        var d = Entities.Digimon();
        long needed = TestSupport.Data.DigimonExpToNext(1) + TestSupport.Data.DigimonExpToNext(2);
        Assert.Equal(2, d.GainExp(needed + 3, maxLevel: 100));
        Assert.Equal(3, d.Level);
        Assert.Equal(3, d.Exp);
        Assert.Equal(d.Stats.MaxHp, d.Hp);
    }

    [Fact]
    public void Level_cap_is_respected()
    {
        var d = Entities.Digimon();
        d.GainExp(long.MaxValue / 2, maxLevel: 5);
        Assert.Equal(5, d.Level);
    }
}

public class AccountRegistryTests
{
    private static readonly AuthenticatedAccount Account = new(1, "tester", Authority.None);

    [Fact]
    public void Map_needs_channel_login_and_ticket_expires()
    {
        var time = new ManualTime(DateTimeOffset.UnixEpoch);
        var registry = new AccountRegistry(TestSupport.GameOptions(o => o.AccountTicketGraceSeconds = 60), time);
        var channel = TestSupport.NewSession(ServerRole.Channel);
        var map = TestSupport.NewSession(ServerRole.Map);

        Assert.Null(registry.Claim("tester", map));
        registry.SignIn(Account, channel);
        registry.Release(channel);                      // o channel cai antes de o mapa conectar
        time.Now += TimeSpan.FromSeconds(30);
        Assert.Equal(Account, registry.Claim("TESTER", map));
        Assert.Equal(Account, map.Account);

        registry.Release(map);
        time.Now += TimeSpan.FromSeconds(61);
        Assert.Null(registry.Claim("tester", TestSupport.NewSession()));
    }

    [Fact]
    public void Second_channel_login_closes_the_first()
    {
        var registry = new AccountRegistry(TestSupport.GameOptions(), TimeProvider.System);
        var first = TestSupport.NewSession(ServerRole.Channel);
        var second = TestSupport.NewSession(ServerRole.Channel);
        registry.SignIn(Account, first);
        registry.SignIn(Account, second);
        Assert.True(first.IsClosed);
        Assert.False(second.IsClosed);
    }
}

public class BattleTests
{
    private readonly GameWorld _world = TestSupport.NewWorld();
    private readonly ZoneSystem _zones;
    private readonly BattleSystem _battles;
    private readonly Session _session = TestSupport.NewSession();
    private readonly Player _player;

    public BattleTests()
    {
        _zones = new ZoneSystem(TestSupport.Data, _world, NullLogger<ZoneSystem>.Instance);
        _battles = new BattleSystem(_world, TestSupport.Data, TestSupport.GameOptions(), NullLogger<BattleSystem>.Instance);
        _player = new Player(_session, new AuthenticatedAccount(1, "u", Authority.GameMaster), Entities.Character());
        _zones.Find(79)!.Enter(_player);
        _session.DrainOutgoing();
    }

    private void Tick(int times = 1)
    {
        for (int i = 0; i < times; i++)
            _battles.Update(TimeSpan.Zero, TimeSpan.FromMilliseconds(200));
    }

    [Fact]
    public void Win_rewards_and_removes_wild_group()
    {
        var wild = _zones.Find(79)!.WildDigimons.First();
        var battle = _battles.StartPve(_player, wild.Serial)!;
        Assert.Same(battle, _player.Battle);
        Assert.Equal(WildState.InBattle, wild.State);
        Assert.Null(_battles.StartPve(_player, wild.Serial));   // já em batalha
        Assert.Contains((ushort)0x62, _session.SentOpcodes());

        battle.SendTeams(_player);
        battle.ClientReady(_player);
        Assert.Equal([(ushort)0x63, (ushort)0x69], _session.SentOpcodes());

        Tick(6);                                                // passa o atraso inicial e começa a encher
        Assert.True(battle.IsIdle);
        Assert.Contains((ushort)0x6B, _session.SentOpcodes());

        battle.KnockOut(BattleSide.B);
        Tick();
        Assert.True(battle.IsOver);
        Assert.Null(_player.Battle);
        Assert.Equal(WildState.Dead, wild.State);
        Assert.Equal(1, _player.Character.TotalWins);
        Assert.Contains((ushort)0x6D, _session.SentOpcodes());
        Assert.Equal(0, _battles.Count);
    }

    [Fact]
    public void Disconnect_cancels_battle_and_frees_wild_group()
    {
        var wild = _zones.Find(79)!.WildDigimons.First();
        _battles.StartPve(_player, wild.Serial);
        _battles.PlayerLeft(_player);
        Tick();
        Assert.Equal(WildState.Roaming, wild.State);
        Assert.Equal(0, _battles.Count);
    }

    [Fact]
    public void Cannot_command_a_digimon_you_do_not_own()
    {
        var wild = _zones.Find(79)!.WildDigimons.First();
        var battle = _battles.StartPve(_player, wild.Serial)!;
        battle.ClientReady(_player);
        Tick(6);
        _session.DrainOutgoing();
        var enemy = battle.TeamB[0].Digimon.Serial;
        battle.RequestAction(_player, enemy, battle.TeamA[0].Digimon.Serial);
        Assert.DoesNotContain((ushort)0x35, _session.SentOpcodes());
    }
}

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
