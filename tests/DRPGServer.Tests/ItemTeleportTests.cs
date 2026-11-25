using DRPGServer.Game;
using DRPGServer.Game.Entities;
using DRPGServer.Game.World;
using DRPGServer.Network;
using DRPGServer.Network.Handlers;
using DRPGServer.Network.Packets.Map;
using DRPGServer.Network.Protocol;
using Microsoft.Extensions.Logging.Abstractions;

namespace DRPGServer.Tests;

public class ItemTeleportTests
{
    private static byte[] TeleportPayload(int x, int y, ItemPlace place, uint uid, uint itemId)
    {
        var w = new PacketWriter();
        w.WriteI32(x);
        w.WriteI32(y);
        w.WriteU32((uint)place);
        w.WriteU32(uid);
        w.WriteU32(itemId);
        w.WriteZeros(138 - 20);
        return w.Payload.ToArray();
    }

    [Fact]
    public void ItemTeleportHandler_moves_player_consumes_907_and_sends_official_sequence()
    {
        var world = TestSupport.NewWorld();
        var zones = new ZoneSystem(TestSupport.Data, world, TestSupport.Clock, NullLogger<ZoneSystem>.Instance);
        var character = new Character(Entities.Digimon(), TestSupport.EmptyInventory(999))
        {
            Uid = 1, AccountUid = 1, Slot = 0, Name = "Teleporter", TamerId = 1, MapId = 79, X = 56, Y = 56,
        };
        var teleport = character.Inventory.Main.Add(ItemTeleportHandler.TeleportXItemId, 2)[0];
        var session = TestSupport.NewSession();
        var player = new Player(session, new AuthenticatedAccount(1, "u", Authority.None), character);
        session.Player = player;
        zones.Find(79)!.Enter(player);
        session.DrainOutgoing();

        var handler = new ItemTeleportHandler(world);
        handler.Dispatch(session, new PacketReader(TeleportPayload(70, 80, ItemPlace.Inventory, teleport.Uid, 907)));

        Assert.Equal((short)70, character.X);
        Assert.Equal((short)80, character.Y);
        Assert.Equal(1u, teleport.Amount);
        Assert.True(player.NeedsImmediateSave);

        var frames = session.DrainOutgoing();
        Assert.Equal(
            [(ushort)MapOpcode.RefreshCharacterInfo, (ushort)MapOpcode.ItemTeleport, (ushort)MapOpcode.InventoryItemReceive],
            frames.Select(f => PacketHeader.Read(f).Opcode).ToList());
        Assert.Equal(124, frames[1].Length - PacketHeader.Length);
        Assert.Equal(604, frames[2].Length - PacketHeader.Length);

        // Última carga: consome e esvazia o slot
        handler.Dispatch(session, new PacketReader(TeleportPayload(56, 56, ItemPlace.Inventory, teleport.Uid, 907)));
        Assert.Null(character.Inventory.Main.FindByUid(teleport.Uid));
        Assert.Equal(3, session.DrainOutgoing().Count);
    }

    [Fact]
    public void ItemTeleportHandler_rejects_invalid_items_places_and_unwalkable_cells()
    {
        var grid = new DRPGServer.Data.GameData.MapGrid(10, 10, new byte[(10 * 10 + 7) / 8]);
        var data = Grids.DataWithGrid(1, grid);
        var world = TestSupport.NewWorld();
        var zones = new ZoneSystem(data, world, TestSupport.Clock, NullLogger<ZoneSystem>.Instance);
        var character = new Character(Entities.Digimon(), TestSupport.EmptyInventory(999))
        {
            Uid = 1, AccountUid = 1, Slot = 0, Name = "Teleporter", TamerId = 1, MapId = 1, X = 56, Y = 56,
        };
        var seed = character.Inventory.Main.Add(41, 5)[0];
        var teleport = character.Inventory.Main.Add(907, 5)[0];
        var session = TestSupport.NewSession();
        var player = new Player(session, new AuthenticatedAccount(1, "u", Authority.None), character);
        session.Player = player;
        zones.Find(1)!.Enter(player);
        session.DrainOutgoing();

        var handler = new ItemTeleportHandler(world);

        // Item comum (41) não pode ser usado como teleporte
        handler.Dispatch(session, new PacketReader(TeleportPayload(5, 5, ItemPlace.Inventory, seed.Uid, 41)));
        // Place inválido (Ground)
        handler.Dispatch(session, new PacketReader(TeleportPayload(5, 5, ItemPlace.Ground, teleport.Uid, 907)));
        // Célula não-andável no grid (todos 0)
        handler.Dispatch(session, new PacketReader(TeleportPayload(5, 5, ItemPlace.Inventory, teleport.Uid, 907)));
        // UID / ItemId trocados
        handler.Dispatch(session, new PacketReader(TeleportPayload(5, 5, ItemPlace.Inventory, seed.Uid, 907)));

        Assert.Empty(session.DrainOutgoing());
        Assert.Equal(5u, teleport.Amount);
        Assert.Equal((short)56, character.X);
    }
}
