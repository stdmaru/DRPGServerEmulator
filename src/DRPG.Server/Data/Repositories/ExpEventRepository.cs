using Dapper;
using DRPG.Server.Game.World;

namespace DRPG.Server.Data.Repositories;

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
