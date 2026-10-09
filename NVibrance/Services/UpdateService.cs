using System.Windows.Threading;
using Velopack;
using Velopack.Sources;

namespace NVibrance.Services;

/// <summary>
/// Keeps an installed NVibrance current from GitHub Releases: checks quietly in the
/// background and downloads a new version, then offers "restart to update". Nothing is ever
/// installed without the user choosing that: a downloaded update just waits, across
/// restarts, until they do. Does nothing for development copies, which Velopack didn't install.
/// </summary>
public sealed class UpdateService : IDisposable
{
    private const string RepositoryUrl = "https://github.com/PanamaP/NVibrance";

    /// <summary>Overrides the release feed (a local folder or URL) to test a release before publishing it.</summary>
    private const string FeedOverrideVariable = "NVIBRANCE_UPDATE_FEED";

    private static readonly TimeSpan FirstCheckDelay = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan CheckInterval = TimeSpan.FromHours(6);

    private readonly UpdateManager? _manager;
    private readonly DispatcherTimer _timer = new();
    private bool _checking;
    private bool _restartScheduled;

    /// <summary>The downloaded update waiting for a restart, if any.</summary>
    public VelopackAsset? ReadyUpdate { get; private set; }

    /// <summary>The GitHub release page describing <see cref="ReadyUpdate"/>, so users can see what they're installing.</summary>
    public string? ReleaseNotesUrl => ReadyUpdate is null ? null : $"{RepositoryUrl}/releases/tag/v{ReadyUpdate.Version}";

    /// <summary>Raised on the UI thread once an update has been downloaded.</summary>
    public event EventHandler? UpdateReady;

    public UpdateService()
    {
        try
        {
            var feed = Environment.GetEnvironmentVariable(FeedOverrideVariable);
            var manager = string.IsNullOrWhiteSpace(feed)
                ? new UpdateManager(new GithubSource(RepositoryUrl, null, false))
                : new UpdateManager(feed);

            if (manager.IsInstalled)
                _manager = manager;
        }
        catch (Exception ex)
        {
            Log.Warn($"Updates unavailable: {ex.Message}");
        }
    }

    public void Start()
    {
        if (_manager is null)
            return;

        Log.Info($"NVibrance {_manager.CurrentVersion} installed; checking for updates every {CheckInterval.TotalHours:0} hours.");

        // a previous session may already have downloaded one
        if (_manager.UpdatePendingRestart is { } pending)
            SetReady(pending);

        // wait a little, so the check doesn't compete with startup (or a game launching at login)
        _timer.Interval = FirstCheckDelay;
        _timer.Tick += async (_, _) =>
        {
            _timer.Interval = CheckInterval;
            await CheckAsync();
        };
        _timer.Start();
    }

    private async Task CheckAsync()
    {
        if (_manager is null || _checking || ReadyUpdate is not null)
            return;

        _checking = true;
        try
        {
            var update = await _manager.CheckForUpdatesAsync();
            if (update is null)
                return;

            Log.Info($"Downloading update {update.TargetFullRelease.Version}.");
            await _manager.DownloadUpdatesAsync(update);
            SetReady(update.TargetFullRelease);
        }
        catch (Exception ex)
        {
            // offline, rate-limited, GitHub down: try again next interval
            Log.Debug($"Update check failed: {ex.Message}");
        }
        finally
        {
            _checking = false;
        }
    }

    private void SetReady(VelopackAsset update)
    {
        ReadyUpdate = update;
        Log.Info($"Update {update.Version} is ready.");
        UpdateReady?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Hands the update to Velopack's updater, which waits for NVibrance to exit, applies it
    /// and starts the new version. The caller then shuts down normally, so profiles are saved
    /// and the desktop vibrance restored first.
    /// </summary>
    public bool PrepareRestartToUpdate(bool startMinimized)
    {
        if (_manager is null || ReadyUpdate is null || _restartScheduled)
            return false;

        try
        {
            string[] restartArgs = startMinimized ? ["--minimized"] : [];
            _manager.WaitExitThenApplyUpdates(ReadyUpdate, silent: true, restart: true, restartArgs);
            _restartScheduled = true;
            return true;
        }
        catch (Exception ex)
        {
            Log.Error("Could not start the updater.", ex);
            return false;
        }
    }

    public void Dispose() => _timer.Stop();
}
