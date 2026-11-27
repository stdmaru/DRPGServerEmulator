using System.Text.RegularExpressions;
using DRPG.Server.Configuration;
using DRPG.Server.Data.Repositories;
using DRPG.Server.Game.Accounts;
using DRPG.Server.Game.Entities;
using DRPG.Server.Game.World;
using DRPG.Shared.Packets.Channel;
using DRPG.Shared.Protocol;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DRPG.Server.Network.Handlers;

/// <summary>Ajuda comum dos handlers do channel: a lista vem sempre do banco.</summary>
public sealed class CharacterListService(CharacterRepository characters)
{
    public async Task<CharacterSummary?[]> LoadSlotsAsync(uint accountUid, CancellationToken ct)
    {
        var slots = new CharacterSummary?[CharacterListReply.SlotCount];
        foreach (var row in await characters.ListByAccountAsync(accountUid, ct))
            if (row.Slot < slots.Length)
                slots[row.Slot] = CharacterFactory.Summary(row);
        return slots;
    }

    /// <summary>
    /// The list reply for the four slots. ✅ Every slot carries its own random check offset (the client only checks
    /// <c>check − offset = map + level</c>).
    /// </summary>
    public static CharacterListReply BuildList(IReadOnlyList<CharacterSummary?> slots, Random random)
    {
        var reply = new CharacterListSlot[CharacterListReply.SlotCount];
        for (byte slot = 0; slot < reply.Length; slot++)
        {
            var offset = random.Next(0, 255);
            reply[slot] = slot < slots.Count && slots[slot] is { } c
                ? new CharacterListSlot(c.Uid, c.TamerId, c.Name, c.Level, c.X, c.Y, c.LeaderSpeciesId, c.LeaderLevel,
                    c.LeaderName, c.TotalBattles, c.TotalWins, slot, c.MapId, offset)
                : CharacterListSlot.Empty(slot, offset);
        }
        return new CharacterListReply(reply);
    }

    public async Task SendAsync(Session session, uint accountUid, CancellationToken ct) =>
        session.Send(BuildList(await LoadSlotsAsync(accountUid, ct), Random.Shared));
}

/// <summary>0x03: o jogador clicou num canal.</summary>
[PacketHandler(ChannelOpcode.ChannelSelect)]
public sealed class ChannelSelectHandler(IOptions<GameOptions> options) : SessionHandler<ChannelSelectRequest>
{
    protected override ValueTask HandleAsync(Session session, ChannelSelectRequest request, CancellationToken ct)
    {
        session.Send(new ChannelSelectReply(options.Value.FourthStarterDigimon, request.Name));
        return ValueTask.CompletedTask;
    }
}

/// <summary>0x04: autenticação por usuário + auth key (a do launcher).</summary>
[PacketHandler(ChannelOpcode.Authenticate)]
public sealed class AuthenticateHandler(AccountAuthenticator authenticator, AccountRegistry registry, ILogger<AuthenticateHandler> log)
    : SessionHandler<AuthenticateRequest>
{
    protected override async ValueTask HandleAsync(Session session, AuthenticateRequest request, CancellationToken ct)
    {
        if (session.Account is not null)
        {
            session.Close("segunda autenticação na mesma conexão");
            return;
        }

        if (await authenticator.AuthenticateAsync(request.Username, request.AuthKey, ct) is not { } account)
        {
            log.LogWarning("Autenticação recusada para {Username} de {Session}", request.Username, session);
            session.Send(new AuthenticateReply(ChannelError.InvalidCredentials, request.Username, request.AuthKey,
                request.Mac1, request.Mac2, request.Mac3));
            return;
        }

        if (!registry.SignIn(account, session))
        {
            session.Close("conexão sem endereço");
            return;
        }
        log.LogInformation("{Username} autenticado ({Authority})", account.Username, account.Authority);
        session.Send(new AuthenticateReply(ChannelError.None, account.Username));
    }
}

/// <summary>0x12: lista de personagens.</summary>
[PacketHandler(ChannelOpcode.CharacterList)]
public sealed class CharacterListHandler(CharacterListService list) : AccountHandler<CharacterListRequest>
{
    protected override async ValueTask HandleAsync(Session session, AuthenticatedAccount account, CharacterListRequest request, CancellationToken ct) =>
        await list.SendAsync(session, account.Uid, ct);
}

/// <summary>0x14: confirmação do usuário e estado da senha secundária (byte 21).</summary>
[PacketHandler(ChannelOpcode.UserCheck)]
public sealed class UserCheckHandler(SecondaryPasswordService secondaryPasswords) : AccountHandler<UserCheckRequest>
{
    protected override async ValueTask HandleAsync(Session session, AuthenticatedAccount account, UserCheckRequest request, CancellationToken ct)
    {
        var status = await secondaryPasswords.GetStatusAsync(account.Uid, ct);
        session.Send(new UserCheckReply(account.Username, status));
    }
}

/// <summary>0x15: cadastro ou troca da senha secundária (✅ v322 <c>0x00ae6070</c> / <c>0x00b45c30</c>).</summary>
[PacketHandler(ChannelOpcode.SecondaryPassword)]
public sealed class SecondaryPasswordHandler(SecondaryPasswordService secondaryPasswords, ILogger<SecondaryPasswordHandler> log)
    : AccountHandler<SecondaryPasswordRequest>
{
    protected override async ValueTask HandleAsync(Session session, AuthenticatedAccount account, SecondaryPasswordRequest request, CancellationToken ct)
    {
        var result = await secondaryPasswords.ChangeAsync(account.Uid, request.OldPassword, request.NewPassword, request.ConfirmPassword, ct);
        log.LogInformation("{Username} secondary password change: {Result}", account.Username, result);
        session.Send(new SecondaryPasswordReply(result));
    }
}

