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

/// <summary>Um digimon do personagem (tabela <c>digimon</c>).</summary>
public sealed class DigimonRow
{
    public uint Uid { get; set; }
    public byte Slot { get; set; }
    public ushort DigimonId { get; set; }
    public string Name { get; set; } = "";
    public ushort Level { get; set; }
    public long Exp { get; set; }
    public byte UnlockedStage { get; set; }
    public int TotalWins { get; set; }
    public int TotalBattles { get; set; }
}

/// <summary>Uma forma de um digimon (tabela <c>digimon_form</c>).</summary>
public sealed record FormRow(uint DigimonUid, ushort SpeciesId, int AllocStr, int AllocAgi, int AllocCon, int AllocInt,
    byte Skill1Level, byte Skill2Level);

/// <summary>Um item de uma bolsa (tabela <c>inventory_item</c>).</summary>
public sealed record ItemRow(byte Bag, byte Slot, uint Uid, uint ItemId, uint Amount);

/// <summary>Personagem completo para entrar no mapa.</summary>
public sealed record CharacterRecord(
    CharacterRow Character, IReadOnlyList<DigimonRow> Digimons, IReadOnlyList<FormRow> Forms,
    IReadOnlyList<ItemRow> Items, IReadOnlyList<ushort> Flags);

/// <summary>O que é salvo de um personagem. Montado na thread do mundo, gravado fora dela.</summary>
public sealed record CharacterSnapshot(
    uint Uid, ushort Level, byte MapId, short X, short Y, double Bits, int TotalWins, int TotalBattles,
    IReadOnlyList<DigimonSnapshot> Digimons, IReadOnlyList<FormRow> Forms, IReadOnlyList<ItemRow> Items,
    IReadOnlyList<ushort> Flags)
{
    public static CharacterSnapshot Of(Character c)
    {
        var digimons = new List<DigimonSnapshot>();
        var forms = new List<FormRow>();
        for (int slot = 0; slot < Character.PartySize; slot++)
        {
            if (c.Party[slot] is not { Uid: > 0 } d) continue;
            digimons.Add(new DigimonSnapshot(d.Uid, (byte)slot, slot == 0, d.BaseForm.Species.Id, d.Name, d.Level, d.Exp,
                d.UnlockedStage, d.PointsOf(d.BaseForm), d.SkillPoints, d.TotalWins, d.TotalBattles));
            forms.AddRange(d.Forms.Select(f => new FormRow(d.Uid, f.Species.Id, f.Allocated.Str, f.Allocated.Agi,
                f.Allocated.Con, f.Allocated.Int, (byte)f.SkillLevels[0], (byte)f.SkillLevels[1])));
        }
        var items = new[] { c.Inventory.Main, c.Inventory.Battle }
            .SelectMany(b => b.Items.Select(i => new ItemRow((byte)b.Kind, (byte)i.Slot, i.Uid, i.ItemId, i.Amount)))
            .ToList();
        return new(c.Uid, c.Level, c.MapId, c.X, c.Y, c.Bits, c.TotalWins, c.TotalBattles, digimons, forms, items,
            c.Flags.Order().ToList());
    }
}

