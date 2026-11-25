using System.Text.RegularExpressions;
using DRPGServer.Configuration;
using DRPGServer.Data.Repositories;
using DRPGServer.Game.Entities;
using DRPGServer.Game.World;
using DRPGServer.Network.Packets.Channel;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DRPGServer.Network.Handlers;

/// <summary>Ajuda comum dos handlers do channel: a lista vem sempre do banco.</summary>
public sealed class CharacterListService(CharacterRepository characters)
{
    public async Task<CharacterSummary?[]> LoadSlotsAsync(uint accountUid, CancellationToken ct)
    {
        var slots = new CharacterSummary?[CharacterListPacket.Slots];
        foreach (var row in await characters.ListByAccountAsync(accountUid, ct))
            if (row.Slot < slots.Length)
                slots[row.Slot] = CharacterFactory.Summary(row);
        return slots;
    }

    public async Task SendAsync(Session session, uint accountUid, CancellationToken ct) =>
        session.Send(new CharacterListPacket(await LoadSlotsAsync(accountUid, ct), Random.Shared));
}

/// <summary>0x03: o jogador clicou num canal.</summary>
[PacketHandler(ChannelOpcode.ChannelSelect)]
public sealed class ChannelSelectHandler(IOptions<GameOptions> options) : SessionHandler<ChannelSelectRequest>
{
    protected override ValueTask HandleAsync(Session session, ChannelSelectRequest request, CancellationToken ct)
    {
        session.Send(new ChannelSelectPacket(options.Value.FourthStarterDigimon));
        return ValueTask.CompletedTask;
    }
}

/// <summary>0x04: autenticação por usuário + auth key (a do launcher).</summary>
[PacketHandler(ChannelOpcode.Authenticate)]
public sealed class AuthenticateHandler(AccountRepository accounts, AccountRegistry registry, ILogger<AuthenticateHandler> log)
    : SessionHandler<AuthenticateRequest>
{
    protected override async ValueTask HandleAsync(Session session, AuthenticateRequest request, CancellationToken ct)
    {
        var record = await accounts.FindByUsernameAsync(request.Username, ct);
        if (record is null || string.IsNullOrEmpty(record.AuthKey) || record.AuthKey != request.AuthKey)
        {
            log.LogWarning("Autenticação recusada para {Username} de {Session}", request.Username, session);
            session.Send(new AuthenticationPacket(ChannelError.InvalidCredentials, request.Username, request.AuthKey,
                request.Mac1, request.Mac2, request.Mac3));
            return;
        }

        registry.SignIn(record.Account, session);
        log.LogInformation("{Username} autenticado ({Authority})", record.Account.Username, record.Account.Authority);
        session.Send(new AuthenticationPacket(ChannelError.None, record.Account.Username));
    }
}

/// <summary>0x12: lista de personagens.</summary>
[PacketHandler(ChannelOpcode.CharacterList)]
public sealed class CharacterListHandler(CharacterListService list) : AccountHandler<CharacterListRequest>
{
    protected override async ValueTask HandleAsync(Session session, AuthenticatedAccount account, CharacterListRequest request, CancellationToken ct) =>
        await list.SendAsync(session, account.Uid, ct);
}

/// <summary>0x14: confirmação do usuário.</summary>
[PacketHandler(ChannelOpcode.UserCheck)]
public sealed class UserCheckHandler : AccountHandler<UserCheckRequest>
{
    protected override ValueTask HandleAsync(Session session, AuthenticatedAccount account, UserCheckRequest request, CancellationToken ct)
    {
        session.Send(new UserCheckPacket(account.Username));
        return ValueTask.CompletedTask;
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

        var reply = (int error) => new CharacterCreatePacket(error, header, tamerId, name, digimonId, digimonName);
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
public sealed class CharacterDeleteHandler(CharacterRepository characters, CharacterListService list, ILogger<CharacterDeleteHandler> log)
    : AccountHandler<CharacterDeleteRequest>
{
    protected override async ValueTask HandleAsync(Session session, AuthenticatedAccount account, CharacterDeleteRequest request, CancellationToken ct)
    {
        var uid = request.CharacterUid;
        if (!await characters.DeleteAsync(uid, account.Uid, ct)) return;
        log.LogInformation("{Username} apagou o personagem {Uid}", account.Username, uid);
        session.Send(new CharacterDeletePacket(uid, request.PasswordMd5));
        await list.SendAsync(session, account.Uid, ct);
    }
}

public sealed class ChannelLifecycle(AccountRegistry accounts) : ISessionLifecycle
{
    public ServerRole Role => ServerRole.Channel;
    public void OnConnected(Session session) { }
    public void OnDisconnected(Session session) => accounts.Release(session);
}
