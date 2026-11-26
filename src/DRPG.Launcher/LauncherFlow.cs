using DRPG.Launcher.Login;
using DRPG.Launcher.Patching;
using DRPG.Shared.Protocol.Launcher;

namespace DRPG.Launcher;

public enum LauncherStage
{
    Patching,    // Loading box with the patch bar
    Connecting,  // login panel shown but frozen, "Connecting to server..."
    Ready,       // login panel usable; the status box shows the last login error, if any
    LoggingIn,   // panel frozen, "Logging in..."
    LoggedIn,    // game key received: the launcher starts the game (see GameLogin)
    Failed,      // message in the status box; OK retries the failed step
}

/// <summary>
/// The launcher sequence (docs/cliente/tela-login-launcher.md#fluxo-do-launcher): patch, then handshake, then login.
/// Patching and the handshake run in the background; the frame loop reads <see cref="Snapshot"/> each frame and never
/// waits on them.
/// </summary>
public sealed class LauncherFlow(LauncherSettings settings, Patcher patcher, Func<PatchConfiguration.LoginServerSection, LoginClient> createClient)
{
    /// <summary><paramref name="Status"/> is empty when there is nothing to show (the status box hides).</summary>
    public sealed record State(LauncherStage Stage, string Status, PatchProgress? Progress);

    private readonly Lock _lock = new();
    private State _state = new(LauncherStage.Patching, "Checking for updates...", null);
    private PatchConfiguration? _configuration;
    private Func<Task>? _retry;
    private Task _running = Task.CompletedTask;

    public LoginClient? Client { get; private set; }

    /// <summary>The game username and key, set on <see cref="LauncherStage.LoggedIn"/>.</summary>
    public LauncherLoginResult? GameLogin { get; private set; }
    public PatchConfiguration? Configuration => _configuration;

    public State Snapshot
    {
        get { lock (_lock) return _state; }
    }

    public bool IsBusy => !_running.IsCompleted;

    public void Start(CancellationToken ct) => _running = Guard(() => PatchAsync(ct));

    /// <summary>Repeats the step that failed (patch or handshake).</summary>
    public void Retry()
    {
        if (Snapshot.Stage == LauncherStage.Failed && !IsBusy && _retry is { } retry)
            _running = Guard(retry);
    }

    /// <summary>Sends ID and password (Ready only). Empty fields are refused here without asking the server.</summary>
    public void Login(string login, string password, CancellationToken ct)
    {
        if (Snapshot.Stage != LauncherStage.Ready || IsBusy || Client is null)
            return;
        if (login.Length == 0 || password.Length == 0)
        {
            Set(LauncherStage.Ready, "Enter your ID and password.");
            return;
        }
        // an unexpected error retries the handshake, never a login with a password kept in a closure
        _running = Guard(() => LoginAsync(login, password, ct), () => ConnectAsync(ct));
    }

    /// <summary>The game could not be started: back to the panel with the reason.</summary>
    public void GameStartFailed(string message)
    {
        if (Snapshot.Stage != LauncherStage.LoggedIn)
            return;
        GameLogin = null;
        Set(LauncherStage.Ready, message);
    }

    /// <summary>
    /// Runs a background step so that nothing can leave the screen stuck: an unexpected exception becomes a Failed
    /// state with its message, retrying <paramref name="retry"/> (the same step by default).
    /// </summary>
    private async Task Guard(Func<Task> step, Func<Task>? retry = null)
    {
        try
        {
            await step();
        }
        catch (OperationCanceledException)
        {
            // launcher closing
        }
        catch (Exception e)
        {
            Fail($"Unexpected error: {e.Message}", retry ?? step);
        }
    }

