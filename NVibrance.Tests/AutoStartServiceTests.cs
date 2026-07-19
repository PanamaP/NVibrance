using NVibrance.Services;
using Xunit;

namespace NVibrance.Tests;

/// <summary>
/// Covers the path-comparison logic only. The registry reads and writes around it are
/// left untested rather than pointed at the real HKCU hive.
/// </summary>
public class AutoStartServiceTests
{
    [Fact]
    public void BuildRunCommand_QuotesPathAndAddsMinimized()
    {
        var command = AutoStartService.BuildRunCommand(@"C:\Program Files\NVibrance\NVibrance.exe");

        Assert.Equal(@"""C:\Program Files\NVibrance\NVibrance.exe"" --minimized", command);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void BuildRunCommand_MissingPath_ReturnsNull(string? exePath)
    {
        Assert.Null(AutoStartService.BuildRunCommand(exePath));
    }

    private static readonly Func<string, bool> AllPathsExist = _ => true;
    private static readonly Func<string, bool> NoPathExists = _ => false;

    [Fact]
    public void NeedsRepair_IdenticalCommand_IsFalse()
    {
        var expected = AutoStartService.BuildRunCommand(@"C:\Apps\NVibrance.exe")!;

        Assert.False(AutoStartService.NeedsRepair(expected, expected, NoPathExists));
    }

    [Fact]
    public void NeedsRepair_RegisteredPathGone_IsTrue()
    {
        // the user moved the app folder: repair to where it runs from now
        var stored = AutoStartService.BuildRunCommand(@"C:\Old\NVibrance.exe")!;
        var expected = AutoStartService.BuildRunCommand(@"D:\New\NVibrance.exe")!;

        Assert.True(AutoStartService.NeedsRepair(stored, expected, NoPathExists));
    }

    [Fact]
    public void NeedsRepair_DifferentButValidPath_IsFalse()
    {
        // a second copy must not steal autostart from the install the user chose
        var stored = AutoStartService.BuildRunCommand(@"C:\Installed\NVibrance.exe")!;
        var expected = AutoStartService.BuildRunCommand(@"C:\Dev\bin\NVibrance.exe")!;

        Assert.False(AutoStartService.NeedsRepair(stored, expected, AllPathsExist));
    }

    [Fact]
    public void NeedsRepair_DifferentCasing_IsFalse()
    {
        // Windows paths are case-insensitive; casing alone must not trigger a rewrite
        var stored = @"""C:\APPS\NVIBRANCE.EXE"" --minimized";
        var expected = AutoStartService.BuildRunCommand(@"C:\Apps\NVibrance.exe")!;

        Assert.False(AutoStartService.NeedsRepair(stored, expected, NoPathExists));
    }

    [Fact]
    public void NeedsRepair_SurroundingWhitespace_IsFalse()
    {
        var expected = AutoStartService.BuildRunCommand(@"C:\Apps\NVibrance.exe")!;

        Assert.False(AutoStartService.NeedsRepair("  " + expected + "  ", expected, NoPathExists));
    }

    [Fact]
    public void NeedsRepair_UnparseablePath_IsTrue()
    {
        var expected = AutoStartService.BuildRunCommand(@"C:\Apps\NVibrance.exe")!;

        Assert.True(AutoStartService.NeedsRepair("\"unterminated", expected, AllPathsExist));
    }

    [Theory]
    [InlineData(@"""C:\Apps\NVibrance.exe"" --minimized", @"C:\Apps\NVibrance.exe")]
    [InlineData(@"""C:\Program Files\N V\app.exe""", @"C:\Program Files\N V\app.exe")]
    [InlineData(@"C:\Apps\NVibrance.exe --minimized", @"C:\Apps\NVibrance.exe")]
    [InlineData(@"C:\Apps\NVibrance.exe", @"C:\Apps\NVibrance.exe")]
    public void ExtractExePath_ParsesCommandLine(string stored, string expected)
    {
        Assert.Equal(expected, AutoStartService.ExtractExePath(stored));
    }
}
