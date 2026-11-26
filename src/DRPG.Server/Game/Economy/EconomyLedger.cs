using DRPG.Server.Game.Entities;

namespace DRPG.Server.Game.Economy;

/// <summary>
/// Movimentações do personagem que ainda não chegaram ao banco. Cada snapshot leva as pendentes, e elas são gravadas em
/// <c>economy_log</c> na mesma transação do snapshot (docs/emulador/seguranca.md#auditoria). Depois do commit, o mundo
/// confirma (<see cref="Acknowledge"/>) e elas saem daqui; se a confirmação se perder, o próximo snapshot as leva de
/// novo e o banco ignora a repetida pelo id. Thread do mundo.
/// </summary>
public sealed class EconomyLedger(Character owner)
{
    private readonly List<EconomyEntry> _pending = [];

    public IReadOnlyList<EconomyEntry> Pending => _pending;

    /// <summary>Movimentação de <paramref name="amount"/> (positivo entrou). Zero não é registrado.</summary>
    public void Record(EconomyReason reason, EconomyAsset asset, uint assetId, long amount, long? balanceAfter = null)
    {
        if (amount == 0) return;
        _pending.Add(EconomyEntry.Create(owner.Inventory.Clock.UtcNow, owner.AccountUid, owner.Uid, reason, asset, assetId,
            amount, balanceAfter));
    }

    /// <summary>Itens que entraram (ou saíram, com <paramref name="sign"/> -1), somados por id.</summary>
    public void RecordItems(EconomyReason reason, IEnumerable<(uint ItemId, uint Amount)> items, bool cards, int sign = 1)
    {
        foreach (var group in items.GroupBy(i => i.ItemId))
            Record(reason, cards ? EconomyAsset.Card : EconomyAsset.Item, group.Key, sign * group.Sum(i => (long)i.Amount));
    }

    /// <summary>Tira as movimentações que um snapshot gravado levou.</summary>
    public void Acknowledge(IReadOnlyList<EconomyEntry> written)
    {
        if (written.Count == 0) return;
        var ids = written.Select(e => e.Id).ToHashSet();
        _pending.RemoveAll(e => ids.Contains(e.Id));
    }
}
