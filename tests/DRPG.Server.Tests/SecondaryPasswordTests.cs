using System.Security.Cryptography;
using System.Text;
using DRPG.Server.Configuration;
using DRPG.Server.Data.Repositories;
using DRPG.Server.Game.Accounts;
using DRPG.Shared.Packets.Channel;
using Microsoft.Extensions.Options;

namespace DRPG.Server.Tests;

/// <summary>Tests for <see cref="SecondaryPasswordService"/> across Disabled, Optional and Required modes.</summary>
public sealed class SecondaryPasswordTests
{
    private sealed class FakeSecondaryPasswordStore : ISecondaryPasswordStore
    {
        private readonly Dictionary<uint, string> _hashes = [];

        public Task<string?> GetSecondaryPasswordHashAsync(uint accountUid, CancellationToken ct) =>
            Task.FromResult(_hashes.GetValueOrDefault(accountUid));

        public Task SetSecondaryPasswordHashAsync(uint accountUid, string hash, CancellationToken ct)
        {
            _hashes[accountUid] = hash;
            return Task.CompletedTask;
        }
    }

    private static string Md5(string text) => Convert.ToHexStringLower(MD5.HashData(Encoding.UTF8.GetBytes(text)));

    private static SecondaryPasswordService CreateService(SecondaryPasswordMode mode, FakeSecondaryPasswordStore? store = null) =>
        new(store ?? new FakeSecondaryPasswordStore(), Options.Create(new GameOptions { SecondaryPasswordMode = mode }));

    [Fact]
    public async Task Disabled_mode_hides_system_refuses_change_and_allows_delete()
    {
        var service = CreateService(SecondaryPasswordMode.Disabled);
        Assert.Equal(SecondaryPasswordStatus.Disabled, await service.GetStatusAsync(1, CancellationToken.None));
        Assert.Equal(SecondaryPasswordResult.InvalidOldPassword,
            await service.ChangeAsync(1, "You are a New Tamer.", Md5("abc1"), Md5("abc1"), CancellationToken.None));
        Assert.True(await service.VerifyForDeleteAsync(1, Md5(""), CancellationToken.None));
    }

    [Fact]
    public async Task Optional_mode_allows_delete_until_registered_then_requires_matching_password()
    {
        var store = new FakeSecondaryPasswordStore();
        var service = CreateService(SecondaryPasswordMode.Optional, store);

        Assert.Equal(SecondaryPasswordStatus.OptionalUnset, await service.GetStatusAsync(1, CancellationToken.None));
        Assert.True(await service.VerifyForDeleteAsync(1, Md5(""), CancellationToken.None));

        // Register via Exchange Password
        var first = Md5("pass1234");
        Assert.Equal(SecondaryPasswordResult.Success,
            await service.ChangeAsync(1, "You are a New Tamer.", first, first, CancellationToken.None));
        Assert.Equal(SecondaryPasswordStatus.Set, await service.GetStatusAsync(1, CancellationToken.None));

        // Now delete requires the registered secondary password
        Assert.False(await service.VerifyForDeleteAsync(1, Md5(""), CancellationToken.None));
        Assert.False(await service.VerifyForDeleteAsync(1, Md5("wrong99"), CancellationToken.None));
        Assert.True(await service.VerifyForDeleteAsync(1, first, CancellationToken.None));

        // Changing requires the old password
        var second = Md5("newpass9");
        Assert.Equal(SecondaryPasswordResult.InvalidOldPassword,
            await service.ChangeAsync(1, Md5("wrong99"), second, second, CancellationToken.None));
        Assert.Equal(SecondaryPasswordResult.Success,
            await service.ChangeAsync(1, first, second, second, CancellationToken.None));
        Assert.False(await service.VerifyForDeleteAsync(1, first, CancellationToken.None));
        Assert.True(await service.VerifyForDeleteAsync(1, second, CancellationToken.None));
    }

    [Fact]
    public async Task Required_mode_reports_required_unset_and_blocks_delete_until_registered()
    {
        var store = new FakeSecondaryPasswordStore();
        var service = CreateService(SecondaryPasswordMode.Required, store);

        Assert.Equal(SecondaryPasswordStatus.RequiredUnset, await service.GetStatusAsync(1, CancellationToken.None));
        Assert.False(await service.VerifyForDeleteAsync(1, Md5(""), CancellationToken.None));

        // Hostile / mismatched new password rejected with InvalidNewPassword
        Assert.Equal(SecondaryPasswordResult.InvalidNewPassword,
            await service.ChangeAsync(1, "You are a New Tamer.", "not-md5", "not-md5", CancellationToken.None));
        Assert.Equal(SecondaryPasswordResult.InvalidNewPassword,
            await service.ChangeAsync(1, "You are a New Tamer.", Md5("abc1"), Md5("abc2"), CancellationToken.None));

        var pass = Md5("hero2026");
        Assert.Equal(SecondaryPasswordResult.Success,
            await service.ChangeAsync(1, "You are a New Tamer.", pass, pass, CancellationToken.None));
        Assert.Equal(SecondaryPasswordStatus.Set, await service.GetStatusAsync(1, CancellationToken.None));
        Assert.True(await service.VerifyForDeleteAsync(1, pass, CancellationToken.None));
    }
}
