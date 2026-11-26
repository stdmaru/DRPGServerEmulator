using System.Buffers.Binary;
using System.Text;
using DRPGServer.Data.Repositories;
using DRPGServer.Game;
using DRPGServer.Game.Battle;
using DRPGServer.Game.Digimons;
using DRPGServer.Game.Entities;
using DRPGServer.Game.Stats;
using DRPGServer.Game.World;
using DRPGServer.Network;
using DRPGServer.Network.Handlers;
using DRPGServer.Network.Packets.Map;
using DRPGServer.Network.Protocol;
using Microsoft.Extensions.Logging.Abstractions;

namespace DRPGServer.Tests;

/// <summary>Captura (Taming Net e comum) e renomeação grátis. O selvagem do mapa 79 da fixture é in-training.</summary>
public class CaptureTests
{
    private const uint InTrainingNet = 10022, RookieNet = 10026;

    private readonly GameWorld _world = TestSupport.NewWorld();
    private readonly ZoneSystem _zones;
    private readonly DigimonUids _uids = new();

    public CaptureTests()
    {
        _uids.Seed(100);
        _zones = new ZoneSystem(TestSupport.Data, _world, TestSupport.Clock, NullLogger<ZoneSystem>.Instance);
    }

    private Taming NewTaming(double chance = 0.10) =>
        TestSupport.NewTaming(_uids, TestSupport.GameOptions(o => o.CaptureChance = chance));

    private static Digimon NewDigimon(ushort species, ushort level, uint uid) =>
        new(TestSupport.Data, TestSupport.Data.Species[species], level) { Uid = uid };

    private Player Join(params Digimon[] party)
    {
        var c = new Character(party[0], new Inventory(TestSupport.Data.FindItem, 999, _ => null, TestSupport.Clock))
        {
            Uid = 1, AccountUid = 1, Slot = 0, Name = "Tester", TamerId = 1, MapId = 79, X = 56, Y = 56,
        };
        for (int i = 1; i < party.Length; i++) Assert.True(c.Place(i, party[i]));
        var session = TestSupport.NewSession();
        var player = new Player(session, new AuthenticatedAccount(1, "u", Authority.GameMaster), c);
        session.Player = player;
        _zones.Find(79)!.Enter(player);
        session.DrainOutgoing();
        return player;
    }

    private WildDigimon Wild => _zones.Find(79)!.WildDigimons.First(w => w.CanBattle);

    private static byte[] ItemUsePayload(uint itemUid, Serial target)
    {
        var p = new byte[822];
        BinaryPrimitives.WriteUInt32LittleEndian(p, 3);
        BinaryPrimitives.WriteUInt32LittleEndian(p.AsSpan(4), itemUid);
        BinaryPrimitives.WriteUInt64LittleEndian(p.AsSpan(116), target.Low);
        BinaryPrimitives.WriteUInt64LittleEndian(p.AsSpan(124), target.High);
        return p;
    }

    private static byte[] RenamePayload(uint uid, string name, byte kind = 2)
    {
        var p = new byte[42];
        p[1] = kind;
        BinaryPrimitives.WriteUInt32LittleEndian(p.AsSpan(4), uid);
        Encoding.ASCII.GetBytes(name).CopyTo(p, 8);
        return p;
    }

    private void UseItem(Player player, Taming taming, InventoryItem item, Serial target) =>
        new ItemUseHandler(TestSupport.NewItemUse(_world, taming)).Dispatch(player.Session, new PacketReader(ItemUsePayload(item.Uid, target)));

    // ---------- Taming Net ----------

    [Fact]
    public void Net_tames_the_wild_into_a_free_slot_with_default_name()
    {
        var player = Join(NewDigimon(50, 15, 1));
        var net = player.Character.Inventory.Main.Add(InTrainingNet, 2)[0];
        var wild = Wild;

        UseItem(player, NewTaming(), net, player.StandBy(wild));

        // ✅ 0xEF (bloco do novo) e 0x41 (rede consumida)
        Assert.Equal([(ushort)0xEF, (ushort)0x41], player.Session.SentOpcodes());
        var tamed = player.Character.Party[1]!;
        Assert.Equal(wild.Leader.Species.Id, tamed.BaseForm.Species.Id);
        Assert.Equal(Digimon.DefaultName, tamed.Name);
        Assert.True(tamed.FreeRename);
        Assert.Equal(StatFormulas.StageLevel(StatFormulas.InTraining), tamed.Level);
        Assert.Equal(101u, tamed.Uid);
        Assert.Equal(1u, player.Character.Inventory.Main.Count(InTrainingNet));
        Assert.Contains(wild, _zones.Find(79)!.WildDigimons);   // ✅ o selvagem continua no mapa
    }

    [Fact]
    public void Net_of_another_stage_fails_and_keeps_the_net()
    {
        var player = Join(NewDigimon(50, 15, 1));
        var net = player.Character.Inventory.Main.Add(RookieNet, 1)[0];

        UseItem(player, NewTaming(), net, player.StandBy(Wild));

        Assert.Equal([(ushort)0xC3], player.Session.SentOpcodes());
        Assert.Single(player.Character.Digimons);
        Assert.Equal(1u, player.Character.Inventory.Main.Count(RookieNet));
    }

