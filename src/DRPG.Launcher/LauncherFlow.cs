using DRPG.Launcher.Login;
using DRPG.Launcher.Patching;

namespace DRPG.Launcher;

public enum LauncherStage
{
    Patching,    // Loading box with the patch bar
    Connecting,  // login panel shown but frozen, "Connecting to server..."
    Ready,       // login panel usable
    Failed,      // message in the status box; OK retries the failed step
}

/// <summary>
/// The launcher sequence (docs/cliente/tela-login-launcher.md#fluxo-do-launcher): patch, then handshake, then login.
/// Patching and the handshake run in the background; the frame loop reads <see cref="Snapshot"/> each frame and never
/// waits on them.
/// </summary>
public sealed class LauncherFlow(LauncherSettings settings, Patcher patcher, Func<PatchConfiguration.LoginServerSection, LoginClient> createClient)
{
    public sealed record State(LauncherStage Stage, string Status, PatchProgress? Progress);

    private readonly Lock _lock = new();
    private State _state = new(LauncherStage.Patching, "Checking for updates...", null);
    private PatchConfiguration? _configuration;
    private Func<Task>? _retry;
    private Task _running = Task.CompletedTask;

    public LoginClient? Client { get; private set; }
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

    /// <summary>
    /// Runs a background step so that nothing can leave the screen stuck: an unexpected exception becomes a Failed
    /// state with its message, retrying the same step.
    /// </summary>
    private async Task Guard(Func<Task> step)
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
            Fail($"Unexpected error: {e.Message}", step);
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
                Set(LauncherStage.Ready, settings.AllowInsecure ? "DEVELOPMENT MODE: server key not checked" : "Enter your ID and password.");
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
