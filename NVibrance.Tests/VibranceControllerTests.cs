using System.IO;
using NVibrance.Focus;
using NVibrance.Nvidia;
using NVibrance.Services;
using Xunit;

namespace NVibrance.Tests;

public class VibranceControllerTests : IDisposable
{
    private const string ApexPath = @"C:\Games\Apex\r5apex.exe";
    private static readonly IntPtr GameHwnd = new(0x1111);
    private static readonly IntPtr OtherHwnd = new(0x2222);
    private const uint GamePid = 100;
    private const uint OtherPid = 200;
    private static readonly DisplayId Primary = new(@"\\.\DISPLAY1");
    private static readonly DisplayId Secondary = new(@"\\.\DISPLAY2");

    private readonly string _tempDir;
    private readonly ProgramRegistry _registry;
    private readonly FakeVibranceService _vibrance = new();
    private readonly Dictionary<uint, string?> _paths = new();
    private readonly Dictionary<uint, string?> _names = new();
    private readonly Dictionary<IntPtr, DisplayId> _monitors = new();
    private (IntPtr Hwnd, uint Pid) _foreground = (IntPtr.Zero, 0);

    public VibranceControllerTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "NVibranceTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _registry = new ProgramRegistry(Path.Combine(_tempDir, "profiles.json"));
    }

    public void Dispose()
    {
        _registry.Dispose();
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
    }

    private VibranceController CreateController()
        => new(
            hook: null,
            registry: _registry,
            vibrance: _vibrance,
            debounce: new ImmediateScheduler(),
            resolveExePath: pid => _paths.GetValueOrDefault(pid),
            resolveProcessName: pid => _names.GetValueOrDefault(pid),
            getForeground: () => _foreground,
            resolveDisplay: hwnd => _monitors.GetValueOrDefault(hwnd, Primary));

    private void AddApexProfile(int vibrance = 80)
        => _registry.Add(new ProgramProfile("Apex", ApexPath, vibrance));

    [Fact]
    public void ProfiledWindowFocused_CapturesAndApplies()
    {
        AddApexProfile();
        _paths[GamePid] = ApexPath;
        using var controller = CreateController();

        controller.Evaluate(GameHwnd, GamePid);

        Assert.Equal(new[] { (Primary, 80) }, _vibrance.SetCalls);
        Assert.Equal(80, _vibrance.Values[Primary]);
    }

    [Fact]
    public void ActiveProfile_TracksWhatIsApplied()
    {
        AddApexProfile();
        _paths[GamePid] = ApexPath;
        _paths[OtherPid] = @"C:\Windows\explorer.exe";
        using var controller = CreateController();
        var changes = 0;
        controller.StatusChanged += (_, _) => changes++;

        controller.Evaluate(GameHwnd, GamePid);
        Assert.Equal("Apex", controller.ActiveProfile?.Name);

        controller.Evaluate(OtherHwnd, OtherPid);
        Assert.Null(controller.ActiveProfile);
        Assert.Equal(2, changes);
    }

    [Fact]
    public void Pause_RestoresDesktopAndStopsSwitching()
    {
        AddApexProfile();
        _paths[GamePid] = ApexPath;
        _foreground = (GameHwnd, GamePid);
        using var controller = CreateController();
        controller.Evaluate(GameHwnd, GamePid);

        controller.IsPaused = true;
        controller.Evaluate(GameHwnd, GamePid);

        Assert.Equal(new[] { (Primary, 80), (Primary, 50) }, _vibrance.SetCalls);
        Assert.Null(controller.ActiveProfile);
    }

    [Fact]
    public void Resume_ReappliesForTheFocusedWindow()
    {
        AddApexProfile();
        _paths[GamePid] = ApexPath;
        _foreground = (GameHwnd, GamePid);
        using var controller = CreateController();
        controller.Evaluate(GameHwnd, GamePid);
        controller.IsPaused = true;

        controller.IsPaused = false;

        Assert.Equal(new[] { (Primary, 80), (Primary, 50), (Primary, 80) }, _vibrance.SetCalls);
        Assert.Equal("Apex", controller.ActiveProfile?.Name);
    }

    [Fact]
    public void ProfileChangesWhilePaused_DoNotApply()
    {
        AddApexProfile();
        _paths[GamePid] = ApexPath;
        _foreground = (GameHwnd, GamePid);
        using var controller = CreateController();
        controller.IsPaused = true;

        _registry.FindByExePath(ApexPath)!.Vibrance = 90;

        Assert.Empty(_vibrance.SetCalls);
    }

    [Fact]
    public void AlreadyAtTarget_DoesNotCaptureOrSet()
    {
        AddApexProfile();
        _paths[GamePid] = ApexPath;
        _vibrance.Values[Primary] = 80;
        using var controller = CreateController();

        controller.Evaluate(GameHwnd, GamePid);

        Assert.Empty(_vibrance.SetCalls);
        Assert.Null(controller.State.Restore()); // nothing was captured
    }

    [Fact]
    public void UnknownWindowAfterProfile_RestoresCapturedValue()
    {
        AddApexProfile();
        _paths[GamePid] = ApexPath;
        _paths[OtherPid] = @"C:\Tools\other.exe";
        _vibrance.Values[Primary] = 55;
        using var controller = CreateController();

        controller.Evaluate(GameHwnd, GamePid);
        controller.Evaluate(OtherHwnd, OtherPid);

        Assert.Equal(new[] { (Primary, 80), (Primary, 55) }, _vibrance.SetCalls);
    }

    [Fact]
    public void UnknownWindow_WithoutOverride_DoesNotTouchDriver()
    {
        _paths[OtherPid] = @"C:\Tools\other.exe";
        using var controller = CreateController();

        controller.Evaluate(OtherHwnd, OtherPid);

        Assert.Empty(_vibrance.SetCalls);
    }

    [Fact]
    public void NullResolution_IsNotCached_SecondEvaluateApplies()
    {
        // Regression test for the Apex bug: a transient failed path resolution for a
        // window must not suppress re-evaluation of the same window.
        AddApexProfile();
        _paths[GamePid] = null;
        using var controller = CreateController();

        controller.Evaluate(GameHwnd, GamePid);
        Assert.Empty(_vibrance.SetCalls);

        _paths[GamePid] = ApexPath; // resolution now succeeds for the same hwnd/pid
        controller.Evaluate(GameHwnd, GamePid);

        Assert.Equal(new[] { (Primary, 80) }, _vibrance.SetCalls);
    }

    [Fact]
    public void SameWindowSamePath_EvaluatedTwice_TouchesNvapiOnce()
    {
        AddApexProfile();
        _paths[GamePid] = ApexPath;
        using var controller = CreateController();

        controller.Evaluate(GameHwnd, GamePid);
        controller.Evaluate(GameHwnd, GamePid);

        Assert.Equal(1, _vibrance.GetCurrentCalls);
        Assert.Single(_vibrance.SetCalls);
    }

    [Fact]
    public void PathUnavailable_MatchesByProcessName()
    {
        // Apex under EAC: path query denied, but the process name is readable.
        AddApexProfile();
        _paths[GamePid] = null;
        _names[GamePid] = "r5apex";
        using var controller = CreateController();

        controller.Evaluate(GameHwnd, GamePid);

        Assert.Equal(new[] { (Primary, 80) }, _vibrance.SetCalls);
    }

    [Fact]
    public void NameMatchedWindow_RepeatedPollTicks_TouchNvapiOnce()
    {
        AddApexProfile();
        _paths[GamePid] = null;
        _names[GamePid] = "r5apex";
        using var controller = CreateController();

        controller.Evaluate(GameHwnd, GamePid);
        controller.Evaluate(GameHwnd, GamePid);
        controller.Evaluate(GameHwnd, GamePid);

        Assert.Equal(1, _vibrance.GetCurrentCalls);
        Assert.Single(_vibrance.SetCalls);
    }

    [Fact]
    public void ProfileRemovedWhileFocused_RestoresOnProfilesChanged()
    {
        AddApexProfile();
        _paths[GamePid] = ApexPath;
        _vibrance.Values[Primary] = 55;
        _foreground = (GameHwnd, GamePid);
        using var controller = CreateController();

        controller.Evaluate(GameHwnd, GamePid);
        _registry.RemoveByExePath(ApexPath); // triggers re-evaluation of the foreground

        Assert.Equal(new[] { (Primary, 80), (Primary, 55) }, _vibrance.SetCalls);
    }

    [Fact]
    public void ProfileAddedWhileGameFocused_AppliesOnProfilesChanged()
    {
        _paths[GamePid] = ApexPath;
        _foreground = (GameHwnd, GamePid);
        using var controller = CreateController();

        controller.Evaluate(GameHwnd, GamePid); // no profile yet → nothing applied
        Assert.Empty(_vibrance.SetCalls);

        AddApexProfile(); // triggers re-evaluation of the foreground

        Assert.Equal(new[] { (Primary, 80) }, _vibrance.SetCalls);
    }

    [Fact]
    public void VibranceValueChangedWhileFocused_AppliesNewValue()
    {
        var profile = new ProgramProfile("Apex", ApexPath, 80);
        _registry.Add(profile);
        _paths[GamePid] = ApexPath;
        _vibrance.Values[Primary] = 55;
        _foreground = (GameHwnd, GamePid);
        using var controller = CreateController();

        controller.Evaluate(GameHwnd, GamePid);
        profile.Vibrance = 90; // triggers re-evaluation

        Assert.Equal(new[] { (Primary, 80), (Primary, 90) }, _vibrance.SetCalls);
        // original desktop value survives the profile edit
        controller.Evaluate(OtherHwnd, OtherPid);
        Assert.Equal((Primary, 55), _vibrance.SetCalls[^1]);
    }

    [Fact]
    public void ManualDesktopChange_WhileOverridden_UpdatesRestoreTarget()
    {
        AddApexProfile();
        _paths[GamePid] = ApexPath;
        _paths[OtherPid] = @"C:\Tools\other.exe";
        _vibrance.Values[Primary] = 55;
        using var controller = CreateController();

        controller.Evaluate(GameHwnd, GamePid);
        controller.NotifyManualDesktopChange(60);
        controller.Evaluate(OtherHwnd, OtherPid);

        Assert.Equal((Primary, 60), _vibrance.SetCalls[^1]);
    }

    [Fact]
    public void Dispose_RestoresWhileOverridden()
    {
        AddApexProfile();
        _paths[GamePid] = ApexPath;
        _vibrance.Values[Primary] = 55;
        var controller = CreateController();

        controller.Evaluate(GameHwnd, GamePid);
        controller.Dispose();

        Assert.Equal(new[] { (Primary, 80), (Primary, 55) }, _vibrance.SetCalls);
    }

    [Fact]
    public void ZeroForegroundWindow_KeepsCurrentState()
    {
        // Secure desktop / UAC prompt: must not restore while the game is still running.
        AddApexProfile();
        _paths[GamePid] = ApexPath;
        _vibrance.Values[Primary] = 55;
        _foreground = (IntPtr.Zero, 0);
        using var controller = CreateController();

        controller.Evaluate(GameHwnd, GamePid);
        controller.EvaluateCurrentForeground();

        Assert.Equal(new[] { (Primary, 80) }, _vibrance.SetCalls);
    }

    [Fact]
    public void GameOnSecondaryDisplay_ChangesOnlyTheSecondary()
    {
        AddApexProfile();
        _paths[GamePid] = ApexPath;
        _monitors[GameHwnd] = Secondary;
        using var controller = CreateController();

        controller.Evaluate(GameHwnd, GamePid);

        Assert.Equal(new[] { (Secondary, 80) }, _vibrance.SetCalls);
        Assert.Equal(50, _vibrance.Values[Primary]);
    }

    [Fact]
    public void LeavingGame_RestoresTheDisplayItWasAppliedOn()
    {
        AddApexProfile();
        _paths[GamePid] = ApexPath;
        _paths[OtherPid] = @"C:\Tools\other.exe";
        _monitors[GameHwnd] = Secondary;
        _vibrance.Values[Secondary] = 55;
        using var controller = CreateController();

        controller.Evaluate(GameHwnd, GamePid);
        controller.Evaluate(OtherHwnd, OtherPid);

        Assert.Equal(new[] { (Secondary, 80), (Secondary, 55) }, _vibrance.SetCalls);
    }

    [Fact]
    public void GameWindowMovedToAnotherDisplay_RestoresTheOldAndAppliesOnTheNew()
    {
        AddApexProfile();
        _paths[GamePid] = ApexPath;
        _vibrance.Values[Primary] = 55;
        _vibrance.Values[Secondary] = 60;
        using var controller = CreateController();
        controller.Evaluate(GameHwnd, GamePid);

        _monitors[GameHwnd] = Secondary;
        controller.Evaluate(GameHwnd, GamePid);

        Assert.Equal(new[] { (Primary, 80), (Primary, 55), (Secondary, 80) }, _vibrance.SetCalls);
        Assert.Equal(new ActiveOverride(Secondary, 60), controller.State.Current);
    }

    [Fact]
    public void SwitchingToAGameOnAnotherDisplay_RestoresEachDisplayToItsOwnValue()
    {
        const string otherGamePath = @"C:\Games\Other\other.exe";
        AddApexProfile();
        _registry.Add(new ProgramProfile("Other", otherGamePath, 90));
        _paths[GamePid] = ApexPath;
        _paths[OtherPid] = otherGamePath;
        _monitors[OtherHwnd] = Secondary;
        _vibrance.Values[Primary] = 55;
        _vibrance.Values[Secondary] = 60;
        var controller = CreateController();

        controller.Evaluate(GameHwnd, GamePid);
        controller.Evaluate(OtherHwnd, OtherPid);
        controller.Dispose();

        Assert.Equal(
            new[] { (Primary, 80), (Primary, 55), (Secondary, 90), (Secondary, 60) },
            _vibrance.SetCalls);
    }

    private sealed class FakeVibranceService : IVibranceService
    {
        public Dictionary<DisplayId, int> Values { get; } = new() { [Primary] = 50, [Secondary] = 50 };
        public List<(DisplayId Display, int Value)> SetCalls { get; } = new();
        public int GetCurrentCalls { get; private set; }

        public DisplayId PrimaryDisplay => Primary;

        public IReadOnlyList<DisplayId> GetDisplays() => Values.Keys.ToList();

        public int GetCurrent(DisplayId display)
        {
            GetCurrentCalls++;
            return Values[display];
        }

        public void Set(DisplayId display, int value)
        {
            SetCalls.Add((display, value));
            Values[display] = value;
        }
    }

    /// <summary>Runs the scheduled action synchronously, bypassing the debounce delay.</summary>
    private sealed class ImmediateScheduler : IDelayScheduler
    {
        public void Schedule(Action action) => action();

        public void Cancel() { }
    }
}