/// <summary>0x10: criar personagem.</summary>
[PacketHandler(ChannelOpcode.CharacterCreate)]
public sealed partial class CharacterCreateHandler(
    CharacterRepository characters, CharacterListService list, IOptions<GameOptions> options, ILogger<CharacterCreateHandler> log) : AccountHandler<CharacterCreateRequest>
{
    private static readonly ushort[] Starters = [10, 11, 12];
    private static readonly ushort[] FourthStarters = [24, 27, 31, 40]; // Chibimon, Yarmon, Hopmon, Dorimon

    [GeneratedRegex("^[a-zA-Z0-9]{1,20}$")]
    private static partial Regex ValidName();

    // a lista de personagens tem 24 bytes para o nome do digimon
    [GeneratedRegex("^[a-zA-Z0-9]{1,23}$")]
    private static partial Regex ValidDigimonName();

    protected override async ValueTask HandleAsync(Session session, AuthenticatedAccount account, CharacterCreateRequest request, CancellationToken ct)
    {
        var (header, tamerId, name, digimonId, digimonName) = request;

        var o = options.Value;
        bool validDigimon = Starters.Contains(digimonId) ||
                            (o.FourthStarterDigimon is >= 0 and < 4 && FourthStarters[o.FourthStarterDigimon] == digimonId);
        if (tamerId is < 1 or > 4 || !validDigimon || !ValidName().IsMatch(name) || !ValidDigimonName().IsMatch(digimonName))
        {
            log.LogWarning("{Session}: criação inválida (tamer {Tamer}, digimon {Digimon}, nomes '{Name}'/'{DigimonName}')",
                session, tamerId, digimonId, name, digimonName);
            return;
        }

        // o cliente escolhe o slot (✅ sessões 3 e 53); se ele estiver ocupado, usa o primeiro livre
        var slots = await list.LoadSlotsAsync(account.Uid, ct);
        int slot = header.Slot < slots.Length && slots[header.Slot] is null ? header.Slot : Array.IndexOf(slots, null);
        if (slot < 0) return;

        var reply = (int error) => new CharacterCreateReply(error, header, tamerId, name, digimonId, digimonName);
        if (await characters.IsNameTakenAsync(name, ct))
        {
            session.Send(reply(ChannelError.NameTaken));
            return;
        }

        var uid = await characters.CreateAsync(new NewCharacter(account.Uid, (byte)slot, name, tamerId, digimonId, digimonName,
            o.StartMapId, o.StartPosX, o.StartPosY, Math.Min(o.DefaultDepositorySlots, (byte)Character.MaxDepositorySlots)), ct);
        if (uid is null)
        {
            session.Send(reply(ChannelError.NameTaken));
            return;
        }

        log.LogInformation("{Username} criou {Name} (uid {Uid}, slot {Slot})", account.Username, name, uid, slot);
        session.Send(reply(ChannelError.None));
        await list.SendAsync(session, account.Uid, ct);
    }
}

/// <summary>0x11: apagar personagem.</summary>
[PacketHandler(ChannelOpcode.CharacterDelete)]
public sealed class CharacterDeleteHandler(
    CharacterRepository characters, CharacterListService list, SecondaryPasswordService secondaryPasswords, PlayerSystem players,
    ILogger<CharacterDeleteHandler> log)
    : AccountHandler<CharacterDeleteRequest>
{
    private const int SecondaryPasswordMismatchError = 2000;

    protected override async ValueTask HandleAsync(Session session, AuthenticatedAccount account, CharacterDeleteRequest request, CancellationToken ct)
    {
        var uid = request.CharacterUid;
        if (!await secondaryPasswords.VerifyForDeleteAsync(account.Uid, request.PasswordMd5, ct))
        {
            log.LogWarning("{Username}: secondary password mismatch when deleting character {Uid}", account.Username, uid);
            session.Send(new CharacterDeleteReply(SecondaryPasswordMismatchError, uid, request.PasswordMd5));
            return;
        }
        // o personagem no mundo, ou com o save de saída ainda na fila, não se apaga: o save quebraria contra a linha apagada
        // (❓ o original não tem resposta capturada para a recusa: fica sem resposta, como o personagem que não existe)
        if (!await players.ReleaseForDeleteAsync(account.Uid, uid))
        {
            log.LogWarning("{Username}: character {Uid} is in the world, delete refused", account.Username, uid);
            return;
        }
        if (!await characters.DeleteAsync(uid, account.Uid, ct)) return;
        log.LogInformation("{Username} apagou o personagem {Uid}", account.Username, uid);
        session.Send(new CharacterDeleteReply(ChannelError.None, uid, request.PasswordMd5));
        await list.SendAsync(session, account.Uid, ct);
    }
}

public sealed class ChannelLifecycle(AccountRegistry accounts) : ISessionLifecycle
{
    public ServerRole Role => ServerRole.Channel;
    public void OnConnected(Session session) { }
    public void OnDisconnected(Session session) => accounts.Release(session);
}
