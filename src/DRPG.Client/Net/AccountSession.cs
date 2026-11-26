using DRPG.Shared.Packets.Channel;
using DRPG.Shared.Packets.Login;
using DRPG.Shared.Protocol;

namespace DRPG.Client.Net;

/// <summary>Where <see cref="AccountSession"/> is. The steps that wait for the server end by themselves; the others wait for a scene.</summary>
public enum AccountStep
{
    NotStarted,
    ConnectingLogin,
    WaitingLoginReply,

    /// <summary>The login server answered: the world selection may offer the channels.</summary>
    LoggedIn,
    ConnectingChannel,
    WaitingChannelSelect,

    /// <summary>The <c>0x03</c> reply is in: the scene sends <c>0x04</c> (✅ "<c>0x04</c> on the reply to <c>0x03</c>").</summary>
    ChannelSelected,
    WaitingAuthentication,

    /// <summary>The <c>0x04</c> reply accepted the user: the world selection fades out.</summary>
    Authenticated,
    WaitingCharacterList,
    Ready,
    Failed,
}

/// <summary>
/// The login → channel sequence up to the character list, in the order of the official captures
/// (✅ session 56, conn 98/99) and driven by the scenes like the original (docs/cliente/cenas.md#cruzamento-com-as-capturas-e-o-accountsession):
/// <see cref="StartLogin"/> when the world selection opens (login <c>0x05</c>), <see cref="SelectChannel"/> when a channel
/// is clicked (<c>0x03</c>), <see cref="Authenticate"/> on its reply (<c>0x04</c>, user + key), and
/// <see cref="RequestCharacterList"/> when the character selection opens (<c>0x12</c> and <c>0x14</c> together).
/// Updated once per frame from the game thread with <see cref="Update"/>: no <c>await</c> inside the frame, the connect
/// tasks are polled. After <see cref="AccountStep.Ready"/> it keeps the channel connection alive (<c>0xC4</c>).
/// </summary>
public sealed class AccountSession(ClientSettings settings, GameCommandLine command, Action<string> log) : IDisposable
{
    /// <summary>✅ The original pings the channel every ~166.5 s (captures: 171.9 s and 338.4 s after connecting).</summary>
    public const ulong KeepAliveIntervalMs = 166_500;

    /// <summary>❓ How long a connect or a reply may take before the attempt is given up; the original's limit is unknown.</summary>
    public const ulong StepTimeoutMs = 15_000;

    /// <summary>✅ <c>0x00b524b0</c>: the second field of the login reply is <c>0x68</c> when BATTLE ZONE is closed.</summary>
    public const int BattleZoneClosedMarker = 0x68;

    private CancellationTokenSource _cancel = new();
    private Task<ClientConnection>? _connecting;
    private ClientConnection? _connection;
    private ulong _stepStartedMs;
    private ulong _lastKeepAliveMs;

    public AccountStep Step { get; private set; }

    /// <summary>Why the sequence failed; null otherwise. Never contains the key.</summary>
    public string? FailureMessage { get; private set; }

    /// <summary>Set when the list arrives.</summary>
    public CharacterListReply? Characters { get; private set; }

    /// <summary>True once the server confirmed the user (<c>0x14</c>).</summary>
    public bool UserConfirmed { get; private set; }

    /// <summary>The server's offer of the fourth starter digimon (from the <c>0x03</c> reply).</summary>
    public int FourthStarter { get; private set; } = -1;

    /// <summary>✅ From the login reply: whether the BATTLE ZONE button works (the emulator sends <c>0x68</c>: closed).</summary>
    public bool BattleZoneEnabled { get; private set; }

    /// <summary>The code of the last <c>0x04</c> the server refused; null when none. The session is back at <see cref="AccountStep.LoggedIn"/>.</summary>
    public int? AuthenticationRefusal { get; private set; }

    /// <summary>Connects to the login server and sends <c>0x05</c>.</summary>
    public void StartLogin(ulong nowMs)
    {
        if (Step != AccountStep.NotStarted)
            throw new InvalidOperationException($"login started while {Step}");
        Connect(AccountStep.ConnectingLogin, settings.Login, AccountDecoder.Login, nowMs);
    }

    /// <summary>Connects to the channel server and sends <c>0x03</c>. Allowed once logged in.</summary>
    public void SelectChannel(ulong nowMs)
    {
        if (Step != AccountStep.LoggedIn)
            throw new InvalidOperationException($"channel selected while {Step}");
        AuthenticationRefusal = null;
        log($"Login {settings.Login.Host} {settings.Login.Port} Session {settings.Channel.Host} {settings.Channel.Port}");
        Connect(AccountStep.ConnectingChannel, settings.Channel, AccountDecoder.Channel, nowMs);
    }

    /// <summary>Sends <c>0x04</c> with the user and the key of the command line.</summary>
    public void Authenticate(ulong nowMs)
    {
        if (Step != AccountStep.ChannelSelected)
            throw new InvalidOperationException($"authentication while {Step}");
        _connection!.Send(new AuthenticateRequest(command.Username, command.Key));
        Advance(AccountStep.WaitingAuthentication, nowMs);
    }

    /// <summary>✅ Sends <c>0x12</c> and <c>0x14</c>, in that order (the init of the character selection).</summary>
    public void RequestCharacterList(ulong nowMs)
    {
        if (Step != AccountStep.Authenticated)
            throw new InvalidOperationException($"character list requested while {Step}");
        _connection!.Send(new CharacterListRequest());
        _connection.Send(new UserCheckRequest(command.Username));
        Advance(AccountStep.WaitingCharacterList, nowMs);
    }

