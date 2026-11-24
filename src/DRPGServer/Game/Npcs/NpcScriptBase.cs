using DRPGServer.Game.Entities;
using DRPGServer.Network.Packets.Map;

namespace DRPGServer.Game.Npcs;

/// <summary>
/// Base dos scripts de NPC agrupados por mapa/cidade (escolhas <c>Map 0x56</c>).
/// Fornece helpers reutilizáveis de flags, troca atômica de itens e resposta padrão (<c>0x56</c> + <c>0xC9</c> + <c>0xFC</c>).
/// Ver <c>docs/mecanicas/npcs.md</c>.
/// </summary>
public abstract class NpcScriptBase
{
    /// <summary>✅ S2C 0x56 = 99 (0x63): resposta oficial de falha/fechar janela 0x38 (DigimonRPG.bin @ 0x00AC5A05, S56/S62).</summary>
    public const uint CloseWindowError = 99;

    private readonly Dictionary<uint, Func<Player, bool>> _choices = [];

    /// <summary>Mapa ao qual estes NPCs pertencem (validado pelo <see cref="NpcScriptRegistry"/>).</summary>
    public abstract byte MapId { get; }

    internal IReadOnlyDictionary<uint, Func<Player, bool>> Choices => _choices;

    protected void RegisterChoice(uint choiceId, Func<Player, bool> handler)
    {
        if (!_choices.TryAdd(choiceId, handler))
            throw new InvalidOperationException($"{GetType().Name}: escolha 0x56 #{choiceId} duplicada.");
    }

    protected static bool HasDone(Player player, uint choiceId) =>
        choiceId <= ushort.MaxValue && player.Character.HasFlag((ushort)choiceId);

    protected static void MarkDone(Player player, uint choiceId) =>
        player.Character.SetFlag((ushort)choiceId);

    /// <summary>Entrega um item na aba Items uma única vez (usa o próprio <paramref name="choiceId"/> como flag).</summary>
    protected static bool GiveItemOnce(Player player, uint choiceId, uint itemId, uint amount = 1)
    {
        if (HasDone(player, choiceId)) return false;
        if (player.Character.Inventory.Main.Room(itemId) < amount) return false;

        MarkDone(player, choiceId);
        player.Send(new NpcChoicePacket(choiceId));
        player.GiveItem(itemId, amount, refresh: true);
        return true;
    }

    /// <summary>
    /// Troca atômica de itens uma única vez: valida espaço e quantidade antes de mexer no inventário,
    /// aplica o rollback se algum item não couber, marca a flag e envia <c>0x56</c> + <c>0xC9</c> + <c>0xFC</c>.
    /// </summary>
    protected static bool ExchangeItemsOnce(
        Player player, uint choiceId, uint takeItemId, uint takeAmount, params (uint ItemId, uint Amount)[] giveItems)
    {
        if (HasDone(player, choiceId)) return false;
        var bag = player.Character.Inventory.Main;
        if (bag.Count(takeItemId) < takeAmount) return false;

        var removed = bag.Remove(takeItemId, takeAmount);
        var added = new List<InventoryItem>();
        foreach (var (itemId, amount) in giveItems)
        {
            if (bag.Room(itemId) < amount)
            {
                // Rollback: devolve tudo que foi removido e remove o que já tinha sido adicionado
                foreach (var item in added)
                    bag.Remove(item.ItemId, item.Amount);
                bag.Add(takeItemId, takeAmount);
                return false;
            }
            added.AddRange(bag.Add(itemId, amount));
        }

        FinishChoice(player, choiceId, [.. removed, .. added]);
        return true;
    }

    /// <summary>Marca a flag da escolha, envia eco do <c>0x56</c>, itens alterados (<c>0xC9</c>) e <c>0xFC</c>.</summary>
    protected static void FinishChoice(Player player, uint choiceId, IReadOnlyList<InventoryItem> changed)
    {
        MarkDone(player, choiceId);
        player.Send(new NpcChoicePacket(choiceId));
        // posição esvaziada e ocupada de novo na mesma troca: vale o último estado
        var distinctBySlot = changed.GroupBy(i => i.Slot).Select(g => g.Last()).ToList();
        if (distinctBySlot.Count > 0)
            player.Send(new ItemReceivePacket(distinctBySlot));
        player.RefreshInventory();
    }

    /// <summary>Envia o código oficial de erro/fechar janela (<c>S2C 0x56 = 99</c>).</summary>
    protected static void ReplyError(Player player) =>
        player.Send(new NpcChoicePacket(CloseWindowError));
}
