using System.Diagnostics;
using System.Runtime.InteropServices;

namespace DevAtlas.Services;

public static class PlatformHelper
{
    private static (string command, string dirArg)? _cachedLinuxTerminal;

    private static readonly (string command, string dirArg)[] _linuxTerminals =
    {
        ("gnome-terminal", "--working-directory="),
        ("konsole", "--workdir "),
        ("xfce4-terminal", "--working-directory="),
        ("mate-terminal", "--working-directory="),
        ("tilix", "--working-directory="),
        ("terminator", "--working-directory="),
        ("alacritty", "--working-directory "),
        ("kitty", "--directory "),
        ("wezterm", "start --cwd "),
        ("lxterminal", "--working-directory="),
        ("sakura", "--working-directory="),
        ("xterm", "-e cd "),
    };

    public static string GetTerminalCommand()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return "cmd.exe";
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            return "open -a Terminal";
        }

        return DetectLinuxTerminal().command;
    }

    public static void OpenTerminal(string? workingDirectory)
    {
        if (string.IsNullOrWhiteSpace(workingDirectory))
        {
            return;
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            Process.Start(new ProcessStartInfo("cmd.exe")
            {
                WorkingDirectory = workingDirectory,
                UseShellExecute = true,
            });
            return;
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            Process.Start(new ProcessStartInfo("open", $"-a Terminal \"{workingDirectory}\"")
            {
                UseShellExecute = false,
            });
            return;
        }

        var (terminal, dirArg) = DetectLinuxTerminal();
        var arguments = $"{dirArg}\"{workingDirectory}\"";
        Process.Start(new ProcessStartInfo(terminal, arguments)
        {
            UseShellExecute = false,
        });
    }

    private static (string command, string dirArg) DetectLinuxTerminal()
    {
        if (_cachedLinuxTerminal.HasValue)
        {
            return _cachedLinuxTerminal.Value;
        }

        foreach (var (command, dirArg) in _linuxTerminals)
        {
            try
            {
                using var process = Process.Start(new ProcessStartInfo("which", command)
                {
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                });
                var output = process?.StandardOutput.ReadToEnd();
                process?.WaitForExit(2000);
                if (process?.ExitCode == 0 && !string.IsNullOrWhiteSpace(output))
                {
                    _cachedLinuxTerminal = (command, dirArg);
                    return (command, dirArg);
                }
            }
            catch
            {
            }
        }

        _cachedLinuxTerminal = ("xterm", "-e cd ");
        return _cachedLinuxTerminal.Value;
    }

    public static string GetFileManagerCommand()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return "explorer.exe";
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            return "open";
        }

        return "xdg-open";
    }

    public static void OpenInFileBrowser(string path)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            Process.Start(new ProcessStartInfo("explorer.exe", path) { UseShellExecute = true });
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            Process.Start(new ProcessStartInfo("open", path));
        }
        else
        {
            Process.Start(new ProcessStartInfo("xdg-open", path));
        }
    }
}