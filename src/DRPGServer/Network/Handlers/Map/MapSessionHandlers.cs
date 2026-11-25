using DRPGServer.Data.Persistence;
using DRPGServer.Data.Repositories;
using DRPGServer.Game;
using DRPGServer.Game.Commands;
using DRPGServer.Game.Entities;
using DRPGServer.Game.World;
using DRPGServer.Network.Packets.Map;
using DRPGServer.Network.Protocol;
using Microsoft.Extensions.Logging;

namespace DRPGServer.Network.Handlers;

/// <summary>Base dos handlers do mapa que precisam do jogador já no mundo.</summary>
public abstract class PlayerHandler : IWorldPacketHandler
{
    public void Handle(Session session, PacketReader packet)
    {
        if (session.Player is not { } player)
        {
            session.Close("pacote de jogo antes de entrar no mapa");
            return;
        }
        Handle(player, packet);
    }

    protected abstract void Handle(Player player, PacketReader packet);
}

/// <summary>0x49: primeiro pedido no mapa.</summary>
[PacketHandler(MapOpcode.JoinHandshakeReq)]
public sealed class JoinHandshakeHandler : ISessionPacketHandler
{
    public ValueTask HandleAsync(Session session, PacketReader packet, CancellationToken ct)
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
    GameWorld world, PlayerSystem players, ExpEventService expEvents, ILogger<JoinCharacterHandler> log) : ISessionPacketHandler
{
    public async ValueTask HandleAsync(Session session, PacketReader packet, CancellationToken ct)
    {
        if (session.Account is not null) return;   // já entrou
        var nickname = packet.ReadFixedString(21);
        var username = packet.ReadFixedString(21);
        packet.ReadU8();

        var account = accounts.Claim(username, session);
        if (account is null)
        {
            log.LogWarning("{Session}: {Username} tentou entrar no mapa sem login no channel", session, username);
            session.Close("sem autenticação no channel");
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
        player.Send(new CharacterDataPacket(player.Character));
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
public sealed class RefreshCharacterInfoHandler : PlayerHandler
{
    protected override void Handle(Player player, PacketReader packet)
    {
        packet.ReadBytes(Serial.Length);        // serial do personagem
        player.Send(new RefreshCharacterInfoPacket(player.Character));
    }
}

/// <summary>0x20: andar. Pisar num portal troca de mapa.</summary>
[PacketHandler(MapOpcode.MoveCharacter)]
public sealed class MoveCharacterHandler(ZoneSystem zones) : PlayerHandler
{
    protected override void Handle(Player player, PacketReader packet)
    {
        packet.ReadI16();                       // ✅ sempre 5
        short x = packet.ReadI16();
        short y = packet.ReadI16();             // ✅ os 14 bytes seguintes são zero
        if (player.InBattle) return;
        zones.Move(player, x, y);
    }
}

/// <summary>0x21: chat normal; "/" no começo é comando de GM.</summary>
[PacketHandler(MapOpcode.ChatNormal)]
public sealed class ChatNormalHandler(ChatCommands commands) : PlayerHandler
{
    protected override void Handle(Player player, PacketReader packet)
    {
        packet.ReadFixedString(21);             // apelido (o do servidor vale)
        var message = packet.ReadFixedString(44);
        if (message.StartsWith('/') && commands.TryExecute(player, message[1..]))
            return;
        player.Zone?.Broadcast(new ChatNormalPacket(player.Character.Name, message));
    }
}

/// <summary>0x2B: sair do jogo.</summary>
[PacketHandler(MapOpcode.ExitGame)]
public sealed class ExitGameHandler : ISessionPacketHandler
{
    public ValueTask HandleAsync(Session session, PacketReader packet, CancellationToken ct)
    {
        session.Send(new ExitGamePacket());
        return ValueTask.CompletedTask;
    }
}

/// <summary>0x121: o cliente pede; o servidor responde com o conteúdo capturado.</summary>
[PacketHandler(MapOpcode.Unknown121)]
public sealed class Unknown121Handler : ISessionPacketHandler
{
    public ValueTask HandleAsync(Session session, PacketReader packet, CancellationToken ct)
    {
        session.Send(new Unknown121Packet());
        return ValueTask.CompletedTask;
    }
}
