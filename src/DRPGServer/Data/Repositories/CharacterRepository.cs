using Dapper;
using DRPGServer.Game.Entities;
using MySqlConnector;

namespace DRPGServer.Data.Repositories;

/// <summary>Linha de `character` com o digimon líder.</summary>
public sealed class CharacterRow
{
    public uint Uid { get; set; }
    public uint AccountUid { get; set; }
    public byte Slot { get; set; }
    public string Name { get; set; } = "";
    public byte TamerId { get; set; }
    public ushort Level { get; set; }
    public byte MapId { get; set; }
    public short PosX { get; set; }
    public short PosY { get; set; }
    public double Bits { get; set; }
    public int TotalWins { get; set; }
    public int TotalBattles { get; set; }

    public uint DigimonUid { get; set; }
    public ushort DigimonId { get; set; }
    public string DigimonName { get; set; } = "";
    public ushort DigimonLevel { get; set; }
    public long DigimonExp { get; set; }
    public int AllocStr { get; set; }
    public int AllocAgi { get; set; }
    public int AllocCon { get; set; }
    public int AllocInt { get; set; }
    public ushort AbilityPoints { get; set; }
    public ushort SkillPoints { get; set; }
    public int DigimonWins { get; set; }
    public int DigimonBattles { get; set; }
}

/// <summary>O que é salvo de um personagem. Montado na thread do mundo, gravado fora dela.</summary>
public sealed record CharacterSnapshot(
    uint Uid, ushort Level, byte MapId, short X, short Y, double Bits, int TotalWins, int TotalBattles,
    uint DigimonUid, string DigimonName, ushort DigimonLevel, long DigimonExp,
    int AllocStr, int AllocAgi, int AllocCon, int AllocInt, ushort AbilityPoints, ushort SkillPoints,
    int DigimonWins, int DigimonBattles)
{
    public static CharacterSnapshot Of(Character c)
    {
        var d = c.Leader;
        return new(c.Uid, c.Level, c.MapId, c.X, c.Y, c.Bits, c.TotalWins, c.TotalBattles,
            d.Uid, d.Name, d.Level, d.Exp, d.Allocated.Str, d.Allocated.Agi, d.Allocated.Con, d.Allocated.Int,
            d.AbilityPoints, d.SkillPoints, d.TotalWins, d.TotalBattles);
    }
}

public sealed record NewCharacter(
    uint AccountUid, byte Slot, string Name, byte TamerId, ushort DigimonId, string DigimonName, byte MapId, short X, short Y);

public sealed class CharacterRepository(Database database)
{
    private const string SelectWithLeader = """
        SELECT c.uid, c.account_uid, c.slot, c.name, c.tamer_id, c.level, c.map_id, c.pos_x, c.pos_y,
               c.bits, c.total_wins, c.total_battles,
               d.uid AS digimon_uid, d.digimon_id, d.name AS digimon_name, d.level AS digimon_level,
               d.exp AS digimon_exp, d.alloc_str, d.alloc_agi, d.alloc_con, d.alloc_int,
               d.ability_points, d.skill_points, d.total_wins AS digimon_wins, d.total_battles AS digimon_battles
        FROM `character` c
        JOIN digimon d ON d.character_uid = c.uid AND d.is_leader = 1
        """;

    public async Task<IReadOnlyList<CharacterRow>> ListByAccountAsync(uint accountUid, CancellationToken ct)
    {
        await using var db = await database.OpenAsync(ct);
        var rows = await db.QueryAsync<CharacterRow>(new CommandDefinition(
            SelectWithLeader + " WHERE c.account_uid = @accountUid ORDER BY c.slot", new { accountUid }, cancellationToken: ct));
        return rows.ToList();
    }

    public async Task<CharacterRow?> FindAsync(uint accountUid, string name, CancellationToken ct)
    {
        await using var db = await database.OpenAsync(ct);
        return await db.QuerySingleOrDefaultAsync<CharacterRow>(new CommandDefinition(
            SelectWithLeader + " WHERE c.account_uid = @accountUid AND c.name = @name", new { accountUid, name }, cancellationToken: ct));
    }

    public async Task<bool> IsNameTakenAsync(string name, CancellationToken ct)
    {
        await using var db = await database.OpenAsync(ct);
        return await db.ExecuteScalarAsync<int>(new CommandDefinition(
            "SELECT COUNT(*) FROM `character` WHERE name = @name", new { name }, cancellationToken: ct)) > 0;
    }

    /// <summary>Cria personagem e digimon líder numa transação. Null se o nome ou o slot já existem.</summary>
    public async Task<uint?> CreateAsync(NewCharacter c, CancellationToken ct)
    {
        await using var db = await database.OpenAsync(ct);
        await using var tx = await db.BeginTransactionAsync(ct);
        try
        {
            var uid = await db.ExecuteScalarAsync<uint>(new CommandDefinition("""
                INSERT INTO `character` (account_uid, slot, name, tamer_id, map_id, pos_x, pos_y)
                VALUES (@AccountUid, @Slot, @Name, @TamerId, @MapId, @X, @Y);
                SELECT LAST_INSERT_ID();
                """, c, tx, cancellationToken: ct));
            await db.ExecuteAsync(new CommandDefinition("""
                INSERT INTO digimon (character_uid, digimon_id, name, level, is_leader)
                VALUES (@uid, @DigimonId, @DigimonName, 1, 1)
                """, new { uid, c.DigimonId, c.DigimonName }, tx, cancellationToken: ct));
            await tx.CommitAsync(ct);
            return uid;
        }
        catch (MySqlException e) when (e.ErrorCode == MySqlErrorCode.DuplicateKeyEntry)
        {
            await tx.RollbackAsync(CancellationToken.None);
            return null;
        }
    }

    public async Task<bool> DeleteAsync(uint characterUid, uint accountUid, CancellationToken ct)
    {
        await using var db = await database.OpenAsync(ct);
        return await db.ExecuteAsync(new CommandDefinition(
            "DELETE FROM `character` WHERE uid = @characterUid AND account_uid = @accountUid",
            new { characterUid, accountUid }, cancellationToken: ct)) > 0;
    }

    public async Task SaveAsync(CharacterSnapshot s, CancellationToken ct)
    {
        await using var db = await database.OpenAsync(ct);
        await using var tx = await db.BeginTransactionAsync(ct);
        await db.ExecuteAsync(new CommandDefinition("""
            UPDATE `character`
            SET level = @Level, map_id = @MapId, pos_x = @X, pos_y = @Y, bits = @Bits,
                total_wins = @TotalWins, total_battles = @TotalBattles
            WHERE uid = @Uid
            """, s, tx, cancellationToken: ct));
        await db.ExecuteAsync(new CommandDefinition("""
            UPDATE digimon
            SET name = @DigimonName, level = @DigimonLevel, exp = @DigimonExp,
                alloc_str = @AllocStr, alloc_agi = @AllocAgi, alloc_con = @AllocCon, alloc_int = @AllocInt,
                ability_points = @AbilityPoints, skill_points = @SkillPoints,
                total_wins = @DigimonWins, total_battles = @DigimonBattles
            WHERE uid = @DigimonUid
            """, s, tx, cancellationToken: ct));
        await tx.CommitAsync(ct);
    }
}
