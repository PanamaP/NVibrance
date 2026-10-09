using System.IO;
using NVibrance.Services;
using NVibrance.ViewModels;
using Xunit;

namespace NVibrance.Tests;

public class MainViewModelTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "NVibranceTests_" + Guid.NewGuid().ToString("N"));
    private readonly ProgramRegistry _registry;
    private readonly FakeVibranceService _vibrance = new();

    public MainViewModelTests()
    {
        Directory.CreateDirectory(_dir);
        _registry = new ProgramRegistry(Path.Combine(_dir, "profiles.json"));
    }

    public void Dispose()
    {
        _registry.Dispose();
        Directory.Delete(_dir, recursive: true);
    }

    private MainViewModel CreateWithProfiles(params string[] names)
    {
        foreach (var name in names)
            _registry.Add(new ProgramProfile(name, $@"C:\Games\{name}.exe", 70));

        return new MainViewModel(_registry, _vibrance);
    }

    [Fact]
    public void Entries_ListDesktopFirstAndSelectIt()
    {
        var vm = CreateWithProfiles("A", "B");

        Assert.Same(vm.Desktop, vm.Entries[0]);
        Assert.Equal(new[] { "A", "B" }, vm.Entries.OfType<ProgramProfile>().Select(p => p.Name));
        Assert.True(vm.IsDesktopSelected);
        Assert.True(vm.HasProfiles);
    }

    [Fact]
    public void HasProfiles_IsFalseWithOnlyDesktop()
    {
        var vm = CreateWithProfiles();

        Assert.False(vm.HasProfiles);
    }

    [Fact]
    public void DetailVibrance_OnDesktop_WritesToDriver()
    {
        var vm = CreateWithProfiles();

        vm.DetailVibrance = 65;

        Assert.Equal(new[] { 65 }, _vibrance.SetCalls);
        Assert.Equal(65, vm.Desktop.Vibrance);
    }

    [Fact]
    public void DetailVibrance_OnProfile_UpdatesProfileWithoutTouchingDriver()
    {
        var vm = CreateWithProfiles("A");
        var edited = 0;
        vm.ProfileEdited += (_, _) => edited++;
        vm.SelectedEntry = vm.Entries[1];

        vm.DetailVibrance = 90;

        Assert.Equal(90, ((ProgramProfile)vm.Entries[1]).Vibrance);
        Assert.Empty(_vibrance.SetCalls);
        Assert.Equal(1, edited);
    }

    [Fact]
    public void SelectingAProfile_IgnoresTheSliderWritingBackACoercedValue()
    {
        var vm = CreateWithProfiles("A");
        var profile = (ProgramProfile)vm.Entries[1];

        // a Slider whose range excludes the value coerces it and pushes the result back
        // through the binding while it is still handling the change notification
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MainViewModel.DetailVibrance))
                vm.DetailVibrance = 0;
        };

        vm.SelectedEntry = profile;

        Assert.Equal(70, profile.Vibrance);
    }

    [Fact]
    public void CapturedValue_IsTheDesktopValueOnlyWhileOverridden()
    {
        var state = new VibranceState();
        Assert.Null(state.CapturedValue);

        state.Capture(55);
        Assert.Equal(55, state.CapturedValue);

        state.Restore();
        Assert.Null(state.CapturedValue);
    }

    [Fact]
    public void DeleteSelectedProfile_SelectsTheRowThatTakesItsPlace()
    {
        var vm = CreateWithProfiles("A", "B", "C");
        vm.SelectedEntry = vm.Entries[2]; // B

        var removed = vm.DeleteSelectedProfile();

        Assert.Equal("B", removed!.Name);
        Assert.Equal("C", vm.SelectedProfile!.Name);
        Assert.Null(_registry.FindByExePath(@"C:\Games\B.exe"));
    }

    [Fact]
    public void DeleteSelectedProfile_LastRow_SelectsTheRowAbove()
    {
        var vm = CreateWithProfiles("A");
        vm.SelectedEntry = vm.Entries[1];

        vm.DeleteSelectedProfile();

        Assert.True(vm.IsDesktopSelected);
        Assert.False(vm.HasProfiles);
    }

    [Fact]
    public void DeleteSelectedProfile_OnDesktop_DoesNothing()
    {
        var vm = CreateWithProfiles("A");

        Assert.Null(vm.DeleteSelectedProfile());
        Assert.Equal(2, vm.Entries.Count);
    }

    [Fact]
    public void RestoreProfile_BringsBackNameAndVibranceAndSelectsIt()
    {
        var vm = CreateWithProfiles("A");
        vm.SelectedEntry = vm.Entries[1];
        vm.DetailVibrance = 95;
        var removed = vm.DeleteSelectedProfile()!;

        vm.RestoreProfile(removed);

        var restored = _registry.FindByExePath(@"C:\Games\A.exe");
        Assert.NotNull(restored);
        Assert.Equal(95, restored.Vibrance);
        Assert.Same(restored, vm.SelectedProfile);
    }

    [Fact]
    public void RestoreProfile_WhenReAddedMeanwhile_KeepsTheNewOne()
    {
        var vm = CreateWithProfiles("A");
        vm.SelectedEntry = vm.Entries[1];
        var removed = vm.DeleteSelectedProfile()!;
        vm.AddOrSelectProfile("A again", removed.ExecutablePath, 50);

        vm.RestoreProfile(removed);

        Assert.Single(vm.Entries.OfType<ProgramProfile>());
        Assert.Equal("A again", vm.SelectedProfile!.Name);
    }

    [Fact]
    public void RenameSelectedProfile_IgnoresBlankAndUnchangedNames()
    {
        var vm = CreateWithProfiles("A");
        var edited = 0;
        vm.ProfileEdited += (_, _) => edited++;
        vm.SelectedEntry = vm.Entries[1];

        vm.RenameSelectedProfile("   ");
        vm.RenameSelectedProfile(" A ");
        vm.RenameSelectedProfile("  Renamed ");

        Assert.Equal("Renamed", vm.SelectedProfile!.Name);
        Assert.Equal(1, edited);
    }

    private sealed class FakeVibranceService : IVibranceService
    {
        public List<int> SetCalls { get; } = new();

        public int GetCurrent() => 50;

        public void Set(int value) => SetCalls.Add(value);
    }
}
