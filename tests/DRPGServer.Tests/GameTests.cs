using DRPGServer.Game;
using DRPGServer.Network.Packets.Map;
using DRPGServer.Network.Protocol;
using DRPGServer.Data;
using System.Collections.Frozen;
using DRPGServer.Data.GameData;
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
            _battles.Update(TimeSpan.Zero, Battle.TickInterval);
    }

    /// <summary>Abre a batalha até as barras começarem a encher.</summary>
    private Battle Open()
    {
        var wild = _zones.Find(79)!.WildDigimons.First();
        var battle = _battles.StartPve(_player, wild.Serial)!;
        Assert.Equal([(ushort)0x62], _session.SentOpcodes());
        battle.SendTeams(_player);
        battle.ClientReady(_player);
        Assert.Equal([(ushort)0x63, (ushort)0x10, (ushort)0x69], _session.SentOpcodes());
        Assert.True(battle.IsRunning);
        return battle;
    }

    [Fact]
    public void Shared_wild_group_keeps_roaming_and_battle_uses_copies()
    {
        var wild = _zones.Find(79)!.WildDigimons.First();
        var battle = Open();
        Assert.Same(battle, _player.Battle);
        Assert.NotEqual(WildState.InBattle, wild.State);
        Assert.DoesNotContain(battle.TeamB, c => wild.Members.Any(m => m.Digimon == c.Digimon));
        Assert.Null(_battles.StartPve(_player, wild.Serial));   // já em batalha
    }

    [Fact]
    public void Gauges_fill_every_step_and_the_marked_attack_happens_when_full()
    {
        var battle = Open();
        var me = battle.TeamA[0];
        var enemy = battle.TeamB[0];
        Tick();
        Assert.Equal(1 + battle.TeamB.Count, _session.SentOpcodes().Count(o => o == 0x6B));
        Assert.Equal(BattleFormulas.GaugeGain(me.Digimon, wild: false), me.Gauge);

        // ✅ clicar alterna: marca (1), desmarca (4)
        battle.RequestAction(_player, BattleIcon.Attack, me.Digimon.Serial, enemy.Digimon.Serial);
        battle.RequestAction(_player, BattleIcon.Attack, me.Digimon.Serial, enemy.Digimon.Serial);
        var frames = _session.DrainOutgoing();
        Assert.Equal([(byte)BattleAction.Selected, (byte)BattleAction.Cleared], frames.Select(f => f[PacketHeader.Length]).ToArray());
        battle.RequestAction(_player, BattleIcon.Attack, me.Digimon.Serial, enemy.Digimon.Serial);
        _session.DrainOutgoing();

        enemy.Gauge = int.MinValue / 2;                         // o inimigo não age neste teste
        var sent = new List<ushort>();
        for (int i = 0; i < 20 && battle.IsRunning; i++)
        {
            Tick();
            sent.AddRange(_session.SentOpcodes());
        }
        Assert.False(battle.IsRunning);
        int at = sent.IndexOf(0x68);
        Assert.Equal((ushort)0x35, sent[at - 1]);               // 3: ação executada
        Assert.Equal((ushort)0x67, sent[^1]);
        Assert.Equal(0, me.Gauge);
        Assert.Equal(1, me.Actions);
        Assert.Equal(BattleIcon.None, me.Choice);

        // ✅ barras paradas até o cliente terminar a animação (0x69)
        Tick(5);
        Assert.DoesNotContain((ushort)0x6B, _session.SentOpcodes());
        battle.ClientReady(_player);
        Assert.Contains((ushort)0x69, _session.SentOpcodes());
        Tick();
        Assert.Contains((ushort)0x6B, _session.SentOpcodes());
    }

    [Fact]
    public void Wild_attacks_on_its_own_when_full()
    {
        var battle = Open();
        battle.TeamB[0].Gauge = BattleFormulas.MaxGauge;
        Tick();
        var sent = _session.SentOpcodes();
        Assert.Equal([(ushort)0x68, (ushort)0x67], sent.Where(o => o is 0x68 or 0x67 or 0x35).ToList());
        Assert.False(battle.IsRunning);
    }

    [Fact]
    public void Win_sends_end_sequence_and_recovery_restarts_right_after()
    {
        var battle = Open();
        _player.Leader.TakeDamage(10);
        _zones.Find(79)!.Update(TimeSpan.FromHours(1), TimeSpan.FromMilliseconds(50));
        Assert.DoesNotContain((ushort)0xB2, _session.SentOpcodes());   // ✅ sem recuperação durante a batalha

        battle.KnockOut(BattleSide.B);
        var sent = _session.SentOpcodes();
        Assert.Equal([(ushort)0xB1, (ushort)0x10, (ushort)0x84, (ushort)0x6D], sent.Where(o => o is 0xB1 or 0x10 or 0x84 or 0x6D).ToList());
        Assert.Contains((ushort)0x6E, sent);
        Assert.True(battle.IsOver);
        Assert.Null(_player.Battle);
        Assert.Equal(1, _player.Character.TotalWins);
        Tick();
        Assert.Equal(0, _battles.Count);

        // ✅ o oficial cura ~0,2 s depois do 0x6D, ainda na tela de resultado
        Assert.InRange(_player.NextRecoveryAt - _world.Now, TimeSpan.Zero, Zone.RecoveryAfterBattle);
    }

    [Fact]
    public void Flee_happens_when_gauge_fills()
    {
        var battle = Open();
        var me = battle.TeamA[0];
        battle.RequestAction(_player, BattleIcon.Flee, me.Digimon.Serial, Serial.Empty);
        foreach (var c in battle.TeamB) c.Gauge = int.MinValue / 2;
        me.Gauge = BattleFormulas.MaxGauge;
        _session.DrainOutgoing();
        Tick();
        var sent = _session.SentOpcodes();
        Assert.Equal([(ushort)0x35, (ushort)0x65, (ushort)0x6A], sent.Where(o => o is 0x35 or 0x65 or 0x6A).ToList());
        Assert.Equal((ushort)0x6D, sent[^1]);
        Assert.True(battle.IsOver);
        Assert.Equal(0, _player.Character.TotalWins);
        Assert.Equal(1, _player.Character.TotalBattles);
    }

    [Fact]
    public void Disconnect_cancels_battle_silently()
    {
        var battle = Open();
        _battles.PlayerLeft(_player);
        Tick();
        Assert.True(battle.IsOver);
        Assert.DoesNotContain((ushort)0x6D, _session.SentOpcodes());
        Assert.Equal(0, _battles.Count);
        Assert.Equal(0, _player.Character.TotalBattles);
    }

    [Fact]
    public void Cannot_command_a_digimon_you_do_not_own()
    {
        var battle = Open();
        var enemy = battle.TeamB[0].Digimon.Serial;
        battle.RequestAction(_player, BattleIcon.Attack, enemy, battle.TeamA[0].Digimon.Serial);
        battle.RequestAction(_player, BattleIcon.Attack, battle.TeamA[0].Digimon.Serial, battle.TeamA[0].Digimon.Serial);
        Assert.DoesNotContain((ushort)0x35, _session.SentOpcodes());
    }
}

