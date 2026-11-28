using DRPG.Client.Data;
using DRPG.Client.Features.Chat;
using DRPG.Client.Features.Exit;
using DRPG.Client.Features.Options;
using DRPG.Client.Features.Field;
using DRPG.Client.Net;
using DRPG.Client.Startup;
using DRPG.Client.Ui;
using DRPG.Client.World;
using DRPG.Engine.Platform;
using DRPG.Engine.Rendering;
using DRPG.Engine.Scenes;
using DRPG.Shared.Formats.Map;
using DRPG.Shared.Formats.Pack;

namespace DRPG.Client.Scenes;

/// <summary>
/// <c>CSceneGame</c>, scene 5 (✅ v322 ctor <c>0x00b494f0</c>, init <c>0x00b4bab0</c>, frame <c>0x00b4e550</c>, field <c>0x00b4b550</c>;
/// docs/cliente/cena-jogo.md, docs/cliente/campo.md). It wires the field feature (<see cref="FieldState"/>, <see cref="FieldHandlers"/>,
/// <see cref="FieldActions"/>, <see cref="FieldView"/>) to the frame. First the loading screen of the original's loading thread
/// (<see cref="WorldLoading"/>): it sends the <c>0x01</c> when its first run ends, the map is loaded when the server's <c>0x02</c> and
/// <c>0x03</c> are in, and the entry packets (<c>0x04</c>, <c>0xF0</c>, <c>0x121</c>, the global connection) go out when it ends. Then,
/// each frame: the map packets go to the handlers, a ground click to the actions, the logic runs in fixed steps, and the field and
/// the HUD are drawn. An <c>0xB0</c> to another map opens the loading screen again. A lost connection shows the text <c>0xed0</c> in
/// the middle of the screen until a click takes the player back to scene 2.
/// </summary>
public sealed class GameScene(GameContext game, Random random) : IScene
{
    /// <summary>
    /// ✅ 30 logic steps a second. The main loop (<c>0x00aa4a90</c>) runs a frame only when the performance counter passes the due time,
    /// which goes forward by <c>frequency / 30</c> each frame (<c>0x00867ed0(0x1e)</c>, <c>0x00867eb0</c>, <c>0x00868020</c>): at most 30
    /// frames a second. The field frame (<c>0x00b4e550</c>) runs exactly one step per frame: its catch-up adds
    /// <c>DAT_0135af08 − 0x00867e40()</c>, and 0x00867e40 returns the measured rate or 30.0 under 30, so it never adds. Steps by the
    /// clock at 30 a second give the same pace.
    /// </summary>
    public const int StepsPerSecond = 30;

    /// <summary>✅ <c>0x00b4e550</c>: at most 10000 steps in one frame.</summary>
    public const int MaxStepsPerFrame = 10_000;

    /// <summary>✅ The text shown when the connection is lost (<c>0xed0</c>), at the middle of the screen.</summary>
    public const int ConnectionLostText = 0xed0;
    private const int MessageCenterX = 400, MessageCenterY = 300;

    /// <summary>✅ The 9-part box of the messages and the side tooltips (<c>0x007dcfb0</c>).</summary>
    private const string BoxSprite = "gui\\dr2_in_000_003.spr";

    private AnimatedSprites _sprites = null!;                  // set in Init
    private FieldHud _hud = null!;                             // set in Init
    private WindowManager _windows = null!;                    // set in Init
    private UiSprites _ui = null!;                             // set in Init
    private Tooltips _tooltips = null!;                        // set in Init
    private MiniMapWindow? _miniMap;
    private readonly ChatLog _chat = new();
    private ExitWindow _exit = null!;                          // set in Init
    private MessageBox _box = null!;                           // set in Init
    private LoadingScreen _screen = null!;                     // set in Init
    private WorldLoading _loading = null!;                     // set in Init
    private FieldView _view = null!;                           // set in Init
    private EntityPlates _plates = null!;                      // set in Init
    private FieldMap? _map;
    private FieldState? _state;
    private FieldActions? _actions;
    private FieldHandlers? _handlers;
    private CursorKind _cursor = CursorKind.Normal;
    private bool _aborted, _lost;
    private long _stepsRun;
    private ulong _startMs;
    private bool _started;
    private TimeSpan _clockOffset;
    private readonly FrameStats _stats = new();

