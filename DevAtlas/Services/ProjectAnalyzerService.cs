using DevAtlas.Models;

namespace DevAtlas.Services
{
    /// <summary>
    /// Service that analyzes project source code files - counts lines, detects languages, etc.
    /// Excludes dependency/build directories and non-source files.
    /// </summary>
    public class ProjectAnalyzerService
    {
        // Directories to always exclude from analysis
        private static readonly HashSet<string> ExcludedDirectories = new(StringComparer.OrdinalIgnoreCase)
        {
            "node_modules",
            "bin",
            "obj",
            ".git",
            ".vs",
            ".vscode",
            ".idea",
            "packages",
            "dist",
            "build",
            "out",
            "output",
            "target",           // Java/Rust
            "vendor",           // Go/PHP
            "__pycache__",      // Python
            ".mypy_cache",
            ".pytest_cache",
            "venv",
            ".venv",
            "env",
            ".env",
            ".tox",
            "coverage",
            ".coverage",
            ".nyc_output",
            ".next",            // Next.js
            ".nuxt",            // Nuxt.js
            ".svelte-kit",
            ".angular",
            ".gradle",
            "gradle",
            ".dart_tool",
            ".pub-cache",
            "Pods",             // iOS CocoaPods
            "DerivedData",
            "cmake-build-debug",
            "cmake-build-release",
            "Debug",
            "Release",
            "x64",
            "x86",
            "TestResults",
            ".sass-cache",
            "bower_components",
            "jspm_packages",
            ".parcel-cache",
            ".cache",
            "tmp",
            "temp",
            "logs",
        };

        // File extensions to exclude (non-source, binary, config, data files)
        private static readonly HashSet<string> ExcludedExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".json",
            ".lock",
            ".sum",
            ".exe",
            ".dll",
            ".pdb",
            ".so",
            ".dylib",
            ".class",
            ".jar",
            ".war",
            ".ear",
            ".zip",
            ".tar",
            ".gz",
            ".rar",
            ".7z",
            ".png",
            ".jpg",
            ".jpeg",
            ".gif",
            ".bmp",
            ".ico",
            ".svg",
            ".webp",
            ".mp3",
            ".mp4",
            ".wav",
            ".avi",
            ".mov",
            ".ttf",
            ".woff",
            ".woff2",
            ".eot",
            ".otf",
            ".pdf",
            ".doc",
            ".docx",
            ".xls",
            ".xlsx",
            ".ppt",
            ".pptx",
            ".min.js",
            ".min.css",
            ".map",
            ".nupkg",
            ".snupkg",
            ".db",
            ".sqlite",
            ".sqlite3",
            ".mdf",
            ".ldf",
            ".suo",
            ".user",
            ".cache",
            ".log",
            ".DS_Store",
            ".pbxproj",
        };

        // Source file extensions we want to count, mapped to language names
        private static readonly Dictionary<string, string> ExtensionToLanguage = new(StringComparer.OrdinalIgnoreCase)
        {
            // C#
            { ".cs", "C#" },
            { ".csx", "C#" },
            // JavaScript/TypeScript
            { ".js", "JavaScript" },
            { ".jsx", "JavaScript" },
            { ".ts", "TypeScript" },
            { ".tsx", "TypeScript" },
            { ".mjs", "JavaScript" },
            { ".cjs", "JavaScript" },
            // Web
            { ".html", "HTML" },
            { ".htm", "HTML" },
            { ".css", "CSS" },
            { ".scss", "SCSS" },
            { ".sass", "SASS" },
            { ".less", "LESS" },
            // Python
            { ".py", "Python" },
            { ".pyw", "Python" },
            { ".pyi", "Python" },
            // Java
            { ".java", "Java" },
            { ".kt", "Kotlin" },
            { ".kts", "Kotlin" },
            // C/C++
            { ".c", "C" },
            { ".h", "C" },
            { ".cpp", "C++" },
            { ".hpp", "C++" },
            { ".cc", "C++" },
            { ".cxx", "C++" },
            // Go
            { ".go", "Go" },
            // Rust
            { ".rs", "Rust" },
            // Ruby
            { ".rb", "Ruby" },
            { ".erb", "Ruby" },
            // PHP
            { ".php", "PHP" },
            // Swift
            { ".swift", "Swift" },
            // Dart
            { ".dart", "Dart" },
            // Shell
            { ".sh", "Shell" },
            { ".bash", "Shell" },
            { ".zsh", "Shell" },
            { ".ps1", "PowerShell" },
            { ".psm1", "PowerShell" },
            // Config/Markup
            { ".xml", "XML" },
            { ".xaml", "XAML" },
            { ".yml", "YAML" },
            { ".yaml", "YAML" },
            { ".toml", "TOML" },
            // SQL
            { ".sql", "SQL" },
            // Markdown
            { ".md", "Markdown" },
            { ".mdx", "Markdown" },
            // Vue/Svelte
            { ".vue", "Vue" },
            { ".svelte", "Svelte" },
            // R
            { ".r", "R" }, // case-insensitive comparer covers .R
            // Lua
            { ".lua", "Lua" },
            // Scala
            { ".scala", "Scala" },
            // Elixir
            { ".ex", "Elixir" },
            { ".exs", "Elixir" },
            // Haskell
            { ".hs", "Haskell" },
            // Docker
            { ".dockerfile", "Dockerfile" },
            // Proto
            { ".proto", "Protobuf" },
            // GraphQL
            { ".graphql", "GraphQL" },
            { ".gql", "GraphQL" },
            // Razor
            { ".razor", "Razor" },
            { ".cshtml", "Razor" },
        };

