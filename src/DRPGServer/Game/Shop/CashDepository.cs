namespace DRPGServer.Game.Shop;

/// <summary>Tamanhos do Cash Depository e do histórico do Cash Shop, fixados pelo cliente.</summary>
public static class CashDepository
{
    /// <summary>✅ Posições por aba (24 cards + 24 itens no <c>0x00DE</c>).</summary>
    public const int CapacityPerKind = 24;

    /// <summary>✅ Registros por página do histórico (<c>0x0161</c>: 20 × 48 B, <c>DigimonRPG.bin @ 0x0097AC93</c>).</summary>
    public const int HistoryPageSize = 20;
}
