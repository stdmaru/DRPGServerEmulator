using DRPG.Shared.Packets.Channel;
using DRPG.Shared.Packets.Login;
using DRPG.Shared.Protocol;

namespace DRPG.Client.Net;

/// <summary>Where <see cref="AccountSession"/> is on the way to the character list.</summary>
public enum AccountStep
{
    NotStarted,
    ConnectingLogin,
    WaitingLoginReply,
    ConnectingChannel,
    WaitingChannelSelect,
    WaitingAuthentication,
    WaitingCharacterList,
    Ready,
    Failed,
}

/// <summary>
/// The login → channel sequence up to the character list, in the order of the official captures
/// (✅ session 56, conn 98/99): login <c>0x05</c> on the login server; then on the channel server <c>0x03</c> →
/// reply, <c>0x04</c> (user + key) → reply, <c>0x12</c> and <c>0x14</c> sent together → <c>0x12</c> list.
/// Driven once per frame from the game thread with <see cref="Update"/>: no <c>await</c> inside the frame, the connect
/// tasks are polled. After <see cref="AccountStep.Ready"/> it keeps the channel connection alive (<c>0xC4</c>).
/// </summary>
public sealed class AccountSession(ClientSettings settings, GameCommandLine command, Action<string> log) : IDisposable
{
    /// <summary>✅ The original pings the channel every ~166.5 s (captures: 171.9 s and 338.4 s after connecting).</summary>
    public const ulong KeepAliveIntervalMs = 166_500;

    /// <summary>❓ How long a connect or a reply may take before the attempt is given up; the original's limit is unknown.</summary>
    public const ulong StepTimeoutMs = 15_000;

    private readonly CancellationTokenSource _cancel = new();
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

    public void Start(ulong nowMs)
    {
        if (Step != AccountStep.NotStarted)
            throw new InvalidOperationException("already started");
        Connect(AccountStep.ConnectingLogin, settings.Login, AccountDecoder.Login, nowMs);
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
        if (Step != AccountStep.Ready && nowMs - _stepStartedMs > StepTimeoutMs)
        {
            Fail($"No answer from the server in {StepTimeoutMs / 1000} s.");
            return;
        }

        switch (Step)
        {
            case AccountStep.ConnectingLogin:
                if (TakeConnection(nowMs))
                {
                    _connection!.Send(new LoginHandshakeRequest(command.Username));
                    Advance(AccountStep.WaitingLoginReply, nowMs);
                }
                break;
            case AccountStep.ConnectingChannel:
                if (TakeConnection(nowMs))
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

    private void Handle(object packet, ulong nowMs)
    {
        switch (packet, Step)
        {
            case (LoginHandshakeReply, AccountStep.WaitingLoginReply):
                // ❓ The original keeps the login connection while it shows the world list; here it is not needed.
                DisposeConnection();
                Connect(AccountStep.ConnectingChannel, settings.Channel, AccountDecoder.Channel, nowMs);
                break;
            case (ChannelSelectReply reply, AccountStep.WaitingChannelSelect):
                FourthStarter = reply.FourthStarter;
                _connection!.Send(new AuthenticateRequest(command.Username, command.Key));
                Advance(AccountStep.WaitingAuthentication, nowMs);
                break;
            case (AuthenticateReply { Succeeded: true }, AccountStep.WaitingAuthentication):
                // ✅ the original asks for the list and confirms the user in the same breath
                _connection!.Send(new CharacterListRequest());
                _connection.Send(new UserCheckRequest(command.Username));
                Advance(AccountStep.WaitingCharacterList, nowMs);
                break;
            case (AuthenticateReply reply, AccountStep.WaitingAuthentication):
                Fail($"The server refused the user or the key (code {reply.ErrorCode}).");
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

    private bool TakeConnection(ulong nowMs)
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