    /// <summary>Drops the connections and starts over from <see cref="AccountStep.NotStarted"/> (Back, or a failure).</summary>
    public void Reset()
    {
        _cancel.Cancel();
        _cancel.Dispose();
        _cancel = new CancellationTokenSource();
        _connecting = null;
        DisposeConnection();
        Step = AccountStep.NotStarted;
        FailureMessage = null;
        Characters = null;
        UserConfirmed = false;
        AuthenticationRefusal = null;
    }

    /// <summary>One step of the state machine; call every frame.</summary>
    public void Update(ulong nowMs)
    {
        if (Step is AccountStep.NotStarted or AccountStep.Failed)
            return;
        if (_connection?.Failure is { } failure)
        {
            Fail(failure);
            return;
        }
        if (IsWaitingForServer && nowMs - _stepStartedMs > StepTimeoutMs)
        {
            Fail($"No answer from the server in {StepTimeoutMs / 1000} s.");
            return;
        }

        switch (Step)
        {
            case AccountStep.ConnectingLogin:
                if (TakeConnection())
                {
                    _connection!.Send(new LoginHandshakeRequest(command.Username));
                    Advance(AccountStep.WaitingLoginReply, nowMs);
                }
                break;
            case AccountStep.ConnectingChannel:
                if (TakeConnection())
                {
                    _connection!.Send(new ChannelSelectRequest());
                    Advance(AccountStep.WaitingChannelSelect, nowMs);
                }
                break;
            case AccountStep.Ready:
                KeepAlive(nowMs);
                break;
        }
        while (Step is not (AccountStep.Failed or AccountStep.NotStarted) && _connection is { } connection && connection.TryReceive(out var packet))
            Handle(packet, nowMs);
    }

    private bool IsWaitingForServer => Step is AccountStep.ConnectingLogin or AccountStep.WaitingLoginReply or AccountStep.ConnectingChannel
        or AccountStep.WaitingChannelSelect or AccountStep.WaitingAuthentication or AccountStep.WaitingCharacterList;

    private void Handle(object packet, ulong nowMs)
    {
        switch (packet, Step)
        {
            case (LoginHandshakeReply reply, AccountStep.WaitingLoginReply):
                BattleZoneEnabled = reply.Unknown2 != BattleZoneClosedMarker;
                // ❓ The original keeps one socket; the channel connection replaces the login one.
                DisposeConnection();
                Advance(AccountStep.LoggedIn, nowMs);
                break;
            case (ChannelSelectReply reply, AccountStep.WaitingChannelSelect):
                FourthStarter = reply.FourthStarter;
                Advance(AccountStep.ChannelSelected, nowMs);
                break;
            case (AuthenticateReply { Succeeded: true }, AccountStep.WaitingAuthentication):
                Advance(AccountStep.Authenticated, nowMs);
                break;
            case (AuthenticateReply reply, AccountStep.WaitingAuthentication):
                // refused: the world selection shows why and offers the channels again
                AuthenticationRefusal = reply.ErrorCode;
                DisposeConnection();
                Advance(AccountStep.LoggedIn, nowMs);
                break;
            case (CharacterListReply list, AccountStep.WaitingCharacterList):
                Characters = list;
                _lastKeepAliveMs = nowMs;
                Advance(AccountStep.Ready, nowMs);
                log($"character list: {list.Slots.Count(slot => !slot.IsEmpty)} of {list.Slots.Count} slots in use");
                break;
            case (UserCheckReply, AccountStep.WaitingCharacterList or AccountStep.Ready):
                UserConfirmed = true;
                break;
            case (KeepAliveReply, AccountStep.Ready):
                break;
            default:
                Fail($"Unexpected {packet.GetType().Name} while {Step}.");
                break;
        }
    }

    private void Connect(AccountStep step, ServerAddress server, Func<IncomingFrame, object> decoder, ulong nowMs)
    {
        log($"connecting to {server}");
        _connecting = ClientConnection.ConnectAsync(server, decoder, _cancel.Token);
        Advance(step, nowMs);
    }

    private bool TakeConnection()
    {
        if (_connecting is not { IsCompleted: true } task)
            return false;
        _connecting = null;
        if (task.IsCompletedSuccessfully)
        {
            _connection = task.Result;
            return true;
        }
        Fail($"Could not connect: {task.Exception?.GetBaseException().Message ?? "cancelled"}");
        return false;
    }

    private void KeepAlive(ulong nowMs)
    {
        if (nowMs - _lastKeepAliveMs < KeepAliveIntervalMs)
            return;
        _lastKeepAliveMs = nowMs;
        _connection?.Send(new KeepAliveRequest());
    }

    private void Advance(AccountStep step, ulong nowMs)
    {
        Step = step;
        _stepStartedMs = nowMs;
        log($"step: {step}");
    }

    private void Fail(string message)
    {
        FailureMessage = message;
        Step = AccountStep.Failed;
        log($"failed: {message}");
        DisposeConnection();
    }

    private void DisposeConnection()
    {
        _connection?.Dispose();
        _connection = null;
    }

    public void Dispose()
    {
        _cancel.Cancel();
        DisposeConnection();
        _cancel.Dispose();
    }
}
