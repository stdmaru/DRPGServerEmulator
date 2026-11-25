using DRPGServer.Data.Persistence;
using DRPGServer.Data.Repositories;
using DRPGServer.Game;
using DRPGServer.Game.Battle;
using DRPGServer.Game.Entities;
using DRPGServer.Game.World;
using DRPGServer.Network;
using DRPGServer.Network.Handlers;
using DRPGServer.Network.Packets.Map;
using DRPGServer.Network.Protocol;
using Microsoft.Extensions.Logging.Abstractions;

namespace DRPGServer.Tests;

public class PersistenceTests
{
    [Fact]
    public async Task Queue_coalesces_multiple_pending_saves_for_same_character_to_latest()
    {
        var queue = new PersistenceQueue();
        var executed = new List<(uint Uid, int Version)>();

        for (int v = 1; v <= 5; v++)
        {
            int version = v;
            queue.EnqueueCharacterSave(7, _ => { executed.Add((7, version)); return Task.CompletedTask; });
        }
        queue.EnqueueCharacterSave(8, _ => { executed.Add((8, 1)); return Task.CompletedTask; });

        var service = new PersistenceService(queue, NullLogger<PersistenceService>.Instance);
        await service.StartAsync(CancellationToken.None);
        await queue.FlushAsync();
        await service.StopAsync(CancellationToken.None);

        Assert.Equal([(7u, 5), (8u, 1)], executed);
    }

    [Fact]
    public async Task Queue_runs_followup_save_when_new_snapshot_arrives_during_inflight_save()
    {
        var queue = new PersistenceQueue();
        var startedFirst = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirst = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var executed = new List<int>();

        queue.EnqueueCharacterSave(7, async _ =>
        {
            startedFirst.TrySetResult();
            await releaseFirst.Task;
            executed.Add(1);
        });

        var service = new PersistenceService(queue, NullLogger<PersistenceService>.Instance);
        await service.StartAsync(CancellationToken.None);
        await startedFirst.Task;

        // Enquanto a gravação #1 está em andamento, chegam #2 e #3 -> devem coalescer para #3 logo após #1
        queue.EnqueueCharacterSave(7, _ => { executed.Add(2); return Task.CompletedTask; });
        queue.EnqueueCharacterSave(7, _ => { executed.Add(3); return Task.CompletedTask; });
        releaseFirst.TrySetResult();

        await queue.FlushAsync();
        await service.StopAsync(CancellationToken.None);

        Assert.Equal([1, 3], executed);
    }

    [Fact]
    public void PlayerSystem_saves_immediately_on_critical_events_and_periodically_only_when_dirty()
    {
        var world = TestSupport.NewWorld();
        var zones = new ZoneSystem(TestSupport.Data, world, TestSupport.Clock, NullLogger<ZoneSystem>.Instance);
        var battles = TestSupport.NewBattles(world, TestSupport.Data, TestSupport.GameOptions());
        var queue = new PersistenceQueue();
        var options = TestSupport.GameOptions(o => o.AutosaveSeconds = 60);
        var players = new PlayerSystem(world, zones, battles, queue, new RecordingSnapshotStore(), options, NullLogger<PlayerSystem>.Instance);

        var session1 = TestSupport.NewSession(ServerRole.Map);
        var player1 = players.EnterNow(session1, new AuthenticatedAccount(1, "u1", Authority.None), Entities.Character());

        // Jogador limpo não dispara gravação mesmo após 120 s
        players.Update(TimeSpan.FromSeconds(120), TimeSpan.FromSeconds(120));
        Assert.False(queue.Reader.TryRead(out _));

        // MarkDirty (Tier 2): não salva antes do intervalo, mas salva quando o intervalo vence
        player1.MarkDirty();
        players.Update(TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30));
        Assert.False(queue.Reader.TryRead(out _));
        Assert.True(player1.IsDirty);

        players.Update(TimeSpan.FromSeconds(60), TimeSpan.FromSeconds(30));
        Assert.True(queue.Reader.TryRead(out _));
        Assert.False(player1.IsDirty);
        Assert.False(player1.NeedsImmediateSave);
        Assert.Equal(TimeSpan.FromSeconds(60), player1.LastSavedAt);

