using System.Net;
using System.Reflection;
using DRPGServer.Configuration;
using DRPGServer.Data.GameData;
using DRPGServer.Data.Repositories;
using DRPGServer.Game.Battle;
using DRPGServer.Game.Entities;
using DRPGServer.Game.World;
using DRPGServer.Game;
using DRPGServer.Network.Handlers;
using DRPGServer.Network.Protocol;
using DRPGServer.Network;
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

    public static IOptions<NetworkOptions> NetworkOptions(Action<NetworkOptions>? configure = null)
    {
        var o = new NetworkOptions();
        configure?.Invoke(o);
        return Options.Create(o);
    }

    /// <summary>Sessão sem socket, vinda de <paramref name="address"/> (padrão 127.0.0.1).</summary>
    public static Session NewSession(ServerRole role = ServerRole.Map, string address = "127.0.0.1") =>
        new(null, role, NullLogger.Instance, new PacketTrace(NullLogger<PacketTrace>.Instance),
            new IPEndPoint(IPAddress.Parse(address), 50_000));

    public static GameWorld NewWorld() => new(NullLogger<GameWorld>.Instance);

    /// <summary>Relógio real; testes de expiração usam um <see cref="GameClock"/> sobre <see cref="ManualTime"/>.</summary>
    public static IGameClock Clock { get; } = new GameClock(TimeProvider.System);

    /// <summary>Inventário sem tabelas: toda pilha vai até <paramref name="maxStack"/>.</summary>
    public static Inventory EmptyInventory(uint maxStack) => new(_ => null, maxStack, _ => null, Clock);

    /// <summary>Serviço de eventos de EXP sobre um <see cref="FakeExpEventStore"/> (sem evento até um <c>Apply</c>).</summary>
    public static ExpEventService NewExpEvents(IOptions<GameOptions>? options = null, FakeExpEventStore? store = null) =>
        new(store ?? new FakeExpEventStore(), options ?? GameOptions(), NullLogger<ExpEventService>.Instance);

    /// <summary>Sistema de batalha com as dependências reais de jogo e eventos de EXP falsos.</summary>
    public static BattleSystem NewBattles(GameWorld world, GameData? data = null, IOptions<GameOptions>? options = null,
        Taming? taming = null, ExpEventService? expEvents = null)
    {
        data ??= Data;
        options ??= GameOptions();
        return new BattleSystem(world, data, options, NullLogger<BattleSystem>.Instance,
            taming ?? new Taming(data, new DigimonUids(), options), expEvents ?? NewExpEvents(options));
    }

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
        new(Digimon(), new DRPGServer.Game.Entities.Inventory(TestSupport.Data.FindItem, 999, _ => null, TestSupport.Clock))
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

/// <summary>Evento de EXP configurável no lugar da tabela <c>exp_event</c>.</summary>
internal sealed class FakeExpEventStore : IExpEventStore
{
    public ActiveExpEvent? Active { get; set; }
    public int Queries { get; private set; }

    public Task<ActiveExpEvent?> FindActiveAsync(CancellationToken ct)
    {
        Queries++;
        return Task.FromResult(Active);
    }
}

/// <summary>Guarda os snapshots que seriam gravados no banco.</summary>
internal sealed class RecordingSnapshotStore : ICharacterSnapshotStore
{
    public List<CharacterSnapshot> Saved { get; } = [];

    public Task SaveAsync(CharacterSnapshot snapshot, CancellationToken ct)
    {
        Saved.Add(snapshot);
        return Task.CompletedTask;
    }
}
