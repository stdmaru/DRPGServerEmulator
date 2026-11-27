using System.Security.Cryptography;
using DRPG.Server.Game;
using DRPG.Server.Game.Commands;
using DRPG.Server.Game.Entities;
using DRPG.Server.Game.World;
using DRPG.Server.Network.Packets.Map;
using DRPG.Shared.Packets.Map;
using DRPG.Shared.Protocol;
using Microsoft.Extensions.Logging;

namespace DRPG.Server.Network.Handlers;

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
/// 0x01: entrar no mapa com um personagem. Confere o ticket da conta (login feito no channel) e entra pelo
/// <see cref="WorldEntry"/>, que espera o último save da conta antes de carregar.
/// </summary>
[PacketHandler(MapOpcode.JoinCharacter)]
public sealed class JoinCharacterHandler(
    AccountRegistry accounts, WorldEntry entry, ExpEventService expEvents, ILogger<JoinCharacterHandler> log)
    : SessionHandler<JoinCharacterRequest>
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

        if (!await entry.EnterAsync(session, account, nickname, p => p.Send(MapSequences.WorldState(p, expEvents.Current)), ct))
            session.Close("entrada no mapa recusada");
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
