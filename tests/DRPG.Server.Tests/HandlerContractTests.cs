using System.Reflection;
using DRPG.Server.Game.Entities;
using DRPG.Server.Network;
using DRPG.Server.Network.Handlers;
using DRPG.Server.Network.Packets;
using DRPG.Server.Network.Packets.Map;
using DRPG.Shared.Protocol;

namespace DRPG.Server.Tests;

/// <summary>Contrato dos requests tipados e das bases de handler (docs/emulador/handlers.md).</summary>
public class HandlerContractTests
{
    private static readonly Assembly Server = typeof(PacketHandlerAttribute).Assembly;

    public static TheoryData<Type> RequestTypes()
    {
        var data = new TheoryData<Type>();
        foreach (var t in Server.GetTypes().Where(IsRequest).OrderBy(t => t.FullName))
            data.Add(t);
        return data;
    }

    public static TheoryData<Type> HandlerTypes()
    {
        var data = new TheoryData<Type>();
        foreach (var t in Server.GetTypes().Where(t => t.GetCustomAttributes<PacketHandlerAttribute>().Any()).OrderBy(t => t.Name))
            data.Add(t);
        return data;
    }

    [Theory]
    [MemberData(nameof(RequestTypes))]
    public void Request_reads_exactly_its_declared_length(Type requestType)
    {
        var length = LengthOf(requestType);
        Parse(requestType, new byte[length.Min]);                   // consome tudo, sem sobra

        if (length.Min > 0)
            Assert.Throws<PacketFormatException>(() => Parse(requestType, new byte[length.Min - 1]));
        if (length.Max == length.Min)
            Assert.Throws<PacketFormatException>(() => Parse(requestType, new byte[length.Min + 1]));
    }

    [Theory]
    [MemberData(nameof(HandlerTypes))]
    public void Handler_uses_a_typed_base(Type handlerType)
    {
        HandlerDispatch.RequestLength(handlerType);                 // lança se não deriva de SessionHandler<T>/PlayerHandler<T>
        if (DerivesFrom(handlerType, typeof(PlayerHandler<>)))
            Assert.NotNull(handlerType.GetCustomAttribute<RequiresStateAttribute>());
    }

    [Fact]
    public void Player_handler_without_required_state_fails_at_construction()
    {
        Assert.Throws<InvalidOperationException>(() => new UndeclaredProbe());
    }

    [Fact]
    public void ChatNormalRequest_reads_291_byte_v322_payload_and_rejects_277_bytes()
    {
        var payload = new byte[291];
        System.Text.Encoding.ASCII.GetBytes("Maruzak").CopyTo(payload, 0);
        System.Text.Encoding.ASCII.GetBytes("/teleport 10 20").CopyTo(payload, 21);

        var parsed = ClientPacket.Parse<ChatNormalRequest>(payload);
        Assert.Equal("/teleport 10 20", parsed.Message);
        Assert.Throws<PacketFormatException>(() => ClientPacket.Parse<ChatNormalRequest>(new byte[277]));
    }

    [Fact]
    public void Player_packet_before_entering_world_closes_session()
    {
        var session = TestSupport.NewSession();
        var probe = new BattleOnlyProbe();
        probe.Dispatch(session, new byte[KeepAliveRequest.Length.Min]);
        Assert.True(session.IsClosed);
        Assert.False(probe.Handled);
    }

    [Fact]
    public void Player_packet_in_wrong_state_is_rejected_without_handling()
    {
        var (session, _) = PlayerSession();
        var probe = new BattleOnlyProbe();
        probe.Dispatch(session, new byte[KeepAliveRequest.Length.Min]);
        Assert.False(session.IsClosed);
        Assert.False(probe.Handled);
        Assert.True(probe.Rejected);
    }

    [Fact]
    public void Player_packet_in_allowed_state_is_handled()
    {
        var (session, _) = PlayerSession();
        var probe = new IdleProbe();
        probe.Dispatch(session, new byte[KeepAliveRequest.Length.Min]);
        Assert.True(probe.Handled);
    }

    [Fact]
    public async Task Account_packet_before_authentication_closes_session()
    {
        var session = TestSupport.NewSession(ServerRole.Channel);
        var probe = new AccountProbe();
        await probe.DispatchAsync(session, new byte[KeepAliveRequest.Length.Min]);
        Assert.True(session.IsClosed);
        Assert.False(probe.Handled);
    }

    [Fact]
    public void Rename_query_before_entering_world_answers_without_closing()
    {
        var session = TestSupport.NewSession();
        new RenameHandler().Dispatch(session, new byte[RenameRequest.Length.Min]);
        Assert.False(session.IsClosed);
        Assert.Equal([(ushort)MapOpcode.Rename], session.SentOpcodes());
    }

    private static (Session, Player) PlayerSession()
    {
        var session = TestSupport.NewSession();
        var player = new Player(session, new AuthenticatedAccount(1, "u", Authority.None), Entities.Character());
        session.Player = player;
        return (session, player);
    }

    private static bool IsRequest(Type t) =>
        !t.IsAbstract && t.GetInterfaces().Any(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IClientPacket<>));

    private static bool DerivesFrom(Type t, Type genericBase)
    {
        for (var b = t.BaseType; b is not null; b = b.BaseType)
            if (b.IsGenericType && b.GetGenericTypeDefinition() == genericBase) return true;
        return false;
    }

    private static PacketLength LengthOf(Type requestType) =>
        (PacketLength)requestType.GetProperty("Length", BindingFlags.Public | BindingFlags.Static)!.GetValue(null)!;

    private static void Parse(Type requestType, byte[] payload)
    {
        var parse = typeof(ClientPacket).GetMethod(nameof(ClientPacket.Parse))!.MakeGenericMethod(requestType);
        try
        {
            parse.Invoke(null, [new ReadOnlyMemory<byte>(payload)]);
        }
        catch (TargetInvocationException e) when (e.InnerException is not null)
        {
            throw e.InnerException;
        }
    }

    private sealed class UndeclaredProbe : PlayerHandler<KeepAliveRequest>
    {
        protected override void Handle(Player player, KeepAliveRequest request) { }
    }

    [RequiresState(PlayerStates.InBattle)]
    private sealed class BattleOnlyProbe : PlayerHandler<KeepAliveRequest>
    {
        public bool Handled { get; private set; }
        public bool Rejected { get; private set; }
        protected override void Handle(Player player, KeepAliveRequest request) => Handled = true;
        protected override void OnRejected(Player player, KeepAliveRequest request) => Rejected = true;
    }

    [RequiresState(PlayerStates.Idle)]
    private sealed class IdleProbe : PlayerHandler<KeepAliveRequest>
    {
        public bool Handled { get; private set; }
        protected override void Handle(Player player, KeepAliveRequest request) => Handled = true;
    }

    private sealed class AccountProbe : AccountHandler<KeepAliveRequest>
    {
        public bool Handled { get; private set; }

        protected override ValueTask HandleAsync(Session session, AuthenticatedAccount account, KeepAliveRequest request, CancellationToken ct)
        {
            Handled = true;
            return ValueTask.CompletedTask;
        }
    }
}
