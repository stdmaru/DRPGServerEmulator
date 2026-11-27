using DRPG.Client.Net;
using DRPG.Client.Startup;
using DRPG.Client.World;
using DRPG.Shared.Packets.Map;
using DRPG.Shared.Protocol;

namespace DRPG.Client.Tests;

/// <summary>
/// Client-side NPC tests (<c>npc_info_us</c>, <c>0x00ad3730</c>, <c>0x00abb750</c>, <c>0x00abb9e0</c>, <c>0x00acfed0</c>,
/// <c>0x09</c>): the table read once with fail-fast rows, map auto-spawn, col 9 filtering, map 0x66 exclusion, collision
/// occupancy, quest <c>0x09</c> add/remove/reload persistence through the field handlers, and hostile <c>0x09</c> frames.
/// </summary>
public sealed class FieldNpcsTests
{
    private static readonly Cell AwayFromNpcs = new(0, 9);

    private static NpcTable Table(params string[][] npcRows) =>
        NpcTable.From(new Dictionary<string, IReadOnlyList<string[]>>(StringComparer.OrdinalIgnoreCase)
        {
            ["NPC_INFO"] = npcRows,
            ["Custom_AvatarNPC"] = new List<string[]> { new[] { "2" } },
        });

    private static string[] NpcRow(int mapId, int npcId, string name, int dir, int x, int y, string sprite, int autoSpawn) =>
        [$"{mapId}", $"{npcId}", name, $"{dir}", $"{x}", $"{y}", sprite, "icon_npc", "0", $"{autoSpawn}", "0", "0", "0", "2", "1"];

    [Fact]
    public void Map_load_spawns_only_col9_npcs_for_map_and_occupies_collision()
    {
        var table = Table(
            NpcRow(1, 322, "GM. Mitsuo Yamaki", 0, 5, 6, "NPC_yamaki", autoSpawn: 1),
            NpcRow(1, 323, "Quest Hidden NPC", 2, 7, 6, "NPC_hidden", autoSpawn: 0),
            NpcRow(2, 400, "Other Map NPC", 0, 3, 3, "NPC_other", autoSpawn: 1));
        using var field = new FieldFixture(FieldFixture.Open(10, 10), AwayFromNpcs, mapId: 1, npcs: table);

        var spawned = Assert.Single(field.AreaNpcs.All);
        Assert.Equal(322, spawned.NpcId);
        Assert.Equal("GM. Mitsuo Yamaki", spawned.Name);
        Assert.Equal(new Cell(5, 6), spawned.Cell);
        Assert.Equal(spawned.Serial, field.Collision.OccupantAt(5, 6));
        Assert.Equal(spawned.Serial, field.Collision.OccupantAt(5, 5));
        // Clicking the NPC's occupied cell stops adjacent (one step before)
        Assert.False(field.Collision.IsFree(5, 6));
        Assert.Null(field.Area.Pathfinder.NextStep(new Cell(4, 6), new Cell(5, 6)));
    }

    [Fact]
    public void Map_0x66_skips_standard_auto_spawn()
    {
        var table = Table(NpcRow(FieldNpcs.ExcludedAutoSpawnMapId, 500, "Special", 0, 4, 4, "NPC_x", autoSpawn: 1));
        using var field = new FieldFixture(FieldFixture.Open(10, 10), AwayFromNpcs, mapId: FieldNpcs.ExcludedAutoSpawnMapId, npcs: table);

        Assert.Empty(field.AreaNpcs.All);
    }

