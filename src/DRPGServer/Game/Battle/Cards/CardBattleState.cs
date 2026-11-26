using DRPGServer.Data.GameData;
using DRPGServer.Game.Entities;

namespace DRPGServer.Game.Battle.Cards;

/// <summary>Card ativado em um dos 3 slots de batalha durante um turno (escrito nos offsets 40..63 do 0x67).</summary>
public readonly record struct ActivatedCard(uint CardId, uint RemainingAmount, IReadOnlyList<ICardEffect> Effects);

/// <summary>
/// Cards ativados por um combatente em um único turno, preservando exatamente os índices dos slots 0, 1 e 2
/// (✅ conn=428, 433, 446: +40/+44/+48 = CardId, +52/+56/+60 = quantidade restante).
/// </summary>
public sealed class ActivatedCards
{
    public const int SlotCount = 3;

    private readonly ActivatedCard?[] _slots = new ActivatedCard?[SlotCount];

    public ActivatedCard? this[int slot]
    {
        get => slot >= 0 && slot < SlotCount ? _slots[slot] : null;
        set
        {
            if (slot >= 0 && slot < SlotCount)
                _slots[slot] = value;
        }
    }

    public bool Any => _slots[0] is not null || _slots[1] is not null || _slots[2] is not null;

    public IEnumerable<uint> CardIds()
    {
        for (int i = 0; i < SlotCount; i++)
            if (_slots[i] is { } card)
                yield return card.CardId;
    }

    /// <summary>Efeitos de todos os cards ativados, na ordem dos slots.</summary>
    public IEnumerable<ICardEffect> Effects()
    {
        for (int i = 0; i < SlotCount; i++)
            if (_slots[i] is { } card)
                foreach (var effect in card.Effects)
                    yield return effect;
    }
}

/// <summary>
/// Estado dos 3 slots de batalha (0xA0/0xA1) e do contador de 20 Card Slash por jogador dentro de uma batalha.
/// </summary>
public sealed class CardBattleState(int maxCardSlashes = CardBattleState.MaxCardSlashesPerBattle)
{
    public const int SlotCount = ActivatedCards.SlotCount;
    public const int MaxCardSlashesPerBattle = 20;

    private readonly InventoryItem?[] _slots = new InventoryItem?[SlotCount];

    public int SlashesRemaining { get; private set; } = Math.Max(MaxCardSlashesPerBattle, maxCardSlashes);
    public bool AnyConsumed { get; private set; }
    public bool UsedHasteLastAttack { get; private set; }

    public InventoryItem? this[int slot] => slot >= 0 && slot < SlotCount ? _slots[slot] : null;

    /// <summary>✅ C2S 0xA0: equipa um card dos 6 CardSlots do inventário no slot de batalha 0..2.</summary>
    public bool Equip(Player player, int slot, uint uid, uint cardId)
    {
        if (slot < 0 || slot >= SlotCount) return false;
        var card = player.Character.Inventory.CardSlots.FindByUid(uid);
        if (card is null || card.ItemId != cardId || card.Amount == 0) return false;
        // Impede equipar a mesma pilha (mesmo UID) em múltiplos slots de batalha simultaneamente
        for (int i = 0; i < SlotCount; i++)
            if (i != slot && _slots[i]?.Uid == uid)
                return false;
        _slots[slot] = card;
        return true;
    }

    /// <summary>✅ C2S 0xA1: desequipa o card do slot de batalha 0..2.</summary>
    public bool Unequip(int slot)
    {
        if (slot < 0 || slot >= SlotCount) return false;
        _slots[slot] = null;
        return true;
    }

    /// <summary>
    /// Ativa e consome (1 unidade) todos os cards equipados cujo <see cref="CardTrigger"/> corresponde ao momento
    /// atual (<see cref="CardTrigger.Attack"/> no ataque do jogador; <see cref="CardTrigger.Defense"/> quando o
    /// jogador é atacado). Decrementa <see cref="SlashesRemaining"/> em 1 por turno com ao menos 1 card ativado.
    /// </summary>
    public bool TryActivate(Player player, CardTrigger trigger, out ActivatedCards activated)
    {
        activated = new ActivatedCards();
        if (SlashesRemaining <= 0)
        {
            if (trigger == CardTrigger.Attack) UsedHasteLastAttack = false;
            return false;
        }

        var cardSlots = player.Character.Inventory.CardSlots;
        bool slotReturnActive = false;
        bool usedHasteThisTurn = false;
        bool anyActivated = false;

        for (int i = 0; i < SlotCount; i++)
        {
            var card = _slots[i];
            if (card is null) continue;

            // Garante que o card continua equipado nos 6 CardSlots e ainda tem quantidade
            if (cardSlots[card.Slot] != card || card.Amount == 0)
            {
                _slots[i] = null;
                continue;
            }

            var cardTrigger = card.CardInfo?.Trigger ?? CardTrigger.Attack;
            if (cardTrigger != trigger) continue;

            // Haste: não ativa em ataques seguidos
            bool isHaste = card.CardInfo?.Has<HasteEffect>() == true;
            if (isHaste && UsedHasteLastAttack) continue;

            if (!slotReturnActive)
            {
                cardSlots.Consume(card, 1);
                AnyConsumed = true;
            }

            activated[i] = new ActivatedCard(card.ItemId, card.Amount, card.CardInfo?.Effects ?? []);
            anyActivated = true;

            if (card.CardInfo?.Has<SlotReturnEffect>() == true)
                slotReturnActive = true;
            if (isHaste)
                usedHasteThisTurn = true;

            if (card.Amount == 0)
                ClearEmptyReferences(card);
        }

        if (trigger == CardTrigger.Attack)
            UsedHasteLastAttack = usedHasteThisTurn;

        if (!anyActivated) return false;

        SlashesRemaining--;
        player.MarkDirty();
        return true;
    }

    private void ClearEmptyReferences(InventoryItem emptied)
    {
        for (int i = 0; i < SlotCount; i++)
            if (_slots[i] == emptied)
                _slots[i] = null;
    }
}
