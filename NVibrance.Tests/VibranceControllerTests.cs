using System.IO;
using NVibrance.Focus;
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

    private readonly string _tempDir;
    private readonly ProgramRegistry _registry;
    private readonly FakeVibranceService _vibrance = new();
    private readonly Dictionary<uint, string?> _paths = new();
    private readonly Dictionary<uint, string?> _names = new();
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
            getForeground: () => _foreground);

    private void AddApexProfile(int vibrance = 80)
        => _registry.Add(new ProgramProfile("Apex", ApexPath, vibrance));

    [Fact]
    public void ProfiledWindowFocused_CapturesAndApplies()
    {
        AddApexProfile();
        _paths[GamePid] = ApexPath;
        using var controller = CreateController();

        controller.Evaluate(GameHwnd, GamePid);

        Assert.Equal(new[] { 80 }, _vibrance.SetCalls);
        Assert.Equal(80, _vibrance.Current);
    }

    [Fact]
    public void AlreadyAtTarget_DoesNotCaptureOrSet()
    {
        AddApexProfile();
        _paths[GamePid] = ApexPath;
        _vibrance.Current = 80;
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
        _vibrance.Current = 55;
        using var controller = CreateController();

        controller.Evaluate(GameHwnd, GamePid);
        controller.Evaluate(OtherHwnd, OtherPid);

        Assert.Equal(new[] { 80, 55 }, _vibrance.SetCalls);
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

        Assert.Equal(new[] { 80 }, _vibrance.SetCalls);
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

        Assert.Equal(new[] { 80 }, _vibrance.SetCalls);
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
        _vibrance.Current = 55;
        _foreground = (GameHwnd, GamePid);
        using var controller = CreateController();

        controller.Evaluate(GameHwnd, GamePid);
        _registry.RemoveByExePath(ApexPath); // triggers re-evaluation of the foreground

        Assert.Equal(new[] { 80, 55 }, _vibrance.SetCalls);
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

        Assert.Equal(new[] { 80 }, _vibrance.SetCalls);
    }

    [Fact]
    public void VibranceValueChangedWhileFocused_AppliesNewValue()
    {
        var profile = new ProgramProfile("Apex", ApexPath, 80);
        _registry.Add(profile);
        _paths[GamePid] = ApexPath;
        _vibrance.Current = 55;
        _foreground = (GameHwnd, GamePid);
        using var controller = CreateController();

        controller.Evaluate(GameHwnd, GamePid);
        profile.Vibrance = 90; // triggers re-evaluation

        Assert.Equal(new[] { 80, 90 }, _vibrance.SetCalls);
        // original desktop value survives the profile edit
        controller.Evaluate(OtherHwnd, OtherPid);
        Assert.Equal(55, _vibrance.SetCalls[^1]);
    }

    [Fact]
    public void ManualDesktopChange_WhileOverridden_UpdatesRestoreTarget()
    {
        AddApexProfile();
        _paths[GamePid] = ApexPath;
        _paths[OtherPid] = @"C:\Tools\other.exe";
        _vibrance.Current = 55;
        using var controller = CreateController();

        controller.Evaluate(GameHwnd, GamePid);
        controller.NotifyManualDesktopChange(60);
        controller.Evaluate(OtherHwnd, OtherPid);

        Assert.Equal(60, _vibrance.SetCalls[^1]);
    }

    [Fact]
    public void Dispose_RestoresWhileOverridden()
    {
        AddApexProfile();
        _paths[GamePid] = ApexPath;
        _vibrance.Current = 55;
        var controller = CreateController();

        controller.Evaluate(GameHwnd, GamePid);
        controller.Dispose();

        Assert.Equal(new[] { 80, 55 }, _vibrance.SetCalls);
    }

    [Fact]
    public void ZeroForegroundWindow_KeepsCurrentState()
    {
        // Secure desktop / UAC prompt: must not restore while the game is still running.
        AddApexProfile();
        _paths[GamePid] = ApexPath;
        _vibrance.Current = 55;
        _foreground = (IntPtr.Zero, 0);
        using var controller = CreateController();

        controller.Evaluate(GameHwnd, GamePid);
        controller.EvaluateCurrentForeground();

        Assert.Equal(new[] { 80 }, _vibrance.SetCalls);
    }

    private sealed class FakeVibranceService : IVibranceService
    {
        public int Current { get; set; } = 50;
        public List<int> SetCalls { get; } = new();
        public int GetCurrentCalls { get; private set; }

        public int GetCurrent()
        {
            GetCurrentCalls++;
            return Current;
        }

        public void Set(int value)
        {
            SetCalls.Add(value);
            Current = value;
        }
    }

    /// <summary>Runs the scheduled action synchronously, bypassing the debounce delay.</summary>
    private sealed class ImmediateScheduler : IDelayScheduler
    {
        public void Schedule(Action action) => action();

        public void Cancel() { }
    }
}
