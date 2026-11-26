using System.Buffers;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using DRPG.Launcher.Login;
using DRPG.Launcher.Patching;
using DRPG.Shared.Formats.Pack;
using DRPG.Shared.Protocol;
using DRPG.Shared.Protocol.Launcher;

namespace DRPG.Launcher.Tests;

public sealed class LauncherFlowTests : IDisposable
{
    private readonly string _install = Directory.CreateTempSubdirectory("drpg-flow-").FullName;
    private readonly ECDiffieHellman _key = LauncherCrypto.CreateKey();

    public LauncherFlowTests()
    {
        PackWriter.Create(Path.Combine(_install, "resource"), [("gui\\a.spr", [1])]);
        VersionInfo.Write(Path.Combine(_install, "VersionInfo.ini"), 240);
    }

    public void Dispose()
    {
        Directory.Delete(_install, recursive: true);
        _key.Dispose();
    }

    private static string Config(int port) => $$"""
    {
      "PatchInfo": { "DownloadUrl": "http://patch.test", "PatchVersion": 240 },
      "LoginServer": { "Host": "127.0.0.1", "Port": {{port}}, "KeyFingerprint": "" }
    }
    """;

    private LauncherFlow Flow(int port)
    {
        var http = new HttpClient(new OneFile(Encoding.UTF8.GetBytes(Config(port))));
        var settings = new LauncherSettings("http://patch.test/DR_Configuration.json", AllowInsecure: true, Runner: null);
        return new LauncherFlow(settings, new Patcher(http, _install, allowInsecure: true), server => new LoginClient(
            new LoginClient.Options(LoginClient.Tcp(server.Host, server.Port), 1, null, TimeSpan.FromSeconds(5))));
    }

    private static async Task<LauncherFlow.State> WaitFor(LauncherFlow flow, LauncherStage stage)
    {
        for (int i = 0; i < 500 && flow.Snapshot.Stage != stage; i++)
            await Task.Delay(10);
        return flow.Snapshot;
    }

    [Fact]
    public async Task Up_to_date_install_goes_to_connecting_then_ready()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var server = AnswerHello(listener);
        var flow = Flow(((IPEndPoint)listener.LocalEndpoint).Port);

        flow.Start(default);

        var state = await WaitFor(flow, LauncherStage.Ready);
        Assert.Equal(LauncherStage.Ready, state.Stage);
        Assert.Equal("", state.Status); // the status box hides once ready
        await server;
    }

    [Fact]
    public async Task Unreachable_server_fails_and_retry_connects_again()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        var flow = Flow(port);

        flow.Start(default);
        var failed = await WaitFor(flow, LauncherStage.Failed);
        Assert.True(failed.Status.Contains("Could not connect"), failed.Status);

        using var again = new TcpListener(IPAddress.Loopback, port);
        again.Start();
        var server = AnswerHello(again);
        flow.Retry();

        Assert.Equal(LauncherStage.Ready, (await WaitFor(flow, LauncherStage.Ready)).Stage);
        await server;
    }

    private Task AnswerHello(TcpListener listener) => Task.Run(async () =>
    {
        using var socket = await listener.AcceptTcpClientAsync();
        var stream = socket.GetStream();
        var buffer = new byte[PacketHeader.MaxPacketSize];
        int have = 0;
        while (true)
        {
            var seq = new ReadOnlySequence<byte>(buffer, 0, have);
            if (PacketFramer.TryRead(ref seq, out _, out _) == FrameStatus.Complete)
                break;
            have += await stream.ReadAsync(buffer.AsMemory(have));
        }
        var reply = new LauncherHelloReply(LauncherLimits.ProtocolVersion, LauncherHelloStatus.Ok,
            RandomNumberGenerator.GetBytes(LauncherLimits.NonceBytes), _key.PublicKey.ExportSubjectPublicKeyInfo());
        await stream.WriteAsync(TwoWayPacket.Encode(reply));
    });

    private sealed class OneFile(byte[] body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(body) });
    }
}
