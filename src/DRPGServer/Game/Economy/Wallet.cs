namespace DRPGServer.Game.Economy;

/// <summary>Moedas do personagem. Cash e bônus são da conta (<c>CashRepository</c>), não daqui.</summary>
public enum CurrencyKind : byte
{
    Bits,
    Medals,
    Coins,
    LadderPoints,
}

/// <summary>
/// Carteira do personagem: o único lugar que gasta, credita, aplica os tetos e audita moedas. Toda mudança em jogo
/// passa por <see cref="TrySpend"/>, <see cref="Credit"/> ou <see cref="CreditBits"/> e fica no
/// <see cref="EconomyLedger"/>; <see cref="Load"/> é só a carga do banco, sem auditoria.
/// </summary>
public sealed class Wallet
{
    /// <summary>✅ Teto de Coins / DR's Earned Coin (máx. 20.000 em <c>DigimonRPG.bin @ 0x00AE8094</c>).</summary>
    public const ushort MaxCoins = 20_000;

    private readonly EconomyLedger _ledger;

    internal Wallet(EconomyLedger ledger) => _ledger = ledger;

    /// <summary>✅ Bits (f64 no protocolo). Pode ter fração: recompensa de batalha é multiplicada por <c>BitRate</c>.</summary>
    public double Bits { get; private set; }

    /// <summary>✅ Medals (WORD <c>[player+0x4070]</c>, 0x0053 action 3 / S2C 0x00CB +0x08).</summary>
    public ushort Medals { get; private set; }

    /// <summary>✅ Coins / DR's Earned Coin (WORD <c>[player+0x4072]</c>, 0x0053 action 4 / S2C 0x00CB +0x0C).</summary>
    public ushort Coins { get; private set; }

    /// <summary>✅ Ladder Points (DWORD <c>[player+0x12B0]</c>, 0x0053 action 5).</summary>
    public uint LadderPoints { get; private set; }

    /// <summary>Saldo inteiro (Bits sem a fração).</summary>
    public long Balance(CurrencyKind kind) => kind switch
    {
        CurrencyKind.Bits => (long)Math.Floor(Bits),
        CurrencyKind.Medals => Medals,
        CurrencyKind.Coins => Coins,
        CurrencyKind.LadderPoints => LadderPoints,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
    };

    /// <summary>Teto do saldo.</summary>
    public static long Max(CurrencyKind kind) => kind switch
    {
        CurrencyKind.Bits => long.MaxValue,
        CurrencyKind.Medals => ushort.MaxValue,
        CurrencyKind.Coins => MaxCoins,
        CurrencyKind.LadderPoints => uint.MaxValue,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
    };

    public bool Has(CurrencyKind kind, long amount) => amount <= 0 || Balance(kind) >= amount;

    /// <summary>Gasta tudo ou nada. Zero sempre dá certo e não registra.</summary>
    public bool TrySpend(CurrencyKind kind, long amount, EconomyReason reason)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(amount);
        if (amount == 0) return true;
        if (!Has(kind, amount)) return false;
        Change(kind, -amount, reason);
        return true;
    }

    /// <summary>Credita até o teto (estorno também é crédito). Devolve o que entrou de fato.</summary>
    public long Credit(CurrencyKind kind, long amount, EconomyReason reason)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(amount);
        long room = kind == CurrencyKind.Bits ? long.MaxValue : Max(kind) - Balance(kind);
        long credited = Math.Min(amount, room);
        if (credited > 0) Change(kind, credited, reason);
        return credited;
    }

    /// <summary>Credita Bits com fração (recompensa multiplicada por taxa). A auditoria registra o arredondado.</summary>
    public void CreditBits(double amount, EconomyReason reason)
    {
        if (!double.IsFinite(amount) || amount < 0) throw new ArgumentOutOfRangeException(nameof(amount), amount, null);
        double before = Bits;
        Bits += amount;
        RecordBits(reason, before);
    }

    /// <summary>Carga do banco: aplica os tetos, sem auditoria.</summary>
    public void Load(double bits, ushort medals, ushort coins, uint ladderPoints)
    {
        Bits = double.IsFinite(bits) ? Math.Max(0, bits) : 0;
        Medals = medals;
        Coins = Math.Min(coins, MaxCoins);
        LadderPoints = ladderPoints;
    }

    private void Change(CurrencyKind kind, long delta, EconomyReason reason)
    {
        switch (kind)
        {
            case CurrencyKind.Bits:
                double before = Bits;
                Bits += delta;
                RecordBits(reason, before);
                return;
            case CurrencyKind.Medals: Medals = (ushort)(Medals + delta); break;
            case CurrencyKind.Coins: Coins = (ushort)(Coins + delta); break;
            case CurrencyKind.LadderPoints: LadderPoints = (uint)(LadderPoints + delta); break;
        }
        _ledger.Record(reason, Asset(kind), 0, delta, Balance(kind));
    }

    private void RecordBits(EconomyReason reason, double before) =>
        _ledger.Record(reason, EconomyAsset.Bits, 0, (long)Math.Round(Bits - before), (long)Bits);

    private static EconomyAsset Asset(CurrencyKind kind) => kind switch
    {
        CurrencyKind.Bits => EconomyAsset.Bits,
        CurrencyKind.Medals => EconomyAsset.Medals,
        CurrencyKind.Coins => EconomyAsset.Coins,
        _ => EconomyAsset.LadderPoints,
    };
}
