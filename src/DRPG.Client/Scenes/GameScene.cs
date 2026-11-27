using DRPG.Client.Data;
using DRPG.Client.Net;
using DRPG.Client.Ui;
using DRPG.Client.World;
using DRPG.Engine.Platform;
using DRPG.Engine.Rendering;
using DRPG.Engine.Scenes;
using DRPG.Shared.Formats.Map;
using DRPG.Shared.Formats.Pack;
using DRPG.Shared.Packets.Map;
using DRPG.Shared.Protocol;

namespace DRPG.Client.Scenes;

/// <summary>
/// <c>CSceneGame</c>, scene 5 (✅ v322 ctor <c>0x00b494f0</c>, init <c>0x00b4bab0</c>, frame <c>0x00b4e550</c>, field <c>0x00b4b550</c>;
/// docs/cliente/cena-jogo.md): the map of the character's slot with the tamer and the leader digimon standing on it and the basic
/// HUD. This step only enters the world: nothing moves and nothing reacts. First the loading screen of the original's loading thread
/// (<see cref="WorldLoading"/>): it sends the <c>0x01</c> when its first run ends, the map is loaded when the server's <c>0x02</c> and
/// <c>0x03</c> are in, and the entry packets (<c>0x04</c>, <c>0xF0</c>, <c>0x121</c>, the global connection) go out when it ends. Then the
/// logic runs in fixed steps, and the field is drawn: ground, the objects and the two characters sorted by y, the HUD. A lost connection shows the text <c>0xed0</c> in the middle of the screen until a click takes the player back to scene 2.
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

    /// <summary>✅ <c>0x008669e0</c>: the species whose sprite is drawn 32 px lower (<c>0x149</c>, <c>0x3e9</c>).</summary>
    private static readonly HashSet<int> LowerSpecies = [0x149, 0x3e9];

    private AnimatedSprites _animated = null!;                 // set in Init
    private FieldHud _hud = null!;                             // set in Init
    private MessageBox _box = null!;                           // set in Init
    private LoadingScreen _screen = null!;                     // set in Init
    private WorldLoading _loading = null!;                     // set in Init
    private FieldMap? _field;
    private FieldCollision? _collision;
    private FieldPathfinder? _pathfinder;
    private FieldAnimator? _tamerAnimation, _digimonAnimation;
    private TamerDoll? _tamer;
    private SpriteAnimator? _digimon;
    private int _digimonSpecies;
    private Serial _tamerSerial, _digimonSerial;
    private FieldEntity _tamerEntity = new(0, 0);
    private FieldEntity? _digimonEntity;
    private readonly FieldWilds _wilds = new();
    private readonly FieldNpcs _npcs = new();
    private FieldPortals _portals = null!;                     // set in Init
    private readonly MovePacketQueue _moveQueue = new();
    private (int X, int Y)? _lastClickCell;
    private TeleportCharacterPacket? _pendingTransition;
    private bool _transitionWaitingStop, _transitionReadyToLoad, _inputLockedForTransition;
    private CursorKind _cursor = CursorKind.Normal;
    private bool _aborted, _lost;
    private long _stepsRun;
    private ulong _startMs;
    private bool _started;
    private TimeSpan _clockOffset;
    private readonly List<DrawItem> _queue = [];
    private readonly FrameStats _stats = new();

    public int FrameIntervalMs => 0;

    internal bool WorldEntered => _field is not null;
    internal bool ConnectionLost => _lost;
    internal FieldMap? Field => _field;
    internal FieldCollision? Collision => _collision;
    internal FieldAnimator? TamerAnimation => _tamerAnimation;
    internal FieldAnimator? DigimonAnimation => _digimonAnimation;
    internal (int X, int Y) TamerWorld => (_tamerEntity.WorldX, _tamerEntity.WorldY);
    internal (int X, int Y) DigimonWorld => _digimonEntity is { } d ? (d.WorldX, d.WorldY) : (0, 0);
    internal FieldEntity TamerEntity => _tamerEntity;
    internal FieldEntity? DigimonEntity => _digimonEntity;
    internal FieldWilds Wilds => _wilds;
    internal FieldNpcs Npcs => _npcs;
    internal FieldPortals Portals => _portals;
    internal FrameStats Stats => _stats;
    internal WorldLoading Loading => _loading;

    /// <summary>✅ The loading thread does not draw the cursor (no <c>0x00b3e3d0</c>).</summary>
    public bool ShowCursor => _aborted || !_loading.Showing;

    /// <summary>✅ <c>0x00b4e550</c> + <c>0x00b3e3d0</c> case 10: <c>cursor_door.spr</c> over portal cells.</summary>
    public CursorKind Cursor => _cursor;

    public void Init()
    {
        // ✅ 0x00b4bab0: without a connection to the map the scene goes back to the world selection (scene 2)
        if (game.World?.Session is not { MapConnected: true })
        {
            _aborted = true;
            game.Scenes.Switch(SceneId.Server);
            return;
        }
        _animated = new AnimatedSprites(game.Resource, game.Sprites, game.Log);
        _portals = new FieldPortals(game.Sprites);
        _hud = new FieldHud(game);
        _box = new MessageBox(game.Sprites.Load("gui\\dr2_in_000_003.spr"), game.Text);
        _screen = new LoadingScreen(game);
        _loading = new WorldLoading(random, _screen.BackgroundCount);
        // ✅ the scene's sound effects (0x00b4bab0) are not registered yet: nothing in this step plays one
    }

    public void Handle(InputEvent input)
    {
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
                _screen.Draw(renderer, _loading.Background, _loading.GaugeFrame, _screen.TipOf(_loading.Tip));
                return;
            }
        }

        if (_field is not null && !_lost)
        {
            HandleFieldInput(session, nowMs);
            RunLogic(nowMs);
            if (_transitionReadyToLoad && _pendingTransition is { } pending)
            {
                _pendingTransition = null;
                _transitionReadyToLoad = false;
                _inputLockedForTransition = false;
                BeginMapTransition(renderer, world, pending, nowMs);
                if (_lost)
                    DrawLost(renderer);
                else if (_loading.Showing)
                    _screen.Draw(renderer, _loading.Background, _loading.GaugeFrame, _screen.TipOf(_loading.Tip));
                return;
            }
            UpdateCursor();
            DrawField(renderer, session);
        }
        else if (_field is not null)
        {
            DrawField(renderer, session);
        }
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
            if (!LoadWorld(renderer, session, world))
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
                _startMs = nowMs;
                _stepsRun = 0;
                _started = true;
                // ✅ no brightening: the 0x03 handler (0x00abbb20) sets +0x34c = 0x5f, and the first field frame (0x00b4e550) sees the
                // marker, runs the entry logic (0x00aba410, quests: a later step) and sets it back to 0 at once. The darkening of
                // 100..0x71 (0x00b55f10) belongs to coming back from a battle or an event (0x18395/0x18396), outside this milestone.
                break;
            case LoadingAction.MapTransitionDone:
                session.CompleteMapTransition();
                _startMs = nowMs;
                _stepsRun = 0;
                _started = true;
                break;
        }
    }

    /// <summary>
    /// ✅ <c>0x00abbb20</c>: loads the map (<c>0x00ad3730</c> → <c>map\%s.map</c>), starts its music, puts the tamer and the digimon on their
    /// cells (<c>0x0085e7b0</c>). The entry packets wait for the end of the loading screen (<see cref="AdvanceLoading"/>).
    /// </summary>
    private bool LoadWorld(IRenderer renderer, WorldSession session, WorldEntry world)
    {
        var character = session.Character!;
        var path = game.Data?.MapFile(world.MapId);
        try
        {
            if (path is null || !game.Resource.Contains(path))
                throw new MapFormatException($"map {world.MapId} has no file in the pack");
            var map = MapFile.Parse(game.Resource.ReadEntry(path));
            _field = new FieldMap(map, game.Resource, renderer, game.Log);
            _collision = new FieldCollision(map);
            _pathfinder = new FieldPathfinder(_collision);
            if (map.InvalidCellReferences > 0)
                game.Log($"{path}: {map.InvalidCellReferences} cell references outside the lists were cleared");
        }
        catch (Exception e) when (e is MapFormatException or PackFormatException or InvalidDataException or IOException)
        {
            game.Log($"LoadMap Error : {path}: {e.Message}");
            Lose($"map {world.MapId}: {e.Message}");
            return false;
        }

        // ✅ the BGM of the map load: the current one stops, the map's starts (no entry: silence)
        if (MapBgm.FileOf(world.MapId) is { } music)
            game.Sounds.PlayMusic(music);
        else
            game.Sounds.StopMusic();

        // ✅ the server clock: the offset of the server's time from the local clock (0x00abbd50)
        _clockOffset = DateTimeOffset.FromUnixTimeSeconds(character.ServerUnixTime) - DateTimeOffset.UtcNow;
        _npcs.LoadForMap(world.MapId, game.Data, _collision, _animated, random);
        PlaceCharacters(character);
        return true;
    }

    /// <summary>✅ <c>0x0085e7b0</c>: the centre of the cell, <c>cx · 64 + 32</c> and <c>cy · 32 + 16</c>; the digimon one cell up and to the left.</summary>
    private void PlaceCharacters(CharacterDataPacket character)
    {
        _tamerSerial = character.Serial;
        _tamerEntity = new FieldEntity(character.X, character.Y);
        _collision?.Occupy(_tamerEntity.CellX, _tamerEntity.CellY, _tamerSerial);
        _moveQueue.Reset();
        _lastClickCell = null;
        _tamer = _animated.OpenTamer(character.TamerId);
        if (_tamer is not null)
            _tamerAnimation = new FieldAnimator(_tamer.Body, digimon: false, species: 0, random.Next(8));       // ✅ rand() % 8

        // ✅ 0x00abbd50: a tamer of 5, 6 or 7 has no digimon on the map
        var leader = character.Party[0];
        bool hasDigimon = character.TamerId is not (5 or 6 or 7) && !leader.IsEmpty;
        if (!hasDigimon)
        {
            _digimonEntity = null;
            _digimonSerial = default;
            return;
        }
        _digimonSpecies = leader.SpeciesId;
        _digimonSerial = leader.Serial;
        _digimonEntity = new FieldEntity(character.X - 1, character.Y - 1);
        _collision?.Occupy(_digimonEntity.CellX, _digimonEntity.CellY, _digimonSerial);
        _digimon = _animated.OpenDigimon(leader.SpeciesId);
        if (_digimon is not null)
            _digimonAnimation = new FieldAnimator(_digimon, digimon: true, leader.SpeciesId, random.Next(8));
    }

    internal static (int X, int Y) CellCenter(int cellX, int cellY) =>
        (cellX * MapFile.CellWidth + MapFile.CellWidth / 2, cellY * MapFile.CellHeight + MapFile.CellHeight / 2);

    /// <summary>
    /// ✅ <c>0x00b4c1a0</c> / <c>0x00b4d290</c> / <c>0x00b4e550</c> / <c>0x00b4b360</c> / <c>0x00af0d70</c>: processes ground clicks
    /// and holds, drains throttled <c>0x20</c> packets and applies <c>0xB0</c> position corrections or queues cross-map transitions.
    /// </summary>
    private void HandleFieldInput(WorldSession session, ulong nowMs)
    {
        while (session.TryReceiveFieldDigimon(out var wild))
            _wilds.Apply(wild, game.World!.MapId, _collision, _animated.OpenDigimon, random);

        while (session.TryReceiveFieldNpc(out var npc))
            _npcs.Apply(npc, game.World!.MapId, game.Data, _collision, _animated, random);

        while (session.TryReceiveTeleport(out var teleport))
        {
            if (teleport.MapId == game.World!.MapId)
            {
                _collision?.Vacate(_tamerEntity.CellX, _tamerEntity.CellY);
                if (_digimonEntity is not null)
                    _collision?.Vacate(_digimonEntity.CellX, _digimonEntity.CellY);
                _tamerEntity.PlaceAt(teleport.X, teleport.Y);
                _digimonEntity?.PlaceAt(teleport.X - 1, teleport.Y - 1);
                _collision?.Occupy(_tamerEntity.CellX, _tamerEntity.CellY, _tamerSerial);
                if (_digimonEntity is not null)
                    _collision?.Occupy(_digimonEntity.CellX, _digimonEntity.CellY, _digimonSerial);
                _moveQueue.Reset();
                _lastClickCell = null;
            }
            else
            {
                // ✅ 0x00acb072 → 0x00af0d70: lock field clicks (+0x772 = 0), keep walking until stopped (+0x771 = 1),
                // then play effect 0xa8 (Effect\effect_map.spr) before starting the loading screen (0x00aba410).
                _pendingTransition = teleport;
                _transitionWaitingStop = true;
                _transitionReadyToLoad = false;
                _inputLockedForTransition = true;
                _moveQueue.Reset();
                _lastClickCell = null;
            }
        }

        if (_inputLockedForTransition)
            return;

        var mouse = game.Mouse;
        if (!mouse.Down)
            _lastClickCell = null;

        if ((mouse.Pressed || mouse.Down) && mouse.X >= 0 && mouse.Y >= 0 && mouse.X < IRenderer.FrameWidth && mouse.Y < IRenderer.FrameHeight
            && !FieldHud.Covers(mouse.X, mouse.Y))
        {
            var field = _field!;
            var (cameraX, cameraY) = field.Camera(_tamerEntity.WorldX, _tamerEntity.WorldY);
            int cellX = (mouse.X + cameraX) / MapFile.CellWidth;
            int cellY = (mouse.Y + cameraY) / MapFile.CellHeight;
            if (cellX >= 0 && cellX < field.Map.Width && cellY >= 0 && cellY < field.Map.Height
                && _lastClickCell != (cellX, cellY))
            {
                _lastClickCell = (cellX, cellY);
                if (NextStep(_tamerEntity.CellX, _tamerEntity.CellY, cellX, cellY) is not null)
                {
                    _tamerEntity.SetDestination(cellX, cellY);
                    _moveQueue.Enqueue((short)cellX, (short)cellY);
                }
            }
        }

        if (_moveQueue.TryDequeue(nowMs, out short sendX, out short sendY))
            session.SendMove(sendX, sendY);
    }

    /// <summary>
    /// ✅ <c>0x00acb072</c> → <c>0x00af0d70</c> → <c>0x00aba410</c>: transitions to another map on S2C <c>0xB0</c>.
    /// Stops BGM, clears current map entities, opens the Map Loading Thread, loads the new <c>.map</c>, spawns its NPCs,
    /// starts its BGM, and places the tamer and partner digimon at the target cell.
    /// </summary>
    private void BeginMapTransition(IRenderer renderer, WorldEntry world, TeleportCharacterPacket teleport, ulong nowMs)
    {
        game.Sounds.StopMusic();
        _wilds.Clear(_collision);
        _npcs.ClearCurrent(_collision);
        _moveQueue.Reset();
        _lastClickCell = null;
        _started = false;

        world.MapId = teleport.MapId;
        _loading.BeginMapTransition(_screen.BackgroundCount);
        _loading.Update(nowMs);

        var path = game.Data?.MapFile(world.MapId);
        try
        {
            if (path is null || !game.Resource.Contains(path))
                throw new MapFormatException($"map {world.MapId} has no file in the pack");
            var map = MapFile.Parse(game.Resource.ReadEntry(path));
            _field?.Dispose();
            _field = new FieldMap(map, game.Resource, renderer, game.Log);
            _collision = new FieldCollision(map);
            _pathfinder = new FieldPathfinder(_collision);
            if (map.InvalidCellReferences > 0)
                game.Log($"{path}: {map.InvalidCellReferences} cell references outside the lists were cleared");
        }
        catch (Exception e) when (e is MapFormatException or PackFormatException or InvalidDataException or IOException)
        {
            game.Log($"LoadMap Error : {path}: {e.Message}");
            Lose($"map {world.MapId}: {e.Message}");
            return;
        }

        if (MapBgm.FileOf(world.MapId) is { } music)
            game.Sounds.PlayMusic(music);
        else
            game.Sounds.StopMusic();

        _npcs.LoadForMap(world.MapId, game.Data, _collision, _animated, random);
        _tamerEntity.PlaceAt(teleport.X, teleport.Y);
        _digimonEntity?.PlaceAt(teleport.X - 1, teleport.Y - 1);
        _collision.Occupy(_tamerEntity.CellX, _tamerEntity.CellY, _tamerSerial);
        if (_digimonEntity is not null)
            _collision.Occupy(_digimonEntity.CellX, _digimonEntity.CellY, _digimonSerial);
        _loading.MapLoaded();
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

    internal void Step()
    {
        int oldTamerX = _tamerEntity.CellX, oldTamerY = _tamerEntity.CellY;
        _tamerEntity.StepPhysics(NextStep);
        UpdateOccupancy(oldTamerX, oldTamerY, _tamerEntity, _tamerSerial);
        SyncAnimation(_tamerAnimation, _tamerEntity);
        _tamerAnimation?.Step();

        if (_digimonEntity is not null)
        {
            int oldDigiX = _digimonEntity.CellX, oldDigiY = _digimonEntity.CellY;
            _digimonEntity.FollowLeader(_tamerEntity, NextStep);
            UpdateOccupancy(oldDigiX, oldDigiY, _digimonEntity, _digimonSerial);
            SyncAnimation(_digimonAnimation, _digimonEntity);
            _digimonAnimation?.Step(random.Next(4));                       // ✅ 1 in 4 for the animation 3
        }

        _wilds.Step(_tamerEntity, _collision, _pathfinder, random);
        _npcs.Step(random);
        _portals.Step();

        // ✅ 0x00afe310 lines 49-53: once the tamer stops walking on a pending 0xB0 transition, trigger effect 0xa8 (effect_map.spr)
        if (_transitionWaitingStop && !_tamerEntity.IsMoving)
        {
            _transitionWaitingStop = false;
            _portals.StartWarpEffect();
        }
        // ✅ 0x00a4ab73..0x00a4ab91: when effect 0xa8 finishes its frames, run 0x00aba410 (loading screen + map reload)
        if (_portals.IsWarpPlaying && _portals.StepWarpEffect())
            _transitionReadyToLoad = true;
    }

    /// <summary>✅ <c>0x00b4e550</c> lines 604-606: switches the cursor to <see cref="CursorKind.Portal"/> when hovering a portal cell.</summary>
    private void UpdateCursor()
    {
        _cursor = CursorKind.Normal;
        if (_field is not { } field || _loading.Showing || _lost)
            return;
        var mouse = game.Mouse;
        if (mouse.X < 0 || mouse.Y < 0 || mouse.X >= IRenderer.FrameWidth || mouse.Y >= IRenderer.FrameHeight || FieldHud.Covers(mouse.X, mouse.Y))
            return;
        var (cameraX, cameraY) = field.Camera(_tamerEntity.WorldX, _tamerEntity.WorldY);
        int cellX = (mouse.X + cameraX) / MapFile.CellWidth;
        int cellY = (mouse.Y + cameraY) / MapFile.CellHeight;
        if (cellX >= 0 && cellX < field.Map.Width && cellY >= 0 && cellY < field.Map.Height
            && FieldPortals.IsPortalCursorType(field.Map[cellX, cellY].Type))
            _cursor = CursorKind.Portal;
    }

    private (int NextX, int NextY)? NextStep(int fromX, int fromY, int destX, int destY)
    {
        if (fromX == destX && fromY == destY)
            return null;
        if (_pathfinder is not null)
            return _pathfinder.NextStep(fromX, fromY, destX, destY);
        return (fromX + Math.Sign(destX - fromX), fromY + Math.Sign(destY - fromY));
    }

    private void UpdateOccupancy(int oldX, int oldY, FieldEntity entity, Serial serial)
    {
        if (_collision is null)
            return;
        if (entity.CellX != oldX || entity.CellY != oldY)
            _collision.Vacate(oldX, oldY);
        _collision.Occupy(entity.CellX, entity.CellY, serial);
    }

    private static void SyncAnimation(FieldAnimator? animator, FieldEntity entity)
    {
        if (animator is null)
            return;
        animator.SetDirection(entity.Direction);
        if (entity.IsMoving)
        {
            int target = (int)entity.State;
            if (animator.Animation != target)
                animator.Switch(target);
        }
        else if (animator.Animation >= FieldAnimator.Walk)
        {
            animator.Switch(FieldAnimator.Standing);
        }
    }

    /// <summary>✅ <c>0x00b4b550</c>: the camera on the tamer, the ground, then the queue with the objects, the digimon and the tamer, then the HUD.</summary>
    private void DrawField(IRenderer renderer, WorldSession session)
    {
        var field = _field!;
        var (cameraX, cameraY) = field.Camera(_tamerEntity.WorldX, _tamerEntity.WorldY);
        field.DrawGround(renderer, cameraX, cameraY);
        _portals.DrawGround(renderer, field, cameraX, cameraY, game.SpriteOptions);

        _queue.Clear();
        field.QueueObjects(_queue, cameraX, cameraY, SpriteEffects.ObjectLookOf(game.World!.MapId, game.SpriteOptions));
        int currentMapId = game.World!.MapId;
        _wilds.QueueDraw(_queue, cameraX, cameraY, LowerSpecies.Contains, (r, sprite, drawX, drawY) => DrawDigimon(r, sprite, currentMapId, drawX, drawY));
        if (_digimon?.Texture is { } frame && _digimonEntity is { } digimonEntity)
        {
            var (anchorX, anchorY) = _digimon.Anchor;               // ✅ the digimon is drawn by the anchor of the .ani
            int x = digimonEntity.WorldX - cameraX, y = digimonEntity.WorldY - cameraY;
            int lower = LowerSpecies.Contains(_digimonSpecies) ? 32 : 0;
            var digimon = _digimon;
            int mapId = game.World!.MapId;
            _queue.Add(new DrawItem(y, frame, x - anchorX, y - anchorY + lower, (r, drawX, drawY) => DrawDigimon(r, digimon, mapId, drawX, drawY)));
        }
        if (_tamer is not null)
        {
            // ✅ the paper doll of the character selection, by the top-left corner at (x − 120, y − 162), no anchor
            int x = _tamerEntity.WorldX - cameraX, y = _tamerEntity.WorldY - cameraY;
            var tamer = _tamer;
            bool mapHasShadow = SpriteEffects.TamerHasShadow(game.World!.MapId);
            _queue.Add(new DrawItem(y, null, x - 120, y - 162, (r, drawX, drawY) => tamer.Draw(r, drawX, drawY, game.SpriteOptions, mapHasShadow)));
        }
        _npcs.QueueDraw(_queue, cameraX, cameraY, currentMapId, game.SpriteOptions, (r, sprite, drawX, drawY) => DrawDigimon(r, sprite, currentMapId, drawX, drawY));
        FieldMap.Sort(_queue);
        FieldMap.DrawQueue(renderer, _queue);
        _portals.DrawOverlay(renderer, field, cameraX, cameraY, game.SpriteOptions);
        _portals.DrawWarpEffect(renderer, _tamerEntity.WorldX, _tamerEntity.WorldY, cameraX, cameraY, game.SpriteOptions);

        var character = session.Character!;
        var leader = character.Party[0];
        _hud.Draw(renderer, new HudValues(
            character.Name, character.TamerId, character.TamerClass, character.Level, game.ChannelName, game.World!.MapId, game.Data?.MapName(game.World.MapId) ?? "",
            _tamerEntity.CellX, _tamerEntity.CellY, leader.IsEmpty ? null : leader, DateTime.UtcNow + _clockOffset));
    }

    /// <summary>
    /// ✅ <c>0x007e83d0</c>: with the <c>shadow</c> option the frame's shadow layer, then with the <c>anti</c> option (outside the animations
    /// 9/10) its edge layer, both blended at the same corner as the sprite (<see cref="SpriteEffects"/>), then the sprite.
    /// </summary>
    private void DrawDigimon(IRenderer renderer, SpriteAnimator digimon, int mapId, int x, int y)
    {
        var options = game.SpriteOptions;
        if (options.Shadow && digimon.ShadowTexture is { } shadow)
            options.DrawBlended(renderer, shadow, x, y, SpriteEffects.DigimonShadowAlpha(mapId));
        if (options.Edge && SpriteEffects.DigimonHasEdge(digimon.Animation) && digimon.EdgeTexture is { } edge)
            options.DrawBlended(renderer, edge, x, y, SpriteEffects.DigimonEdgeAlpha(mapId));
        if (digimon.Texture is { } frame)
            renderer.Draw(frame, x, y);
    }

    private void Lose(string reason)
    {
        _lost = true;
        game.Log($"connection lost: {reason}");
        game.Sounds.StopMusic();
    }

    /// <summary>✅ <c>0x00b4e550</c> step 2: the text of a lost connection in the middle of the screen; ❓ the original's way out is not read, a click goes back to scene 2.</summary>
    private void DrawLost(IRenderer renderer)
    {
        _box.Draw(renderer, game.Texts[ConnectionLostText], MessageCenterX, MessageCenterY);
        if (game.Mouse.Pressed)
        {
            game.World?.Dispose();
            game.World = null;
            game.Account?.Reset();
            game.Scenes.Switch(SceneId.Server);
            _aborted = true;
        }
    }

    public void Dispose()
    {
        if (_stats.Count > 0)
            game.Log($"game scene: {_stats}");
        _field?.Dispose();
    }
}
