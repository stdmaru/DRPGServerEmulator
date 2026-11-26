namespace DRPGServer.Game.Items;

public enum ItemDurationKind : byte
{
    /// <summary>A duração padrão do item (<c>ItemInfo.DurationSeconds</c>; zero é permanente).</summary>
    Default,
    Permanent,
    Seconds,
}

/// <summary>
/// Prazo de um item não empilhável entregue. Empilháveis e cards não têm prazo: só aceitam <see cref="Default"/>.
/// </summary>
public readonly record struct ItemDuration
{
    private ItemDuration(ItemDurationKind kind, uint seconds)
    {
        Kind = kind;
        Seconds = seconds;
    }

    public ItemDurationKind Kind { get; }

    /// <summary>Segundos a partir da entrega (só em <see cref="ItemDurationKind.Seconds"/>).</summary>
    public uint Seconds { get; }

    public static ItemDuration Default => default;
    public static ItemDuration Permanent => new(ItemDurationKind.Permanent, 0);

    public static ItemDuration FromSeconds(long seconds) => seconds > 0
        ? new(ItemDurationKind.Seconds, (uint)Math.Min(seconds, uint.MaxValue))
        : throw new ArgumentOutOfRangeException(nameof(seconds), seconds, "prazo precisa ser positivo");

    public static ItemDuration FromDays(int days) => FromSeconds(days * 86_400L);

    public override string ToString() => Kind == ItemDurationKind.Seconds ? $"{Seconds} s" : Kind.ToString();
}

/// <summary>
/// Itens ou cards a entregar. Num item não empilhável, <paramref name="Amount"/> é o número de posições, cada uma com
/// o prazo <paramref name="Duration"/>.
/// </summary>
public readonly record struct ItemGrant(uint ItemId, uint Amount = 1, ItemDuration Duration = default);
