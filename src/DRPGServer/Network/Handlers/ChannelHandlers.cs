using System.Text.RegularExpressions;
using DRPGServer.Configuration;
using DRPGServer.Data.Repositories;
using DRPGServer.Game.Entities;
using DRPGServer.Game.World;
using DRPGServer.Network.Packets.Channel;
using DRPGServer.Network.Protocol;
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
public sealed class ChannelSelectHandler(IOptions<GameOptions> options) : ISessionPacketHandler
{
    public ValueTask HandleAsync(Session session, PacketReader packet, CancellationToken ct)
    {
        session.Send(new ChannelSelectPacket(options.Value.FourthStarterDigimon));
        return ValueTask.CompletedTask;
    }
}

/// <summary>0x04: autenticação por usuário + auth key (a do launcher).</summary>
[PacketHandler(ChannelOpcode.Authenticate)]
public sealed class AuthenticateHandler(AccountRepository accounts, AccountRegistry registry, ILogger<AuthenticateHandler> log) : ISessionPacketHandler
{
    public async ValueTask HandleAsync(Session session, PacketReader packet, CancellationToken ct)
    {
        packet.ReadI32();
        packet.ReadI32();
        var username = packet.ReadFixedString(21);
        var authKey = packet.ReadFixedString(40);
        packet.Skip(60);
        var mac1 = packet.ReadFixedString(21);
        var mac2 = packet.ReadFixedString(21);
        var mac3 = packet.ReadFixedString(21);

        var record = await accounts.FindByUsernameAsync(username, ct);
        if (record is null || string.IsNullOrEmpty(record.AuthKey) || record.AuthKey != authKey)
        {
            log.LogWarning("Autenticação recusada para {Username} de {Session}", username, session);
            session.Send(new AuthenticationPacket(ChannelError.InvalidCredentials, username, authKey, mac1, mac2, mac3));
            return;
        }

        registry.SignIn(record.Account, session);
        log.LogInformation("{Username} autenticado ({Authority})", record.Account.Username, record.Account.Authority);
        session.Send(new AuthenticationPacket(ChannelError.None, record.Account.Username));
    }
}

/// <summary>0x12: lista de personagens.</summary>
[PacketHandler(ChannelOpcode.CharacterList)]
public sealed class CharacterListHandler(CharacterListService list) : ISessionPacketHandler
{
    public async ValueTask HandleAsync(Session session, PacketReader packet, CancellationToken ct)
    {
        if (session.Account is { } account)
            await list.SendAsync(session, account.Uid, ct);
    }
}

/// <summary>0x14: confirmação do usuário.</summary>
[PacketHandler(ChannelOpcode.UserCheck)]
public sealed class UserCheckHandler : ISessionPacketHandler
{
    public ValueTask HandleAsync(Session session, PacketReader packet, CancellationToken ct)
    {
        if (session.Account is { } account)
            session.Send(new UserCheckPacket(account.Username));
        return ValueTask.CompletedTask;
    }
}

/// <summary>0x10: criar personagem.</summary>
[PacketHandler(ChannelOpcode.CharacterCreate)]
public sealed partial class CharacterCreateHandler(
    CharacterRepository characters, CharacterListService list, IOptions<GameOptions> options, ILogger<CharacterCreateHandler> log) : ISessionPacketHandler
{
    private static readonly ushort[] Starters = [10, 11, 12];
    private static readonly ushort[] FourthStarters = [24, 27, 31, 40]; // Chibimon, Yarmon, Hopmon, Dorimon

    [GeneratedRegex("^[a-zA-Z0-9]{1,20}$")]
    private static partial Regex ValidName();

    // a lista de personagens tem 24 bytes para o nome do digimon
    [GeneratedRegex("^[a-zA-Z0-9]{1,23}$")]
    private static partial Regex ValidDigimonName();

    public async ValueTask HandleAsync(Session session, PacketReader packet, CancellationToken ct)
    {
        if (session.Account is not { } account) return;

        packet.ReadI32();
        packet.ReadI32();
        packet.ReadU16();
        packet.ReadU16();                       // semente 1
        packet.ReadU16();                       // semente 2
        var tamerId = packet.ReadU8();
        var name = packet.ReadFixedString(21);
        var digimonId = packet.ReadU16();
        var digimonName = packet.ReadFixedString(26);

        var o = options.Value;
        bool validDigimon = Starters.Contains(digimonId) ||
                            (o.FourthStarterDigimon is >= 0 and < 4 && FourthStarters[o.FourthStarterDigimon] == digimonId);
        if (tamerId is < 1 or > 4 || !validDigimon || !ValidName().IsMatch(name) || !ValidDigimonName().IsMatch(digimonName))
        {
            log.LogWarning("{Session}: criação inválida (tamer {Tamer}, digimon {Digimon}, nomes '{Name}'/'{DigimonName}')",
                session, tamerId, digimonId, name, digimonName);
            return;
        }

        var slots = await list.LoadSlotsAsync(account.Uid, ct);
        int slot = Array.IndexOf(slots, null);
        if (slot < 0) return;

        var reply = (int error) => new CharacterCreatePacket(error, tamerId, name, digimonId, digimonName);
        if (await characters.IsNameTakenAsync(name, ct))
        {
            session.Send(reply(ChannelError.NameTaken));
            return;
        }

        var uid = await characters.CreateAsync(new NewCharacter(account.Uid, (byte)slot, name, tamerId, digimonId, digimonName,
            o.StartMapId, o.StartPosX, o.StartPosY), ct);
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
public sealed class CharacterDeleteHandler(CharacterRepository characters, CharacterListService list, ILogger<CharacterDeleteHandler> log) : ISessionPacketHandler
{
    public async ValueTask HandleAsync(Session session, PacketReader packet, CancellationToken ct)
    {
        if (session.Account is not { } account) return;
        packet.ReadI32();
        packet.ReadI32();
        var uid = packet.ReadU32();
        packet.ReadFixedString(40);             // MD5 da senha. ❓ o oficial confere; aqui não há senha local

        if (!await characters.DeleteAsync(uid, account.Uid, ct)) return;
        log.LogInformation("{Username} apagou o personagem {Uid}", account.Username, uid);
        session.Send(new CharacterDeletePacket(uid));
        await list.SendAsync(session, account.Uid, ct);
    }
}
