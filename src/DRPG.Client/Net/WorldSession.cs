using DRPG.Shared.Packets.Global;
using DRPG.Shared.Packets.Map;
using DRPG.Shared.Protocol;

namespace DRPG.Client.Net;

/// <summary>Where <see cref="WorldSession"/> is.</summary>
public enum WorldStep
{
    NotStarted,
    ConnectingMap,

    /// <summary>
    /// The map server is connected and the <c>0x49</c> are sent; the loading screen is up and the <c>0x01</c> waits for
    /// <see cref="WorldSession.Join"/>.
    /// </summary>
    Connected,

    /// <summary>The <c>0x01</c> is sent: the server answers with the world (<c>0x02</c>, <c>0x03</c>, …).</summary>
    Joining,

    /// <summary>The <c>0x02</c> and the <c>0x03</c> are in: the scene loads the map and calls <see cref="WorldSession.EnterWorld"/>.</summary>
    DataReady,

    /// <summary><c>0x04</c>, <c>0xF0</c> (and <c>0x121</c>) are sent and the global connection is opened.</summary>
    InWorld,
    Failed,
}

/// <summary>
/// The map and global connections of the world entry (✅ docs/cliente/cena-jogo.md#sequência-na-rede, captures.db session 52,
/// connections 88 and 89), driven by the game scene once per frame with <see cref="Update"/> (the connect tasks are polled).
/// <see cref="Start"/> connects to the map server and sends <c>0x49</c> ×3; the server's <c>0x0E</c> is echoed; <see cref="Join"/>
/// sends the <c>0x01</c> when the first loading screen ends (✅ <c>0x00adca00</c>, 2.75–2.9 s after the connection in the pcaps);
/// after the <c>0x02</c> and <c>0x03</c> the scene loads the map and calls <see cref="EnterWorld"/> (✅ <c>0x00abbb20</c>:
/// the <c>0x04</c>, the <c>0xF0</c>, on the first entry the <c>0x121</c>, and the global connection with <c>0x64</c> and <c>0xDE</c> ×2).
/// The server's <c>0xC4</c> ping is answered. Nothing here logs a key, a password or an address of the machine.
/// </summary>
public sealed class WorldSession(ClientSettings settings, GameCommandLine command, Action<string> log) : IDisposable
{
    /// <summary>✅ <c>0x00ae07b0(0x121)</c> goes only on the first entry of a game run (<c>+0x3f15 == 0</c>).</summary>
    public const int GlobalJoinCount = 2;

    private CancellationTokenSource _cancel = new();
    private Task<ClientConnection>? _connectingMap, _connectingGlobal;
    private ClientConnection? _map, _global;
    private string _characterName = "";
    private bool _firstEntry;
    private bool _disposed;
    private readonly List<ushort> _unknownLogged = [];
    private readonly Queue<TeleportCharacterPacket> _teleports = new();
    private readonly Queue<FieldDigimonPacket> _wilds = new();
    private readonly Queue<FieldNpcPacket> _npcs = new();

    public WorldStep Step { get; private set; }

    /// <summary>Why the connection failed; null otherwise.</summary>
    public string? FailureMessage { get; private set; }

    /// <summary>✅ <c>0x02</c>: the character and the party.</summary>
    public CharacterDataPacket? Character { get; private set; }

    /// <summary>✅ <c>0x03</c>: the bags.</summary>
    public InventoryDataPacket? Inventory { get; private set; }

    /// <summary>✅ The seed of the <c>0x0E</c> of the map connection: the key of the <c>0xE6</c> event comes from it (not used yet).</summary>
    public byte[]? MapSeed { get; private set; }

    /// <summary>Packets received and ignored, by opcode (for tests and the log).</summary>
    public Dictionary<ushort, int> IgnoredCounts { get; } = [];

    /// <summary>Opcodes the client does not know that were received.</summary>
    public IReadOnlyList<ushort> UnknownOpcodes => _unknownLogged;

    public bool GlobalConnected { get; private set; }

    /// <summary>True from the moment the map server is connected: the scene may start; false while connecting and after a failure.</summary>
    public bool MapConnected => Step is WorldStep.Connected or WorldStep.Joining or WorldStep.DataReady or WorldStep.InWorld;

    /// <summary>Opens the map connection for <paramref name="characterName"/>.</summary>
    public void Start(string characterName, bool firstEntry)
    {
        if (Step != WorldStep.NotStarted)
            throw new InvalidOperationException($"world entry started while {Step}");
        _characterName = characterName;
        _firstEntry = firstEntry;
        log($"connecting to {settings.Map}");
        _connectingMap = ClientConnection.ConnectAsync(settings.Map, MapDecoder.Map, _cancel.Token);
        Step = WorldStep.ConnectingMap;
    }

    /// <summary>
    /// ✅ <c>0x00adca00</c>, called by the loading thread when its 1 s timer runs out: sends the <c>0x01</c> (the character and the user).
    /// </summary>
    public void Join()
    {
        if (Step != WorldStep.Connected)
            throw new InvalidOperationException($"joining the world while {Step}");
        _map!.Send(new JoinCharacterRequest(_characterName, command.Username));
        Step = WorldStep.Joining;
        CheckDataReady();                     // a server that sent the world before the 0x01 does not leave the entry hanging
    }

