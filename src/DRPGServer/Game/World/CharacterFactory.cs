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
    /// <summary>Monta o personagem. Null se o líder não tem espécie válida.</summary>
    public Character? Build(CharacterRecord record)
    {
        var row = record.Character;
        var formsByDigimon = record.Forms.ToLookup(f => f.DigimonUid);
        var digimons = record.Digimons
            .Select(r => (Row: r, Digimon: BuildDigimon(r, formsByDigimon[r.Uid])))
            .Where(d => d.Digimon is not null)
            .ToList();
        var leader = digimons.FirstOrDefault(d => !d.Row.InDepository && d.Row.Slot == 0).Digimon
                     ?? digimons.FirstOrDefault(d => !d.Row.InDepository).Digimon
                     ?? digimons.FirstOrDefault().Digimon;
        if (leader is null) return null;

        var inventory = new Inventory(data.FindItem, (uint)options.Value.MaxStackPerItem);
        foreach (var i in record.Items)
        {
            if (data.FindItem(i.ItemId) is null || i.Amount == 0) continue;
            var bag = inventory.BagOf(i.Bag == (byte)BagKind.Battle ? BagKind.Battle : BagKind.Inventory);
            bag.Place(inventory.Create(i.ItemId, i.Slot, i.Amount, i.Uid));
        }

        var character = new Character(leader, inventory)
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
            DepositorySlots = Math.Min(row.DepositorySlots, (byte)Character.MaxDepositorySlots),
        };
        foreach (var (r, d) in digimons)
        {
            if (d == leader) continue;
            if (r.InDepository)
                character.AddToDepository(d!);
            else if (!character.Place(r.Slot, d!))
                character.Place(character.Party.ToList().IndexOf(null), d!);   // posição repetida: primeira livre
        }
        foreach (var flag in record.Flags) character.SetFlag(flag);
        character.SetBits(row.Bits);
        return character;
    }

    private Digimon? BuildDigimon(DigimonRow r, IEnumerable<FormRow> forms)
    {
        if (data.FindSpecies(r.DigimonId) is not { } species) return null;
        var digimon = new Digimon(data, species, r.Level)
        {
            Uid = r.Uid,
            Name = string.IsNullOrEmpty(r.Name) ? species.Name : r.Name,
            FreeRename = r.FreeRename,
        };
        digimon.Load(r.Exp, r.UnlockedStage, r.TotalWins, r.TotalBattles);
        foreach (var f in forms)
            if (data.FindSpecies(f.SpeciesId) is { } formSpecies)
                digimon.LoadForm(formSpecies, new Attributes(f.AllocStr, f.AllocAgi, f.AllocCon, f.AllocInt),
                    f.Skill1Level, f.Skill2Level);
        digimon.RestoreAll();
        return digimon;
    }

    public static CharacterSummary Summary(CharacterRow r) => new(
        r.Uid, r.Slot, r.Name, r.TamerId, r.Level, r.MapId, r.PosX, r.PosY, r.TotalWins, r.TotalBattles,
        r.DigimonId, r.DigimonLevel, r.DigimonName);
}