public class BattleFormulaTests
{
    [Theory]
    // ✅ v322, ataque básico entre in-training (sessões 53–54): ATK, DEF, dano
    [InlineData(145, 63, 33)]
    [InlineData(145, 43, 48)]
    [InlineData(218, 63, 75)]
    [InlineData(261, 63, 108)]
    [InlineData(283, 69, 116)]
    [InlineData(80, 94, 6)]
    public void Basic_attack_damage_matches_captures(int atk, int def, int damage) =>
        Assert.Equal(damage, (int)BattleFormulas.BaseDamage(atk, def));

    [Fact]
    public void Gauge_gain_matches_captures()
    {
        // ✅ v322: jogador pelo nível (L1 42, L2 77, L10 BR 201 → 109, L20 BR 349 → 124); selvagem pelo BR
        Assert.Equal(42, BattleFormulas.GaugeGain(1, 131, wild: false));
        Assert.Equal(77, BattleFormulas.GaugeGain(2, 139, wild: false));
        Assert.InRange(BattleFormulas.GaugeGain(10, 201, wild: false), 108, 110);
        Assert.Equal(124, BattleFormulas.GaugeGain(20, 349, wild: false));
        Assert.Equal(8, BattleFormulas.GaugeGain(1, 85, wild: true));
        Assert.Equal(14, BattleFormulas.GaugeGain(17, 165, wild: true));
        Assert.Equal(33, BattleFormulas.GaugeGain(50, 1040, wild: true));
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
}

public class ExpFormulaTests
{
    // ✅ capturas v242 e v322: EXP para o próximo nível, níveis 1–21 (offset 256 do bloco de status)
    private static readonly long[] Captured =
        [24, 57, 103, 165, 248, 358, 501, 687, 928, 1238, 1634, 2139, 2781, 3594, 4622, 5916, 7543, 9584, 12140, 15335, 19322];

