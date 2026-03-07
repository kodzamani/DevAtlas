using DevAtlas.Models;
using System.Collections.Concurrent;

namespace DevAtlas.Services
{
    /// <summary>
    /// Optimized service that calculates and caches aggregate statistics for projects
    /// </summary>
    public class ProjectStatisticsService : IDisposable
    {
        private static readonly HashSet<string> ExcludedDirectories = new(StringComparer.OrdinalIgnoreCase)
        {
            "node_modules", "bin", "obj", ".git", ".vs", ".vscode", ".idea",
            "packages", "dist", "build", "out", "output", "target", "vendor",
            "__pycache__", ".mypy_cache", ".pytest_cache", "venv", ".venv",
            "env", ".env", ".tox", "coverage", ".coverage", ".nyc_output",
            ".next", ".nuxt", ".svelte-kit", ".angular", ".gradle", "gradle",
            ".dart_tool", ".pub-cache", "Pods", "DerivedData", ".json",
            "cmake-build-debug", "cmake-build-release", "Debug", "Release",
            "x64", "x86", "TestResults", ".sass-cache", "bower_components",
            "jspm_packages", ".parcel-cache", ".cache", "tmp", "temp", "logs"
        };

        private static readonly HashSet<string> SourceExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".cs", ".csx", ".js", ".jsx", ".ts", ".tsx", ".mjs", ".cjs",
            ".html", ".htm", ".css", ".scss", ".sass", ".less",
            ".py", ".pyw", ".pyi", ".java", ".kt", ".kts",
            ".c", ".h", ".cpp", ".hpp", ".cc", ".cxx",
            ".go", ".rs", ".rb", ".erb", ".php", ".swift", ".dart",
            ".sh", ".bash", ".zsh", ".ps1", ".psm1",
            ".xml", ".xaml", ".yml", ".yaml", ".toml", ".sql",
            ".md", ".mdx", ".vue", ".svelte", ".r", ".lua",
            ".scala", ".ex", ".exs", ".hs", ".dockerfile", ".proto",
            ".graphql", ".gql", ".razor", ".cshtml"
        };

        private readonly ConcurrentDictionary<string, (ProjectStatistics stats, DateTime[] modifiedTimes)> _statisticsCache = new();
        private readonly ConcurrentDictionary<string, bool> _binaryFileCache = new();
        private readonly ProjectAnalyzerService _analyzer;
        private CancellationTokenSource? _currentCalculationCancellationToken;
        private bool _disposed = false;