    public int FrameIntervalMs => 0;

    internal bool WorldEntered => _map is not null;
    internal bool ConnectionLost => _lost;
    internal FieldMap? Field => _map;
    internal FieldState? State => _state;
    internal FrameStats Stats => _stats;
    internal WorldLoading Loading => _loading;

    /// <summary>✅ The loading thread does not draw the cursor (no <c>0x00b3e3d0</c>).</summary>
    public bool ShowCursor => _aborted || !_loading.Showing;

    /// <summary>✅ <c>0x00b4e550</c> + <c>0x00b3e3d0</c> case 10: <c>cursor_door.spr</c> over portal cells.</summary>
    public CursorKind Cursor => _cursor;

    private GameData Data => game.Data ?? throw new InvalidOperationException("the game scene runs after the loading screen");

    public void Init()
    {
        // ✅ 0x00b4bab0: without a connection to the map the scene goes back to the world selection (scene 2)
        if (game.World?.Session is not { MapConnected: true })
        {
            _aborted = true;
            game.Scenes.Switch(SceneId.Server);
            return;
        }
        _sprites = new AnimatedSprites(game.Resource, game.Sprites, game.Log);
        _view = new FieldView(_sprites, game.SpriteOptions);
        _windows = new WindowManager(game.Sounds);
        _ui = new UiSprites(game);
        _plates = new EntityPlates(_ui, game.Text, Data.MonsterRegen);
        _tooltips = new Tooltips(_ui.Load(Tooltips.PanelSprite), new MessageBox(_ui.Load(BoxSprite), game.Text), game.Text);
        _hud = new FieldHud(game, _ui, _windows, _tooltips);
        RegisterWindows(game.World.Session);
        _box = new MessageBox(game.Sprites.Load("gui\\dr2_in_000_003.spr"), game.Text);
        _screen = new LoadingScreen(game);
        _loading = new WorldLoading(random, _screen.BackgroundCount);
        GameSceneSounds.Register(game.Sounds);                         // ✅ 0x00b4bab0
    }

    internal WindowManager Windows => _windows;

    /// <summary>
    /// The field's windows (✅ the original makes them all at start, <c>0x00b9a170</c>). Until the window registry generator exists
    /// (docs/arquitetura/cliente.md), each one is one line here.
    /// </summary>
    private void RegisterWindows(WorldSession session)
    {
        _exit = new ExitWindow(_windows, new ExitActions(session), game.Text, game.Texts,
            _ui.Load("gui\\dr2_sub_007_005.spr"), _ui.Gui("dr2_sub_007_001"));
        _windows.Register(_exit);
        _windows.Register(new OptionsWindow(game, _ui, _windows, _chat));
    }

    /// <summary>
    /// ✅ <c>0x00b4c080</c>, the scene's key: nothing while the exit window counts down (<c>+0x3eb0</c>). Escape in the field closes the
    /// topmost window (<c>0x00b93030</c>) or, with none, opens the exit window (6) with the text <c>0xecc</c>. ❓ The window hotkeys
    /// (<c>0x00b9a8d0</c>) wait for their windows.
    /// </summary>
    public void Handle(InputEvent input)
    {
        if (_aborted || _lost || _windows is null || _windows.Locked || input is not KeyEvent { Key: GameKey.Escape })
            return;
        if (_map is null || _loading.Showing)
            return;
        if (!_windows.CloseTopmost())
            _windows.Open(WindowManager.ExitWindow);
    }

