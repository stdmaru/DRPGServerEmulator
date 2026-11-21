namespace DRPGServer.Game.Entities;

/// <summary>Item no chão.</summary>
public sealed record ItemDrop(uint Uid, uint ItemId, short X, short Y, TimeSpan DroppedAt, uint? OwnerCharacterUid)
{
    public Data.GameData.ItemInfo? Info { get; init; }
}
