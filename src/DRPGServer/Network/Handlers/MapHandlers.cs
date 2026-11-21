using DRPGServer.Data.Persistence;
using DRPGServer.Data.Repositories;
using DRPGServer.Game;
using DRPGServer.Game.Battle;
using DRPGServer.Game.Commands;
using DRPGServer.Game.Entities;
using DRPGServer.Game.World;
using DRPGServer.Network.Packets.Map;
using DRPGServer.Network.Protocol;
using Microsoft.Extensions.Logging;

namespace DRPGServer.Network.Handlers;

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
    GameWorld world, PlayerSystem players, ILogger<JoinCharacterHandler> log) : ISessionPacketHandler
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

    /// <summary>Sequência que o oficial manda na entrada (capturas v242).</summary>
    private static void SendWorldState(Player player)
    {
        player.Send(new CharacterDataPacket(player.Character));
        player.Send(new InventoryDataPacket(player.Character.Inventory));
        for (int i = 0x29; i <= 0x2F; i++) player.Send(new Unknown7EPacket(i));
        for (int i = 0x3D; i <= 0x43; i++) player.Send(new Unknown7FPacket(i));
        player.Send(new Unknown05Packet());
        player.Send(new UnknownE6Packet());
        player.Send(new Unknown74Packet());
        player.Send(new Unknown152Packet());
        player.Send(new SingleDigimonInfoPacket(player.Leader));
        player.Send(new Unknown73Packet());
    }
}

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

/// <summary>0x71: distribuir um ponto de atributo.</summary>
[PacketHandler(MapOpcode.DigimonAddAbilityPoint)]
public sealed class AddAbilityPointHandler : PlayerHandler
{
    protected override void Handle(Player player, PacketReader packet)
    {
        packet.ReadU32();                       // UID do digimon
        var serial = Serial.Read(packet);
        packet.Skip(668);                       // resto do bloco de status que o cliente ecoa
        var flag = packet.ReadU8();             // ❓ talvez a evolução
        var stat = packet.ReadU8();

        var digimon = player.Leader;
        if (serial != digimon.Serial || stat is < 1 or > 4) return;
        if (digimon.AllocatePoint((StatKind)stat))
            player.Send(new AddAbilityPointPacket(digimon, flag, stat));
    }
}

/// <summary>0x56: escolha num diálogo de NPC.</summary>
[PacketHandler(MapOpcode.NpcDialogChoice)]
public sealed class NpcChoiceHandler : PlayerHandler
{
    /// <summary>Escolha → (item dado, item cobrado, quantidade cobrada). Vila do Começo.</summary>
    private static readonly Dictionary<uint, (uint Give, uint Cost, uint CostAmount)> Choices = new()
    {
        [1] = (22004, 0, 0),    // Patamon
        [2] = (22005, 0, 0),    // Botamon
        [3] = (22006, 0, 0),    // Mochimon
        [4] = (22007, 0, 0),    // Pyocomon
        [5] = (22008, 41, 5),   // Tunomon: troca 5 sementes de Yuramon
        [7] = (0, 0, 0),        // Elecmon
        [8] = (0, 0, 0),        // Elecmon
    };

    protected override void Handle(Player player, PacketReader packet)
    {
        uint choice = packet.ReadU32();
        if (!Choices.TryGetValue(choice, out var c)) return;

        var inventory = player.Character.Inventory;
        if (c.Give == 0)
        {
            player.Send(new NpcChoicePacket(choice));
            return;
        }
        if (!inventory.HasFreeSlot && inventory.Find(c.Give) is null) return;
        if (c.Cost != 0 && !inventory.Remove(c.Cost, c.CostAmount)) return;
        if (inventory.Add(c.Give, 1) is not { } item) return;
        player.Send(new NpcChoicePacket(choice));
        player.Send(new ItemReceivePacket(item));
    }
}

/// <summary>0x91: pegar item do chão.</summary>
[PacketHandler(MapOpcode.ItemDropGet)]
public sealed class ItemDropGetHandler : PlayerHandler
{
    protected override void Handle(Player player, PacketReader packet)
    {
        packet.ReadU32();
        packet.ReadI32();                       // Y
        packet.ReadI32();                       // X
        packet.ReadI32();
        packet.ReadI32();
        uint dropUid = packet.ReadU32();
        uint itemId = packet.ReadU32();

        if (player.Zone is not { } zone || zone.FindDrop(dropUid) is not { } drop || drop.ItemId != itemId) return;
        if (player.Character.Inventory.Add(drop.ItemId, 1) is not { } item) return;   // inventário cheio: fica no chão
        zone.RemoveDrop(drop);
        player.Send(new ItemDropGetPacket(drop, item));
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

/// <summary>0xF0: o cliente pede; o servidor responde com o conteúdo capturado.</summary>
[PacketHandler(MapOpcode.UnknownF0)]
public sealed class UnknownF0Handler : ISessionPacketHandler
{
    public ValueTask HandleAsync(Session session, PacketReader packet, CancellationToken ct)
    {
        session.Send(new UnknownF0Packet());
        return ValueTask.CompletedTask;
    }
}

// ---------- batalha ----------

/// <summary>0x61: o jogador encostou num digimon selvagem.</summary>
[PacketHandler(MapOpcode.BattleStartReq)]
public sealed class BattleStartHandler(BattleSystem battles) : PlayerHandler
{
    protected override void Handle(Player player, PacketReader packet)
    {
        packet.ReadI32();
        battles.StartPve(player, Serial.Read(packet));
    }
}

/// <summary>0x62 (C2S): o cliente confirma o serial da batalha; o servidor manda os times.</summary>
[PacketHandler(MapOpcode.BattleStartRes)]
public sealed class BattleConfirmHandler : PlayerHandler
{
    protected override void Handle(Player player, PacketReader packet)
    {
        var serial = Serial.Read(packet);
        if (player.Battle is { } battle && battle.Serial == serial)
            battle.SendTeams(player);
    }
}

/// <summary>0x69: o cliente está pronto (no começo e depois de cada animação).</summary>
[PacketHandler(MapOpcode.BattleBegin)]
public sealed class BattleReadyHandler : PlayerHandler
{
    protected override void Handle(Player player, PacketReader packet) => player.Battle?.ClientReady(player);
}

/// <summary>0x35 (50 bytes): u8 ação (2), u16 ícone (0x0A ataque, 0x04 fuga), u8 0, quem age, alvo.</summary>
[PacketHandler(MapOpcode.BattleActionReq)]
public sealed class BattleActionHandler : PlayerHandler
{
    protected override void Handle(Player player, PacketReader packet)
    {
        if (packet.ReadU8() != (byte)BattleAction.Choose) return;
        var icon = (BattleIcon)packet.ReadU16();
        packet.ReadU8();
        var requester = Serial.Read(packet);
        var target = Serial.Read(packet);
        player.Battle?.RequestAction(player, icon, requester, target);
    }
}

/// <summary>
/// 0xB4 (C2S, bloco de status): ✅ o cliente manda um por digimon 6,6 s depois do 0x6D, ao sair da tela de
/// resultado; o oficial ecoa o bloco de cada um com os dados dele.
/// </summary>
[PacketHandler(MapOpcode.SingleDigimonInfo)]
public sealed class DigimonInfoHandler : PlayerHandler
{
    protected override void Handle(Player player, PacketReader packet)
    {
        packet.ReadU32();
        var serial = Serial.Read(packet);
        if (player.Leader.Serial == serial)
            player.Send(new SingleDigimonInfoPacket(player.Leader));
    }
}
