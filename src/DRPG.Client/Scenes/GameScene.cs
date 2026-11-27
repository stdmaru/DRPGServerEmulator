using DRPG.Client.Data;
using DRPG.Client.Net;
using DRPG.Client.Ui;
using DRPG.Client.World;
using DRPG.Engine.Platform;
using DRPG.Engine.Rendering;
using DRPG.Shared.Formats.Map;
using DRPG.Shared.Formats.Pack;
using DRPG.Shared.Packets.Map;

namespace DRPG.Client.Scenes;

/// <summary>
/// <c>CSceneGame</c>, scene 5 (✅ v322 ctor <c>0x00b494f0</c>, init <c>0x00b4bab0</c>, frame <c>0x00b4e550</c>, field <c>0x00b4b550</c>;
/// docs/cliente/cena-jogo.md): the map of the character's slot with the tamer and the leader digimon standing on it and the basic
/// HUD. This step only enters the world: nothing moves and nothing reacts. Per frame: the connections are read (the server's <c>0x02</c>
/// and <c>0x03</c> arrive), the map is loaded when they are in and the entry packets go out (<c>0x04</c>, <c>0xF0</c>, <c>0x121</c>, the global
/// connection), then the logic runs in fixed steps, and the field is drawn: ground, the objects and the two characters sorted by y,
/// the HUD. A lost connection shows the text <c>0xed0</c> in the middle of the screen until a click takes the player back to scene 2.
/// </summary>
public sealed class GameScene(GameContext game, Random random) : IScene
{
    /// <summary>
    /// ❓ The logic runs every <c>DAT_0135af08 = 30</c> units of a time whose unit is not read yet (✅ <c>0x00867ed0(30)</c>); like the
    /// emulator, 60 steps a second.
    /// </summary>
    public const int StepsPerSecond = 60;

    /// <summary>✅ <c>0x00b4e550</c>: at most 10000 steps in one frame.</summary>
    public const int MaxStepsPerFrame = 10_000;

    /// <summary>✅ <c>0x00abbb20</c>: the transition counter <c>+0x34c</c> starts at 0x5f; from 100 to 199 the screen brightens (❓ the curve).</summary>
    public const int FadeStart = 0x5f, FadeFullyDark = 100, FadeEnd = 200;

    /// <summary>✅ The text shown when the connection is lost (<c>0xed0</c>), at the middle of the screen.</summary>
    public const int ConnectionLostText = 0xed0;
    private const int MessageCenterX = 400, MessageCenterY = 300;

    /// <summary>✅ <c>0x008669e0</c>: the species whose sprite is drawn 32 px lower (<c>0x149</c>, <c>0x3e9</c>).</summary>
    private static readonly HashSet<int> LowerSpecies = [0x149, 0x3e9];

    private AnimatedSprites _animated = null!;                 // set in Init
    private FieldHud _hud = null!;                             // set in Init
    private MessageBox _box = null!;                           // set in Init
    private FieldMap? _field;
    private FieldAnimator? _tamerAnimation, _digimonAnimation;
    private TamerDoll? _tamer;
    private SpriteAnimator? _digimon;
    private int _digimonSpecies;
    private (int X, int Y) _tamerWorld;
    private (int X, int Y) _digimonWorld;
    private bool _hasDigimon;
    private bool _aborted, _lost;
    private long _stepsRun;
    private ulong _startMs;
    private bool _started;
    private int _transition = FadeStart;
    private TimeSpan _clockOffset;
    private readonly List<DrawItem> _queue = [];
    private readonly FrameStats _stats = new();

    public int FrameIntervalMs => 0;

    internal bool WorldEntered => _field is not null;
    internal bool ConnectionLost => _lost;
    internal int Transition => _transition;
    internal FieldMap? Field => _field;
    internal FieldAnimator? TamerAnimation => _tamerAnimation;
    internal FieldAnimator? DigimonAnimation => _digimonAnimation;
    internal (int X, int Y) TamerWorld => _tamerWorld;
    internal (int X, int Y) DigimonWorld => _digimonWorld;
    internal FrameStats Stats => _stats;

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
        _hud = new FieldHud(game);
        _box = new MessageBox(game.Sprites.Load("gui\\dr2_in_000_003.spr"), game.Text);
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

        if (!_lost && _field is null && session.Step == WorldStep.DataReady)
            LoadWorld(renderer, session, world, nowMs);