    public void Frame(IRenderer renderer, ulong nowMs)
    {
        if (_aborted)
            return;
        _stats.Tick();
        var world = game.World!;
        var session = world.Session!;
        game.Account?.Update(nowMs);                                   // ✅ the channel connection stays up: its keep-alive goes on
        session.Update();
        if (!_lost && (session.Step == WorldStep.Failed || game.Account is { Step: AccountStep.Failed }))
            Lose(session.FailureMessage ?? game.Account?.FailureMessage ?? "connection lost");

        if (!_lost && _loading.Showing)
        {
            AdvanceLoading(renderer, session, world, nowMs);
            if (!_lost && _loading.Showing)
            {
                DrawLoading(renderer);
                return;
            }
        }

        if (!_lost && _map is not null && _state is not null)
        {
            RunField(session, nowMs);
            if (_state.Transition.Phase == MapTransitionPhase.ReadyToLoad)
            {
                ChangeMap(renderer, world, _state, nowMs);
                if (!_lost)
                {
                    DrawLoading(renderer);
                    return;
                }
            }
        }
        if (_map is not null && _state is not null)
            DrawField(renderer, _map, _state, session, nowMs);
        if (_lost)
            DrawLost(renderer);
    }

    /// <summary>
    /// ✅ The loading thread (<c>0x00b34ea0</c>) and the <c>0x03</c> handler (<c>0x00abbb20</c>): the <c>0x01</c> when the first run
    /// ends (then the channel's <c>0x16</c>/<c>0x18</c>, <c>0x00ae8700</c>), the map load when the world data is in, the entry packets
    /// when the second run ends.
    /// </summary>
    private void AdvanceLoading(IRenderer renderer, WorldSession session, WorldEntry world, ulong nowMs)
    {
        if (_loading.Phase == LoadingPhase.WaitingForData && session.Step == WorldStep.DataReady)
        {
            _loading.DataArrived();
            if (!EnterFirstMap(renderer, session, world))
                return;
            _loading.MapLoaded();
        }
        switch (_loading.Update(nowMs))
        {
            case LoadingAction.Join:
                session.Join();
                game.Account?.SendMapConnection(world.Slot.Name, HardwareAddresses.Read(3));   // the addresses are never logged
                break;
            case LoadingAction.EnterWorld:
                session.EnterWorld();
                game.HasEnteredWorld = true;
                StartLogic(nowMs);
                // ✅ no brightening: the 0x03 handler (0x00abbb20) sets +0x34c = 0x5f, and the first field frame (0x00b4e550) sees the
                // marker, runs the entry logic (0x00aba410, quests: a later step) and sets it back to 0 at once. The darkening of
                // 100..0x71 (0x00b55f10) belongs to coming back from a battle or an event (0x18395/0x18396), outside this milestone.
                break;
            case LoadingAction.MapTransitionDone:
                _actions?.CompleteMapTransition();
                StartLogic(nowMs);
                break;
        }
    }

    /// <summary>
    /// ✅ <c>0x00abbb20</c>: the first map of the entry, with the tamer and the digimon on the character's cell (<c>0x0085e7b0</c>);
    /// the server clock (<c>0x00abbd50</c>). The entry packets wait for the end of the loading screen (<see cref="AdvanceLoading"/>).
    /// </summary>
    private bool EnterFirstMap(IRenderer renderer, WorldSession session, WorldEntry world)
    {
        var character = session.Character!;
        if (LoadArea(renderer, world.MapId) is not { } area)
            return false;
        // ✅ the server clock: the offset of the server's time from the local clock (0x00abbd50)
        _clockOffset = DateTimeOffset.FromUnixTimeSeconds(character.ServerUnixTime) - DateTimeOffset.UtcNow;
        var state = new FieldState(area, FieldParty.Of(character, _sprites, random), _sprites);
        var actions = new FieldActions(session, state);
        _state = state;
        _actions = actions;
        _handlers = new FieldHandlers(state, actions, Data.Npcs);
        // ✅ the minimap is open when the field first shows (the capture); ❓ who opens it is not read
        _miniMap = new MiniMapWindow(game, _ui, _tooltips, state, Data.Npcs, Data.MapPortals);
        _windows.Register(_miniMap);
        _windows.Open(MiniMapWindow.WindowId);
        return true;
    }

