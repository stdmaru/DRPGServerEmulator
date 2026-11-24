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
        var zones = new ZoneSystem(TestSupport.Data, world, NullLogger<ZoneSystem>.Instance);
        var battles = new BattleSystem(world, TestSupport.Data, TestSupport.GameOptions(), NullLogger<BattleSystem>.Instance);
        var queue = new PersistenceQueue();
        var options = TestSupport.GameOptions(o => o.AutosaveSeconds = 60);
        var players = new PlayerSystem(world, zones, battles, queue, null!, options, NullLogger<PlayerSystem>.Instance);

        var session1 = TestSupport.NewSession(ServerRole.Map);
        var player1 = players.Enter(session1, new AuthenticatedAccount(1, "u1", Authority.None), Entities.Character())!;

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
        var char2 = new Character(Entities.Digimon(), new Inventory(TestSupport.Data.FindItem, 999))
        {
            Uid = 2, AccountUid = 2, Slot = 0, Name = "Tester2", TamerId = 1, MapId = 79, X = 56, Y = 56,
        };
        var player2 = players.Enter(TestSupport.NewSession(ServerRole.Map), new AuthenticatedAccount(2, "u2", Authority.None), char2)!;
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
        var zones = new ZoneSystem(TestSupport.Data, world, NullLogger<ZoneSystem>.Instance);
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
        new InventorySortHandler().Handle(session, new PacketReader(sortPayload));
        Assert.True(player.IsDirty);
        Assert.False(player.NeedsImmediateSave);

        // Coleta do chão (0x91) -> RequestImmediateSave (Tier 1)
        var drop = zone.Drop(41, 56, 56, character.Uid);
        var w = new PacketWriter();
        new ItemMoveHeader(ItemPlace.Ground, ItemPlace.Inventory, 56, 56, 0, 0).Write(w);
        w.WriteU32(drop.Uid);
        w.WriteU32(drop.ItemId);
        new ItemMoveHandler().Handle(session, new PacketReader(w.Payload.ToArray()));
        Assert.True(player.NeedsImmediateSave);
    }
}
