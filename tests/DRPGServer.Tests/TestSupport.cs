using DRPGServer.Configuration;
using DRPGServer.Data.GameData;
using DRPGServer.Game.World;
using System.Reflection;
using DRPGServer.Network;
using DRPGServer.Network.Handlers;
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
        new(Digimon(), new DRPGServer.Game.Entities.Inventory(TestSupport.Data.FindItem, 999))
        {
            Uid = 1, AccountUid = 1, Slot = 0, Name = "Tester", TamerId = 1, MapId = map, X = 56, Y = 56,
        };
}

/// <summary>
/// Entrega um payload ao handler pelo mesmo caminho do <see cref="PacketDispatcher"/> (parse tipado e, no mundo,
/// <c>Prepare</c> seguido da ação). Payloads mais curtos que o mínimo do request são completados com zeros, para os
/// testes montarem só os campos que importam.
/// </summary>
internal static class HandlerDispatch
{
    public static void Dispatch(this IWorldPacketHandler handler, Session session, ReadOnlyMemory<byte> payload) =>
        handler.Prepare(session, Pad(handler, payload))();

    public static void Dispatch(this IWorldPacketHandler handler, Session session, PacketReader packet) =>
        handler.Dispatch(session, packet.ReadBytes(packet.Remaining));

    public static ValueTask DispatchAsync(this ISessionPacketHandler handler, Session session, ReadOnlyMemory<byte> payload,
        CancellationToken ct = default) => handler.HandleAsync(session, Pad(handler, payload), ct);

    public static ValueTask DispatchAsync(this ISessionPacketHandler handler, Session session, PacketReader packet,
        CancellationToken ct = default) => handler.DispatchAsync(session, packet.ReadBytes(packet.Remaining), ct);

    /// <summary>Tamanho mínimo do request do handler (<see cref="IClientPacket{TSelf}.Length"/>).</summary>
    public static PacketLength RequestLength(Type handlerType)
    {
        for (var t = handlerType; t is not null; t = t.BaseType)
        {
            if (!t.IsGenericType) continue;
            var def = t.GetGenericTypeDefinition();
            if (def == typeof(PlayerHandler<>) || def == typeof(SessionHandler<>))
                return (PacketLength)t.GetGenericArguments()[0]
                    .GetProperty(nameof(IClientPacket<IgnoredPayload>.Length), BindingFlags.Public | BindingFlags.Static)!
                    .GetValue(null)!;
        }
        throw new InvalidOperationException($"{handlerType.Name} não deriva de PlayerHandler<T> nem SessionHandler<T>");
    }

    private static ReadOnlyMemory<byte> Pad(object handler, ReadOnlyMemory<byte> payload)
    {
        int min = RequestLength(handler.GetType()).Min;
        if (payload.Length >= min) return payload;
        var padded = new byte[min];
        payload.CopyTo(padded);
        return padded;
    }
}

/// <summary>
/// Mundo com a thread rodando e o <see cref="WorldIo"/> dele, para testes com I/O. <see cref="SettleAsync"/> espera o
/// I/O pendente e as continuações dele terminarem na thread do mundo.
/// </summary>
internal sealed class WorldHarness : IAsyncDisposable
{
    private static readonly TimeSpan SettleTimeout = TimeSpan.FromSeconds(10);

    public GameWorld World { get; } = TestSupport.NewWorld();
    public WorldIo Io { get; }

    public WorldHarness() => Io = new WorldIo(World, NullLogger<WorldIo>.Instance);

    public Task StartAsync() => World.StartAsync(CancellationToken.None);

    public async Task StopAsync()
    {
        await Io.StopAsync(CancellationToken.None);
        await World.StopAsync(CancellationToken.None);
    }

    public ValueTask DisposeAsync() => new(StopAsync());

    public Task<T> InvokeAsync<T>(Func<T> work) => World.InvokeAsync(work);

    /// <summary>Roda <paramref name="action"/> na thread do mundo e espera o I/O que ela iniciou.</summary>
    public async Task RunAsync(Action action)
    {
        await World.InvokeAsync(() =>
        {
            action();
            return true;
        });
        await SettleAsync();
    }

    public Task SettleAsync() => Io.DrainAsync(new CancellationTokenSource(SettleTimeout).Token);

    /// <summary>Entrega o payload ao handler na thread do mundo e espera o I/O dele.</summary>
    public Task DispatchAsync(IWorldPacketHandler handler, Session session, PacketReader packet) =>
        RunAsync(() => handler.Dispatch(session, packet));
}