    /// <summary>
    /// ✅ <c>0x00af0d70</c> → <c>0x00aba410</c>: the map change of an <c>0xB0</c>, once the warp effect ended. The music stops, the
    /// loading screen opens, the new map loads (<see cref="LoadArea"/>) and the tamer and the digimon stand on the target cell. The
    /// <c>0x04</c> and <c>0xB0</c> go out when the loading screen ends (<see cref="AdvanceLoading"/>).
    /// </summary>
    private void ChangeMap(IRenderer renderer, WorldEntry world, FieldState state, ulong nowMs)
    {
        var target = state.Transition.Target;
        state.Transition.Complete();
        state.Area.Wilds.Clear();
        _started = false;
        game.Sounds.StopMusic();
        world.MapId = target.MapId;
        _loading.BeginMapTransition(_screen.BackgroundCount);
        _loading.Update(nowMs);
        if (LoadArea(renderer, target.MapId) is not { } area)
            return;
        state.EnterArea(area, target.Cell);
        _loading.MapLoaded();
    }

    /// <summary>
    /// ✅ <c>0x00ad3730</c>: loads <c>map\%s.map</c> of <paramref name="mapId"/> and starts its music (no entry: silence). A map the
    /// pack lacks or a broken file loses the connection with the original's log line.
    /// </summary>
    private FieldArea? LoadArea(IRenderer renderer, int mapId)
    {
        var path = Data.MapFile(mapId);
        MapFile map;
        try
        {
            if (path is null || !game.Resource.Contains(path))
                throw new MapFormatException($"map {mapId} has no file in the pack");
            map = MapFile.Parse(game.Resource.ReadEntry(path));
            var field = new FieldMap(map, game.Resource, renderer, game.Log);
            _map?.Dispose();
            _map = field;
        }
        catch (Exception e) when (e is MapFormatException or PackFormatException or InvalidDataException or IOException)
        {
            game.Log($"LoadMap Error : {path}: {e.Message}");
            Lose($"map {mapId}: {e.Message}");
            return null;
        }
        if (map.InvalidCellReferences > 0)
            game.Log($"{path}: {map.InvalidCellReferences} cell references outside the lists were cleared");

        if (MapBgm.FileOf(mapId) is { } music)
            game.Sounds.PlayMusic(music);
        else
            game.Sounds.StopMusic();
        return new FieldArea(mapId, map, Data.Npcs, _sprites, random);
    }

    /// <summary>
    /// ✅ <c>0x00b4c1a0</c> / <c>0x00b4e550</c>: the map packets to the handlers; while no map change locks the field, a ground click
    /// or hold to the actions and the throttled <c>0x20</c>; then the logic steps and the cursor.
    /// </summary>
    private void RunField(WorldSession session, ulong nowMs)
    {
        while (session.TryReceive(out var packet))
            if (!_handlers!.Handle(packet))
                game.Log($"map: {packet.GetType().Name} has no handler");

        if (!_state!.Transition.LocksInput && !_windows.Locked)
        {
            var mouse = game.Mouse;
            if (!mouse.Down)
                _actions!.ReleaseGround();
            if ((mouse.Pressed || mouse.Down) && GroundCellUnderMouse() is { } cell)
                _actions!.PressGround(cell);
            _actions!.Update(nowMs);
        }

        RunLogic(nowMs);
        _state.Hover(GroundCellUnderMouse());
        UpdateCursor();
    }

    private void StartLogic(ulong nowMs)
    {
        _startMs = nowMs;
        _stepsRun = 0;
        _started = true;
    }

    /// <summary>✅ <c>0x00b4e550</c>: the logic in fixed steps from the real time, never more than <see cref="MaxStepsPerFrame"/> at once.</summary>
    private void RunLogic(ulong nowMs)
    {
        if (!_started)
            return;
        long due = (long)(nowMs - _startMs) * StepsPerSecond / 1000;
        long steps = Math.Min(due - _stepsRun, MaxStepsPerFrame);
        for (long i = 0; i < steps; i++)
            Step();
        _stepsRun = steps == MaxStepsPerFrame ? due : _stepsRun + Math.Max(0, steps);
    }