    [Fact]
    public void Digimon_exp_matches_captures()
    {
        for (int level = 1; level <= Captured.Length; level++)
            Assert.Equal(Captured[level - 1], ExpFormulas.DigimonExpToNext(level));
        // ✅ digimon nível 160 da sessão 56: o valor do nível 77
        Assert.Equal(1_925_209_472, ExpFormulas.DigimonExpToNext(77));
        Assert.Equal(1_925_209_472, ExpFormulas.DigimonExpToNext(160));
    }

    [Theory]
    [InlineData(1, 10)]
    [InlineData(5, 30)]
    [InlineData(14, 75)]
    [InlineData(15, 80)]
    [InlineData(201, 1010)]
    public void Tamer_exp_matches_captures(int level, long expected) =>
        Assert.Equal(expected, ExpFormulas.TamerExpToNext(level));
}

public class ClientTablesTests
{
    [Fact]
    public void Species_take_growth_from_dex_by_id_then_by_name()
    {
        var s = TestSupport.Data.Species;
        Assert.Equal(new Attributes(2, 1, 1, 1), s[10].Growth);
        Assert.Equal((byte)2, s[50].Stage);
        Assert.Equal((ushort)31, s[50].Skill2);
        Assert.Equal(new Attributes(1, 2, 3, 4), s[60].Growth);   // dex com outro id, mesmo nome
        Assert.Null(s[61].Growth);                                 // sem linha na dex
        Assert.Equal("Namedmon", s[62].Name);                      // nome vazio no digimon_us
    }

    [Fact]
    public void Base_attributes_grow_per_level_and_rookie_adds_bonus()
    {
        var s = TestSupport.Data.Species;
        Assert.Equal(new Attributes(10, 10, 10, 10), StatFormulas.BaseAttributes(s[10], 1));
        Assert.Equal(new Attributes(28, 19, 19, 19), StatFormulas.BaseAttributes(s[10], 10));
        Assert.Equal(new Attributes(30 + 30, 30 + 20, 30 + 20, 30 + 10), StatFormulas.BaseAttributes(s[50], 11));
        Assert.Equal(new Attributes(10, 10, 10, 10), StatFormulas.BaseAttributes(s[61], 30));
    }

    [Fact]
    public void Items_and_zones_come_from_the_client()
    {
        var data = TestSupport.Data;
        Assert.Equal("Test Seed", data.Items[41].Name);
        Assert.False(data.Items.ContainsKey(0));
        Assert.Equal([1, 2, 3, 4, 5, 6, 7, 8, 13, 40, 44, 79, 90, 91], data.ZoneIds.Select(i => (int)i));
    }

    [Fact]
    public void Captured_portal_wins_over_map_table_name()
    {
        var p = Assert.Single(TestSupport.Data.PortalsOf(40), p => p.X == 4 && p.Y == 80);
        Assert.Equal(new PortalInfo(40, 4, 80, 44, 193, 43), p);
    }