    [Fact]
    public void Quest_0x09_adds_removes_and_recreates_on_map_reload()
    {
        var table = Table(NpcRow(1, 85, "Quest NPC", 0, 2, 2, "Custom_1", autoSpawn: 0));
        using var field = new FieldFixture(FieldFixture.Open(10, 10), AwayFromNpcs, mapId: 1, npcs: table);
        Assert.Empty(field.AreaNpcs.All);

        // Add on another map is ignored
        field.Handlers.Handle(new FieldNpcPacket(2, 85, 6, 7, FieldNpcPacket.ActionAdd));
        Assert.Empty(field.AreaNpcs.All);

        // Add of an id the table lacks is ignored
        field.Handlers.Handle(new FieldNpcPacket(1, 999, 6, 7, FieldNpcPacket.ActionAdd));
        Assert.Empty(field.AreaNpcs.All);
        Assert.Empty(field.State.QuestNpcs.All);

        // Add on current map spawns at packet coordinates and occupies collision
        field.Handlers.Handle(new FieldNpcPacket(1, 85, 6, 7, FieldNpcPacket.ActionAdd));
        var npc = Assert.Single(field.AreaNpcs.All);
        Assert.Equal(new Cell(6, 7), npc.Cell);
        Assert.Equal(npc.Serial, field.Collision.OccupantAt(6, 7));

        // Duplicate add at same (map, x, y) is ignored
        field.Handlers.Handle(new FieldNpcPacket(1, 85, 6, 7, FieldNpcPacket.ActionAdd));
        Assert.Single(field.AreaNpcs.All);

        // Reloading map 1 recreates the quest NPC (0x00ad3730 +0x760)
        var reloadedArea = new FieldArea(1, FieldFixture.Grid(FieldFixture.Open(10, 10)), table, NoFieldSprites.Instance, field.Random);
        field.State.EnterArea(reloadedArea, AwayFromNpcs);
        var reloaded = Assert.Single(field.AreaNpcs.All);
        Assert.Equal(new Cell(6, 7), reloaded.Cell);
        Assert.Equal(reloaded.Serial, reloadedArea.Collision.OccupantAt(6, 7));

        // Remove vacates collision and deletes from the quest list
        field.Handlers.Handle(new FieldNpcPacket(1, 85, 6, 7, FieldNpcPacket.ActionRemove));
        Assert.Empty(field.AreaNpcs.All);
        Assert.Equal(default, reloadedArea.Collision.OccupantAt(6, 7));

        field.State.EnterArea(new FieldArea(1, FieldFixture.Grid(FieldFixture.Open(10, 10)), table, NoFieldSprites.Instance, field.Random), AwayFromNpcs);
        Assert.Empty(field.AreaNpcs.All);
    }

    [Fact]
    public void Removal_with_map_0_forgets_the_npc_on_every_map()
    {
        var table = Table(NpcRow(1, 85, "Quest NPC", 0, 2, 2, "NPC_q", autoSpawn: 0));
        using var field = new FieldFixture(FieldFixture.Open(10, 10), AwayFromNpcs, mapId: 1, npcs: table);
        field.Handlers.Handle(new FieldNpcPacket(1, 85, 6, 7, FieldNpcPacket.ActionAdd));
        field.State.QuestNpcs.Add(new Features.Field.QuestNpc(2, 85, new Cell(3, 3)));

        field.Handlers.Handle(new FieldNpcPacket(FieldNpcPacket.AllMaps, 85, 0, 0, FieldNpcPacket.ActionRemove));

        Assert.Empty(field.AreaNpcs.All);
        Assert.Empty(field.State.QuestNpcs.All);
    }

    [Fact]
    public void Custom_avatar_index_is_parsed_from_sprite_column()
    {
        var table = Table(NpcRow(1, 77, "Avatar NPC", 0, 3, 3, "Custom_1", autoSpawn: 1));
        var npc = table.Find(77);
        Assert.NotNull(npc);
        Assert.Equal(1, npc.CustomAvatarIndex);
        Assert.Equal(2, table.CustomAvatarTamerId(1));
        Assert.Null(table.CustomAvatarTamerId(2));
    }

    [Fact]
    public void Malformed_npc_rows_stop_the_load()
    {
        Assert.Throws<InvalidDataException>(() => Table(["1", "2", "short row"]));
        Assert.Throws<InvalidDataException>(() => Table(NpcRow(1, 2, "Bad direction", 8, 3, 3, "NPC_a", autoSpawn: 1)));
        var notNumber = NpcRow(1, 2, "Bad x", 0, 3, 3, "NPC_a", autoSpawn: 1);
        notNumber[4] = "x";
        Assert.Throws<InvalidDataException>(() => Table(notNumber));
    }

    [Fact]
    public void Map_decoder_parses_0x09_and_rejects_wrong_length()
    {
        var payload = Convert.FromHexString("8D005500C800FF000100");
        var frame = new IncomingFrame(new PacketHeader((ushort)MapOpcode.FieldNpcUpdate, (uint)(PacketHeader.Length + payload.Length), 0, 0), payload);
        var decoded = Assert.IsType<FieldNpcPacket>(MapDecoder.Map(frame));
        Assert.Equal(new FieldNpcPacket(141, 85, 200, 255, FieldNpcPacket.ActionAdd), decoded);

        var shortFrame = new IncomingFrame(new PacketHeader((ushort)MapOpcode.FieldNpcUpdate, (uint)(PacketHeader.Length + 9), 0, 0), new byte[9]);
        Assert.Throws<PacketFormatException>(() => MapDecoder.Map(shortFrame));
    }
}