        // Colors for language visualization
        private static readonly Dictionary<string, string> LanguageColors = new(StringComparer.OrdinalIgnoreCase)
        {
            { "C#", "#178600" },
            { "JavaScript", "#F1E05A" },
            { "TypeScript", "#3178C6" },
            { "HTML", "#E34C26" },
            { "CSS", "#563D7C" },
            { "SCSS", "#C6538C" },
            { "SASS", "#A2006D" },
            { "LESS", "#1D365D" },
            { "Python", "#3572A5" },
            { "Java", "#B07219" },
            { "Kotlin", "#A97BFF" },
            { "C", "#555555" },
            { "C++", "#F34B7D" },
            { "Go", "#00ADD8" },
            { "Rust", "#DEA584" },
            { "Ruby", "#701516" },
            { "PHP", "#4F5D95" },
            { "Swift", "#F05138" },
            { "Dart", "#00B4AB" },
            { "Shell", "#89E051" },
            { "PowerShell", "#012456" },
            { "XML", "#0060AC" },
            { "XAML", "#0C54C2" },
            { "YAML", "#CB171E" },
            { "SQL", "#E38C00" },
            { "Markdown", "#083FA1" },
            { "Vue", "#41B883" },
            { "Svelte", "#FF3E00" },
            { "Razor", "#512BD4" },
            { "Dockerfile", "#384D54" },
            { "TOML", "#9C4221" },
            { "R", "#198CE7" },
            { "Lua", "#000080" },
            { "Scala", "#C22D40" },
            { "Elixir", "#6E4A7E" },
            { "Haskell", "#5E5086" },
            { "Protobuf", "#5A5A5A" },
            { "GraphQL", "#E10098" },
        };

        /// <summary>
        /// Analyze a project directory and return line counts, language breakdown, etc.
        /// </summary>
        public async Task<ProjectAnalysisResult> AnalyzeProjectAsync(string projectPath, CancellationToken cancellationToken = default)
        {
            if (cancellationToken.IsCancellationRequested)
                return new ProjectAnalysisResult();

            return await Task.Run(() => AnalyzeProject(projectPath, cancellationToken));
        }

        public async Task<(int TotalFiles, int TotalLines)> AnalyzeProjectSummaryAsync(string projectPath, CancellationToken cancellationToken = default)
        {
            if (cancellationToken.IsCancellationRequested)
                return (0, 0);

            return await Task.Run(() => AnalyzeProjectSummary(projectPath, cancellationToken));
        }

        public async Task<List<string>> GetIncludedProjectFilesAsync(string projectPath, CancellationToken cancellationToken = default)
        {
            if (cancellationToken.IsCancellationRequested || !Directory.Exists(projectPath))
                return new List<string>();

            return await Task.Run(() => EnumerateIncludedProjectFiles(projectPath, cancellationToken).ToList(), cancellationToken);
        }

