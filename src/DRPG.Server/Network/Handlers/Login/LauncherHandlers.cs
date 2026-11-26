using System.Security.Cryptography;
using DRPG.Shared.Protocol;
using DRPG.Shared.Protocol.Launcher;
using DRPG.Server.Configuration;
using DRPG.Server.Game.Accounts;
using DRPG.Server.Network.Packets;
using Microsoft.Extensions.Options;

namespace DRPG.Server.Network.Handlers;

/// <summary>0x0F00 (launcher hello): answers with the server public key and a fresh one-time nonce.</summary>
[PacketHandler(LoginOpcode.LauncherHello)]
public sealed class LauncherHelloHandler(LauncherServerKey key, IOptions<LauncherLoginOptions> options)
    : SessionHandler<LauncherHello>
{
    protected override ValueTask HandleAsync(Session session, LauncherHello request, CancellationToken ct)
    {
        var status = !options.Value.Enabled ? LauncherHelloStatus.Disabled
            : request.Protocol != LauncherLimits.ProtocolVersion ? LauncherHelloStatus.UnsupportedProtocol
            : LauncherHelloStatus.Ok;
        var nonce = RandomNumberGenerator.GetBytes(LauncherLimits.NonceBytes);
        session.LauncherNonce = status == LauncherHelloStatus.Ok ? nonce : null;
        session.Send(new TwoWayServerPacket<LauncherHelloReply>(
            new LauncherHelloReply(LauncherLimits.ProtocolVersion, status, nonce, status == LauncherHelloStatus.Ok ? key.PublicKey : [])));
        return ValueTask.CompletedTask;
    }
}

/// <summary>
/// 0x0F02 (launcher login): opens the sealed credentials with the nonce of this connection's hello (used once), checks
/// them and answers with the result encrypted for the response key. A body that does not decrypt closes the connection.
/// </summary>
[PacketHandler(LoginOpcode.LauncherLogin)]
public sealed class LauncherLoginHandler(LauncherServerKey key, LauncherLoginService logins) : SessionHandler<LauncherLogin>
{
    protected override async ValueTask HandleAsync(Session session, LauncherLogin request, CancellationToken ct)
    {
        if (session.LauncherNonce is not { } nonce)
        {
            session.Close("launcher login without a hello");
            return;
        }
        session.LauncherNonce = null;

        LauncherCredentials credentials;
        try
        {
            credentials = LauncherCredentials.Decode(LauncherCrypto.Open(key.Key, nonce, request));
        }
        catch (Exception e) when (e is CryptographicException or PacketFormatException)
        {
            session.Close("launcher login body does not decrypt");
            return;
        }
        if (!CryptographicOperations.FixedTimeEquals(credentials.ServerNonce, nonce))
        {
            session.Close("launcher login with a stale nonce");
            return;
        }

        var address = session.RemoteAddress?.ToString() ?? "unknown";
        var result = await logins.LoginAsync(credentials, address, ct);
        session.Send(new TwoWayServerPacket<LauncherLoginReply>(LauncherCrypto.SealReply(credentials.ResponseKey, result.Encode())));
    }
}
