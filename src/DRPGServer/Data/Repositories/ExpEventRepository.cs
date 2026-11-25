using Dapper;

namespace DRPGServer.Data.Repositories;

/// <summary>Evento de bônus de EXP em vigor, lido da tabela <c>exp_event</c>.</summary>
public sealed record ActiveExpEvent(
    int Id,
    string Name,
    int BonusPercent,
    bool IsHotTime,
    DateTime StartsAt,
    DateTime EndsAt)
{
    /// <summary>✅ Limite de frames em <c>Exp_Bonus.spr</c> (81 frames: 0..80 = até +2000%) e <c>Exp_Bonus_Hot.spr</c> (29 frames: 0..28 = até +700%).</summary>
    public const int MaxNormalSpriteIndex = 80;
    public const int MaxHotTimeSpriteIndex = 28;

    /// <summary>Multiplicador de EXP aplicado em batalha (ex.: 500% -> 6.0x, 1200% -> 13.0x).</summary>
    public double ExpMultiplier => 1.0 + Math.Max(0, BonusPercent) / 100.0;

    /// <summary>✅ Índice do frame em <c>Exp_Bonus.spr</c> / <c>Exp_Bonus_Hot.spr</c> (passos de +25%, limitado ao teto do sprite).</summary>
    public byte SpriteIndex => (byte)Math.Clamp(BonusPercent / 25, 0, IsHotTime ? MaxHotTimeSpriteIndex : MaxNormalSpriteIndex);
}

/// <summary>Fonte do evento de EXP em vigor.</summary>
public interface IExpEventStore
{
    Task<ActiveExpEvent?> FindActiveAsync(CancellationToken ct);
}

/// <summary>Leitura de eventos de EXP ativos na tabela <c>exp_event</c>.</summary>
public sealed class ExpEventRepository(Database db) : IExpEventStore
{
    private sealed class Row
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
        public int BonusPercent { get; set; }
        public byte IsHotTime { get; set; }
        public DateTime StartsAt { get; set; }
        public DateTime EndsAt { get; set; }
    }

    public async Task<ActiveExpEvent?> FindActiveAsync(CancellationToken ct)
    {
        await using var conn = await db.OpenAsync(ct);
        var row = await conn.QuerySingleOrDefaultAsync<Row>(new CommandDefinition(
            """
            SELECT id, name, bonus_percent, is_hot_time, starts_at, ends_at
            FROM exp_event
            WHERE enabled = 1 AND starts_at <= NOW() AND ends_at >= NOW()
            ORDER BY bonus_percent DESC, ends_at DESC
            LIMIT 1;
            """,
            cancellationToken: ct));

        return row is null
            ? null
            : new ActiveExpEvent(row.Id, row.Name, row.BonusPercent, row.IsHotTime != 0, row.StartsAt, row.EndsAt);
    }
}
