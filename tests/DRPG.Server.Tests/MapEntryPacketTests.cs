using DRPG.Server.Game.Entities;
using DRPG.Server.Network.Packets.Map;
using DRPG.Shared.Packets.Map;
using DRPG.Shared.Protocol;

namespace DRPG.Server.Tests;

/// <summary>The packets the emulator sends on entering the map are the <c>DRPG.Shared</c> ones: what the server builds, the client reads.</summary>
public class MapEntryPacketTests
{
    [Fact]
    public void The_tail_slots_of_the_emulator_are_the_shared_offsets()
    {
        Assert.Equal(CharacterDataPacket.TailOffsets, EquipmentSlots.TailSlots.Select(s => s.TailOffset));
    }

    [Fact]
    public void The_character_data_the_server_builds_is_read_back_unchanged()
    {
        var character = Entities.Character();
        var packet = CharacterData.Of(character, TestSupport.Clock.UnixNow);
        var bytes = TwoWayPacket.EncodePayload(packet);

        var read = ClientPacket.Parse<CharacterDataPacket>(bytes);
        Assert.Equal(bytes, TwoWayPacket.EncodePayload(read));
        Assert.Equal((character.Uid, character.Name, character.Level, character.X, character.Y),
            (read.Uid, read.Name, read.Level, read.X, read.Y));
        Assert.Equal(character.Leader.Name, read.Party[0].Name);
        Assert.Equal(character.Leader.SpeciesId, read.Party[0].SpeciesId);
    }

    [Fact]
    public void The_inventory_the_server_builds_is_read_back_unchanged()
    {
        var inventory = TestSupport.EmptyInventory(999);
        inventory.Add(41, 3);
        var bytes = TwoWayPacket.EncodePayload(InventoryData.Of(inventory));

        var read = ClientPacket.Parse<InventoryDataPacket>(bytes);
        Assert.Equal(bytes, TwoWayPacket.EncodePayload(read));
        Assert.Equal(41u, read.Bags.Items[0].ItemId);
        Assert.Equal(3u, read.Bags.Items[0].Amount);
        Assert.True(read.Bags.Items[1].IsEmpty);
    }
}
