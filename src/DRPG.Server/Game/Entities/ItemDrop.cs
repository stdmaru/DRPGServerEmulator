namespace DRPG.Server.Game.Entities;

/// <summary>
/// Item ou card no chão. Com <paramref name="OwnerCharacterUid"/>, só o dono coleta até
/// <see cref="World.Zone.DropOwnerWindow"/>; depois, qualquer um.
/// </summary>
public sealed record ItemDrop(uint Uid, uint ItemId, short X, short Y, TimeSpan DroppedAt, uint? OwnerCharacterUid)
{
    public Data.GameData.ItemInfo? Info { get; init; }
    public bool IsCard { get; init; }

    /// <summary>A janela do dono já terminou e o drop foi anunciado de novo (thread do mundo).</summary>
    public bool Released { get; set; }

    /// <summary>O personagem <paramref name="characterUid"/> pode coletar agora (<paramref name="now"/> no relógio do mundo).</summary>
    public bool IsFreeFor(uint characterUid, TimeSpan now) =>
        OwnerCharacterUid is not { } owner || owner == characterUid || now - DroppedAt >= World.Zone.DropOwnerWindow;
}
