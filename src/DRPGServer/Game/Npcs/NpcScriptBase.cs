using DRPGServer.Game.Economy;
using DRPGServer.Game.Entities;
using DRPGServer.Game.Items;
using DRPGServer.Network.Packets.Map;

namespace DRPGServer.Game.Npcs;

/// <summary>
/// Base dos scripts de NPC agrupados por mapa/cidade (escolhas <c>Map 0x56</c>).
/// Cuida apenas de registro de escolhas, flags e respostas <c>0x56</c>; itens entram e saem pelo
/// <see cref="ItemService"/>.
/// Ver <c>docs/mecanicas/npcs.md</c>.
/// </summary>
public abstract class NpcScriptBase(ItemService items)
{
    /// <summary>✅ S2C 0x56 = 99 (0x63): resposta oficial de falha/fechar janela 0x38 (DigimonRPG.bin @ 0x00AC5A05, S56/S62).</summary>
    public const uint CloseWindowError = 99;

    /// <summary>Uma escolha: o NPC que a oferece (✅ capturas: <c>npc_info_us</c> do mapa) e o que ela faz.</summary>
    internal readonly record struct Choice(ushort NpcId, Func<Player, bool> Handler);

    private readonly Dictionary<uint, Choice> _choices = [];

    protected ItemService Items { get; } = items;

    /// <summary>Mapa ao qual estes NPCs pertencem (validado pelo <see cref="NpcScriptRegistry"/>).</summary>
    public abstract byte MapId { get; }

    internal IReadOnlyDictionary<uint, Choice> Choices => _choices;

    /// <param name="npcId">NPC do <see cref="MapId"/> que oferece a escolha: o jogador precisa estar perto dele.</param>
    protected void RegisterChoice(uint choiceId, ushort npcId, Func<Player, bool> handler)
    {
        if (!_choices.TryAdd(choiceId, new Choice(npcId, handler)))
            throw new InvalidOperationException($"{GetType().Name}: escolha 0x56 #{choiceId} duplicada.");
    }

    protected static bool HasDone(Player player, uint choiceId) =>
        choiceId <= ushort.MaxValue && player.Character.HasFlag((ushort)choiceId);

    protected static void MarkDone(Player player, uint choiceId) =>
        player.Character.SetFlag((ushort)choiceId);

    /// <summary>Entrega itens na aba Items uma única vez (usa o próprio <paramref name="choiceId"/> como flag).</summary>
    protected bool GiveItemsOnce(Player player, uint choiceId, params ItemGrant[] give) =>
        !HasDone(player, choiceId)
        && Items.GiveItems(player, give, ChoiceReason(choiceId), beforeNotify: () => Done(player, choiceId));

    /// <summary>Troca atômica de itens uma única vez (<see cref="ItemService.Exchange"/>).</summary>
    protected bool ExchangeItemsOnce(Player player, uint choiceId, uint takeItemId, uint takeAmount, params ItemGrant[] give) =>
        !HasDone(player, choiceId)
        && Items.Exchange(player, takeItemId, takeAmount, give, ChoiceReason(choiceId), beforeNotify: () => Done(player, choiceId));

    private static void Done(Player player, uint choiceId)
    {
        MarkDone(player, choiceId);
        player.Send(new NpcChoicePacket(choiceId));
    }

    /// <summary>Motivo de auditoria de uma entrega ou troca feita por uma escolha de NPC.</summary>
    protected static EconomyReason ChoiceReason(uint choiceId) => new(EconomySource.Npc, "choice", $"escolha {choiceId}");

    /// <summary>Envia o código oficial de erro/fechar janela (<c>S2C 0x56 = 99</c>).</summary>
    protected static void ReplyError(Player player) =>
        player.Send(new NpcChoicePacket(CloseWindowError));
}