    private async Task PatchAsync(CancellationToken ct)
    {
        Set(LauncherStage.Patching, "Checking for updates...");
        var outcome = await patcher.RunAsync(settings.ConfigUrl, new ProgressSink(this), ct);
        switch (outcome)
        {
            case PatchOutcome.UpToDate up:
                _configuration = up.Configuration;
                await ConnectAsync(ct);
                break;
            case PatchOutcome.Maintenance m:
                Fail($"{m.Title}: {m.Message.Trim()}", () => PatchAsync(ct));
                break;
            case PatchOutcome.NeedsReinstall r:
                Fail($"Version {r.Installed} is too old (minimum {r.Minimum}). Please reinstall the game.", null);
                break;
            case PatchOutcome.Failed f:
                Fail($"Update failed: {f.Reason}", () => PatchAsync(ct));
                break;
        }
    }

    private async Task ConnectAsync(CancellationToken ct)
    {
        if (_configuration?.LoginServer is not { } login)
        {
            Fail("DR_Configuration.json has no LoginServer section.", null);
            return;
        }
        Set(LauncherStage.Connecting, "Connecting to server...");
        Client ??= createClient(login);
        var result = await Client.HandshakeAsync(ct);
        switch (result)
        {
            case HandshakeResult.Ready:
                if (settings.AllowInsecure)
                    Console.WriteLine("DEVELOPMENT MODE: server key not checked");
                Set(LauncherStage.Ready, ""); // nothing to report: the status box hides
                break;
            case HandshakeResult.FingerprintMismatch:
                Fail("Server key does not match. Connection refused.", null);
                break;
            case HandshakeResult.Disabled:
                Fail("Login is disabled on the server.", () => ConnectAsync(ct));
                break;
            case HandshakeResult.UnsupportedProtocol:
                Fail("Launcher is outdated. Please update.", null);
                break;
            default:
                Fail("Could not connect to the server.", () => ConnectAsync(ct));
                break;
        }
    }

    private async Task LoginAsync(string login, string password, CancellationToken ct)
    {
        Set(LauncherStage.LoggingIn, "Logging in...");
        var result = await Client!.LoginAsync(login, password, ct);
        if (result is null)
        {
            Fail("Could not connect to the server.", () => ConnectAsync(ct));
            return;
        }
        switch (result.Status)
        {
            case LauncherLoginStatus.Ok:
                GameLogin = result;
                Set(LauncherStage.LoggedIn, "Starting the game...");
                break;
            case LauncherLoginStatus.InvalidCredentials:
                Set(LauncherStage.Ready, "Wrong ID or password.");
                break;
            case LauncherLoginStatus.TooManyAttempts:
                Set(LauncherStage.Ready, "Too many attempts. Try again later.");
                break;
            case LauncherLoginStatus.AccountUnavailable:
                Set(LauncherStage.Ready, "This account cannot play.");
                break;
            case LauncherLoginStatus.OutdatedLauncher:
                Fail("Launcher is outdated. Please update.", null);
                break;
            default:
                Set(LauncherStage.Ready, "Server error. Try again later.");
                break;
        }
    }

    private void Fail(string status, Func<Task>? retry)
    {
        _retry = retry;
        Set(LauncherStage.Failed, retry is null ? status : status + " (OK to retry)");
    }

    private void Set(LauncherStage stage, string status, PatchProgress? progress = null)
    {
        lock (_lock)
            _state = new State(stage, status, progress);
        Console.WriteLine($"[{stage}] {status}");
    }

    private sealed class ProgressSink(LauncherFlow flow) : IProgress<PatchProgress>
    {
        public void Report(PatchProgress p)
        {
            string status = p.Phase switch
            {
                PatchPhase.FetchingConfiguration => "Checking for updates...",
                PatchPhase.Downloading => $"Downloading Patch_{p.Version}.zip  {p.Done / 1048576.0:0.0} / {p.Total / 1048576.0:0.0} MB",
                _ => $"Applying Patch_{p.Version}...",
            };
            lock (flow._lock)
                flow._state = new State(LauncherStage.Patching, status, p);
        }
    }
}
