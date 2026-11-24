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

        // 1. Monta o inventário e equipamentos primeiro para que os Digimons da party já nasçam com os bônus certos
        long unixNow = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var inventory = new Inventory(data.FindItem, (uint)options.Value.MaxStackPerItem, data.FindCard);
        foreach (var i in record.Items)
        {
            if (i.Amount == 0) continue;
            if (i.ExpiresAt > 0 && unixNow >= i.ExpiresAt) continue; // descarta itens de período já expirados
            var bagKind = i.Bag switch
            {
                (byte)BagKind.Battle => BagKind.Battle,
                (byte)BagKind.EquippedCards => BagKind.EquippedCards,
                (byte)BagKind.Equipment => BagKind.Equipment,
                _ => BagKind.Inventory,
            };
            bool isCard = bagKind is BagKind.Battle or BagKind.EquippedCards;
            bool valid = isCard
                ? (data.Cards.Count > 0 ? data.FindCard(i.ItemId) is not null : data.FindItem(i.ItemId) is not null)
                : data.FindItem(i.ItemId) is not null;
            if (!valid) continue;
            var created = inventory.Create(i.ItemId, i.Slot, i.Amount, i.Uid, isCard, i.ExpiresAt, i.DurationSeconds);
            if (bagKind == BagKind.Equipment && !EquipmentSlots.CanEquip((ItemPlace)i.Slot, created, row.Level))
                continue;
            inventory.BagOf(bagKind).Place(created);
        }

        // 2. Monta todos os Digimons (party recebe o bônus de equipamentos; Depository recebe vazio) e carrega os vitais uma única vez
        var equipBonus = inventory.TotalEquipmentBonus();
        var formsByDigimon = record.Forms.ToLookup(f => f.DigimonUid);
        var titlesByDigimon = (record.Titles ?? []).ToLookup(t => t.DigimonUid, t => t.TitleId);
        var digimons = record.Digimons
            .Select(r => (Row: r, Digimon: BuildDigimon(
                r,
                formsByDigimon[r.Uid],
                titlesByDigimon[r.Uid],
                r.InDepository ? EquipmentBonus.Empty : equipBonus)))
            .Where(d => d.Digimon is not null)
            .ToList();
        var leader = digimons.FirstOrDefault(d => !d.Row.InDepository && d.Row.Slot == 0).Digimon
                     ?? digimons.FirstOrDefault(d => !d.Row.InDepository).Digimon
                     ?? digimons.FirstOrDefault().Digimon;
        if (leader is null) return null;

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
                character.AddToDepository(d!, r.Slot);
            else if (!character.Place(r.Slot, d!))
                character.Place(character.Party.ToList().IndexOf(null), d!);   // posição repetida: primeira livre
        }
        foreach (var flag in record.Flags) character.SetFlag(flag);
        character.SetBits(row.Bits);
        return character;
    }

    private Digimon? BuildDigimon(DigimonRow r, IEnumerable<FormRow> forms, IEnumerable<uint> titles, EquipmentBonus equipBonus)
    {
        if (data.FindSpecies(r.DigimonId) is not { } species) return null;
        var digimon = new Digimon(data, species, r.Level)
        {
            Uid = r.Uid,
            Name = string.IsNullOrEmpty(r.Name) ? species.Name : r.Name,
            FreeRename = r.FreeRename,
        };
        foreach (var f in forms)
            if (data.FindSpecies(f.SpeciesId) is { } formSpecies)
                digimon.LoadForm(formSpecies, new Attributes(f.AllocStr, f.AllocAgi, f.AllocCon, f.AllocInt),
                    f.Skill1Level, f.Skill2Level);
        digimon.LoadTitles(r.TitleId, titles);
        if (!equipBonus.IsEmpty)
            digimon.SetEquipmentBonus(equipBonus);
        digimon.Load(r.Exp, r.UnlockedStage, r.TotalWins, r.TotalBattles, r.Hp, r.Vp, r.Evp);
        return digimon;
    }

    public static CharacterSummary Summary(CharacterRow r) => new(
        r.Uid, r.Slot, r.Name, r.TamerId, r.Level, r.MapId, r.PosX, r.PosY, r.TotalWins, r.TotalBattles,
        r.DigimonId, r.DigimonLevel, r.DigimonName);
}