    [Fact]
    public void Net_needs_a_free_slot()
    {
        var player = Join(Enumerable.Range(1, Character.PartySize).Select(i => NewDigimon(50, 15, (uint)i)).ToArray());
        var net = player.Character.Inventory.Main.Add(InTrainingNet, 1)[0];

        UseItem(player, NewTaming(), net, player.StandBy(Wild));

        Assert.Equal([(ushort)0xC3], player.Session.SentOpcodes());
        Assert.Equal(1u, player.Character.Inventory.Main.Count(InTrainingNet));
    }

    [Fact]
    public void Net_cannot_be_used_in_battle()
    {
        var player = Join(NewDigimon(50, 15, 1));
        var net = player.Character.Inventory.Main.Add(InTrainingNet, 1)[0];
        var battles = TestSupport.NewBattles(_world, TestSupport.Data, TestSupport.GameOptions());
        var wild = Wild;
        Assert.NotNull(battles.StartPve(player, player.StandBy(wild)));
        player.Session.DrainOutgoing();

        UseItem(player, NewTaming(), net, player.StandBy(_zones.Find(79)!.WildDigimons.First()));

        Assert.Equal([(ushort)0xC3], player.Session.SentOpcodes());
        Assert.Single(player.Character.Digimons);
    }

    // ---------- renomeação ----------

    [Fact]
    public void Tamed_digimon_can_be_renamed_once()
    {
        var player = Join(NewDigimon(50, 15, 1));
        var tamed = NewTaming().Tame(player.Character, TestSupport.Data.Species[14])!;
        var handler = new RenameHandler();

        handler.Dispatch(player.Session, new PacketReader(RenamePayload(tamed.Uid, "")));
        handler.Dispatch(player.Session, new PacketReader(RenamePayload(tamed.Uid, "Tuno")));
        handler.Dispatch(player.Session, new PacketReader(RenamePayload(tamed.Uid, "")));
        handler.Dispatch(player.Session, new PacketReader(RenamePayload(tamed.Uid, "Other")));

        var results = player.Session.DrainOutgoing().Select(f => f[PacketHeader.Length]).ToArray();
        // ✅ 1 = grátis, 3 = renomeado, 2 = sem renomeação
        Assert.Equal([(byte)1, (byte)3, (byte)2, (byte)2], results);
        Assert.Equal("Tuno", tamed.Name);
        Assert.False(tamed.FreeRename);
    }

    [Fact]
    public void Starter_and_invalid_names_are_refused()
    {
        var starter = NewDigimon(50, 15, 1);
        var player = Join(starter);
        var tamed = NewTaming().Tame(player.Character, TestSupport.Data.Species[14])!;
        var handler = new RenameHandler();

        handler.Dispatch(player.Session, new PacketReader(RenamePayload(starter.Uid, "Name")));
        handler.Dispatch(player.Session, new PacketReader(RenamePayload(tamed.Uid, "bad name!")));
        handler.Dispatch(player.Session, new PacketReader(RenamePayload(tamed.Uid, "x", kind: 1)));

        var results = player.Session.DrainOutgoing().Select(f => f[PacketHeader.Length]).ToArray();
        Assert.Equal([(byte)2, (byte)2, (byte)2], results);
        Assert.Equal(Digimon.DefaultName, tamed.Name);
        Assert.True(tamed.FreeRename);
    }

    // ---------- captura comum ----------

    private (Battle battle, Player player, Digimon actor) StartBattle(double chance, params Digimon[] party)
    {
        var player = Join(party);
        var battles = TestSupport.NewBattles(_world, TestSupport.Data, TestSupport.GameOptions(o => o.CaptureChance = chance), taming: NewTaming(chance));
        var battle = battles.StartPve(player, player.StandBy(Wild))!;
        battle.SendTeams(player);
        battle.ClientReady(player);
        player.Session.DrainOutgoing();
        _battleSystem = battles;
        return (battle, player, party[0]);
    }

    private BattleSystem? _battleSystem;

    /// <summary>Pede a captura e roda até o 0x35 executado. Devolve os opcodes enviados.</summary>
    private List<ushort> TryCapture(Battle battle, Player player, Digimon actor)
    {
        foreach (var e in battle.TeamB) e.Gauge = int.MinValue / 2;
        var target = battle.TeamB.First(c => !c.Digimon.IsKnockedOut).Digimon.Serial;
        battle.RequestAction(player, BattleIcon.Capture, actor.Serial, target);
        var sent = new List<ushort>();
        for (int i = 0; i < 200 && !sent.Contains(0xB3) && !battle.IsOver; i++)
        {
            _battleSystem!.Update(TimeSpan.Zero, Battle.TickInterval);
            sent.AddRange(player.Session.SentOpcodes());
        }
        for (int i = 0; i < 50 && !battle.IsRunning && !battle.IsOver; i++)
        {
            battle.ClientReady(player);
            _battleSystem!.Update(TimeSpan.Zero, Battle.TickInterval);
            sent.AddRange(player.Session.SentOpcodes());
        }
        return sent;
    }

