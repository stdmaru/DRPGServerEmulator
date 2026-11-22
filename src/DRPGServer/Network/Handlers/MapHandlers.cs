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

/// <summary>
/// 0x71 (C2S 718): ✅ bloco de status ecoado (uid em 0, serial em 4), u8 estágio da forma e u8 atributo (1..4).
/// O ponto vai para a forma daquele estágio (a janela de evolução deixa distribuir nas formas liberadas).
/// </summary>
[PacketHandler(MapOpcode.DigimonAddAbilityPoint)]
public sealed class AddAbilityPointHandler : PlayerHandler
{
    protected override void Handle(Player player, PacketReader packet)
    {
        uint uid = packet.ReadU32();
        packet.Skip(DigimonStatusBlock.Length - 4);
        var stage = packet.ReadU8();
        var stat = packet.ReadU8();

        if (player.InBattle || player.Character.FindDigimon(uid) is not { } digimon || stat is < 1 or > 4) return;
        if (digimon.FormAtStage(stage) is not { } form) return;
        if (digimon.AllocatePoint(form, (StatKind)stat))
            player.Send(new AddAbilityPointPacket(digimon, form, stage, stat));
    }
}

/// <summary>0x72 (C2S 718): ✅ bloco ecoado, u8 estágio da forma e u8 skill (1 ou 2). Gasta um ponto de skill.</summary>
[PacketHandler(MapOpcode.DigimonSkillUpgrade)]
public sealed class SkillUpgradeHandler : PlayerHandler
{
    protected override void Handle(Player player, PacketReader packet)
    {
        uint uid = packet.ReadU32();
        packet.Skip(DigimonStatusBlock.Length - 4);
        var stage = packet.ReadU8();
        var skill = packet.ReadU8();

        if (player.InBattle || player.Character.FindDigimon(uid) is not { } digimon || skill is < 1 or > DigimonForm.SkillSlots) return;
        if (digimon.FormAtStage(stage) is not { } form) return;
        if (digimon.UpgradeSkill(form, skill - 1))
            player.Send(new SkillUpgradePacket(digimon, form, stage, skill));
    }
}

/// <summary>0x82 (C2S 734): ✅ janela de evolução: u32 estágio (0 = atual), u32 0, bloco ecoado. Responde o bloco da forma.</summary>
[PacketHandler(MapOpcode.DigimonFormInfo)]
public sealed class DigimonFormInfoHandler : PlayerHandler
{
    protected override void Handle(Player player, PacketReader packet)
    {
        uint stage = packet.ReadU32();
        packet.ReadU32();
        uint uid = packet.ReadU32();
        if (stage > byte.MaxValue || player.Character.FindDigimon(uid) is not { } digimon) return;
        if (digimon.FormAtStage((byte)stage) is not { } form) return;
        player.Send(new DigimonFormInfoPacket(stage, digimon, form));
    }
}

/// <summary>0x9E (C2S 18): ✅ u32 uid do digimon; o servidor responde as formas para evoluir agora (só em batalha).</summary>
[PacketHandler(MapOpcode.EvolutionList)]
public sealed class EvolutionListHandler : PlayerHandler
{
    protected override void Handle(Player player, PacketReader packet)
    {
        uint uid = packet.ReadU32();
        if (player.Battle is { } battle)
            player.Send(new EvolutionListPacket(battle.EvolutionOptions(player, uid)));
    }
}

/// <summary>0xF1 (C2S 26): ✅ u32 0, u32 personagem, u32 digimon: o digimon vira líder (troca com a posição 0).</summary>
[PacketHandler(MapOpcode.SetLeader)]
public sealed class SetLeaderHandler : PlayerHandler
{
    protected override void Handle(Player player, PacketReader packet)
    {
        packet.ReadU32();
        uint characterUid = packet.ReadU32();
        uint digimonUid = packet.ReadU32();
        var c = player.Character;
        if (player.InBattle || characterUid != c.Uid || c.FindDigimon(digimonUid) is not { } digimon) return;
        if (c.SetLeader(digimon))
            player.Send(new SetLeaderPacket(c.Uid, digimon.Uid));
    }
}

/// <summary>0x173 (C2S 22): ✅ u16 0, u16 botão k (0..2): troca as posições k + 1 e k + 2 do time.</summary>
[PacketHandler(MapOpcode.PartySwap)]
public sealed class PartySwapHandler : PlayerHandler
{
    protected override void Handle(Player player, PacketReader packet)
    {
        packet.ReadU16();
        ushort button = packet.ReadU16();
        if (player.InBattle || button > Character.PartySize - 3) return;
        if (player.Character.SwapSlots(button + 1, button + 2))
            player.Send(new PartySwapPacket(button));
    }
}

/// <summary>0x56: escolha num diálogo de NPC (<see cref="NpcScripts"/>).</summary>
[PacketHandler(MapOpcode.NpcDialogChoice)]
public sealed class NpcChoiceHandler(NpcScripts scripts) : PlayerHandler
{
    protected override void Handle(Player player, PacketReader packet)
    {
        uint choice = packet.ReadU32();
        if (!player.InBattle) scripts.Choose(player, choice);
    }
}

