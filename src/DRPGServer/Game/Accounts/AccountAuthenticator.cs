using DRPGServer.Data.Repositories;
using DRPGServer.Game.Entities;
using Microsoft.Extensions.Logging;

namespace DRPGServer.Game.Accounts;

/// <summary>
/// Confere usuário + <c>auth_key</c> do channel (0x04). A chave fica no banco como hash
/// (<see cref="AuthKeyHash"/>); chave em texto puro, como o emissor ainda grava, é aceita uma vez, em tempo constante, e
/// convertida na hora. Usuário inexistente custa o mesmo tempo que chave errada. Nenhuma chave vai para o log.
/// </summary>
public sealed class AccountAuthenticator(IAccountStore accounts, ILogger<AccountAuthenticator> log)
{
    public async Task<AuthenticatedAccount?> AuthenticateAsync(string username, string key, CancellationToken ct)
    {
        var record = await accounts.FindByUsernameAsync(username, ct);
        if (record is null || key.Length == 0)
            return Refuse(key);

        // a chave em texto puro é a emissão mais nova: o emissor grava nela, e a conversão a apaga
        if (!string.IsNullOrEmpty(record.AuthKey))
        {
            if (!AuthKeyHash.PlainEquals(key, record.AuthKey))
                return Refuse(key);
            await ConvertAsync(record.Account.Uid, record.AuthKey, ct);
            return record.Account;
        }

        if (string.IsNullOrEmpty(record.AuthKeyHash))
            return Refuse(key);
        return AuthKeyHash.Verify(key, record.AuthKeyHash) ? record.Account : null;
    }

    private static AuthenticatedAccount? Refuse(string key)
    {
        AuthKeyHash.SpendVerifyTime(key);
        return null;
    }

    /// <summary>Na partida: converte toda chave ainda em texto puro.</summary>
    public async Task<int> ConvertPlainKeysAsync(CancellationToken ct)
    {
        int converted = 0;
        foreach (var (uid, key) in await accounts.PlainAuthKeysAsync(ct))
        {
            if (await ConvertAsync(uid, key, ct)) converted++;
        }
        return converted;
    }

    private async Task<bool> ConvertAsync(uint accountUid, string plainKey, CancellationToken ct)
    {
        if (await accounts.StoreAuthKeyHashAsync(accountUid, plainKey, AuthKeyHash.Hash(plainKey), ct)) return true;
        log.LogInformation("Conta {Uid}: auth_key mudou durante a conversão; fica para a próxima", accountUid);
        return false;
    }
}
