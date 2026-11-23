using DRPGServer.Configuration;
using DRPGServer.Game.Entities;
using DRPGServer.Network.Packets.Map;
using DRPGServer.Network.Protocol;
using Microsoft.Extensions.Options;

namespace DRPGServer.Network.Handlers;

/// <summary>
/// ✅ Map 0x89 (C2S 750 B): abrir (1), depositar (2), retirar (3) ou trocar (4) Digimon no Depository.
/// Ver <c>docs/mecanicas/depository.md</c>.
/// </summary>
[PacketHandler(MapOpcode.DepositoryAction)]
public sealed class DepositoryActionHandler(IOptions<GameOptions> options) : PlayerHandler
{
    protected override void Handle(Player player, PacketReader packet)
    {
        if (player.InBattle) return;

        ushort page = packet.ReadU16();
        var action = (DepositoryAction)packet.ReadU8();
        packet.ReadU8();
        uint partyDigimonUid = packet.ReadU32();
        uint depositoryDigimonUid = packet.ReadU32();

        var character = player.Character;
        ushort clampedPage = Math.Clamp(page, (ushort)1, character.DepositoryTotalPages);
        bool chargeFee = options.Value.ChargeDepositoryFee;

        switch (action)
        {
            case DepositoryAction.Open:
            {
                var result = character.DepositorySlots > 0 ? DepositoryResult.OpenOk : DepositoryResult.OpenFailed;
                player.Send(new DepositoryListPacket(clampedPage, character));
                player.Send(new DepositoryActionPacket(clampedPage, action, 0, 0, character.Bits, result));
                break;
            }
            case DepositoryAction.Deposit:
            {
                var result = character.Deposit(partyDigimonUid, page, chargeFee);
                if (result == DepositoryResult.DepositOk)
                    player.Send(new DepositoryListPacket(clampedPage, character));
                player.Send(new DepositoryActionPacket(clampedPage, action, partyDigimonUid, 0, character.Bits, result));
                break;
            }
            case DepositoryAction.Withdraw:
            {
                var (result, withdrawn) = character.Withdraw(depositoryDigimonUid, chargeFee);
                if (result == DepositoryResult.WithdrawOk)
                    player.Send(new DepositoryListPacket(clampedPage, character));
                player.Send(new DepositoryActionPacket(clampedPage, action, 0, depositoryDigimonUid, character.Bits, result, withdrawn));
                break;
            }
            case DepositoryAction.Swap:
            {
                var (result, withdrawn) = character.Swap(partyDigimonUid, depositoryDigimonUid, chargeFee);
                if (result == DepositoryResult.SwapOk)
                    player.Send(new DepositoryListPacket(clampedPage, character));
                player.Send(new DepositoryActionPacket(clampedPage, action, partyDigimonUid, depositoryDigimonUid, character.Bits, result, withdrawn));
                break;
            }
            default:
                player.Send(new DepositoryActionPacket(clampedPage, action, partyDigimonUid, depositoryDigimonUid, character.Bits, DepositoryResult.OpenCheckError));
                break;
        }
    }
}

/// <summary>
/// ✅ Map 0x94 (C2S 15 B): expande +1 slot no Digimon Depository consumindo o item 901 (0x385) ou 929 (0x3A1).
/// Confirmado em <c>DigimonRPG.bin @ 0x00B7BBDA / 0x00B7BBEC / 0x00AC2883</c>.
/// </summary>
[PacketHandler(MapOpcode.DepositoryExpand)]
public sealed class DepositoryExpandHandler : PlayerHandler
{
    public const uint ExpansionItemId = 901;        // 0x385
    public const uint EventExpansionItemId = 929;   // 0x3A1

    protected override void Handle(Player player, PacketReader packet)
    {
        if (player.InBattle) return;

        var character = player.Character;
        if (character.DepositorySlots >= Character.MaxDepositorySlots)
        {
            player.Send(new DepositoryExpandPacket(DepositoryExpandResult.MaxSlots));
            return;
        }

        var item = FindExpansionItem(character.Inventory.Main);
        if (item is null)
        {
            player.Send(new DepositoryExpandPacket(DepositoryExpandResult.MissingItem));
            return;
        }

        character.Inventory.Main.Consume(item, 1);
        character.ExpandDepository();
        player.Send(new InventoryRefreshPacket(character));
        player.Send(new DepositoryListPacket(character.DepositoryTotalPages, character));
        player.Send(new DepositoryExpandPacket(DepositoryExpandResult.Ok));
    }

    private static InventoryItem? FindExpansionItem(Bag bag)
    {
        foreach (var item in bag.Items)
        {
            if (item.Amount > 0 && (item.ItemId == ExpansionItemId || item.ItemId == EventExpansionItemId))
                return item;
        }
        return null;
    }
}
