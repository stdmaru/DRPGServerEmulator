using DRPGServer.Configuration;
using DRPGServer.Data.GameData;
using DRPGServer.Game.World;
using DRPGServer.Network;
using DRPGServer.Network.Protocol;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace DRPGServer.Tests;

internal static class TestSupport
{
    private static readonly Lazy<GameData> LazyData = new(() =>
        GameDataLoader.Load(Path.Combine(AppContext.BaseDirectory, "Resources"), Pack));

    /// <summary>Pacote sintético em Fixtures/client (gerado de Fixtures/client-src).</summary>
    public static ClientPack Pack => ClientPack.Open(Path.Combine(AppContext.BaseDirectory, "Fixtures", "client", "table.hf"));

    public static GameData Data => LazyData.Value;

    public static IOptions<GameOptions> GameOptions(Action<GameOptions>? configure = null)
    {
        var o = new GameOptions();
        configure?.Invoke(o);
        return Options.Create(o);
    }

    public static Session NewSession(ServerRole role = ServerRole.Map) =>
        new(null, role, NullLogger.Instance, new PacketTrace(NullLogger<PacketTrace>.Instance));

    public static GameWorld NewWorld() => new(NullLogger<GameWorld>.Instance);

    /// <summary>Opcodes enviados (na ordem) e esvazia a fila.</summary>
    public static List<ushort> SentOpcodes(this Session session) =>
        session.DrainOutgoing().Select(f => PacketHeader.Read(f).Opcode).ToList();
}

internal sealed class ManualTime(DateTimeOffset start) : TimeProvider
{
    public DateTimeOffset Now { get; set; } = start;
    public override DateTimeOffset GetUtcNow() => Now;
}

internal static class Entities
{
    public static DRPGServer.Game.Entities.Digimon Digimon(ushort species = 10, ushort level = 1) =>
        new(TestSupport.Data, TestSupport.Data.Species[species], level) { Uid = 7, Name = "Guilmon" };

    public static DRPGServer.Game.Entities.Character Character(byte map = 79) =>
        new(Digimon(), new DRPGServer.Game.Entities.Inventory(999))
        {
            Uid = 1, AccountUid = 1, Slot = 0, Name = "Tester", TamerId = 1, MapId = map, X = 56, Y = 56,
        };
}
