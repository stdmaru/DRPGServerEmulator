using System.Security.Cryptography;
using System.Text;
using DRPG.Shared.Protocol.Launcher;
using DRPG.Server.Configuration;
using DRPG.Server.Data.Repositories;
using DRPG.Server.Game.World;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DRPG.Server.Game.Accounts;

/// <summary>
/// Checks launcher credentials and issues the game key (docs/protocolo/launcher.md): 40 random hex characters, stored
/// only as a hash, valid for <see cref="LauncherLoginOptions.GameKeyValidHours"/>. The launcher then starts the game
/// with <c>&lt;username&gt; &lt;key&gt;</c>, and the channel 0x04 checks it in <see cref="AccountAuthenticator"/>.
/// Unknown login and wrong password get the same answer and cost the same time. No password or key is logged.
/// </summary>
public sealed class LauncherLoginService(
    ILauncherAccountStore accounts,
    LoginAttemptLimiter limiter,
    IOptions<LauncherLoginOptions> options,
    IGameClock clock,
    ILogger<LauncherLoginService> log)
{
    /// <summary>Prefix of passwords copied in plain text by migration 0015, before conversion.</summary>
    public const string PlainPrefix = "plain$";

    private const int GameKeyBytes = LauncherLimits.GameKeyChars / 2;

    public async Task<LauncherLoginResult> LoginAsync(LauncherCredentials credentials, string address, CancellationToken ct)
    {
        var o = options.Value;
        if (credentials.LauncherVersion < o.MinLauncherVersion)
            return LauncherLoginResult.Failure(LauncherLoginStatus.OutdatedLauncher);

        var login = credentials.Login.Trim();
        if (!limiter.TryAttempt(address, login))
        {
            log.LogWarning("Launcher login: too many attempts for {Login} from {Address}", login, address);
            return LauncherLoginResult.Failure(LauncherLoginStatus.TooManyAttempts);
        }

        var record = login.Length == 0 ? null : await accounts.FindByLoginAsync(login, ct);
        if (record?.PasswordHash is not { Length: > 0 } stored)
        {
            PasswordHash.SpendVerifyTime(credentials.Password);
            return Refused(login, address);
        }
        if (!await CheckPasswordAsync(record.Account.Uid, credentials.Password, stored, ct))
            return Refused(login, address);

        var username = record.Account.Username;
        if (!IsGameUsername(username))
        {
            log.LogWarning("Launcher login: account {Uid} has no valid game username", record.Account.Uid);
            return LauncherLoginResult.Failure(LauncherLoginStatus.AccountUnavailable);
        }

        var key = Convert.ToHexString(RandomNumberGenerator.GetBytes(GameKeyBytes));
        var seconds = (uint)Math.Round(o.GameKeyValidHours * 3600);
        await accounts.IssueAuthKeyAsync(record.Account.Uid, AuthKeyHash.Hash(key), clock.UnixNow + seconds, ct);
        log.LogInformation("Launcher login: {Username} from {Address}, key valid for {Hours} h", username, address, o.GameKeyValidHours);
        return new LauncherLoginResult(LauncherLoginStatus.Ok, username, key, seconds);
    }

    /// <summary>At start: turns every <c>plain$</c> password left by migration 0015 into a PBKDF2 hash.</summary>
    public async Task<int> ConvertPlainPasswordsAsync(CancellationToken ct)
    {
        int converted = 0;
        foreach (var (uid, stored) in await accounts.PlainPasswordsAsync(ct))
            if (await accounts.StorePasswordHashAsync(uid, stored, PasswordHash.Hash(stored[PlainPrefix.Length..]), ct))
                converted++;
        return converted;
    }

    private async Task<bool> CheckPasswordAsync(uint uid, string password, string stored, CancellationToken ct)
    {
        if (stored.StartsWith(PlainPrefix, StringComparison.Ordinal))
        {
            if (!PasswordHash.PlainEquals(password, stored[PlainPrefix.Length..]))
            {
                PasswordHash.SpendVerifyTime(password);
                return false;
            }
        }
        else if (!PasswordHash.Verify(password, stored))
        {
            return false;
        }
        else if (!PasswordHash.NeedsRehash(stored))
        {
            return true;
        }
        await accounts.StorePasswordHashAsync(uid, stored, PasswordHash.Hash(password), ct);
        return true;
    }

    /// <summary>Fits the <c>username[21]</c> field of the channel 0x04 (Latin-1, at most 20 bytes, no spaces).</summary>
    private static bool IsGameUsername(string username) =>
        username.Length is > 0 and <= LauncherLimits.MaxGameUsernameBytes
        && username.All(c => c is > ' ' and <= 'ÿ')
        && Encoding.Latin1.GetByteCount(username) == username.Length;

    private LauncherLoginResult Refused(string login, string address)
    {
        log.LogInformation("Launcher login refused for {Login} from {Address}", login, address);
        return LauncherLoginResult.Failure(LauncherLoginStatus.InvalidCredentials);
    }
}
