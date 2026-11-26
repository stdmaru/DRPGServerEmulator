using System.Security.Cryptography;
using DRPG.Shared.Protocol;
using DRPG.Shared.Protocol.Launcher;

namespace DRPG.Shared.Tests;

public sealed class LauncherProtocolTests
{
    private static T RoundTrip<T>(T packet) where T : ITwoWayPacket<T> =>
        ClientPacket.Parse<T>(TwoWayPacket.EncodePayload(packet));

    [Fact]
    public void Packets_round_trip()
    {
        Assert.Equal(new LauncherHello(1, 7), RoundTrip(new LauncherHello(1, 7)));

        var hello = new LauncherHelloReply(1, LauncherHelloStatus.Ok, new byte[32], [1, 2, 3]);
        var back = RoundTrip(hello);
        Assert.Equal(hello.Status, back.Status);
        Assert.Equal(hello.PublicKey, back.PublicKey);

        var login = new LauncherLogin([9, 9], new byte[LauncherCrypto.IvBytes], [5, 6, 7]);
        Assert.Equal(login.Sealed, RoundTrip(login).Sealed);

        var reply = new LauncherLoginReply(new byte[LauncherCrypto.IvBytes], [4]);
        Assert.Equal(reply.Sealed, RoundTrip(reply).Sealed);
    }

    [Fact]
    public void Server_opens_what_the_launcher_sealed_and_the_launcher_opens_the_reply()
    {
        using var server = LauncherCrypto.CreateKey();
        var publicKey = server.PublicKey.ExportSubjectPublicKeyInfo();
        var nonce = RandomNumberGenerator.GetBytes(LauncherLimits.NonceBytes);
        var responseKey = RandomNumberGenerator.GetBytes(LauncherCrypto.KeyBytes);
        var credentials = new LauncherCredentials(nonce, responseKey, 3, "tamer@example.com", "pässwörd");

        var request = RoundTrip(LauncherCrypto.Seal(publicKey, nonce, credentials.Encode()));
        var opened = LauncherCredentials.Decode(LauncherCrypto.Open(server, nonce, request));

        Assert.Equal("tamer@example.com", opened.Login);
        Assert.Equal("pässwörd", opened.Password);
        Assert.Equal(responseKey, opened.ResponseKey);

        var result = new LauncherLoginResult(LauncherLoginStatus.Ok, "tamer", new string('A', 40), 5 * 3600);
        var reply = RoundTrip(LauncherCrypto.SealReply(opened.ResponseKey, result.Encode()));
        Assert.Equal(result, LauncherLoginResult.Decode(LauncherCrypto.OpenReply(responseKey, reply)));
    }

    [Fact]
    public void Tampered_body_wrong_nonce_or_wrong_server_key_fail()
    {
        using var server = LauncherCrypto.CreateKey();
        using var impostor = LauncherCrypto.CreateKey();
        var nonce = RandomNumberGenerator.GetBytes(LauncherLimits.NonceBytes);
        var request = LauncherCrypto.Seal(server.PublicKey.ExportSubjectPublicKeyInfo(), nonce, [1, 2, 3]);

        var tampered = request with { Sealed = (byte[])request.Sealed.Clone() };
        tampered.Sealed[0] ^= 1;

        Assert.ThrowsAny<CryptographicException>(() => LauncherCrypto.Open(server, nonce, tampered));
        Assert.ThrowsAny<CryptographicException>(() => LauncherCrypto.Open(server, new byte[LauncherLimits.NonceBytes], request));
        Assert.ThrowsAny<CryptographicException>(() => LauncherCrypto.Open(impostor, nonce, request));
    }

    [Fact]
    public void Fingerprint_matches_only_its_key()
    {
        using var a = LauncherCrypto.CreateKey();
        using var b = LauncherCrypto.CreateKey();
        var keyA = a.PublicKey.ExportSubjectPublicKeyInfo();
        var fingerprint = LauncherCrypto.Fingerprint(keyA);

        Assert.Equal(64, fingerprint.Length);
        Assert.True(LauncherCrypto.MatchesFingerprint(keyA, fingerprint.ToUpperInvariant()));
        Assert.False(LauncherCrypto.MatchesFingerprint(b.PublicKey.ExportSubjectPublicKeyInfo(), fingerprint));
    }

    [Fact]
    public void Fields_over_the_limits_are_refused()
    {
        var tooLong = new string('x', LauncherLimits.MaxLoginBytes + 1);
        var credentials = new LauncherCredentials(new byte[32], new byte[32], 1, tooLong, "p");
        Assert.Throws<ArgumentException>(credentials.Encode);

        var w = new PacketWriter();
        w.WriteBytes(new byte[64]);
        w.WriteU32(1);
        w.WriteU16(LauncherLimits.MaxLoginBytes + 1);
        w.WriteBytes(new byte[LauncherLimits.MaxLoginBytes + 1]);
        w.WriteU16(0);
        Assert.Throws<PacketFormatException>(() => LauncherCredentials.Decode(w.Payload.ToArray()));
    }

    [Fact]
    public void Credentials_never_print_the_password()
    {
        var credentials = new LauncherCredentials(new byte[32], new byte[32], 1, "tamer", "secret");
        Assert.DoesNotContain("secret", credentials.ToString());
    }
}
