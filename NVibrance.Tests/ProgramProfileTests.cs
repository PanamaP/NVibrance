using Xunit;

namespace NVibrance.Tests;

public class ProgramProfileTests
{
    [Theory]
    [InlineData(@"C:\Games\Apex\r5apex.exe")]
    [InlineData(@"C:\GAMES\APEX\R5APEX.EXE")]
    [InlineData(@"c:\games\apex\r5apex.exe")]
    public void Matches_IsCaseInsensitive(string candidate)
    {
        var profile = new ProgramProfile("Apex", @"C:\Games\Apex\r5apex.exe", 80);

        Assert.True(profile.Matches(candidate));
    }

    [Fact]
    public void Matches_SameExeNameDifferentDirectory_DoesNotMatch()
    {
        var profile = new ProgramProfile("Apex", @"C:\Games\Apex\r5apex.exe", 80);

        Assert.False(profile.Matches(@"D:\Other\r5apex.exe"));
    }

    [Theory]
    [InlineData("r5apex")]
    [InlineData("R5APEX")]
    public void MatchesProcessName_IsCaseInsensitive(string processName)
    {
        var profile = new ProgramProfile("Apex", @"C:\Games\Apex\r5apex.exe", 80);

        Assert.True(profile.MatchesProcessName(processName));
    }

    [Fact]
    public void MatchesProcessName_DifferentName_DoesNotMatch()
    {
        var profile = new ProgramProfile("Apex", @"C:\Games\Apex\r5apex.exe", 80);

        Assert.False(profile.MatchesProcessName("notepad"));
    }
}