    [Fact]
    public void Unknown_portal_arrives_next_to_the_return_portal()
    {
        // 90 → 91: portal de volta em (50, 50), centro dos portais de 91 em (70, 30) → 5 casas por eixo
        var p = Assert.Single(TestSupport.Data.PortalsOf(90));
        Assert.Equal(new PortalInfo(90, 10, 10, 91, 55, 45), p);
        Assert.Equal(2, TestSupport.Data.PortalsOf(91).Count);
    }
}

/// <summary>
/// O pacote real do cliente (Resources/Client, v322, fora do git) carrega junto com as tabelas do servidor.
/// Sem a cópia local o teste passa sem checar nada.
/// </summary>
public class RealClientTablesTests
{
    [Fact]
    public void Real_client_pack_loads_with_server_resources()
    {
        var resources = Path.Combine(AppContext.BaseDirectory, "Resources");
        var table = Path.Combine(resources, "Client", "table.hf");
        if (!File.Exists(table)) return;
        var grids = Path.Combine(resources, "Client", ClientExtractor.GridsFile);
        var data = GameDataLoader.Load(resources, ClientPack.Open(table), File.Exists(grids) ? MapGrid.Load(grids) : null);
        Assert.True(data.Species.Count > 700);
        Assert.True(data.Items.Count > 10_000);
        Assert.Contains((byte)79, data.ZoneIds);
        Assert.Equal(new PortalInfo(1, 3, 120, 2, 165, 17), data.PortalsOf(1).Single(p => p.X == 3));
        Assert.Equal((byte)1, data.Species[14].Stage);
        Assert.Equal(new DRPGServer.Game.Stats.Attributes(2, 2, 2, 2), data.Species[14].Growth);
        if (data.Grids.Count > 0)
        {
            Assert.Equal((88, 132), (data.Grids[79].Width, data.Grids[79].Height));
            Assert.True(data.Grids[79].IsWalkable(56, 56));     // posição inicial
            Assert.Equal((176, 263), (data.Grids[2].Width, data.Grids[2].Height));
        }
        var yuramon = data.Species[14];
        Console.WriteLine($"real: {data.Species.Count} espécies ({data.Species.Values.Count(s => s.Growth is null)} sem dex), " +
            $"{data.Items.Count} itens, {data.ZoneIds.Count} zonas, {data.Portals.Values.Sum(p => p.Count)} portais; " +
            $"Yuramon {yuramon.Name} estágio {yuramon.Stage} cresc. {yuramon.Growth}");
    }
}

public class CombatFormulaTests
{
    // amostras do oficial (v322): STR AGI CON INT → ATK DEF BR HP VP
    [Theory]
    [InlineData(10, 10, 10, 10, 145, 87, 131, 115, 115)]     // nível 1, qualquer espécie
    [InlineData(13, 12, 12, 11, 163, 94, 139, 128, 123)]     // Gigimon L2 (DEF 94 exato)
    [InlineData(25, 16, 16, 13, 218, 108, 153, 157, 136)]    // Hopmon L4
    [InlineData(44, 32, 31, 21, 293, 147, 201, 216, 179)]    // Hopmon L10
    [InlineData(70, 60, 50, 50, 376, 195, 300, 273, 259)]    // rookie L11
    [InlineData(91, 76, 70, 69, 428, 231, 349, 319, 305)]    // V-mon L20
    [InlineData(114, 98, 91, 91, 481, 264, 399, 362, 349)]   // champion L21
    public void Combat_stats_match_official_samples(int str, int agi, int con, int @int, int atk, int def, int br, int hp, int vp)
    {
        var s = StatFormulas.Combat(new Attributes(str, agi, con, @int), 1);
        Assert.Equal((atk, def, br, hp, vp), (s.Atk, s.Def, s.Br, s.MaxHp, s.MaxVp));
    }

    [Theory]
    [InlineData(1, 11, 3, 11, 1.05)]  // Vacina > Vírus
    [InlineData(3, 11, 2, 11, 1.05)]  // Vírus > Dados
    [InlineData(2, 11, 1, 11, 1.05)]  // Dados > Vacina
    [InlineData(1, 11, 2, 11, 0.90)]  // Vacina < Dados
    [InlineData(0, 1,  0, 2,  1.05)]  // Elemental: Fogo > Natureza (Gigimon vs Yuramon)
    [InlineData(0, 11, 0, 2,  1.00)]  // Elemental: Neutro (Hopmon vs Yuramon)
    public void Affinity_multipliers_match_expected(byte aAttr, byte aElem, byte tAttr, byte tElem, double expected)
    {
        double mult = DRPGServer.Game.Battle.BattleFormulas.AffinityMultiplier(aAttr, aElem, tAttr, tElem);
        Assert.Equal(expected, mult, precision: 2);
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
        zone.Enter(new Player(session, new AuthenticatedAccount(1, "u", Authority.None), Entities.Character()));
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
    public void Defeated_group_shows_dead_then_leaves_then_respawns_at_home()
    {
        var world = TestSupport.NewWorld();
        var zone = new ZoneSystem(TestSupport.Data, world, NullLogger<ZoneSystem>.Instance).Find(79)!;
        var w = zone.WildDigimons.First();
        zone.Kill(w);
        Assert.Equal(8, w.StateCode);
        var ticks = (int)Math.Ceiling(Zone.DeadDisplayTime / Zone.WildTickInterval);
        for (int i = 0; i < ticks; i++) zone.Update(world.Now + Zone.WildTickInterval * (i + 1), Zone.WildTickInterval);
        Assert.Equal(12, w.StateCode);
        var later = world.Now + Zone.DeadDisplayTime + w.Spawn.RespawnTime + Zone.WildTickInterval * (ticks + 2);
        zone.Update(later, Zone.WildTickInterval);
        Assert.DoesNotContain(w, zone.WildDigimons);
        Assert.Contains(zone.WildDigimons, f => (f.HomeX, f.HomeY) == (w.HomeX, w.HomeY));
    }
}
