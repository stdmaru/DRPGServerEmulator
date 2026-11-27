using DRPG.Client.World;

namespace DRPG.Client.Features.Field;

/// <summary>Where a map change started by <c>0xB0</c> is (✅ <c>0x00acb072</c> → <c>0x00af0d70</c> → <c>0x00aba410</c>).</summary>
public enum MapTransitionPhase
{
    /// <summary>No map change: the field takes clicks.</summary>
    None,

    /// <summary>✅ <c>0x00af0d70</c>: clicks are locked (<c>+0x772 = 0</c>) and the tamer finishes its walk (<c>+0x771 = 1</c>).</summary>
    WaitingForStop,

    /// <summary>✅ <c>0x00afe310</c> lines 49-53: the tamer stopped and the <c>0xa8</c> warp effect plays.</summary>
    Warping,

    /// <summary>✅ <c>0x00a4ab73..0x00a4ab91</c>: the effect ended; the scene opens the loading screen and loads <see cref="MapTransition.Target"/>.</summary>
    ReadyToLoad,
}

/// <summary>The map and cell an <c>0xB0</c> sends the player to.</summary>
public readonly record struct MapTarget(int MapId, Cell Cell);

/// <summary>The map change in progress, if any.</summary>
public sealed class MapTransition
{
    public MapTransitionPhase Phase { get; private set; }

    /// <summary>Where the player goes; meaningful while <see cref="Phase"/> is not <see cref="MapTransitionPhase.None"/>.</summary>
    public MapTarget Target { get; private set; }

    /// <summary>True while the field ignores clicks.</summary>
    public bool LocksInput => Phase != MapTransitionPhase.None;

    /// <summary>✅ <c>0x00acb072</c> → <c>0x00af0d70</c>: an <c>0xB0</c> to another map arrived.</summary>
    public void Begin(MapTarget target)
    {
        Target = target;
        Phase = MapTransitionPhase.WaitingForStop;
    }

    /// <summary>
    /// One logic step after the entities moved: once the tamer stands, the warp effect starts; when it ends, the map is ready to load.
    /// </summary>
    public void Step(FieldEntity tamer, WarpEffect warp)
    {
        switch (Phase)
        {
            case MapTransitionPhase.WaitingForStop when !tamer.IsMoving:
                Phase = MapTransitionPhase.Warping;
                warp.Start();
                // the effect takes its first step in the same logic step it starts
                if (warp.Step())
                    Phase = MapTransitionPhase.ReadyToLoad;
                break;
            case MapTransitionPhase.Warping:
                if (warp.Step())
                    Phase = MapTransitionPhase.ReadyToLoad;
                break;
        }
    }

    /// <summary>The scene loaded <see cref="Target"/>: the field takes clicks again.</summary>
    public void Complete() => Phase = MapTransitionPhase.None;
}