        // RequestImmediateSave (Tier 1): salva imediatamente no próximo tick (50 ms) sem esperar AutosaveSeconds
        var char2 = new Character(Entities.Digimon(), new Inventory(TestSupport.Data.FindItem, 999, _ => null, TestSupport.Clock))
        {
            Uid = 2, AccountUid = 2, Slot = 0, Name = "Tester2", TamerId = 1, MapId = 79, X = 56, Y = 56,
        };
        var player2 = players.EnterNow(TestSupport.NewSession(ServerRole.Map), new AuthenticatedAccount(2, "u2", Authority.None), char2);
        player2.RequestImmediateSave();
        players.Update(TimeSpan.FromSeconds(60.05), TimeSpan.FromMilliseconds(50));
        Assert.True(queue.Reader.TryRead(out _));
        Assert.False(player2.IsDirty);
        Assert.False(player2.NeedsImmediateSave);
        Assert.Equal(TimeSpan.FromSeconds(60.05), player2.LastSavedAt);
    }

    [Fact]
    public void Handlers_distinguish_immediate_save_from_dirty_save()
    {
        var world = TestSupport.NewWorld();
        var zones = new ZoneSystem(TestSupport.Data, world, TestSupport.Clock, NullLogger<ZoneSystem>.Instance);
        var session = TestSupport.NewSession(ServerRole.Map);
        var character = Entities.Character();
        var player = new Player(session, new AuthenticatedAccount(1, "u", Authority.None), character);
        session.Player = player;
        var zone = zones.Find(79)!;
        zone.Enter(player);

        // Sort interno (0x148) -> apenas MarkDirty (Tier 2)
        character.Inventory.Main.Place(character.Inventory.Create(41, 2, 1, 10));
        var sortPayload = new byte[12];
        sortPayload[8] = (byte)InventorySortHandler.MainInventoryType;
        new InventorySortHandler().Dispatch(session, new PacketReader(sortPayload));
        Assert.True(player.IsDirty);
        Assert.False(player.NeedsImmediateSave);

        // Coleta do chão (0x91) -> RequestImmediateSave (Tier 1)
        var drop = zone.Drop(41, 56, 56, character.Uid);
        var w = new PacketWriter();
        new ItemMoveHeader(ItemPlace.Ground, ItemPlace.Inventory, 56, 56, 0, 0).Write(w);
        w.WriteU32(drop.Uid);
        w.WriteU32(drop.ItemId);
        new ItemMoveHandler().Dispatch(session, new PacketReader(w.Payload.ToArray()));
        Assert.True(player.NeedsImmediateSave);
    }

    [Fact]
    public void Digimon_vitals_persist_through_snapshot_and_factory_reload()
    {
        var leader = Entities.Digimon(level: 10);
        var ally = Entities.Digimon(level: 10);
        leader.TakeDamage(50);
        leader.SpendVp(30);
        ally.TakeDamage(ally.Stats.MaxHp); // nocauteado (HP = 0)

        var character = new Character(leader, new Inventory(TestSupport.Data.FindItem, 999, _ => null, TestSupport.Clock))
        {
            Uid = 42, AccountUid = 1, Slot = 0, Name = "Hero", TamerId = 1, Level = 10, MapId = 79, X = 50, Y = 50,
        };
        character.Place(1, ally);

        var snap = CharacterSnapshot.Of(character);
        Assert.Equal(2, snap.Digimons.Count);
        Assert.Equal((leader.Hp, leader.Vp, leader.Evp), (snap.Digimons[0].Hp, snap.Digimons[0].Vp, snap.Digimons[0].Evp));
        Assert.Equal(0, snap.Digimons[1].Hp);

        var factory = new CharacterFactory(TestSupport.Data, TestSupport.GameOptions(), TestSupport.Clock);
        var record = new CharacterRecord(
            new CharacterRow { Uid = 42, AccountUid = 1, Slot = 0, Name = "Hero", TamerId = 1, Level = 10, MapId = 79, PosX = 50, PosY = 50, DepositorySlots = 10 },
            [
                new DigimonRow { Uid = 7, Slot = 0, DigimonId = 10, Name = "Guilmon", Level = 10, Hp = snap.Digimons[0].Hp, Vp = snap.Digimons[0].Vp, Evp = snap.Digimons[0].Evp },
                new DigimonRow { Uid = 8, Slot = 1, DigimonId = 10, Name = "Guilmon2", Level = 10, Hp = 0, Vp = 15, Evp = 20 },
                new DigimonRow { Uid = 9, Slot = 0, InDepository = true, DigimonId = 10, Name = "Stored", Level = 10, Hp = 45, Vp = 33, Evp = 12 },
            ],
            [new FormRow(7, 10, 0, 0, 0, 0, 0, 0), new FormRow(8, 10, 0, 0, 0, 0, 0, 0), new FormRow(9, 10, 0, 0, 0, 0, 0, 0)],
            [],
            []);
        var reloaded = factory.Build(record)!;

        // Não recupera HP/VP/EVP ao relogar (nem na party, nem no Depository)
        Assert.Equal(leader.Hp, reloaded.Leader.Hp);
        Assert.Equal(leader.Vp, reloaded.Leader.Vp);
        Assert.Equal(leader.Evp, reloaded.Leader.Evp);
        Assert.Equal(0, reloaded.Party[1]!.Hp);
        Assert.Equal(15, reloaded.Party[1]!.Vp);
        Assert.Equal(20, reloaded.Party[1]!.Evp);

        var stored = reloaded.FindInDepository(9)!;
        Assert.Equal((45, 33, 12), (stored.Hp, stored.Vp, stored.Evp));
    }

    [Fact]
    public void Digimon_vitals_null_loads_full_and_out_of_range_clamps()
    {
        var d = Entities.Digimon(level: 10);
        // Registros antigos/novos (NULL) carregam cheios
        d.TakeDamage(40);
        d.Load(0, 0, 0, 0, null, null, null);
        Assert.Equal((d.Stats.MaxHp, d.Stats.MaxVp, d.Stats.MaxEvp), (d.Hp, d.Vp, d.Evp));

        // Valores fora da faixa são limitados a 0..Max
        d.Load(0, 0, 0, 0, -50, 999_999, -1);
        Assert.Equal(0, d.Hp);
        Assert.Equal(d.Stats.MaxVp, d.Vp);
        Assert.Equal(0, d.Evp);
    }
}
