using DevAtlas.Models;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace DevAtlas.Services
{
    public class ProjectRunner
    {
        /// <summary>
        /// Checks if node_modules folder exists in the project directory
        /// </summary>
        public static bool HasNodeModules(string projectPath)
        {
            try
            {
                var nodeModulesPath = Path.Combine(projectPath, "node_modules");
                return Directory.Exists(nodeModulesPath);
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Runs npm install in the project directory
        /// </summary>
        public static async Task<bool> RunNpmInstallAsync(string projectPath, Action<string>? onOutput = null, Action<string>? onError = null)
        {
            try
            {
                var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "cmd.exe" : "/bin/bash",
                        Arguments = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                            ? "/c cd /d " + projectPath + " && npm install"
                            : $"-c \"cd '{projectPath}' && npm install\"",
                        WorkingDirectory = projectPath,
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        CreateNoWindow = false
                    }
                };

                process.OutputDataReceived += (s, e) => onOutput?.Invoke(e.Data ?? string.Empty);
                process.ErrorDataReceived += (s, e) => onError?.Invoke(e.Data ?? string.Empty);

                process.Start();
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();

                await process.WaitForExitAsync();
                return process.ExitCode == 0;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Gets all scripts from package.json
        /// </summary>
        public static List<NpmScript> GetAllScripts(string projectPath)
        {
            var scripts = new List<NpmScript>();

            try
            {
                var packageJsonPath = Path.Combine(projectPath, "package.json");
                if (!File.Exists(packageJsonPath))
                    return scripts;

                var content = File.ReadAllText(packageJsonPath);
                using JsonDocument doc = JsonDocument.Parse(content);
                var root = doc.RootElement;

                if (!root.TryGetProperty("scripts", out var scriptsElement))
                    return scripts;

                foreach (var prop in scriptsElement.EnumerateObject())
                {
                    scripts.Add(new NpmScript
                    {
                        Name = prop.Name,
                        Command = prop.Value.GetString() ?? string.Empty
                    });
                }
            }
            catch
            {
                // Return empty list on error
            }

            return scripts;
        }

        /// <summary>
        /// Checks if a project is a React project by analyzing package.json
        /// </summary>
        public static bool IsReactProject(string projectPath)
        {
            try
            {
                var packageJsonPath = Path.Combine(projectPath, "package.json");
                if (!File.Exists(packageJsonPath))
                    return false;

                var content = File.ReadAllText(packageJsonPath);
                using JsonDocument doc = JsonDocument.Parse(content);
                var root = doc.RootElement;

                // Check if React is in dependencies
                if (root.TryGetProperty("dependencies", out var dependencies))
                {
                    foreach (var prop in dependencies.EnumerateObject())
                    {
                        if (prop.Name.Equals("react", StringComparison.OrdinalIgnoreCase))
                            return true;
                    }
                }

                // Check if React is in devDependencies
                if (root.TryGetProperty("devDependencies", out var devDependencies))
                {
                    foreach (var prop in devDependencies.EnumerateObject())
                    {
                        if (prop.Name.Equals("react", StringComparison.OrdinalIgnoreCase))
                            return true;
                    }
                }

                return false;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Checks if a project is runnable (has package.json with start/dev/serve script)
        /// </summary>
        public static bool IsRunnableProject(string projectPath)
        {
            return GetStartCommand(projectPath) != null;
        }

        /// <summary>
        /// Gets the start command from package.json scripts
        /// </summary>
        public static string? GetStartCommand(string projectPath)
        {
            try
            {
                var packageJsonPath = Path.Combine(projectPath, "package.json");
                if (!File.Exists(packageJsonPath))
                    return null;

                var content = File.ReadAllText(packageJsonPath);
                using JsonDocument doc = JsonDocument.Parse(content);
                var root = doc.RootElement;

                if (!root.TryGetProperty("scripts", out var scripts))
                    return null;

                // Try to find start command in order of preference
                string[] scriptNames = { "dev", "start", "serve" };

                foreach (var scriptName in scriptNames)
                {
                    foreach (var prop in scripts.EnumerateObject())
                    {
                        if (prop.Name.Equals(scriptName, StringComparison.OrdinalIgnoreCase))
                        {
                            return prop.Name; // Return the script name, not its content
                        }
                    }
                }

                return null;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Starts the React development server in an external terminal window
        /// </summary>
        public static Process? StartDevServerWithOutput(string projectPath, string? command, Action<string> onOutput, Action<string> onError)
        {
            try
            {
                if (string.IsNullOrEmpty(command))
                    return null;

                // Build the full command
                string fullCommand = command;

                // On Windows, npm is often accessed via cmd.exe
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    // If command doesn't start with npm, yarn, pnpm, etc., assume npm
                    if (!command.StartsWith("npm", StringComparison.OrdinalIgnoreCase) &&
                        !command.StartsWith("yarn", StringComparison.OrdinalIgnoreCase) &&
                        !command.StartsWith("pnpm", StringComparison.OrdinalIgnoreCase))
                    {
                        fullCommand = $"npm run {command}";
                    }
                }

                var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "cmd.exe" : GetDefaultShell(),
                        Arguments = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                            ? $"/k \"cd /d \"{projectPath}\" && {fullCommand}\""
                            : $"-c \"cd '{projectPath}' && {fullCommand}\"",
                        UseShellExecute = true,
                        CreateNoWindow = false
                    }
                };

                process.Start();

                // Notify that the process has started
                onOutput?.Invoke($"External terminal started with command: {fullCommand}");

                return process;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Starts the React development server
        /// </summary>
        public static Process? StartDevServer(string projectPath, string? command)
        {
            try
            {
                if (string.IsNullOrEmpty(command))
                    return null;

                // Determine the shell to use based on OS
                string shell = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "cmd.exe" : GetDefaultShell();
                string shellArg = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "/c" : "-c";

                // Build the full command
                string fullCommand = command;

                // On Windows, npm is often accessed via cmd.exe
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    // If command doesn't start with npm, yarn, pnpm, etc., assume npm
                    if (!command.StartsWith("npm", StringComparison.OrdinalIgnoreCase) &&
                        !command.StartsWith("yarn", StringComparison.OrdinalIgnoreCase) &&
                        !command.StartsWith("pnpm", StringComparison.OrdinalIgnoreCase))
                    {
                        fullCommand = $"npm run {command}";
                    }
                }

                var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = shell,
                        Arguments = $"{shellArg} \"{fullCommand}\"",
                        WorkingDirectory = projectPath,
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        CreateNoWindow = false
                    }
                };

                process.Start();
                return process;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Opens a project in the specified code editor without leaving an idle terminal window
        /// </summary>
        public static void OpenInEditor(string editorPath, string projectPath)
        {
            try
            {
                string arguments = $"\"{projectPath}\"";

                // Use the full path to the editor executable with UseShellExecute = true
                // This launches the editor directly without opening a terminal window
                Process.Start(new ProcessStartInfo
                {
                    FileName = editorPath,
                    Arguments = arguments,
                    UseShellExecute = true
                });
            }
            catch
            {
                // Silently fail if editor cannot be opened
            }
        }

        private static string GetDefaultShell()
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                return "/bin/zsh";
            return "/bin/bash";
        }
    }
}