/// <summary>
/// 0x91 (C2S 250): ✅ mover item (<see cref="ItemMoveHeader"/> + registro do item). Do chão: origem (x, y) e destino
/// "qualquer lugar"; o oficial tira o item do chão (0x13 zerado) e responde com a posição usada. Dentro do
/// inventário: linha e coluna de origem e destino, trocando se o destino está ocupado.
/// </summary>
[PacketHandler(MapOpcode.ItemDropGet)]
public sealed class ItemMoveHandler : PlayerHandler
{
    protected override void Handle(Player player, PacketReader packet)
    {
        var header = ItemMoveHeader.Read(packet);
        uint uid = packet.ReadU32();
        uint itemId = packet.ReadU32();
        if (player.InBattle) return;
        var bag = player.Character.Inventory.Main;

        switch (header.From, header.To)
        {
            case (ItemPlace.Ground, ItemPlace.Inventory):
            {
                if (player.Zone is not { } zone || zone.FindDrop(uid) is not { } drop || drop.ItemId != itemId) return;
                if (bag.Add(drop.ItemId, 1) is not [.., var item]) return;   // inventário cheio: fica no chão
                zone.RemoveDrop(drop);
                player.Send(new ItemMovePacket(header with { ToA = item.Row, ToB = item.Column }, item));
                break;
            }
            case (ItemPlace.Inventory, ItemPlace.Inventory):
            {
                int from = SlotOf(header.FromA, header.FromB), to = SlotOf(header.ToA, header.ToB);
                if (bag[from] is not { } item || item.Uid != uid) return;
                if (bag.Move(from, to, out var displaced))
                    player.Send(new ItemMovePacket(header, item, displaced));
                break;
            }
            // ❓ bolsas extras (0x33, 0x3D) ainda não
        }
    }

    internal static int SlotOf(int row, int column) =>
        row is >= 0 and < Inventory.Size / Inventory.Columns && column is >= 0 and < Inventory.Columns
            ? row * Inventory.Columns + column : -1;
}

/// <summary>0xBD (C2S 254): ✅ cabeçalho (inventário → chão, linha e coluna) + registro: o item é destruído.</summary>
[PacketHandler(MapOpcode.ItemDestroy)]
public sealed class ItemDestroyHandler : PlayerHandler
{
    protected override void Handle(Player player, PacketReader packet)
    {
        var header = ItemMoveHeader.Read(packet);
        uint uid = packet.ReadU32();
        if (player.InBattle || header.From != ItemPlace.Inventory) return;
        var bag = player.Character.Inventory.Main;
        int slot = ItemMoveHandler.SlotOf(header.FromA, header.FromB);
        if (bag[slot] is not { } item || item.Uid != uid) return;
        bag.Destroy(slot);
        player.Send(new ItemDestroyPacket(header, item));
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

/// <summary>
/// 0xF0 (C2S 42): ✅ renomear. u8 0, u8 tipo (1 tamer, 2 digimon), u16 0, u32 uid, nome[20]. Sem nome é consulta.
/// Digimon: uma renomeação grátis para quem nasceu "InputName"; o nome segue a regra da criação (letras e números).
/// ❓ Renomeação paga (item) ainda não; tamer sempre 2, como o oficial na entrada.
/// </summary>
[PacketHandler(MapOpcode.Rename)]
public sealed partial class RenameHandler : IWorldPacketHandler
{
    [System.Text.RegularExpressions.GeneratedRegex("^[a-zA-Z0-9]{1,19}$")]
    private static partial System.Text.RegularExpressions.Regex ValidName();

    public void Handle(Session session, PacketReader packet)
    {
        packet.ReadU8();
        var kind = (RenameKind)packet.ReadU8();
        packet.ReadU16();
        uint uid = packet.ReadU32();
        var name = packet.ReadFixedString(RenamePacket.NameLength);
        // o tipo 1 chega logo na entrada, às vezes antes de o personagem estar no mundo: não derruba a conexão
        if (kind != RenameKind.Digimon || session.Player is not { } player)
        {
            session.Send(new RenamePacket(RenameResult.NotAvailable, kind, uid, ""));
            return;
        }

        var digimon = player.Character.FindDigimon(uid);
        bool free = digimon is { FreeRename: true } && !player.InBattle;
        if (name.Length == 0 || !free || !ValidName().IsMatch(name))
        {
            player.Send(new RenamePacket(free && name.Length == 0 ? RenameResult.Free : RenameResult.NotAvailable,
                RenameKind.Digimon, uid, ""));
            return;
        }
        digimon!.Name = name;
        digimon.FreeRename = false;
        player.Send(new RenamePacket(RenameResult.Renamed, RenameKind.Digimon, uid, name));
    }
}

/// <summary>
/// 0x41 (C2S 822): ✅ usar item. u32 3, registro do item (uid em +0), bloco do digimon alvo (selvagem: uid 0 e o
/// serial dele em +4). Por ora só a Taming Net (<see cref="Taming"/>); o resto responde 0xC3.
/// </summary>
[PacketHandler(MapOpcode.ItemUse)]
public sealed class ItemUseHandler(Taming taming, GameWorld world) : PlayerHandler
{
    protected override void Handle(Player player, PacketReader packet)
    {
        packet.ReadU32();
        uint itemUid = packet.ReadU32();
        packet.Skip(ItemRecord.Length - 4);
        packet.ReadU32();                       // uid do alvo (0 no selvagem)
        var target = Serial.Read(packet);

        if (player.Character.Inventory.Main.FindByUid(itemUid) is not { } item)
        {
            player.Send(new ItemUseFailedPacket());
            return;
        }
        if (!taming.UseNet(player, item, player.Zone?.FindWild(target), world.Random))
            player.Send(new ItemUseFailedPacket());     // ❓ outros itens (consumíveis, tickets) ainda não
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
        if (player.Character.FindDigimon(serial) is { } digimon)
            player.Send(new SingleDigimonInfoPacket(digimon));
    }
}
