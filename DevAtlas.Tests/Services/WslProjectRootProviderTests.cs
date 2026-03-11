using DevAtlas.Services;

namespace DevAtlas.Tests.Services;

public class WslProjectRootProviderTests
{
    [Fact]
    public void ParseDistroNames_IgnoresBlankLinesAndNullTerminators()
    {
        var output = "Ubuntu\r\n\r\nDebian\0\r\n docker-desktop \r\n";

        var distros = WslProjectRootProvider.ParseDistroNames(output);

        Assert.Equal(["Ubuntu", "Debian", "docker-desktop"], distros);
    }

    [Fact]
    public void ParseDistroNames_HandlesUtf16StyleNullSeparatedOutput()
    {
        var output = string.Concat(
            "U", "\0", "b", "\0", "u", "\0", "n", "\0", "t", "\0", "u", "\0", "\r", "\0", "\n", "\0",
            "U", "\0", "b", "\0", "u", "\0", "n", "\0", "t", "\0", "u", "\0", "-", "\0", "2", "\0", "2", "\0", ".", "\0", "0", "\0", "4", "\0", "\r", "\0", "\n", "\0");

        var distros = WslProjectRootProvider.ParseDistroNames(output);

        Assert.Equal(["Ubuntu", "Ubuntu-22.04"], distros);
    }

    [Fact]
    public void BuildUncPath_MapsLinuxHomePathToWslShare()
    {
        var path = WslProjectRootProvider.BuildUncPath("Ubuntu-24.04", "/home/developer/projects");

        Assert.Equal(@"\\wsl$\Ubuntu-24.04\home\developer\projects", path);
    }

    [Fact]
    public async Task GetScanRootsAsync_ReturnsExistingHomeRootsForInstalledDistros()
    {
        static Task<WslCommandResult> RunAsync(IReadOnlyList<string> arguments, CancellationToken cancellationToken)
        {
            if (arguments.SequenceEqual(["--list", "--quiet"]))
            {
                return Task.FromResult(new WslCommandResult(0, "Ubuntu\r\nDebian\r\n", string.Empty));
            }

            if (arguments.Count >= 6 && arguments[0] == "-d" && arguments[1] == "Ubuntu" && arguments[^1].Contains("printf %s \"$HOME\"", StringComparison.Ordinal))
            {
                return Task.FromResult(new WslCommandResult(0, "/home/developer", string.Empty));
            }

            if (arguments.Count >= 6 && arguments[0] == "-d" && arguments[1] == "Debian" && arguments[^1].Contains("printf %s \"$HOME\"", StringComparison.Ordinal))
            {
                return Task.FromResult(new WslCommandResult(0, "/home/devuser", string.Empty));
            }

            if (arguments.Count >= 6 && arguments[0] == "-d" && arguments[1] == "Ubuntu" && arguments[^1].Contains("for dir in \"$HOME\"/*/", StringComparison.Ordinal))
            {
                return Task.FromResult(new WslCommandResult(0, ".cache\t/home/developer/.cache\nworkspace\t/home/developer/workspace\nprojects\t/home/developer/projects\nDownloads\t/home/developer/Downloads\n", string.Empty));
            }

            if (arguments.Count >= 6 && arguments[0] == "-d" && arguments[1] == "Debian" && arguments[^1].Contains("for dir in \"$HOME\"/*/", StringComparison.Ordinal))
            {
                return Task.FromResult(new WslCommandResult(0, "sandbox\t/home/devuser/sandbox\n", string.Empty));
            }

            return Task.FromResult(new WslCommandResult(1, string.Empty, "unexpected command"));
        }

        var provider = new WslProjectRootProvider(
            RunAsync,
            path =>
                path == @"\\wsl$\Ubuntu\home\developer\workspace" ||
                path == @"\\wsl$\Ubuntu\home\developer\projects" ||
                path == @"\\wsl$\Debian\home\devuser\sandbox");

        var roots = await provider.GetScanRootsAsync();

        Assert.Equal(
            [
                new ProjectScanRoot("WSL: Ubuntu/workspace", @"\\wsl$\Ubuntu\home\developer\workspace", 8),
                new ProjectScanRoot("WSL: Ubuntu/projects", @"\\wsl$\Ubuntu\home\developer\projects", 8),
                new ProjectScanRoot("WSL: Debian/sandbox", @"\\wsl$\Debian\home\devuser\sandbox", 8)
            ],
            roots);
    }

    [Fact]
    public async Task GetScanRootsAsync_FallsBackToHomeRootWhenNoChildrenAreReturned()
    {
        static Task<WslCommandResult> RunAsync(IReadOnlyList<string> arguments, CancellationToken cancellationToken)
        {
            if (arguments.SequenceEqual(["--list", "--quiet"]))
            {
                return Task.FromResult(new WslCommandResult(0, "Ubuntu\r\n", string.Empty));
            }

            if (arguments.Count >= 6 && arguments[0] == "-d" && arguments[1] == "Ubuntu" && arguments[^1].Contains("printf %s \"$HOME\"", StringComparison.Ordinal))
            {
                return Task.FromResult(new WslCommandResult(0, "/home/developer", string.Empty));
            }

            if (arguments.Count >= 6 && arguments[0] == "-d" && arguments[1] == "Ubuntu" && arguments[^1].Contains("for dir in \"$HOME\"/*/", StringComparison.Ordinal))
            {
                return Task.FromResult(new WslCommandResult(0, ".cache\t/home/developer/.cache\nDownloads\t/home/developer/Downloads\n", string.Empty));
            }

            return Task.FromResult(new WslCommandResult(1, string.Empty, "unexpected command"));
        }

        var provider = new WslProjectRootProvider(RunAsync, path => path == @"\\wsl$\Ubuntu\home\developer");

        var roots = await provider.GetScanRootsAsync();

        Assert.Equal([new ProjectScanRoot("WSL: Ubuntu", @"\\wsl$\Ubuntu\home\developer", 8)], roots);
    }
}
