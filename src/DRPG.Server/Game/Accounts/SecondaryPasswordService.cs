using DRPG.Server.Configuration;
using DRPG.Server.Data.Repositories;
using DRPG.Shared.Packets.Channel;
using Microsoft.Extensions.Options;

namespace DRPG.Server.Game.Accounts;

/// <summary>
/// Secondary password rules for channel packets <c>0x14</c>, <c>0x15</c> and <c>0x11</c>
/// (✅ v322 <c>0x00b45c30</c>, <c>0x00c950d0</c>, <c>0x00ae6070</c>).
/// The client sends a 32-character lowercase hex MD5; the server hashes that MD5 with PBKDF2-SHA256 before storing
/// it, and never logs any password or hash.
/// </summary>
public sealed class SecondaryPasswordService(ISecondaryPasswordStore store, IOptions<GameOptions> options)
{
    public const int Md5HexLength = 32;

    /// <summary>Status byte sent in <see cref="UserCheckReply"/> (<c>0x14</c> S2C byte 21).</summary>
    public async Task<SecondaryPasswordStatus> GetStatusAsync(uint accountUid, CancellationToken ct)
    {
        var mode = options.Value.SecondaryPasswordMode;
        if (mode == SecondaryPasswordMode.Disabled)
            return SecondaryPasswordStatus.Disabled;

        var hash = await store.GetSecondaryPasswordHashAsync(accountUid, ct);
        if (!string.IsNullOrEmpty(hash))
            return SecondaryPasswordStatus.Set;

        return mode == SecondaryPasswordMode.Required
            ? SecondaryPasswordStatus.RequiredUnset
            : SecondaryPasswordStatus.OptionalUnset;
    }

    /// <summary>Registers or changes the account's secondary password (<c>0x15</c> C2S → S2C).</summary>
    public async Task<SecondaryPasswordResult> ChangeAsync(
        uint accountUid, string oldPasswordMd5, string newPasswordMd5, string confirmPasswordMd5, CancellationToken ct)
    {
        if (options.Value.SecondaryPasswordMode == SecondaryPasswordMode.Disabled)
            return SecondaryPasswordResult.InvalidOldPassword;

        if (!IsValidMd5Hex(newPasswordMd5) || !string.Equals(newPasswordMd5, confirmPasswordMd5, StringComparison.OrdinalIgnoreCase))
            return SecondaryPasswordResult.InvalidNewPassword;

        var existingHash = await store.GetSecondaryPasswordHashAsync(accountUid, ct);
        if (!string.IsNullOrEmpty(existingHash))
        {
            if (!IsValidMd5Hex(oldPasswordMd5) || !AuthKeyHash.Verify(oldPasswordMd5.ToLowerInvariant(), existingHash))
                return SecondaryPasswordResult.InvalidOldPassword;
        }

        var newHash = AuthKeyHash.Hash(newPasswordMd5.ToLowerInvariant());
        await store.SetSecondaryPasswordHashAsync(accountUid, newHash, ct);
        return SecondaryPasswordResult.Success;
    }

    /// <summary>Verifies the MD5 sent in <see cref="CharacterDeleteRequest"/> (<c>0x11</c> C2S).</summary>
    public async Task<bool> VerifyForDeleteAsync(uint accountUid, string passwordMd5, CancellationToken ct)
    {
        var mode = options.Value.SecondaryPasswordMode;
        if (mode == SecondaryPasswordMode.Disabled)
            return true;

        var existingHash = await store.GetSecondaryPasswordHashAsync(accountUid, ct);
        if (string.IsNullOrEmpty(existingHash))
            return mode == SecondaryPasswordMode.Optional;

        if (!IsValidMd5Hex(passwordMd5))
            return false;

        return AuthKeyHash.Verify(passwordMd5.ToLowerInvariant(), existingHash);
    }

    private static bool IsValidMd5Hex(string value)
    {
        if (value.Length != Md5HexLength)
            return false;
        foreach (char c in value)
        {
            if (!Uri.IsHexDigit(c))
                return false;
        }
        return true;
    }
}
