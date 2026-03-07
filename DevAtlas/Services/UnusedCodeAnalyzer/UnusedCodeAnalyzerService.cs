using DevAtlas.Models;

namespace DevAtlas.Services.UnusedCodeAnalyzer
{
    /// <summary>
    /// Main coordinator for unused code analysis.
    /// Detects the primary language in a project and dispatches to the appropriate analyzer.
    /// </summary>
    public class UnusedCodeAnalyzerService
    {
        private readonly List<ILanguageAnalyzer> _analyzers;

        // Directories to skip during language detection
        private static readonly HashSet<string> SkipDirs = new(StringComparer.OrdinalIgnoreCase)
        {
            "node_modules", ".git", "bin", "obj", "build", "dist", ".next", ".nuxt",
            "Pods", ".build", "vendor", ".cache", "DerivedData", ".dart_tool",
            ".pub-cache", "android", "ios", "web", "linux", "macos", "windows"
        };

        public UnusedCodeAnalyzerService()
        {
            _analyzers = new List<ILanguageAnalyzer>
            {
                new CSharpUnusedAnalyzer(),
                new JavaScriptUnusedAnalyzer(),
                new SwiftUnusedAnalyzer(),
                new DartUnusedAnalyzer()
            };
        }

        /// <summary>
        /// Analyze a project asynchronously for unused code
        /// </summary>
        public async Task<List<UnusedCodeResult>> AnalyzeAsync(string projectPath, CancellationToken cancellationToken = default)
        {
            return await Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();

                var detectedLanguage = DetectPrimaryLanguage(projectPath);

                // Find appropriate analyzer
                foreach (var analyzer in _analyzers)
                {
                    if (string.Equals(analyzer.LanguageName, detectedLanguage, StringComparison.OrdinalIgnoreCase))
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        return analyzer.Analyze(projectPath);
                    }
                }

                // Default to C# if no match
                var csharpAnalyzer = _analyzers.FirstOrDefault(a => a is CSharpUnusedAnalyzer);
                if (csharpAnalyzer != null)
                    return csharpAnalyzer.Analyze(projectPath);

                return new List<UnusedCodeResult>();
            }, cancellationToken);
        }

        /// <summary>
        /// Detect the primary programming language used in a project
        /// </summary>
        private string DetectPrimaryLanguage(string projectPath)
        {
            var extensionCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            if (!Directory.Exists(projectPath))
                return "C#";

            var stack = new Stack<string>();
            stack.Push(projectPath);

            while (stack.Count > 0)
            {
                var dir = stack.Pop();
                try
                {
                    foreach (var file in Directory.GetFiles(dir))
                    {
                        var ext = Path.GetExtension(file).ToLowerInvariant();
                        switch (ext)
                        {
                            case ".swift":
                                extensionCounts["Swift"] = extensionCounts.GetValueOrDefault("Swift") + 1;
                                break;
                            case ".cs":
                                extensionCounts["C#"] = extensionCounts.GetValueOrDefault("C#") + 1;
                                break;
                            case ".js":
                            case ".jsx":
                            case ".ts":
                            case ".tsx":
                            case ".mjs":
                            case ".cjs":
                                extensionCounts["JavaScript"] = extensionCounts.GetValueOrDefault("JavaScript") + 1;
                                break;
                            case ".dart":
                                extensionCounts["Dart"] = extensionCounts.GetValueOrDefault("Dart") + 1;
                                break;
                        }
                    }

                    foreach (var subDir in Directory.GetDirectories(dir))
                    {
                        var dirName = Path.GetFileName(subDir);
                        if (!SkipDirs.Contains(dirName) && !dirName.StartsWith("."))
                            stack.Push(subDir);
                    }
                }
                catch { }
            }

            if (extensionCounts.Count > 0)
            {
                return extensionCounts.OrderByDescending(kvp => kvp.Value).First().Key;
            }

            return "C#";
        }
    }
}