        private ProjectAnalysisResult AnalyzeProject(string projectPath, CancellationToken cancellationToken)
        {
            var result = new ProjectAnalysisResult();
            var languageStats = new Dictionary<string, (int files, int lines)>(StringComparer.OrdinalIgnoreCase);

            if (!Directory.Exists(projectPath))
                return result;

            var sourceFiles = EnumerateIncludedProjectFiles(projectPath, cancellationToken);

            int largestLines = 0;
            string largestFile = "";

            foreach (var filePath in sourceFiles)
            {
                if (cancellationToken.IsCancellationRequested)
                    break;

                try
                {
                    var ext = Path.GetExtension(filePath);
                    if (!ShouldAnalyzeFile(filePath, ext))
                        continue;

                    int lineCount = CountLines(filePath);
                    if (lineCount <= 0) continue;

                    string relativePath = GetRelativePath(projectPath, filePath);
                    string language = ExtensionToLanguage.TryGetValue(ext, out var lang) ? lang : ext.TrimStart('.').ToUpper();

                    result.Files.Add(new FileAnalysisInfo
                    {
                        RelativePath = relativePath,
                        Extension = ext.TrimStart('.').ToLower(),
                        Lines = lineCount
                    });

                    // Update language stats
                    if (languageStats.TryGetValue(language, out var stats))
                    {
                        languageStats[language] = (stats.files + 1, stats.lines + lineCount);
                    }
                    else
                    {
                        languageStats[language] = (1, lineCount);
                    }

                    result.TotalFiles++;
                    result.TotalLines += lineCount;

                    if (lineCount > largestLines)
                    {
                        largestLines = lineCount;
                        largestFile = relativePath;
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Error analyzing file {filePath}: {ex.Message}");
                }
            }

            result.LargestFileLines = largestLines;
            result.LargestFileName = largestFile;

            // Sort files by line count descending
            result.Files = result.Files.OrderByDescending(f => f.Lines).ToList();

            // Build language breakdown
            result.Languages = languageStats
                .Select(kvp => new LanguageBreakdown
                {
                    Name = kvp.Key,
                    Extension = kvp.Key,
                    FileCount = kvp.Value.files,
                    TotalLines = kvp.Value.lines,
                    Percentage = result.TotalLines > 0 ? Math.Round((double)kvp.Value.lines / result.TotalLines * 100, 1) : 0,
                    Color = LanguageColors.TryGetValue(kvp.Key, out var color) ? color : "#6B7280"
                })
                .OrderByDescending(l => l.TotalLines)
                .ToList();

            return result;
        }

        private (int TotalFiles, int TotalLines) AnalyzeProjectSummary(string projectPath, CancellationToken cancellationToken)
        {
            if (!Directory.Exists(projectPath))
                return (0, 0);

            int totalFiles = 0;
            int totalLines = 0;

            foreach (var filePath in EnumerateIncludedProjectFiles(projectPath, cancellationToken))
            {
                if (cancellationToken.IsCancellationRequested)
                    break;

                try
                {
                    var ext = Path.GetExtension(filePath);
                    if (!ShouldAnalyzeFile(filePath, ext))
                        continue;

                    var lineCount = CountLines(filePath);
                    if (lineCount <= 0)
                        continue;

                    totalFiles++;
                    totalLines += lineCount;
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Error analyzing file summary {filePath}: {ex.Message}");
                }
            }

            return (totalFiles, totalLines);
        }

        /// <summary>
        /// Get line counts per tag/language for display in the tech stack section
        /// </summary>
        public async Task<List<TechStackItem>> GetTechStackWithLinesAsync(string projectPath, List<string> tags, CancellationToken cancellationToken = default)
        {
            return await Task.Run(() =>
            {
                var items = new List<TechStackItem>();
                if (cancellationToken.IsCancellationRequested)
                    return tags.Select(t => new TechStackItem { Name = t, Lines = 0 }).ToList();

                if (!Directory.Exists(projectPath))
                    return tags.Select(t => new TechStackItem { Name = t, Lines = 0 }).ToList();

                var sourceFiles = EnumerateIncludedProjectFiles(projectPath, cancellationToken);

                // Map tags to their extensions
                var tagToExtensions = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
                foreach (var kvp in ExtensionToLanguage)
                {
                    if (!tagToExtensions.ContainsKey(kvp.Value))
                        tagToExtensions[kvp.Value] = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    tagToExtensions[kvp.Value].Add(kvp.Key);
                }

                // Framework/tech to extensions mapping
                var frameworkExtensions = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase)
                {
                    { "React", new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".jsx", ".tsx", ".js", ".ts" } },
                    { "Next.js", new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".jsx", ".tsx", ".js", ".ts" } },
                    { "Vue", new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".vue", ".js", ".ts" } },
                    { "Angular", new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".ts", ".js" } },
                    { "Svelte", new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".svelte", ".js", ".ts" } },
                    { "WPF", new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".cs", ".xaml" } },
                    { "WinForms", new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".cs" } },
                    { "ASP.NET Core", new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".cs", ".cshtml", ".razor" } },
                    { "Blazor", new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".razor", ".cs" } },
                    { "Flutter", new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".dart" } },
                    { "Spring Boot", new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".java", ".kt" } },
                    { "Django", new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".py" } },
                    { "Flask", new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".py" } },
                    { "Rails", new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".rb", ".erb" } },
                    { "Laravel", new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".php" } },
                    { "Express", new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".js", ".ts" } },
                    { "NestJS", new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".ts" } },
                    { "Node.js", new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".js", ".ts" } },
                    { "Tailwind CSS", new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".css", ".scss" } },
                    { "SASS", new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".scss", ".sass" } },
                    { "Docker", new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".dockerfile" } },
                };

                // Count lines per file extension (cache this)
                var extensionLineCount = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                foreach (var filePath in sourceFiles)
                {
                    if (cancellationToken.IsCancellationRequested)
                        return tags.Select(t => new TechStackItem { Name = t, Lines = 0 }).ToList();

                    try
                    {
                        var ext = Path.GetExtension(filePath);
                        if (!ShouldAnalyzeFile(filePath, ext)) continue;

                        int lineCount = CountLines(filePath);
                        if (lineCount <= 0) continue;

                        if (extensionLineCount.TryGetValue(ext, out var existing))
                            extensionLineCount[ext] = existing + lineCount;
                        else
                            extensionLineCount[ext] = lineCount;
                    }
                    catch { }
                }

                foreach (var tag in tags)
                {
                    if (cancellationToken.IsCancellationRequested)
                        return tags.Select(t => new TechStackItem { Name = t, Lines = 0 }).ToList();

                    int totalLines = 0;

                    // First check framework mappings
                    if (frameworkExtensions.TryGetValue(tag, out var fwExts))
                    {
                        foreach (var ext in fwExts)
                        {
                            if (extensionLineCount.TryGetValue(ext, out var count))
                                totalLines += count;
                        }
                    }
                    // Then check language mappings
                    else if (tagToExtensions.TryGetValue(tag, out var langExts))
                    {
                        foreach (var ext in langExts)
                        {
                            if (extensionLineCount.TryGetValue(ext, out var count))
                                totalLines += count;
                        }
                    }

                    items.Add(new TechStackItem { Name = tag, Lines = totalLines });
                }

                return items;
            });
        }

        private IEnumerable<string> EnumerateIncludedProjectFiles(string rootPath, CancellationToken cancellationToken)
        {
            var stack = new Stack<string>();
            stack.Push(rootPath);

            while (stack.Count > 0)
            {
                if (cancellationToken.IsCancellationRequested)
                    yield break;

                var dir = stack.Pop();
                string[] files;
                string[] subDirs;

                try
                {
                    files = Directory.GetFiles(dir);
                    subDirs = Directory.GetDirectories(dir);
                }
                catch (UnauthorizedAccessException) { continue; }
                catch (DirectoryNotFoundException) { continue; }
                catch (IOException) { continue; }

                foreach (var file in files)
                {
                    if (cancellationToken.IsCancellationRequested)
                        yield break;
                    yield return file;
                }

                foreach (var subDir in subDirs)
                {
                    if (cancellationToken.IsCancellationRequested)
                        yield break;

                    var dirName = Path.GetFileName(subDir);
                    if (!ExcludedDirectories.Contains(dirName) &&
                        !dirName.StartsWith('.'))
                    {
                        stack.Push(subDir);
                    }
                }
            }
        }

        private static bool ShouldAnalyzeFile(string filePath, string? extension)
        {
            if (string.IsNullOrEmpty(extension) || ExcludedExtensions.Contains(extension))
                return false;

            return !filePath.EndsWith(".min.js", StringComparison.OrdinalIgnoreCase) &&
                   !filePath.EndsWith(".min.css", StringComparison.OrdinalIgnoreCase);
        }

        private int CountLines(string filePath)
        {
            try
            {
                // Quick check: skip very large files (> 5MB) - likely generated
                var fileInfo = new FileInfo(filePath);
                if (fileInfo.Length > 5 * 1024 * 1024)
                    return 0;

                // Skip binary files by checking first few bytes
                if (IsBinaryFile(filePath))
                    return 0;

                int count = 0;
                using var reader = new StreamReader(filePath);
                while (reader.ReadLine() != null)
                    count++;
                return count;
            }
            catch
            {
                return 0;
            }
        }

        private bool IsBinaryFile(string filePath)
        {
            try
            {
                using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                var buffer = new byte[512];
                int bytesRead = stream.Read(buffer, 0, buffer.Length);
                for (int i = 0; i < bytesRead; i++)
                {
                    // If we find a null byte, it's likely binary
                    if (buffer[i] == 0)
                        return true;
                }
                return false;
            }
            catch
            {
                return true;
            }
        }

        private string GetRelativePath(string rootPath, string fullPath)
        {
            if (fullPath.StartsWith(rootPath, StringComparison.OrdinalIgnoreCase))
            {
                return fullPath.Substring(rootPath.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            }
            return fullPath;
        }
    }
}
