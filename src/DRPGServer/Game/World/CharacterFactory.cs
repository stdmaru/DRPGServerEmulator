using DRPGServer.Configuration;
using DRPGServer.Data.GameData;
using DRPGServer.Data.Repositories;
using DRPGServer.Game.Entities;
using DRPGServer.Game.Stats;
using Microsoft.Extensions.Options;

namespace DRPGServer.Game.World;

/// <summary>Monta as entidades a partir das linhas do banco.</summary>
public sealed class CharacterFactory(GameData data, IOptions<GameOptions> options)
{
    public Character? Build(CharacterRow row)
    {
        if (data.FindSpecies(row.DigimonId) is not { } species) return null;
        var leader = new Digimon(data, species, row.DigimonLevel)
        {
            Uid = row.DigimonUid,
            Name = string.IsNullOrEmpty(row.DigimonName) ? species.Name : row.DigimonName,
        };
        leader.Load(row.DigimonExp, new Attributes(row.AllocStr, row.AllocAgi, row.AllocCon, row.AllocInt),
            row.AbilityPoints, row.SkillPoints, row.DigimonWins, row.DigimonBattles);

        var character = new Character(leader, new Inventory((uint)options.Value.MaxStackPerItem))
        {
            Uid = row.Uid,
            AccountUid = row.AccountUid,
            Slot = row.Slot,
            Name = row.Name,
            TamerId = row.TamerId,
            Level = row.Level,
            MapId = row.MapId,
            X = row.PosX,
            Y = row.PosY,
            TotalWins = row.TotalWins,
            TotalBattles = row.TotalBattles,
        };
        character.SetBits(row.Bits);
        return character;
    }

    public static CharacterSummary Summary(CharacterRow r) => new(
        r.Uid, r.Slot, r.Name, r.TamerId, r.Level, r.MapId, r.PosX, r.PosY, r.TotalWins, r.TotalBattles,
        r.DigimonId, r.DigimonLevel, r.DigimonName);
}
