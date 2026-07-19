using System.IO;
using NVibrance.Focus;
using Xunit;

namespace NVibrance.Tests;

public class ProcessPathResolverTests
{
    [Fact]
    public void TryGetExecutablePath_OwnProcess_ReturnsOwnPath()
    {
        var pid = (uint)Environment.ProcessId;

        var path = ProcessPathResolver.TryGetExecutablePath(pid);

        Assert.NotNull(path);
        Assert.Equal(Environment.ProcessPath, path, ignoreCase: true);
    }

    [Fact]
    public void TryGetExecutablePath_InvalidPid_ReturnsNull()
    {
        Assert.Null(ProcessPathResolver.TryGetExecutablePath(0xFFFFFFF));
    }

    [Fact]
    public void TryGetExecutablePath_PidZero_ReturnsNull()
    {
        Assert.Null(ProcessPathResolver.TryGetExecutablePath(0));
    }

    [Fact]
    public void TryGetProcessName_OwnProcess_ReturnsName()
    {
        var pid = (uint)Environment.ProcessId;

        var name = ProcessPathResolver.TryGetProcessName(pid);

        Assert.NotNull(name);
        Assert.Equal(
            Path.GetFileNameWithoutExtension(Environment.ProcessPath),
            name,
            ignoreCase: true);
    }

    [Fact]
    public void TryGetProcessName_InvalidPid_ReturnsNull()
    {
        Assert.Null(ProcessPathResolver.TryGetProcessName(0xFFFFFFF));
    }
}
