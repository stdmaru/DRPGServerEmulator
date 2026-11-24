using DRPGServer.Game.Entities;
using DRPGServer.Network.Packets.Map;

namespace DRPGServer.Game.Npcs;

/// <summary>
/// Base dos scripts de NPC agrupados por mapa/cidade (escolhas <c>Map 0x56</c>).
/// Cuida apenas de registro de escolhas, flags e respostas <c>0x56</c>; toda manipulação de inventário
/// e envio de <c>0xC8</c>/<c>0xC9</c>/<c>0xFC</c> fica centralizada em <see cref="Player"/>.
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

    /// <summary>Troca atômica de itens uma única vez via <see cref="Player.ExchangeItems"/>.</summary>
    protected static bool ExchangeItemsOnce(
        Player player, uint choiceId, uint takeItemId, uint takeAmount, params (uint ItemId, uint Amount)[] giveItems)
    {
        if (HasDone(player, choiceId)) return false;

        return player.ExchangeItems(takeItemId, takeAmount, giveItems, refresh: true, beforeNotify: () =>
        {
            MarkDone(player, choiceId);
            player.Send(new NpcChoicePacket(choiceId));
        });
    }

    /// <summary>Envia o código oficial de erro/fechar janela (<c>S2C 0x56 = 99</c>).</summary>
    protected static void ReplyError(Player player) =>
        player.Send(new NpcChoicePacket(CloseWindowError));
}