    [Fact]
    public void Failed_capture_sends_68_B3_35_and_allows_three_attempts()
    {
        var (battle, player, actor) = StartBattle(0, NewDigimon(50, 15, 1));

        for (int attempt = 1; attempt <= Taming.AttemptsPerBattle; attempt++)
        {
            var sent = TryCapture(battle, player, actor);
            int b3 = sent.IndexOf(0xB3);
            Assert.True(b3 > 0, $"tentativa {attempt}");
            Assert.Equal(0x68, sent[b3 - 1]);
            Assert.Equal(0x35, sent[b3 + 1]);
            Assert.Equal(attempt, battle.CaptureAttempts(player));
        }

        // a quarta não é aceita
        battle.RequestAction(player, BattleIcon.Capture, actor.Serial, battle.TeamB[0].Digimon.Serial);
        Assert.Empty(player.Session.SentOpcodes());
        Assert.Single(player.Character.Digimons);
    }

    [Fact]
    public void Successful_capture_takes_the_wild_and_ends_the_battle_when_none_is_left()
    {
        var (battle, player, actor) = StartBattle(1, NewDigimon(50, 15, 1));
        int enemies = battle.TeamB.Count;

        for (int i = 0; i < enemies && !battle.IsOver; i++)
        {
            foreach (var e in battle.TeamB) e.Gauge = int.MinValue / 2;
            var targetSerial = battle.TeamB.First(c => !c.Digimon.IsKnockedOut).Digimon.Serial;
            battle.RequestAction(player, BattleIcon.Capture, actor.Serial, targetSerial);
            var frames = new List<byte[]>();
            for (int t = 0; t < 200 && !frames.Any(f => PacketHeader.Read(f).Opcode == 0xB3) && !battle.IsOver; t++)
            {
                _battleSystem!.Update(TimeSpan.Zero, Battle.TickInterval);
                frames.AddRange(player.Session.DrainOutgoing());
            }
            for (int t = 0; t < 50 && !battle.IsRunning && !battle.IsOver; t++)
            {
                battle.ClientReady(player);
                _battleSystem!.Update(TimeSpan.Zero, Battle.TickInterval);
                frames.AddRange(player.Session.DrainOutgoing());
            }

            // ✅ Captura em batalha usa apenas 0xB3 (result = 1) com o serial do alvo, sem 0xEF
            Assert.DoesNotContain(frames, f => PacketHeader.Read(f).Opcode == 0xEF);
            var b3 = Assert.Single(frames, f => PacketHeader.Read(f).Opcode == 0xB3);
            Assert.Equal(1u, BinaryPrimitives.ReadUInt32LittleEndian(b3.AsSpan(PacketHeader.Length, 4)));
            var blockSerial = new Serial(
                BinaryPrimitives.ReadUInt64LittleEndian(b3.AsSpan(PacketHeader.Length + 28, 8)),
                BinaryPrimitives.ReadUInt64LittleEndian(b3.AsSpan(PacketHeader.Length + 36, 8)));
            Assert.Equal(targetSerial, blockSerial);
        }

        Assert.True(battle.IsOver);
        Assert.Equal(1 + enemies, player.Character.Digimons.Count());
        Assert.All(player.Character.Digimons.Skip(1), d => Assert.Equal(Digimon.DefaultName, d.Name));
    }

    [Fact]
    public void Capture_needs_a_rookie_or_higher_digimon()
    {
        var (battle, player, actor) = StartBattle(1, NewDigimon(14, 5, 1));
        battle.RequestAction(player, BattleIcon.Capture, actor.Serial, battle.TeamB[0].Digimon.Serial);
        Assert.Empty(player.Session.SentOpcodes());
    }

    [Fact]
    public void Only_in_training_can_be_captured_in_battle()
    {
        var player = Join(NewDigimon(50, 15, 1));
        Assert.True(Taming.CanBattleCapture(player.Character, NewDigimon(14, 1, 0)));
        Assert.False(Taming.CanBattleCapture(player.Character, NewDigimon(50, 11, 0)));
    }

    // ---------- persistência ----------

    [Fact]
    public void Snapshot_keeps_the_free_rename()
    {
        var player = Join(NewDigimon(50, 15, 1));
        var tamed = NewTaming().Tame(player.Character, TestSupport.Data.Species[14])!;
        var snapshot = CharacterSnapshot.Of(player.Character);
        var row = Assert.Single(snapshot.Digimons, d => d.Uid == tamed.Uid);
        Assert.True(row.FreeRename);
        Assert.Equal(Digimon.DefaultName, row.Name);
        Assert.False(Assert.Single(snapshot.Digimons, d => d.Uid == 1).FreeRename);
    }
}
