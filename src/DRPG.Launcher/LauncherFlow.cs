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
    LockedOut,   // too many login attempts: fields and OK locked
    Failed,      // message in the status box; retried by itself unless nothing can be retried
}

/// <summary>
/// The launcher sequence (docs/cliente/tela-login-launcher.md#fluxo-do-launcher): patch, then handshake, then login.
/// Patching and the handshake run in the background; the frame loop reads <see cref="Snapshot"/> each frame and never
/// waits on them.
/// </summary>
public sealed class LauncherFlow(
    LauncherSettings settings,
    Patcher patcher,
    Func<PatchConfiguration.LoginServerSection, LoginClient> createClient,
    Func<int, TimeSpan> retryDelay)
{
    /// <summary>Wait before the n-th automatic retry in a row (1-based): 5, 10, 20, then every 30 s.</summary>
    public static TimeSpan DefaultRetryDelay(int attempt) => attempt switch
    {
        1 => TimeSpan.FromSeconds(5),
        2 => TimeSpan.FromSeconds(10),
        3 => TimeSpan.FromSeconds(20),
        _ => TimeSpan.FromSeconds(30),
    };

    /// <summary>
    /// Immutable UI snapshot read each frame. <paramref name="Status"/> is for the login status box (empty = hidden);
    /// <paramref name="PatchStatus"/> and <paramref name="PatchFraction"/> stay visible in the left column even after
    /// patching finishes.
    /// </summary>
    public sealed record State(
        LauncherStage Stage,
        string Status,
        PatchProgress? Progress,
        string PatchStatus = "Checking for updates...",
        double PatchFraction = 0,
        bool PatchComplete = false,
        IReadOnlyList<NewsItem>? News = null,
        string PatchDetail = "",
        int StatusVersion = 0);

    private readonly Lock _lock = new();
    private State _state = new(LauncherStage.Patching, "Checking for updates...", null, "Checking for updates...", 0, false, [], "", 0);
    private PatchConfiguration? _configuration;
    private string _patchStatus = "Checking for updates...";
    private string _patchDetail = "";
    private string _patchDetailBase = "";
    private double _patchFraction;
    private bool _patchComplete;
    private int _statusVersion;
    private bool _newsSubscribed;
    private IReadOnlyList<NewsItem> _news = [];
    private Func<Task>? _retry;
    private string _failure = "";
    private int _failuresInARow;
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

    public void Start(CancellationToken ct)
    {
        if (!_newsSubscribed)
        {
            patcher.NewsLoaded += SetNews;
            _newsSubscribed = true;
        }
        _running = Run(() => PatchAsync(ct), null, ct);
    }

    private void SetNews(IReadOnlyList<NewsItem> news)
    {
        lock (_lock)
        {
            _news = news;
            _state = _state with { News = news };
        }
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
        _running = Run(() => LoginAsync(login, password, ct), () => ConnectAsync(ct), ct);
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
    /// state with its message (retrying <paramref name="onError"/>, or the same step). A failure that can be retried
    /// is retried by itself after <c>retryDelay</c>, with a countdown in the status box: nothing waits on a click,
    /// and the login panel stays disabled until the server answers.
    /// </summary>
    private async Task Run(Func<Task> step, Func<Task>? onError, CancellationToken ct)
    {
        var current = step;
        try
        {
            while (true)
            {
                try
                {
                    await current();
                }
                catch (Exception e) when (e is not OperationCanceledException)
                {
                    Fail($"Unexpected error: {e.Message}", onError ?? current);
                }
                onError = null;
                if (Snapshot.Stage != LauncherStage.Failed || _retry is null)
                {
                    if (Snapshot.Stage != LauncherStage.Failed)
                        _failuresInARow = 0;
                    return;
                }
                current = _retry;
                await CountdownAsync(retryDelay(++_failuresInARow), ct);
            }
        }
        catch (OperationCanceledException)
        {
            // launcher closing
        }
    }

    private async Task CountdownAsync(TimeSpan delay, CancellationToken ct)
    {
        var end = DateTime.UtcNow + delay;
        while (true)
        {
            var left = end - DateTime.UtcNow;
            if (left <= TimeSpan.Zero)
                return;
            int seconds = (int)Math.Ceiling(left.TotalSeconds);
            string text = $"{_failure} Retrying in {seconds} s...";
            if (!_patchComplete)
            {
                string detail = string.IsNullOrWhiteSpace(_patchDetailBase)
                    ? $"Retrying in {seconds} s..."
                    : $"{_patchDetailBase}\nRetrying in {seconds} s...";
                SetPatch(_patchStatus, _patchFraction, complete: false, detail: detail, keepDetailBase: true);
            }
            Set(LauncherStage.Failed, text, log: false);
            await Task.Delay(left < TimeSpan.FromSeconds(1) ? left : TimeSpan.FromSeconds(1), ct);
        }
    }

    private async Task PatchAsync(CancellationToken ct)
    {
        SetPatch("Checking for updates...", 0, complete: false);
        Set(LauncherStage.Patching, "Checking for updates...");
        var outcome = await patcher.RunAsync(settings.ConfigUrl, new ProgressSink(this), ct);
        switch (outcome)
        {
            case PatchOutcome.UpToDate up:
                _configuration = up.Configuration;
                if (up.News is not null)
                    _news = up.News;
                string doneText = up.InstalledVersion > 0
                    ? $"Up to date (v{up.InstalledVersion}) - Ready"
                    : "Up to date - Ready";
                SetPatch(doneText, 1.0, complete: true);
                await ConnectAsync(ct);
                break;
            case PatchOutcome.Maintenance m:
                if (m.News is not null)
                    _news = m.News;
                string title = string.IsNullOrWhiteSpace(m.Title) ? "Maintenance" : m.Title.Trim();
                string message = m.Message.Trim();
                SetPatch(title, 1.0, complete: false, detail: message);
                Fail(string.IsNullOrWhiteSpace(message) ? title : $"{title}: {message}", () => PatchAsync(ct));
                break;
            case PatchOutcome.NeedsReinstall r:
                if (r.News is not null)
                    _news = r.News;
                string old = $"Version {r.Installed} is too old (minimum {r.Minimum}). Please reinstall the game.";
                SetPatch("Reinstall required", 0, complete: false, detail: old);
                Fail(old, null);
                break;
            case PatchOutcome.Failed f:
                SetPatch("Update failed", 0, complete: false, detail: f.Reason);
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
                Set(LauncherStage.LockedOut, "Too many attempts. Try again later.");
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

    /// <summary><paramref name="retry"/> null: nothing to retry (the user must update or reinstall).</summary>
    private void Fail(string status, Func<Task>? retry)
    {
        _retry = retry;
        _failure = status;
        Set(LauncherStage.Failed, status);
    }

    private void SetPatch(string patchStatus, double fraction, bool complete, string detail = "", bool keepDetailBase = false)
    {
        lock (_lock)
        {
            _patchStatus = patchStatus;
            _patchDetail = detail;
            if (!keepDetailBase)
                _patchDetailBase = detail;
            _patchFraction = Math.Clamp(fraction, 0, 1);
            _patchComplete = complete;
        }
    }

    private void Set(LauncherStage stage, string status, bool log = true)
    {
        lock (_lock)
        {
            _statusVersion++;
            _state = new State(stage, status, null, _patchStatus, _patchFraction, _patchComplete, _news, _patchDetail, _statusVersion);
        }
        if (log)
            Console.WriteLine($"[{stage}] {status}");
    }

    private sealed class ProgressSink(LauncherFlow flow) : IProgress<PatchProgress>
    {
        public void Report(PatchProgress p)
        {
            string status = p.Phase switch
            {
                PatchPhase.FetchingConfiguration => "Checking for updates...",
                PatchPhase.Downloading => $"Downloading Patch_{p.Version}.zip ({p.Index + 1}/{p.Count})  {p.Done / 1048576.0:0.0} / {p.Total / 1048576.0:0.0} MB",
                _ => $"Applying Patch_{p.Version} ({p.Index + 1}/{p.Count})...",
            };
            lock (flow._lock)
            {
                flow._patchStatus = status;
                flow._patchDetail = "";
                flow._patchDetailBase = "";
                flow._patchFraction = p.Fraction;
                flow._patchComplete = false;
                flow._state = new State(LauncherStage.Patching, status, p, status, p.Fraction, false, flow._news, "", flow._statusVersion);
            }
        }
    }
}