    /// <summary>✅ <c>0x00abbb20</c> after the map is loaded: sends <c>0x04</c>, <c>0xF0</c> and <c>0x121</c> and opens the global connection.</summary>
    public void EnterWorld()
    {
        if (Step != WorldStep.DataReady || Character is null)
            throw new InvalidOperationException($"entering the world while {Step}");
        _map!.Send(new RefreshCharacterInfoRequest(Character.Serial));
        _map.Send(new RenameRequest(RenameKind.Tamer, 0, ""));      // ❓ the rest of the 0xF0 is from the capture: (tamer, uid 0, no name)
        if (_firstEntry)
            _map.Send(new Unknown121Request());
        log($"connecting to {settings.Global}");
        _connectingGlobal = ClientConnection.ConnectAsync(settings.Global, MapDecoder.Global, _cancel.Token);
        Step = WorldStep.InWorld;
    }

    /// <summary>✅ <c>0x00ae1000</c>: sends <c>0x20</c> with the destination cell to the map server.</summary>
    public void SendMove(short x, short y)
    {
        if (Step == WorldStep.InWorld && _map is not null)
            _map.Send(new MoveCharacterRequest(x, y));
    }

    /// <summary>Dequeues the next <c>0xB0</c> teleport / position-correction packet received from the map server.</summary>
    public bool TryReceiveTeleport(out TeleportCharacterPacket packet) => _teleports.TryDequeue(out packet!);

    /// <summary>Dequeues the next <c>0x11</c> wild digimon update received from the map server.</summary>
    public bool TryReceiveFieldDigimon(out FieldDigimonPacket packet) => _wilds.TryDequeue(out packet!);

    /// <summary>Dequeues the next <c>0x09</c> dynamic NPC add/remove packet received from the map server.</summary>
    public bool TryReceiveFieldNpc(out FieldNpcPacket packet) => _npcs.TryDequeue(out packet!);

    /// <summary>One step; call every frame.</summary>
    public void Update()
    {
        if (Step is WorldStep.NotStarted or WorldStep.Failed)
            return;
        if (Step == WorldStep.ConnectingMap && _connectingMap is { IsCompleted: true } task)
        {
            _connectingMap = null;
            if (!task.IsCompletedSuccessfully)
            {
                Fail($"Could not connect: {task.Exception?.GetBaseException().Message ?? "cancelled"}");
                return;
            }
            _map = task.Result;
            // ✅ the pcaps: 0x49 at the connection (❓ two of the three go out with the server's 0x0E; their callers are not read)
            for (int i = 0; i < JoinHandshakeRequest.Times; i++)
                _map.Send(new JoinHandshakeRequest());
            Step = WorldStep.Connected;
        }
        if (_map?.Failure is { } mapFailure)
        {
            Fail(mapFailure);
            return;
        }
        if (_connectingGlobal is { IsCompleted: true } global)
        {
            _connectingGlobal = null;
            if (!global.IsCompletedSuccessfully)
            {
                Fail($"Could not connect to the global server: {global.Exception?.GetBaseException().Message ?? "cancelled"}");
                return;
            }
            _global = global.Result;
            GlobalConnected = true;
            // ✅ the capture: 0x64, then 0xDE twice (the character's cell)
            var character = Character!;
            _global.Send(new GlobalUserAuthRequest(command.Username, _characterName, character.Uid));
            for (int i = 0; i < GlobalJoinCount; i++)
                _global.Send(new GlobalJoinRequest(_characterName, character.X, character.Y));
        }
        if (_global?.Failure is { } globalFailure)
        {
            Fail($"Global server: {globalFailure}");
            return;
        }
        while (_map is { } map && Step != WorldStep.Failed && map.TryReceive(out var packet))
            HandleMap(map, packet);
        while (_global is { } global2 && Step != WorldStep.Failed && global2.TryReceive(out var packet))
            HandleGlobal(global2, packet);
    }

    private void HandleMap(ClientConnection map, object packet)
    {
        switch (packet)
        {
            case SeedHelloPacket hello:
                MapSeed = hello.Seed;
                map.Send(new SeedHelloEcho(hello.Seed));                       // ✅ the capture: the seed back with 14 zero bytes
                break;
            case CharacterDataPacket character:
                Character = character;
                break;
            case InventoryDataPacket inventory:
                Inventory = inventory;
                break;
            case MapPing:
                map.Send(new Shared.Packets.Channel.KeepAliveRequest());        // ✅ answered at once in the capture
                break;
            case TeleportCharacterPacket teleport:
                _teleports.Enqueue(teleport);
                break;
            case FieldDigimonPacket wild:
                _wilds.Enqueue(wild);
                break;
            case FieldNpcPacket npc:
                _npcs.Enqueue(npc);
                break;
            case IgnoredPacket ignored:
                IgnoredCounts[ignored.Opcode] = IgnoredCounts.GetValueOrDefault(ignored.Opcode) + 1;
                break;
            case UnknownPacket unknown:
                if (!_unknownLogged.Contains(unknown.Opcode))
                {
                    _unknownLogged.Add(unknown.Opcode);
                    log($"map: opcode 0x{unknown.Opcode:X} ({unknown.PayloadLength} B) is not used yet");
                }
                break;
        }
        CheckDataReady();
    }

    private void CheckDataReady()
    {
        if (Step == WorldStep.Joining && Character is not null && Inventory is not null)
        {
            Step = WorldStep.DataReady;
            log("world data received");
        }
    }

    private void HandleGlobal(ClientConnection global, object packet)
    {
        if (packet is SeedHelloPacket hello)
            global.Send(new SeedHelloEcho(hello.Seed));
    }

    private void Fail(string message)
    {
        FailureMessage = message;
        Step = WorldStep.Failed;
        log($"world failed: {message}");
        DisposeConnections();
    }

    private void DisposeConnections()
    {
        _map?.Dispose();
        _map = null;
        _global?.Dispose();
        _global = null;
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _cancel.Cancel();
        DisposeConnections();
        _cancel.Dispose();
    }
}
