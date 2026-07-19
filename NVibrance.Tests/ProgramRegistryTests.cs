using System.IO;
using Xunit;

namespace NVibrance.Tests;

public class ProgramRegistryTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _profilesPath;

    public ProgramRegistryTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "NVibranceTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _profilesPath = Path.Combine(_tempDir, "profiles.json");
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
    }

    [Fact]
    public void Add_ThenFindByExePath_ReturnsProfile()
    {
        using var registry = new ProgramRegistry(_profilesPath);

        registry.Add(new ProgramProfile("Apex", @"C:\Games\r5apex.exe", 80));

        var found = registry.FindByExePath(@"c:\games\R5APEX.exe");
        Assert.NotNull(found);
        Assert.Equal("Apex", found.Name);
    }

    [Fact]
    public void Add_DuplicatePath_IsIgnored()
    {
        using var registry = new ProgramRegistry(_profilesPath);

        registry.Add(new ProgramProfile("First", @"C:\Games\r5apex.exe", 80));
        registry.Add(new ProgramProfile("Second", @"c:\games\R5APEX.EXE", 90));

        Assert.Single(registry.GetProfiles());
        Assert.Equal("First", registry.GetProfiles()[0].Name);
    }

    [Fact]
    public void RemoveByExePath_RemovesProfile()
    {
        using var registry = new ProgramRegistry(_profilesPath);
        registry.Add(new ProgramProfile("Apex", @"C:\Games\r5apex.exe", 80));

        var removed = registry.RemoveByExePath(@"C:\Games\r5apex.exe");

        Assert.True(removed);
        Assert.Empty(registry.GetProfiles());
    }

    [Fact]
    public void FindByProcessName_MatchesBareName()
    {
        using var registry = new ProgramRegistry(_profilesPath);
        registry.Add(new ProgramProfile("Apex", @"C:\Games\r5apex.exe", 80));

        var found = registry.FindByProcessName("r5apex");

        Assert.NotNull(found);
        Assert.Equal("Apex", found.Name);
    }

    [Fact]
    public void SaveAndLoad_RoundTripsProfiles()
    {
        using (var registry = new ProgramRegistry(_profilesPath))
        {
            registry.Add(new ProgramProfile("Apex", @"C:\Games\r5apex.exe", 80));
            registry.Add(new ProgramProfile("Notepad", @"C:\Windows\notepad.exe", 65));
            registry.SaveToDisk();
        }

        using var reloaded = new ProgramRegistry(_profilesPath);
        reloaded.LoadFromDisk();

        var profiles = reloaded.GetProfiles();
        Assert.Equal(2, profiles.Count);
        Assert.Equal(80, reloaded.FindByExePath(@"C:\Games\r5apex.exe")!.Vibrance);
        Assert.Equal(65, reloaded.FindByExePath(@"C:\Windows\notepad.exe")!.Vibrance);
    }

    [Fact]
    public void LoadFromDisk_CorruptJson_DoesNotThrowAndKeepsInMemoryProfiles()
    {
        using var registry = new ProgramRegistry(_profilesPath);
        registry.Add(new ProgramProfile("Apex", @"C:\Games\r5apex.exe", 80));

        File.WriteAllText(_profilesPath, "{{{ not json");

        registry.LoadFromDisk();

        Assert.Single(registry.GetProfiles());
    }

    [Fact]
    public void LoadFromDisk_CorruptJson_CreatesBadBackup()
    {
        File.WriteAllText(_profilesPath, "{{{ not json");
        using var registry = new ProgramRegistry(_profilesPath);

        registry.LoadFromDisk();

        Assert.True(File.Exists(_profilesPath + ".bad"));
    }

    [Fact]
    public void LoadFromDisk_EntriesWithBlankNameOrPath_AreFiltered()
    {
        File.WriteAllText(_profilesPath,
            """
            [
              { "Name": "", "ExecutablePath": "C:\\a.exe", "Vibrance": 80 },
              { "Name": "NoPath", "ExecutablePath": "", "Vibrance": 80 },
              { "Name": "Valid", "ExecutablePath": "C:\\b.exe", "Vibrance": 70 }
            ]
            """);
        using var registry = new ProgramRegistry(_profilesPath);

        registry.LoadFromDisk();

        Assert.Single(registry.GetProfiles());
        Assert.Equal("Valid", registry.GetProfiles()[0].Name);
    }

    [Fact]
    public void SaveToDisk_FileLocked_DoesNotThrow()
    {
        using var registry = new ProgramRegistry(_profilesPath);
        registry.Add(new ProgramProfile("Apex", @"C:\Games\r5apex.exe", 80));
        registry.SaveToDisk();

        using var locker = new FileStream(_profilesPath, FileMode.Open, FileAccess.Read, FileShare.None);

        registry.SaveToDisk(); // must swallow and log, not throw
    }

    [Fact]
    public void Add_RaisesProfilesChanged()
    {
        using var registry = new ProgramRegistry(_profilesPath);
        var raised = 0;
        registry.ProfilesChanged += (_, _) => raised++;

        registry.Add(new ProgramProfile("Apex", @"C:\Games\r5apex.exe", 80));

        Assert.Equal(1, raised);
    }

    [Fact]
    public void Remove_RaisesProfilesChanged()
    {
        using var registry = new ProgramRegistry(_profilesPath);
        registry.Add(new ProgramProfile("Apex", @"C:\Games\r5apex.exe", 80));
        var raised = 0;
        registry.ProfilesChanged += (_, _) => raised++;

        registry.RemoveByExePath(@"C:\Games\r5apex.exe");

        Assert.Equal(1, raised);
    }

    [Fact]
    public void VibranceChange_RaisesProfilesChanged()
    {
        using var registry = new ProgramRegistry(_profilesPath);
        var profile = new ProgramProfile("Apex", @"C:\Games\r5apex.exe", 80);
        registry.Add(profile);
        var raised = 0;
        registry.ProfilesChanged += (_, _) => raised++;

        profile.Vibrance = 90;

        Assert.Equal(1, raised);
    }

    [Fact]
    public void NameChange_DoesNotRaiseProfilesChanged()
    {
        using var registry = new ProgramRegistry(_profilesPath);
        var profile = new ProgramProfile("Apex", @"C:\Games\r5apex.exe", 80);
        registry.Add(profile);
        var raised = 0;
        registry.ProfilesChanged += (_, _) => raised++;

        profile.Name = "Renamed";

        Assert.Equal(0, raised);
    }
}
