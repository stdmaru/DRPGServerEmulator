using DRPGServer.Configuration;
using DRPGServer.Game.Entities;
using DRPGServer.Game.World;
using DRPGServer.Network.Packets.Map;
using Microsoft.Extensions.Options;

namespace DRPGServer.Network.Handlers;

/// <summary>
/// ✅ Map 0x89 (C2S 750 B): abrir (1), depositar (2), retirar (3) ou trocar (4) Digimon no Depository.
/// Ver <c>docs/mecanicas/depository.md</c>.
/// </summary>
[PacketHandler(MapOpcode.DepositoryAction)]
[RequiresState(PlayerStates.Idle)]
public sealed class DepositoryActionHandler(IOptions<GameOptions> options, InteractionGuard guard) : PlayerHandler<DepositoryActionRequest>
{
    protected override void Handle(Player player, DepositoryActionRequest request)
    {
        if (!guard.CanUseDigimonCenter(player)) return;
        var (page, action, partyDigimonUid, depositoryDigimonUid) = request;
        var character = player.Character;
        ushort clampedPage = Math.Clamp(page, (ushort)1, character.DepositoryTotalPages);
        double depositoryFee = options.Value.DigimonDepositoryFee;

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
                var result = character.Deposit(partyDigimonUid, page, depositoryFee);
                if (result == DepositoryResult.DepositOk)
                {
                    player.RequestImmediateSave();
                    player.Send(new DepositoryListPacket(clampedPage, character));
                }
                player.Send(new DepositoryActionPacket(clampedPage, action, partyDigimonUid, 0, character.Bits, result));
                break;
            }
            case DepositoryAction.Withdraw:
            {
                var (result, withdrawn) = character.Withdraw(depositoryDigimonUid, depositoryFee);
                if (result == DepositoryResult.WithdrawOk)
                {
                    player.RequestImmediateSave();
                    player.Send(new DepositoryListPacket(clampedPage, character));
                }
                player.Send(new DepositoryActionPacket(clampedPage, action, 0, depositoryDigimonUid, character.Bits, result, withdrawn));
                break;
            }
            case DepositoryAction.Swap:
            {
                var (result, withdrawn) = character.Swap(partyDigimonUid, depositoryDigimonUid, depositoryFee);
                if (result == DepositoryResult.SwapOk)
                {
                    player.RequestImmediateSave();
                    player.Send(new DepositoryListPacket(clampedPage, character));
                }
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
/// ✅ Map 0x94 (C2S 15 B, ❓ não capturado na v322): expande +1 slot no Digimon Depository consumindo o item 901 (0x385) ou 929 (0x3A1).
/// Confirmado em <c>DigimonRPG.bin @ 0x00B7BBDA / 0x00B7BBEC / 0x00AC2883</c>.
/// </summary>
[PacketHandler(MapOpcode.DepositoryExpand)]
[RequiresState(PlayerStates.Idle)]
public sealed class DepositoryExpandHandler(InteractionGuard guard) : PlayerHandler<DepositoryExpandRequest>
{
    public const uint ExpansionItemId = 901;        // 0x385
    public const uint EventExpansionItemId = 929;   // 0x3A1

    protected override void Handle(Player player, DepositoryExpandRequest request)
    {
        if (!guard.CanUseDigimonCenter(player)) return;   // ✅ enviado de dentro da janela do Depository
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
        player.RequestImmediateSave();
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