    /// <summary>One logic step of the field (<see cref="FieldState.Step"/>) and of its portal swirls.</summary>
    internal void Step()
    {
        _state!.Step();
        _view.Step();
    }

    /// <summary>✅ <c>0x00b4e550</c> lines 604-606: <see cref="CursorKind.Portal"/> while the mouse is over a portal cell.</summary>
    private void UpdateCursor() =>
        _cursor = GroundCellUnderMouse() is { } cell && FieldView.IsPortal(_state!, cell) ? CursorKind.Portal : CursorKind.Normal;

    /// <summary>✅ <c>0x00b91ec0</c>: the map cell under the mouse, or null when the mouse is off the frame, over the HUD or off the map.</summary>
    private Cell? GroundCellUnderMouse()
    {
        var mouse = game.Mouse;
        if (mouse.X < 0 || mouse.Y < 0 || mouse.X >= IRenderer.FrameWidth || mouse.Y >= IRenderer.FrameHeight || _hud.Covers(mouse.X, mouse.Y)
            || _windows.Covers(mouse.X, mouse.Y))
            return null;
        return FieldView.CellAt(_map!, _state!, mouse.X, mouse.Y);
    }

    /// <summary>✅ <c>0x00b4b550</c>: the field (<see cref="FieldView"/>), then the HUD (the bar) and the windows (<c>0x00b9a7c0</c>).</summary>
    private void DrawField(IRenderer renderer, FieldMap map, FieldState state, WorldSession session, ulong nowMs)
    {
        _view.Draw(renderer, map, state, game.SpriteOptions);
        var character = session.Character!;
        var (cameraX, cameraY) = map.Camera(state.Party.Tamer.WorldX, state.Party.Tamer.WorldY);
        _plates.Draw(renderer, state, character, cameraX, cameraY, game.SpriteOptions, game.Options.Language);
        var leader = character.Party[0];
        var tamer = state.Party.Tamer.Cell;
        _hud.Frame(renderer, new HudValues(
            character.Name, character.TamerId, character.TamerClass, character.Level, game.ChannelName, state.Area.MapId, Data.MapName(state.Area.MapId),
            tamer.X, tamer.Y, leader.IsEmpty ? null : leader, DateTime.UtcNow + _clockOffset, _miniMap is { ShowsMap: true, Collapsed: false }), game.Mouse);
        _windows.Frame(renderer, game.Mouse, nowMs);
        if (_exit.LeaveRequested)
            LeaveWorld();
    }

    private void DrawLoading(IRenderer renderer) =>
        _screen.Draw(renderer, _loading.Background, _loading.GaugeFrame, _screen.TipOf(_loading.Tip));

    private void Lose(string reason)
    {
        _lost = true;
        game.Log($"connection lost: {reason}");
        game.Sounds.StopMusic();
    }

    /// <summary>
    /// ✅ The end of the exit window's count: <c>0x00ad0ae0</c> leaves the world and <c>0x00ad0bd0</c> sets <c>+0x7a4</c>, so the next
    /// frame (<c>0x00b4e550</c>) stops the music, closes the connections and switches to scene 2.
    /// </summary>
    private void LeaveWorld()
    {
        game.Sounds.StopMusic();
        ReturnToServerScene();
    }

    private void ReturnToServerScene()
    {
        game.World?.Dispose();
        game.World = null;
        game.Account?.Reset();
        game.Scenes.Switch(SceneId.Server);
        _aborted = true;
    }

    /// <summary>✅ <c>0x00b4e550</c> step 2: the text of a lost connection in the middle of the screen; ❓ the original's way out is not read, a click goes back to scene 2.</summary>
    private void DrawLost(IRenderer renderer)
    {
        _box.Draw(renderer, game.Texts[ConnectionLostText], MessageCenterX, MessageCenterY);
        if (game.Mouse.Pressed)
            ReturnToServerScene();
    }

    public void Dispose()
    {
        if (_stats.Count > 0)
            game.Log($"game scene: {_stats}");
        _map?.Dispose();
    }
}
