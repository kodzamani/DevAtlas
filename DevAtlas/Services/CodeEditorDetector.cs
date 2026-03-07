using DevAtlas.Models;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace DevAtlas.Services
{
    /// <summary>
    /// Detects installed code editors on the system with optimized performance
    /// </summary>
    public class CodeEditorDetector
    {
        private static readonly string _localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        private static readonly string _homeDir = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        // Windows editor definitions
        private static readonly (string name, string displayName, string command, string[] paths, string icon)[] _windowsEditorDefinitions =
        {
            ("vscode", "VS Code", "code", new[] { Path.Combine(_localAppData, "Programs", "Microsoft VS Code", "Code.exe") }, "avares://DevAtlas/Assets/EditorIcons/vscode.png"),
            ("cursor", "Cursor", "cursor", new[] { Path.Combine(_localAppData, "Cursor", "Cursor.exe") }, "avares://DevAtlas/Assets/EditorIcons/cursor.png"),
            ("windsurf", "Windsurf", "windsurf", new[] { Path.Combine(_localAppData, "Windsurf", "Windsurf.exe") }, "avares://DevAtlas/Assets/EditorIcons/windsurf.png"),
            ("antigravity", "Antigravity", "antigravity", new[] { Path.Combine(_localAppData, "Antigravity", "Antigravity.exe") }, "avares://DevAtlas/Assets/EditorIcons/antigravity.png")
        };

        // Linux editor definitions with common install locations
        private static readonly (string name, string displayName, string command, string[] paths, string icon)[] _linuxEditorDefinitions =
        {
            ("vscode", "VS Code", "code", new[]
            {
                "/usr/bin/code",
                "/usr/share/code/code",
                "/snap/bin/code",
                Path.Combine(_homeDir, ".local", "bin", "code")
            }, "avares://DevAtlas/Assets/EditorIcons/vscode.png"),

            ("cursor", "Cursor", "cursor", new[]
            {
                "/usr/bin/cursor",
                Path.Combine(_homeDir, ".local", "bin", "cursor"),
                Path.Combine(_homeDir, "Applications", "cursor.AppImage"),
                Path.Combine(_homeDir, ".local", "share", "applications", "cursor.AppImage")
            }, "avares://DevAtlas/Assets/EditorIcons/cursor.png"),

            ("windsurf", "Windsurf", "windsurf", new[]
            {
                "/usr/bin/windsurf",
                Path.Combine(_homeDir, ".local", "bin", "windsurf"),
                "/snap/bin/windsurf"
            }, "avares://DevAtlas/Assets/EditorIcons/windsurf.png"),

            ("antigravity", "Antigravity", "antigravity", new[]
            {
                "/usr/bin/antigravity",
                Path.Combine(_homeDir, ".local", "bin", "antigravity"),
                "/snap/bin/antigravity"
            }, "avares://DevAtlas/Assets/EditorIcons/antigravity.png"),

            ("sublime", "Sublime Text", "subl", new[]
            {
                "/usr/bin/subl",
                "/snap/bin/subl",
                "/opt/sublime_text/sublime_text"
            }, "avares://DevAtlas/Assets/EditorIcons/vscode.png"),

            ("intellij", "IntelliJ IDEA", "idea", new[]
            {
                "/usr/bin/idea",
                "/snap/bin/intellij-idea-ultimate",
                "/snap/bin/intellij-idea-community",
                Path.Combine(_homeDir, ".local", "share", "JetBrains", "Toolbox", "scripts", "idea")
            }, "avares://DevAtlas/Assets/EditorIcons/vscode.png"),

            ("fleet", "Fleet", "fleet", new[]
            {
                "/usr/bin/fleet",
                Path.Combine(_homeDir, ".local", "share", "JetBrains", "Toolbox", "scripts", "fleet")
            }, "avares://DevAtlas/Assets/EditorIcons/vscode.png"),

            ("zed", "Zed", "zed", new[]
            {
                "/usr/bin/zed",
                Path.Combine(_homeDir, ".local", "bin", "zed")
            }, "avares://DevAtlas/Assets/EditorIcons/vscode.png")
        };

        private static readonly ConcurrentDictionary<string, string> _pathCache = new();

        private static (string name, string displayName, string command, string[] paths, string icon)[] GetEditorDefinitions()
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                return _linuxEditorDefinitions;
            return _windowsEditorDefinitions;
        }

        /// <summary>
        /// Detects all installed code editors with parallel processing
        /// </summary>
        public List<CodeEditor> DetectInstalledEditors()
        {
            return GetEditorDefinitions()
                .AsParallel()
                .Select(def => CreateCodeEditor(def.name, def.displayName, def.command, def.paths, def.icon))
                .ToList();
        }

        private CodeEditor CreateCodeEditor(string name, string displayName, string command, string[] specificPaths, string iconPath)
        {
            var fullPath = GetEditorFullPath(command, specificPaths);
            return new CodeEditor
            {
                Name = name,
                DisplayName = displayName,
                Command = command,
                FullPath = fullPath,
                IconPath = iconPath,
                IsInstalled = !string.IsNullOrEmpty(fullPath)
            };
        }

        private string GetEditorFullPath(string command, string[] specificPaths)
        {
            // Check each known path
            foreach (var path in specificPaths)
            {
                if (!string.IsNullOrEmpty(path) && File.Exists(path))
                    return path;
            }

            // Fall back to PATH lookup
            return _pathCache.GetOrAdd(command, cmd => GetCommandPathFromSystem(cmd) ?? string.Empty);
        }

        /// <summary>
        /// Finds the full path of a command using 'which' on Linux/macOS or 'where' on Windows
        /// </summary>
        private string? GetCommandPathFromSystem(string command)
        {
            try
            {
                var isWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
                using var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = isWindows ? "where" : "which",
                        Arguments = command,
                        RedirectStandardOutput = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    }
                };

                process.Start();
                var output = process.StandardOutput.ReadToEnd();
                process.WaitForExit(3000);

                return process.ExitCode == 0 && !string.IsNullOrWhiteSpace(output)
                    ? output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()
                    : null;
            }
            catch
            {
                return null;
            }
        }
    }
}