public sealed record DigimonSnapshot(
    uint Uid, byte Slot, bool IsLeader, ushort DigimonId, string Name, ushort Level, long Exp, byte UnlockedStage,
    ushort AbilityPoints, ushort SkillPoints, int TotalWins, int TotalBattles);

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
        JOIN digimon d ON d.character_uid = c.uid AND d.slot = 0
        """;

    public async Task<IReadOnlyList<CharacterRow>> ListByAccountAsync(uint accountUid, CancellationToken ct)
    {
        await using var db = await database.OpenAsync(ct);
        var rows = await db.QueryAsync<CharacterRow>(new CommandDefinition(
            SelectWithLeader + " WHERE c.account_uid = @accountUid ORDER BY c.slot", new { accountUid }, cancellationToken: ct));
        return rows.ToList();
    }

    /// <summary>Personagem com o time, as formas, os itens e as flags.</summary>
    public async Task<CharacterRecord?> FindAsync(uint accountUid, string name, CancellationToken ct)
    {
        await using var db = await database.OpenAsync(ct);
        var row = await db.QuerySingleOrDefaultAsync<CharacterRow>(new CommandDefinition(
            SelectWithLeader + " WHERE c.account_uid = @accountUid AND c.name = @name", new { accountUid, name }, cancellationToken: ct));
        if (row is null) return null;
        var uid = row.Uid;
        var digimons = await db.QueryAsync<DigimonRow>(new CommandDefinition("""
            SELECT uid, slot, digimon_id, name, level, exp, unlocked_stage, total_wins, total_battles
            FROM digimon WHERE character_uid = @uid ORDER BY slot, uid
            """, new { uid }, cancellationToken: ct));
        var forms = await db.QueryAsync<FormRow>(new CommandDefinition("""
            SELECT f.digimon_uid, f.species_id, f.alloc_str, f.alloc_agi, f.alloc_con, f.alloc_int, f.skill1_level, f.skill2_level
            FROM digimon_form f JOIN digimon d ON d.uid = f.digimon_uid WHERE d.character_uid = @uid
            """, new { uid }, cancellationToken: ct));
        var items = await db.QueryAsync<ItemRow>(new CommandDefinition(
            "SELECT bag, slot, uid, item_id, amount FROM inventory_item WHERE character_uid = @uid", new { uid }, cancellationToken: ct));
        var flags = await db.QueryAsync<ushort>(new CommandDefinition(
            "SELECT flag FROM character_flag WHERE character_uid = @uid", new { uid }, cancellationToken: ct));
        return new CharacterRecord(row, digimons.ToList(), forms.ToList(), items.ToList(), flags.ToList());
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
                INSERT INTO digimon (character_uid, digimon_id, name, level, is_leader, slot)
                VALUES (@uid, @DigimonId, @DigimonName, 1, 1, 0);
                INSERT INTO digimon_form (digimon_uid, species_id) VALUES (LAST_INSERT_ID(), @DigimonId);
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
        // a posição muda nas trocas do time (0xF1, 0x173); is_leader acompanha a posição 0
        await db.ExecuteAsync(new CommandDefinition("""
            UPDATE digimon
            SET slot = @Slot, is_leader = @IsLeader, digimon_id = @DigimonId, name = @Name, level = @Level, exp = @Exp,
                unlocked_stage = @UnlockedStage, ability_points = @AbilityPoints, skill_points = @SkillPoints,
                total_wins = @TotalWins, total_battles = @TotalBattles
            WHERE uid = @Uid AND character_uid = @CharacterUid
            """, s.Digimons.Select(d => new
            {
                d.Uid, CharacterUid = s.Uid, d.Slot, d.IsLeader, d.DigimonId, d.Name, d.Level, d.Exp, d.UnlockedStage,
                d.AbilityPoints, d.SkillPoints, d.TotalWins, d.TotalBattles,
            }), tx, cancellationToken: ct));

        var digimonUids = s.Digimons.Select(d => d.Uid).ToList();
        if (digimonUids.Count > 0)
            await db.ExecuteAsync(new CommandDefinition(
                "DELETE FROM digimon_form WHERE digimon_uid IN @digimonUids", new { digimonUids }, tx, cancellationToken: ct));
        await db.ExecuteAsync(new CommandDefinition("""
            INSERT INTO digimon_form (digimon_uid, species_id, alloc_str, alloc_agi, alloc_con, alloc_int, skill1_level, skill2_level)
            VALUES (@DigimonUid, @SpeciesId, @AllocStr, @AllocAgi, @AllocCon, @AllocInt, @Skill1Level, @Skill2Level)
            """, s.Forms, tx, cancellationToken: ct));

        await db.ExecuteAsync(new CommandDefinition(
            "DELETE FROM inventory_item WHERE character_uid = @Uid", new { s.Uid }, tx, cancellationToken: ct));
        await db.ExecuteAsync(new CommandDefinition("""
            INSERT INTO inventory_item (character_uid, bag, slot, uid, item_id, amount)
            VALUES (@CharacterUid, @Bag, @Slot, @Uid, @ItemId, @Amount)
            """, s.Items.Select(i => new { CharacterUid = s.Uid, i.Bag, i.Slot, i.Uid, i.ItemId, i.Amount }), tx, cancellationToken: ct));

        await db.ExecuteAsync(new CommandDefinition(
            "INSERT IGNORE INTO character_flag (character_uid, flag) VALUES (@CharacterUid, @Flag)",
            s.Flags.Select(f => new { CharacterUid = s.Uid, Flag = f }), tx, cancellationToken: ct));
        await tx.CommitAsync(ct);
    }
}
