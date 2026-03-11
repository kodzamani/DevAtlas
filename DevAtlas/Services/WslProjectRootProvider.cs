using System.Diagnostics;
using System.Text;

namespace DevAtlas.Services;

public readonly record struct ProjectScanRoot(string DisplayName, string RootPath, int MaxDepth = 100);

internal readonly record struct WslCommandResult(int ExitCode, string StandardOutput, string StandardError);
internal readonly record struct WslLinuxDirectory(string Name, string LinuxPath);

internal delegate Task<WslCommandResult> WslCommandRunner(IReadOnlyList<string> arguments, CancellationToken cancellationToken);

public sealed class WslProjectRootProvider
{
    private const string DefaultLinuxHomeRoot = "/home";
    private const int DefaultWslScanDepth = 8;
    private readonly WslCommandRunner _commandRunner;
    private readonly Func<string, bool> _directoryExists;
    private static readonly HashSet<string> IgnoredHomeDirectoryNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "Desktop",
        "Documents",
        "Downloads",
        "Music",
        "Pictures",
        "Public",
        "Templates",
        "Videos"
    };

    public WslProjectRootProvider()
        : this(RunWslCommandAsync, Directory.Exists)
    {
    }

    internal WslProjectRootProvider(WslCommandRunner commandRunner, Func<string, bool>? directoryExists = null)
    {
        _commandRunner = commandRunner;
        _directoryExists = directoryExists ?? Directory.Exists;
    }

    public async Task<IReadOnlyList<ProjectScanRoot>> GetScanRootsAsync(CancellationToken cancellationToken = default)
    {
        if (!OperatingSystem.IsWindows())
        {
            return [];
        }

        var listResult = await _commandRunner(["--list", "--quiet"], cancellationToken).ConfigureAwait(false);
        if (listResult.ExitCode != 0)
        {
            return [];
        }

        var distros = ParseDistroNames(listResult.StandardOutput);
        if (distros.Count == 0)
        {
            return [];
        }

        var roots = new List<ProjectScanRoot>(distros.Count);
        foreach (var distro in distros)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var linuxHomePath = await TryGetLinuxHomePathAsync(distro, cancellationToken).ConfigureAwait(false)
                ?? DefaultLinuxHomeRoot;

            var candidateDirectories = await TryGetHomeChildDirectoriesAsync(distro, cancellationToken).ConfigureAwait(false);
            if (candidateDirectories.Count == 0)
            {
                var uncHomePath = BuildUncPath(distro, linuxHomePath);
                if (_directoryExists(uncHomePath))
                {
                    roots.Add(new ProjectScanRoot($"WSL: {distro}", uncHomePath, DefaultWslScanDepth));
                }

                continue;
            }

            foreach (var directory in candidateDirectories)
            {
                var uncPath = BuildUncPath(distro, directory.LinuxPath);
                if (_directoryExists(uncPath))
                {
                    roots.Add(new ProjectScanRoot($"WSL: {distro}/{directory.Name}", uncPath, DefaultWslScanDepth));
                }
            }
        }

        return roots;
    }

    internal static IReadOnlyList<string> ParseDistroNames(string output)
    {
        if (string.IsNullOrWhiteSpace(output))
        {
            return [];
        }

        return output
            .Replace("\0", string.Empty, StringComparison.Ordinal)
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(name => name.Trim())
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    internal static string BuildUncPath(string distroName, string linuxPath)
    {
        if (string.IsNullOrWhiteSpace(distroName))
        {
            throw new ArgumentException("Distro name is required.", nameof(distroName));
        }

        var normalizedLinuxPath = string.IsNullOrWhiteSpace(linuxPath)
            ? DefaultLinuxHomeRoot
            : linuxPath.Trim().Replace('\\', '/');
        if (!normalizedLinuxPath.StartsWith('/'))
        {
            normalizedLinuxPath = "/" + normalizedLinuxPath;
        }

        if (normalizedLinuxPath.Length > 1)
        {
            normalizedLinuxPath = normalizedLinuxPath.TrimEnd('/');
        }

        return $@"\\wsl$\{distroName}{normalizedLinuxPath.Replace('/', '\\')}";
    }

    internal static IReadOnlyList<WslLinuxDirectory> ParseLinuxDirectories(string output)
    {
        if (string.IsNullOrWhiteSpace(output))
        {
            return [];
        }

        return output
            .Replace("\0", string.Empty, StringComparison.Ordinal)
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(line => line.Split('\t', 2, StringSplitOptions.TrimEntries))
            .Where(parts => parts.Length == 2)
            .Select(parts => new WslLinuxDirectory(parts[0], parts[1]))
            .Where(entry => ShouldIncludeHomeChild(entry.Name))
            .ToList();
    }

    private async Task<string?> TryGetLinuxHomePathAsync(string distroName, CancellationToken cancellationToken)
    {
        var result = await _commandRunner(
            ["-d", distroName, "--exec", "sh", "-lc", "printf %s \"$HOME\""],
            cancellationToken).ConfigureAwait(false);

        if (result.ExitCode != 0)
        {
            return null;
        }

        var homePath = result.StandardOutput.Replace("\0", string.Empty, StringComparison.Ordinal).Trim();
        return string.IsNullOrWhiteSpace(homePath) ? null : homePath;
    }

    private async Task<IReadOnlyList<WslLinuxDirectory>> TryGetHomeChildDirectoriesAsync(string distroName, CancellationToken cancellationToken)
    {
        const string command = "for dir in \"$HOME\"/*/; do [ -d \"$dir\" ] || continue; name=$(basename \"$dir\"); printf '%s\t%s\n' \"$name\" \"${dir%/}\"; done";

        var result = await _commandRunner(
            ["-d", distroName, "--exec", "sh", "-lc", command],
            cancellationToken).ConfigureAwait(false);

        if (result.ExitCode != 0)
        {
            return [];
        }

        return ParseLinuxDirectories(result.StandardOutput);
    }

    private static bool ShouldIncludeHomeChild(string directoryName)
    {
        if (string.IsNullOrWhiteSpace(directoryName))
        {
            return false;
        }

        if (directoryName.StartsWith(".", StringComparison.Ordinal))
        {
            return false;
        }

        return !IgnoredHomeDirectoryNames.Contains(directoryName);
    }

    private static async Task<WslCommandResult> RunWslCommandAsync(IReadOnlyList<string> arguments, CancellationToken cancellationToken)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo("wsl.exe")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };

        foreach (var argument in arguments)
        {
            process.StartInfo.ArgumentList.Add(argument);
        }

        try
        {
            if (!process.Start())
            {
                return new WslCommandResult(-1, string.Empty, "Failed to start wsl.exe.");
            }

            var standardOutputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var standardErrorTask = process.StandardError.ReadToEndAsync(cancellationToken);

            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);

            return new WslCommandResult(
                process.ExitCode,
                await standardOutputTask.ConfigureAwait(false),
                await standardErrorTask.ConfigureAwait(false));
        }
        catch (OperationCanceledException)
        {
            TryKillProcess(process);
            throw;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"WSL root discovery failed: {ex.Message}");
            return new WslCommandResult(-1, string.Empty, ex.Message);
        }
    }

    private static void TryKillProcess(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(true);
            }
        }
        catch
        {
        }
    }
}
