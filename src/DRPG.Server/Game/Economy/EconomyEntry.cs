namespace DRPG.Server.Game.Economy;

/// <summary>De onde vem (ou para onde vai) a movimentação.</summary>
public enum EconomySource : byte
{
    NpcShop,
    /// <summary>Compra de loja NPC entregue no depósito da conta.</summary>
    ShopDepository,
    CashShop,
    CashDepository,
    Gm,
    DropPickup,
    Npc,
    BattleReward,
    /// <summary>Taxa do Digimon Depository.</summary>
    DigimonDepository,
}

/// <summary>O que se moveu.</summary>
public enum EconomyAsset : byte
{
    Bits,
    Medals,
    Coins,
    LadderPoints,
    Cash,
    BonusCash,
    Item,
    Card,
}

/// <summary>Por que uma moeda ou item mudou: obrigatório em toda mudança de moeda do personagem.</summary>
/// <param name="Action">Verbo curto: <c>buy</c>, <c>sell</c>, <c>refund</c>, <c>grant</c>, <c>fee</c>, <c>pickup</c>…</param>
/// <param name="Detail">Contexto para quem investiga (loja, NPC, uid do item). Nunca segredo.</param>
public readonly record struct EconomyReason(EconomySource Source, string Action, string? Detail = null);

/// <summary>
/// Uma linha de <c>economy_log</c>. <see cref="Amount"/> é do ponto de vista do dono: positivo entrou, negativo saiu.
/// <see cref="BalanceAfter"/> é o saldo da moeda depois (nulo para item).
/// </summary>
public sealed record EconomyEntry(
    Guid Id, DateTimeOffset At, uint AccountUid, uint? CharacterUid,
    EconomySource Source, string Action, EconomyAsset Asset, uint AssetId, long Amount, long? BalanceAfter, string? Detail)
{
    public const int DetailLength = 255;

    public static EconomyEntry Create(DateTimeOffset at, uint accountUid, uint? characterUid, EconomyReason reason,
        EconomyAsset asset, uint assetId, long amount, long? balanceAfter) =>
        new(Guid.CreateVersion7(at), at, accountUid, characterUid, reason.Source, reason.Action, asset, assetId, amount,
            balanceAfter, reason.Detail is { Length: > DetailLength } d ? d[..DetailLength] : reason.Detail);
}
