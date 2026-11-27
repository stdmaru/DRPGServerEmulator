using DRPG.Client.Features.Field;
using DRPG.Client.Net;
using DRPG.Client.Startup;
using DRPG.Client.World;
using DRPG.Shared.Formats.Map;
using DRPG.Shared.Packets.Map;
using DRPG.Shared.Protocol;

namespace DRPG.Client.Tests;

/// <summary>
/// A field without a window or a connection: a grid map from text rows (<c>#</c> = blocked), a tamer with no digimon on the map
/// (tamer id 5), and the feature's handlers and actions on it. The session is not started, so the actions send nothing.
/// </summary>
internal sealed class FieldFixture : IDisposable
{
    public static readonly Serial TamerSerial = new(0xAA, 0xBB);

    private readonly WorldSession _session;

    public FieldFixture(string[] rows, Cell tamer, int mapId = 79, NpcTable? npcs = null, Random? random = null)
    {
        Random = random ?? new Random(0);
        Npcs = npcs ?? NpcTable.Empty;
        Area = new FieldArea(mapId, Grid(rows), Npcs, NoFieldSprites.Instance, Random);
        var character = ClientPacket.Parse<CharacterDataPacket>(new byte[CharacterDataPacket.Length.Min])
            with { Serial = TamerSerial, TamerId = 5, X = (short)tamer.X, Y = (short)tamer.Y };
        State = new FieldState(Area, FieldParty.Of(character, NoFieldSprites.Instance, Random), NoFieldSprites.Instance);
        _session = new WorldSession(ClientSettings.Local, new GameCommandLine("tamer", new string('0', 40)), _ => { });
        Actions = new FieldActions(_session, State);
        Handlers = new FieldHandlers(State, Actions, Npcs);
    }

    public Random Random { get; }
    public NpcTable Npcs { get; }
    public FieldArea Area { get; }
    public FieldState State { get; }
    public FieldActions Actions { get; }
    public FieldHandlers Handlers { get; }
    public FieldCollision Collision => State.Area.Collision;
    public FieldWilds Wilds => State.Area.Wilds;
    public FieldNpcs AreaNpcs => State.Area.Npcs;
    public FieldEntity Tamer => State.Party.Tamer;

    /// <summary>A map of <paramref name="rows"/>: <c>#</c> is blocked (byte <c>+0x14</c> = 0), anything else walkable.</summary>
    public static MapFile Grid(params string[] rows)
    {
        int height = rows.Length, width = rows[0].Length;
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
                w.Write(rows[y][x] == '#' ? 0u : 1u);
            }
        w.Write((ushort)0);
        w.Write((ushort)0);
        return MapFile.Parse(ms.ToArray());
    }

    /// <summary>An open map of <paramref name="width"/> × <paramref name="height"/> cells.</summary>
    public static string[] Open(int width, int height) => Enumerable.Repeat(new string('.', width), height).ToArray();

    public void Dispose() => _session.Dispose();
}