        if (_field is not null && !_lost)
        {
            RunLogic(nowMs);
            DrawField(renderer, session);
        }
        else if (_field is not null)
        {
            DrawField(renderer, session);
        }
        if (_lost)
            DrawLost(renderer);
        else if (_field is not null)
            DrawFade(renderer);
    }

    /// <summary>
    /// ✅ <c>0x00abbb20</c>: loads the map (<c>0x00ad3730</c> → <c>map\%s.map</c>), starts its music, puts the tamer and the digimon on their
    /// cells (<c>0x0085e7b0</c>), sends <c>0x04</c>, <c>0xF0</c> and, on the first entry, <c>0x121</c>, opens the global connection and
    /// starts the brightening of the screen.
    /// </summary>
    private void LoadWorld(IRenderer renderer, WorldSession session, WorldEntry world, ulong nowMs)
    {
        var character = session.Character!;
        var path = game.Data?.MapFile(world.MapId);
        try
        {
            if (path is null || !game.Resource.Contains(path))
                throw new MapFormatException($"map {world.MapId} has no file in the pack");
            var map = MapFile.Parse(game.Resource.ReadEntry(path));
            _field = new FieldMap(map, game.Resource, renderer, game.Log);
            if (map.InvalidCellReferences > 0)
                game.Log($"{path}: {map.InvalidCellReferences} cell references outside the lists were cleared");
        }
        catch (Exception e) when (e is MapFormatException or PackFormatException or InvalidDataException or IOException)
        {
            game.Log($"LoadMap Error : {path}: {e.Message}");
            Lose($"map {world.MapId}: {e.Message}");
            return;
        }

        // ✅ the BGM of the map load: the current one stops, the map's starts (no entry: silence)
        if (MapBgm.FileOf(world.MapId) is { } music)
            game.Sounds.PlayMusic(music);
        else
            game.Sounds.StopMusic();

        // ✅ the server clock: the offset of the server's time from the local clock (0x00abbd50)
        _clockOffset = DateTimeOffset.FromUnixTimeSeconds(character.ServerUnixTime) - DateTimeOffset.UtcNow;
        PlaceCharacters(character);
        session.EnterWorld();
        game.HasEnteredWorld = true;
        _startMs = nowMs;
        _started = true;
        _transition = FadeStart;
    }

    /// <summary>✅ <c>0x0085e7b0</c>: the centre of the cell, <c>cx · 64 + 32</c> and <c>cy · 32 + 16</c>; the digimon one cell up and to the left.</summary>
    private void PlaceCharacters(CharacterDataPacket character)
    {
        _tamerWorld = CellCenter(character.X, character.Y);
        _tamer = _animated.OpenTamer(character.TamerId);
        if (_tamer is not null)
            _tamerAnimation = new FieldAnimator(_tamer.Body, digimon: false, species: 0, random.Next(8));       // ✅ rand() % 8

        // ✅ 0x00abbd50: a tamer of 5, 6 or 7 has no digimon on the map
        var leader = character.Party[0];
        _hasDigimon = character.TamerId is not (5 or 6 or 7) && !leader.IsEmpty;
        if (!_hasDigimon)
            return;
        _digimonSpecies = leader.SpeciesId;
        _digimonWorld = CellCenter(character.X - 1, character.Y - 1);
        _digimon = _animated.OpenDigimon(leader.SpeciesId);
        if (_digimon is not null)
            _digimonAnimation = new FieldAnimator(_digimon, digimon: true, leader.SpeciesId, random.Next(8));
    }

    internal static (int X, int Y) CellCenter(int cellX, int cellY) =>
        (cellX * MapFile.CellWidth + MapFile.CellWidth / 2, cellY * MapFile.CellHeight + MapFile.CellHeight / 2);

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
        _tamerAnimation?.Step();
        _digimonAnimation?.Step(random.Next(4));                       // ✅ 1 in 4 for the animation 3
        if (_transition is > 0 and < FadeEnd)                          // 0 = the brightening is over: it does not start again
            _transition++;
        if (_transition >= FadeEnd)
            _transition = 0;
    }

    /// <summary>✅ <c>0x00b4b550</c>: the camera on the tamer, the ground, then the queue with the objects, the digimon and the tamer, then the HUD.</summary>
    private void DrawField(IRenderer renderer, WorldSession session)
    {
        var field = _field!;
        var (cameraX, cameraY) = field.Camera(_tamerWorld.X, _tamerWorld.Y);
        field.DrawGround(renderer, cameraX, cameraY);

        _queue.Clear();
        field.QueueObjects(_queue, cameraX, cameraY);
        if (_digimon?.Texture is { } frame && _hasDigimon)
        {
            var (anchorX, anchorY) = _digimon.Anchor;               // ✅ the digimon is drawn by the anchor of the .ani
            int x = _digimonWorld.X - cameraX, y = _digimonWorld.Y - cameraY;
            int lower = LowerSpecies.Contains(_digimonSpecies) ? 32 : 0;
            _queue.Add(new DrawItem(y, frame, x - anchorX, y - anchorY + lower));
        }
        if (_tamer is not null)
        {
            // ✅ the paper doll of the character selection, by the top-left corner at (x − 120, y − 162), no anchor
            int x = _tamerWorld.X - cameraX, y = _tamerWorld.Y - cameraY;
            var tamer = _tamer;
            _queue.Add(new DrawItem(y, null, x - 120, y - 162, (r, drawX, drawY) => tamer.Draw(r, drawX, drawY)));
        }
        FieldMap.Sort(_queue);
        FieldMap.DrawQueue(renderer, _queue);

        var character = session.Character!;
        var leader = character.Party[0];
        _hud.Draw(renderer, new HudValues(
            character.Name, character.TamerId, character.Level, game.ChannelName, game.World!.MapId, game.Data?.MapName(game.World.MapId) ?? "",
            character.X, character.Y, leader.IsEmpty ? null : leader, DateTime.UtcNow + _clockOffset));
    }

    /// <summary>❓ The darkness: black until the counter reaches 100, then it fades out linearly until 200.</summary>
    internal static int DarknessOf(int transition) => transition is <= 0 or >= FadeEnd ? 0
        : transition < FadeFullyDark ? 255
        : 255 * (FadeEnd - transition) / (FadeEnd - FadeFullyDark);

    private void DrawFade(IRenderer renderer)
    {
        if (DarknessOf(_transition) is > 0 and var amount)
            renderer.Darken(amount);
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