        public ProjectStatisticsService(ProjectAnalyzerService analyzer)
        {
            _analyzer = analyzer;
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    // Cancel any ongoing calculation
                    _currentCalculationCancellationToken?.Cancel();
                    _currentCalculationCancellationToken?.Dispose();

                    // Clear caches to release memory
                    _statisticsCache.Clear();
                    _binaryFileCache.Clear();
                }
                _disposed = true;
            }
        }

        /// <summary>
        /// Calculate statistics for a collection of projects asynchronously with optimized caching
        /// </summary>
        public async Task<(ProjectStatistics statistics, bool isFromCache)> CalculateStatisticsAsync(IEnumerable<ProjectInfo> projects, CancellationToken cancellationToken = default)
        {
            var projectPaths = projects.Select(p => p.Path).OrderBy(p => p).ToArray();
            var cacheKey = string.Join("|", projectPaths);

            if (_statisticsCache.TryGetValue(cacheKey, out var cachedEntry))
            {
                var (cachedStats, modifiedTimes) = cachedEntry;
                var currentModifiedTimes = GetModifiedTimes(projectPaths);

                if (!modifiedTimes.Except(currentModifiedTimes).Any())
                    return (cachedStats, true); // Data is from cache
            }

            _currentCalculationCancellationToken?.Cancel();
            _currentCalculationCancellationToken?.Dispose();
            _currentCalculationCancellationToken = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

            try
            {
                var statistics = await CalculateStatisticsInternalAsync(projectPaths, _currentCalculationCancellationToken.Token);
                var modifiedTimes = GetModifiedTimes(projectPaths);
                _statisticsCache[cacheKey] = (statistics, modifiedTimes);
                return (statistics, false); // Data is freshly calculated
            }
            catch (OperationCanceledException)
            {
                return (new ProjectStatistics(), false);
            }
        }

        private DateTime[] GetModifiedTimes(string[] projectPaths)
        {
            return projectPaths.Select(path =>
            {
                try { return Directory.GetLastWriteTime(path); }
                catch { return DateTime.MinValue; }
            }).ToArray();
        }

        private async Task<ProjectStatistics> CalculateStatisticsInternalAsync(string[] projectPaths, CancellationToken cancellationToken)
        {
            var statistics = new ProjectStatistics { CalculatedAt = DateTime.Now };
            statistics.ProjectCount = projectPaths.Length;

            var tasks = projectPaths.Select(async path =>
            {
                try
                {
                    return await GetProjectStatisticsAsync(path, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    return new ProjectStatisticsData { FileCount = 0, LinesCount = 0 };
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Error calculating statistics for {path}: {ex.Message}");
                    return new ProjectStatisticsData { FileCount = 0, LinesCount = 0 };
                }
            });

            var results = await Task.WhenAll(tasks);
            statistics.FileCount = results.Sum(r => r.FileCount);
            statistics.LinesCount = results.Sum(r => r.LinesCount);

            return statistics;
        }

        private async Task<ProjectStatisticsData> GetProjectStatisticsAsync(string projectPath, CancellationToken cancellationToken)
        {
            return await Task.Run(() =>
            {
                if (cancellationToken.IsCancellationRequested || !Directory.Exists(projectPath))
                    return new ProjectStatisticsData { FileCount = 0, LinesCount = 0 };

                long fileCount = 0, linesCount = 0;

                try
                {
                    foreach (var file in EnumerateFilesOptimized(projectPath, cancellationToken))
                    {
                        if (cancellationToken.IsCancellationRequested)
                            return new ProjectStatisticsData { FileCount = 0, LinesCount = 0 };

                        fileCount++;
                        if (IsSourceFile(file))
                            linesCount += CountLinesOptimized(file);
                    }
                }
                catch (UnauthorizedAccessException) { }
                catch (OperationCanceledException)
                {
                    return new ProjectStatisticsData { FileCount = 0, LinesCount = 0 };
                }

                return new ProjectStatisticsData { FileCount = fileCount, LinesCount = linesCount };
            }, cancellationToken);
        }

        private IEnumerable<string> EnumerateFilesOptimized(string rootPath, CancellationToken cancellationToken)
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
                    if (!ExcludedDirectories.Contains(dirName) && !dirName.StartsWith('.'))
                        stack.Push(subDir);
                }
            }
        }

        private bool IsSourceFile(string filePath) =>
            SourceExtensions.Contains(Path.GetExtension(filePath));

        private int CountLinesOptimized(string filePath)
        {
            try
            {
                var fileInfo = new FileInfo(filePath);
                if (fileInfo.Length > 5 * 1024 * 1024 || IsBinaryFileCached(filePath))
                    return 0;

                return File.ReadLines(filePath).Count();
            }
            catch
            {
                return 0;
            }
        }

        private bool IsBinaryFileCached(string filePath)
        {
            return _binaryFileCache.GetOrAdd(filePath, path =>
            {
                try
                {
                    using var stream = File.OpenRead(path);
                    var buffer = new byte[512];
                    var bytesRead = stream.Read(buffer, 0, buffer.Length);
                    for (int i = 0; i < bytesRead; i++)
                    {
                        if (buffer[i] == 0)
                            return true;
                    }
                    return false;
                }
                catch
                {
                    return true;
                }
            });
        }

        public void ClearCache()
        {
            _statisticsCache.Clear();
            _binaryFileCache.Clear();
        }
    }
}