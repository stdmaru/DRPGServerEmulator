using System.Security.Cryptography;
using DRPGServer.Data.Persistence;
using DRPGServer.Data.Repositories;
using DRPGServer.Game.Commands;
using DRPGServer.Game.Entities;
using DRPGServer.Game.World;
using DRPGServer.Game;
using DRPGServer.Network.Packets.Map;
using DRPGServer.Network.Protocol;
using Microsoft.Extensions.Logging;

namespace DRPGServer.Network.Handlers;

/// <summary>0x49: primeiro pedido no mapa.</summary>
[PacketHandler(MapOpcode.JoinHandshakeReq)]
public sealed class JoinHandshakeHandler : SessionHandler<JoinHandshakeRequest>
{
    protected override ValueTask HandleAsync(Session session, JoinHandshakeRequest request, CancellationToken ct)
    {
        session.Send(new JoinHandshakePacket());
        return ValueTask.CompletedTask;
    }
}

/// <summary>
/// 0x01: entrar no mapa com um personagem. Confere a conta no registro (login feito no channel),
/// carrega o personagem do banco fora da thread do mundo e só então o coloca no mundo.
/// </summary>
[PacketHandler(MapOpcode.JoinCharacter)]
public sealed class JoinCharacterHandler(
    AccountRegistry accounts, CharacterRepository characters, CharacterFactory factory, PersistenceQueue persistence,
    GameWorld world, PlayerSystem players, ExpEventService expEvents, ILogger<JoinCharacterHandler> log) : SessionHandler<JoinCharacterRequest>
{
    protected override async ValueTask HandleAsync(Session session, JoinCharacterRequest request, CancellationToken ct)
    {
        if (session.Account is not null) return;   // já entrou
        var (nickname, username) = request;

        var claim = accounts.Claim(username, session);
        if (claim.Account is not { } account)
        {
            log.LogWarning("{Session}: {Username} recusado no mapa ({Reason})", session, username, claim.Rejection);
            session.Close("ticket do channel inválido");
            return;
        }

        await persistence.FlushAsync();            // garante que o último logout já foi gravado
        var row = await characters.FindAsync(account.Uid, nickname, ct);
        var character = row is null ? null : factory.Build(row);
        if (character is null)
        {
            log.LogWarning("{Session}: personagem {Nickname} não encontrado ou inválido", session, nickname);
            session.Close("personagem inválido");
            return;
        }

        world.Post(() =>
        {
            if (players.Enter(session, account, character) is { } player)
                SendWorldState(player);
        });
    }

    /// <summary>Sequência que o oficial manda na entrada (capturas v242/v322).</summary>
    private void SendWorldState(Player player)
    {
        player.Send(new CharacterDataPacket(player.Character, player.Character.Inventory.Clock.UnixNow));
        player.Send(new InventoryDataPacket(player.Character.Inventory));
        if (player.Character.Medals > 0 || player.Character.Coins > 0)
            player.Send(new CurrencyUpdatePacket(player.Character));
        for (int i = 0x29; i <= 0x2F; i++) player.Send(new Unknown7EPacket(i));
        for (int i = 0x3D; i <= 0x43; i++) player.Send(new Unknown7FPacket(i));
        player.Send(new Unknown05Packet());
        player.Send(new ExpEventPacket(player.Session.MapAesKey, expEvents.Current));
        player.Send(new Unknown74Packet());
        player.Send(new Unknown152Packet());
        player.Send(new SingleDigimonInfoPacket(player.Leader));
        player.Send(new Unknown73Packet());
    }
}

/// <summary>0x04: o cliente pede os dados do personagem (ao conectar no global).</summary>
[PacketHandler(MapOpcode.RefreshCharacterInfoReq)]
[RequiresState(PlayerStates.Any)]
public sealed class RefreshCharacterInfoHandler : PlayerHandler<RefreshCharacterInfoRequest>
{
    protected override void Handle(Player player, RefreshCharacterInfoRequest request)
    {
        player.Send(new RefreshCharacterInfoPacket(player.Character));
    }
}

/// <summary>0x20: andar. Pisar num portal troca de mapa.</summary>
[PacketHandler(MapOpcode.MoveCharacter)]
[RequiresState(PlayerStates.Idle)]
public sealed class MoveCharacterHandler(ZoneSystem zones) : PlayerHandler<MoveCharacterRequest>
{
    protected override void Handle(Player player, MoveCharacterRequest request) => zones.Move(player, request.X, request.Y);
}

/// <summary>0x21: chat normal; "/" no começo é comando de GM.</summary>
[PacketHandler(MapOpcode.ChatNormal)]
[RequiresState(PlayerStates.Any)]
public sealed class ChatNormalHandler(ChatCommands commands) : PlayerHandler<ChatNormalRequest>
{
    protected override void Handle(Player player, ChatNormalRequest request)
    {
        var message = request.Message;
        if (message.StartsWith('/') && commands.TryExecute(player, message[1..]))
            return;
        player.Zone?.Broadcast(new ChatNormalPacket(player.Character.Name, message));
    }
}

/// <summary>0x2B: sair do jogo.</summary>
[PacketHandler(MapOpcode.ExitGame)]
public sealed class ExitGameHandler : SessionHandler<ExitGameRequest>
{
    protected override ValueTask HandleAsync(Session session, ExitGameRequest request, CancellationToken ct)
    {
        session.Send(new ExitGamePacket());
        return ValueTask.CompletedTask;
    }
}

/// <summary>0x121: o cliente pede; o servidor responde com o conteúdo capturado.</summary>
[PacketHandler(MapOpcode.Unknown121)]
public sealed class Unknown121Handler : SessionHandler<Unknown121Request>
{
    protected override ValueTask HandleAsync(Session session, Unknown121Request request, CancellationToken ct)
    {
        session.Send(new Unknown121Packet());
        return ValueTask.CompletedTask;
    }
}

/// <summary>Boas-vindas (0x0E) no mapa; ao desconectar, tira do mundo e solta a conta.</summary>
public sealed class MapLifecycle(GameWorld world, PlayerSystem players, AccountRegistry accounts) : ISessionLifecycle
{
    public ServerRole Role => ServerRole.Map;

    public void OnConnected(Session session)
    {
        var seed = RandomNumberGenerator.GetBytes(GlobalSessionCipher.BlockSize);
        session.MapAesKey = GlobalSessionCipher.DeriveKey(seed);
        session.Send(new HelloPacket((ushort)MapOpcode.Hello, seed));
    }

    public void OnDisconnected(Session session)
    {
        world.Post(() => players.Leave(session));
        accounts.Release(session);
    }
}
