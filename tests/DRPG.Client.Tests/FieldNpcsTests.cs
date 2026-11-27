using DRPG.Client.Net;
using DRPG.Client.Startup;
using DRPG.Client.Text;
using DRPG.Client.World;
using DRPG.Shared.Formats.Map;
using DRPG.Shared.Formats.Pack;
using DRPG.Shared.Formats.Sprite;
using DRPG.Shared.Packets.Map;
using DRPG.Shared.Protocol;

namespace DRPG.Client.Tests;

/// <summary>
/// Client-side NPC entity tests (<c>npc_info_us</c>, <c>0x00ad3730</c>, <c>0x00abb750</c>, <c>0x00abb9e0</c>,
/// <c>0x00acfed0</c>, <c>0x09</c>): map auto-spawn, col 9 filtering, map 0x66 exclusion, collision occupancy,
/// dynamic <c>0x09</c> add/remove/reload persistence, and hostile <c>0x09</c> frames.
/// </summary>
public sealed class FieldNpcsTests : IDisposable
{
    private readonly SceneFixture _fixture = new();

    public void Dispose() => _fixture.Dispose();

    private GameData DataWithNpcs(params string[][] npcRows)
    {
        var table = PackArchive.Open(_fixture.Game.Install.TablePath);
        var tables = new Dictionary<string, IReadOnlyList<string[]>>(StringComparer.OrdinalIgnoreCase)
        {
            ["NPC_INFO"] = npcRows,
            ["Custom_AvatarNPC"] = new List<string[]> { new[] { "2" } },
        };
        return new GameData(_fixture.Resource, table, tables, new Dictionary<string, byte[]>(), new TextAttach(), new ProhibitedWords(),
            new Dictionary<string, SpriteFile>());
    }

    private static FieldCollision OpenGrid(int width = 10, int height = 10)
    {
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        w.Write((uint)width);
        w.Write((uint)height);
        w.Write(64u);
        w.Write(32u);
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                w.Write(0u);
                w.Write(-1);
                w.Write(-1);
                w.Write((short)-1);
                w.Write((short)0);
                w.Write(0u);
                w.Write(1u);
            }
        w.Write((ushort)0);
        w.Write((ushort)0);
        return new FieldCollision(MapFile.Parse(ms.ToArray()));
    }

    private static string[] NpcRow(int mapId, int npcId, string name, int dir, int x, int y, string sprite, int autoSpawn) =>
        [$"{mapId}", $"{npcId}", name, $"{dir}", $"{x}", $"{y}", sprite, "icon_npc", "0", $"{autoSpawn}", "0", "0", "0", "2", "1"];

    [Fact]
    public void LoadForMap_spawns_only_col9_npcs_for_map_and_occupies_collision()
    {
        var data = DataWithNpcs(
            NpcRow(1, 322, "GM. Mitsuo Yamaki", 0, 5, 6, "NPC_yamaki", autoSpawn: 1),
            NpcRow(1, 323, "Quest Hidden NPC", 2, 7, 6, "NPC_hidden", autoSpawn: 0),
            NpcRow(2, 400, "Other Map NPC", 0, 3, 3, "NPC_other", autoSpawn: 1));
        var collision = OpenGrid();
        var pathfinder = new FieldPathfinder(collision);
        var npcs = new FieldNpcs();

        npcs.LoadForMap(1, data, collision, sprites: null, new Random(0));

        var spawned = Assert.Single(npcs.All);
        Assert.Equal(322, spawned.NpcId);
        Assert.Equal("GM. Mitsuo Yamaki", spawned.Name);
        Assert.Equal((5, 6), (spawned.CellX, spawned.CellY));
        Assert.Equal(spawned.Serial, collision.OccupantAt(5, 6));
        Assert.Equal(spawned.Serial, collision.OccupantAt(5, 5));
        // Clicking the NPC's occupied cell stops adjacent (one step before)
        Assert.False(collision.IsFree(5, 6));
        Assert.Null(pathfinder.NextStep(4, 6, 5, 6));
    }

    [Fact]
    public void Map_0x66_skips_standard_auto_spawn()
    {
        var data = DataWithNpcs(NpcRow(FieldNpcs.ExcludedAutoSpawnMapId, 500, "Special", 0, 4, 4, "NPC_x", autoSpawn: 1));
        var npcs = new FieldNpcs();

        npcs.LoadForMap(FieldNpcs.ExcludedAutoSpawnMapId, data, OpenGrid(), sprites: null, new Random(0));

        Assert.Empty(npcs.All);
    }

    [Fact]
    public void Dynamic_0x09_adds_removes_and_recreates_on_map_reload()
    {
        var data = DataWithNpcs(NpcRow(1, 85, "Quest NPC", 0, 2, 2, "Custom_1", autoSpawn: 0));
        var collision = OpenGrid();
        var npcs = new FieldNpcs();
        var rng = new Random(0);

        npcs.LoadForMap(1, data, collision, sprites: null, rng);
        Assert.Empty(npcs.All);

        // Add on wrong map is ignored
        npcs.Apply(new FieldNpcPacket(2, 85, 6, 7, FieldNpcPacket.ActionAdd), currentMapId: 1, data, collision, null, rng);
        Assert.Empty(npcs.All);

        // Add on current map spawns at packet coordinates and occupies collision
        npcs.Apply(new FieldNpcPacket(1, 85, 6, 7, FieldNpcPacket.ActionAdd), currentMapId: 1, data, collision, null, rng);
        var npc = Assert.Single(npcs.All);
        Assert.Equal((6, 7), (npc.CellX, npc.CellY));
        Assert.Equal(npc.Serial, collision.OccupantAt(6, 7));

        // Duplicate add at same (map, x, y) is ignored
        npcs.Apply(new FieldNpcPacket(1, 85, 6, 7, FieldNpcPacket.ActionAdd), currentMapId: 1, data, collision, null, rng);
        Assert.Single(npcs.All);

        // Reloading map 1 recreates the dynamic NPC (0x00ad3730 +0x760)
        var freshCollision = OpenGrid();
        npcs.LoadForMap(1, data, freshCollision, sprites: null, rng);
        var reloaded = Assert.Single(npcs.All);
        Assert.Equal((6, 7), (reloaded.CellX, reloaded.CellY));
        Assert.Equal(reloaded.Serial, freshCollision.OccupantAt(6, 7));

        // Remove vacates collision and deletes from persistent list
        npcs.Apply(new FieldNpcPacket(1, 85, 6, 7, FieldNpcPacket.ActionRemove), currentMapId: 1, data, freshCollision, null, rng);
        Assert.Empty(npcs.All);
        Assert.Equal(default, freshCollision.OccupantAt(6, 7));

        npcs.LoadForMap(1, data, OpenGrid(), sprites: null, rng);
        Assert.Empty(npcs.All);
    }

    [Fact]
    public void Custom_avatar_index_is_parsed_from_sprite_column()
    {
        var data = DataWithNpcs(NpcRow(1, 77, "Avatar NPC", 0, 3, 3, "Custom_1", autoSpawn: 1));
        var def = data.FindNpc(77);
        Assert.NotNull(def);
        Assert.Equal(1, def.CustomAvatarIndex);
        Assert.Equal(2, data.CustomAvatarTamerId(1));
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
