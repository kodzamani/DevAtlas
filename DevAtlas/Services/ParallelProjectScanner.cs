using DevAtlas.Configuration;
using DevAtlas.Data;
using DevAtlas.Models;
using Microsoft.Extensions.Logging;

namespace DevAtlas.Services
{
    public class ParallelProjectScanner
    {
        private readonly DevAtlasDbContext _dbContext;
        private readonly ScalabilityConfiguration _config;
        private readonly ILogger<ParallelProjectScanner>? _logger;
        private readonly SemaphoreSlim _concurrencyLimiter;
        private readonly PriorityScanner _priorityScanner;

        public event EventHandler<ScanProgress>? ProgressChanged;

        public ParallelProjectScanner(
            DevAtlasDbContext dbContext,
            ScalabilityConfiguration config,
            ILogger<ParallelProjectScanner>? logger = null)
        {
            _dbContext = dbContext;
            _config = config;
            _logger = logger;
            _concurrencyLimiter = new SemaphoreSlim(_config.MaxConcurrentScans);
            _priorityScanner = new PriorityScanner(config);
        }

        public async Task<List<ProjectInfo>> ScanAllDrivesAsync(CancellationToken cancellationToken = default)
        {
            var scanHistory = new ScanHistory
            {
                ScanType = _config.EnableParallelScanning ? "Parallel" : "Sequential",
                Status = "Running",
                ScanStartTime = DateTime.Now
            };

            await _dbContext.ScanHistory.AddAsync(scanHistory);
            await _dbContext.SaveChangesAsync();

            var projects = new List<ProjectInfo>();
            var progress = new ScanProgress { IsScanning = true, Status = "Starting scan..." };

            try
            {
                var drives = GetDrivesToScan();
                progress.TotalDrives = drives.Count;
                ReportProgress(progress);

                if (_config.EnableParallelScanning && drives.Count > 1)
                {
                    projects = await ScanDrivesParallelAsync(drives, progress, cancellationToken);
                }
                else
                {
                    projects = await ScanDrivesSequentialAsync(drives, progress, cancellationToken);
                }

                scanHistory.Status = "Completed";
                scanHistory.ScanEndTime = DateTime.Now;
                scanHistory.ProjectsFound = projects.Count;
                scanHistory.DirectoriesScanned = progress.DirectoriesScanned;

                progress.Status = $"Scan complete. Found {projects.Count} projects.";
                progress.IsScanning = false;
                ReportProgress(progress);
            }
            catch (OperationCanceledException)
            {
                scanHistory.Status = "Cancelled";
                scanHistory.ScanEndTime = DateTime.Now;
                progress.Status = "Scan cancelled by user.";
                progress.IsScanning = false;
                ReportProgress(progress);
                return projects;
            }
            catch (Exception ex)
            {
                scanHistory.Status = "Failed";
                scanHistory.ErrorMessage = ex.Message;
                scanHistory.ScanEndTime = DateTime.Now;
                progress.Status = $"Scan failed: {ex.Message}";
                progress.IsScanning = false;
                ReportProgress(progress);
                _logger?.LogError(ex, "Scan failed");
                ErrorDialogService.ShowException(ex, "Project scan failed.");
                return projects;
            }
            finally
            {
                await _dbContext.SaveChangesAsync();
            }

            return projects;
        }

        private async Task<List<ProjectInfo>> ScanDrivesParallelAsync(
            List<DriveInfo> drives,
            ScanProgress progress,
            CancellationToken cancellationToken)
        {
            var driveScanTasks = drives.Select(drive => ScanDriveAsync(drive, progress, cancellationToken));
            var results = await Task.WhenAll(driveScanTasks);
            return results.SelectMany(r => r).ToList();
        }

        private async Task<List<ProjectInfo>> ScanDrivesSequentialAsync(
            List<DriveInfo> drives,
            ScanProgress progress,
            CancellationToken cancellationToken)
        {
            var projects = new List<ProjectInfo>();
            foreach (var drive in drives)
            {
                var driveProjects = await ScanDriveAsync(drive, progress, cancellationToken);
                projects.AddRange(driveProjects);
            }
            return projects;
        }

        private async Task<List<ProjectInfo>> ScanDriveAsync(
            DriveInfo drive,
            ScanProgress progress,
            CancellationToken cancellationToken)
        {
            await _concurrencyLimiter.WaitAsync(cancellationToken);
            try
            {
                progress.CurrentDrive = drive.Name;
                progress.Status = $"Scanning drive {drive.Name}...";
                ReportProgress(progress);

                var projects = new List<ProjectInfo>();
                var foundProjectRoots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                // Get priority paths first
                var priorityPaths = _priorityScanner.GetPriorityPaths(drive);
                var regularPaths = new List<string>();

                // Collect all directories to scan
                await Task.Run(() =>
                {
                    try
                    {
                        CollectDirectoriesToScan(drive.RootDirectory.FullName, priorityPaths, regularPaths, progress, cancellationToken);
                    }
                    catch (OperationCanceledException) { }
                    catch (Exception ex)
                    {
                        _logger?.LogError(ex, $"Error collecting directories on drive {drive.Name}");
                    }
                }, cancellationToken);

                // Scan priority paths first
                foreach (var path in priorityPaths)
                {
                    if (cancellationToken.IsCancellationRequested) break;
                    await ScanDirectoryAsync(path, projects, foundProjectRoots, progress, cancellationToken);
                }

                // Then scan regular paths in parallel if enabled
                if (_config.EnableParallelScanning && regularPaths.Count > 10)
                {
                    var batchSize = Math.Max(1, regularPaths.Count / _config.MaxConcurrentScans);
                    var batches = regularPaths.Chunk(batchSize);

                    var batchTasks = batches.Select(batch =>
                        ScanDirectoryBatchAsync(batch, projects, foundProjectRoots, progress, cancellationToken));

                    await Task.WhenAll(batchTasks);
                }
                else
                {
                    foreach (var path in regularPaths)
                    {
                        if (cancellationToken.IsCancellationRequested) break;
                        await ScanDirectoryAsync(path, projects, foundProjectRoots, progress, cancellationToken);
                    }
                }

                progress.ProcessedDrives++;
                progress.ProgressPercentage = (double)progress.ProcessedDrives / progress.TotalDrives * 100;
                ReportProgress(progress);

                return projects;
            }
            finally
            {
                _concurrencyLimiter.Release();
            }
        }

        private void CollectDirectoriesToScan(
            string rootPath,
            List<string> priorityPaths,
            List<string> regularPaths,
            ScanProgress progress,
            CancellationToken cancellationToken)
        {
            var priorityDirNames = _config.PriorityDirectories.Select(d => d.ToLowerInvariant()).ToHashSet();

            try
            {
                var stack = new Stack<string>();
                stack.Push(rootPath);

                while (stack.Count > 0 && !cancellationToken.IsCancellationRequested)
                {
                    var currentPath = stack.Pop();

                    if (!Directory.Exists(currentPath)) continue;

                    var dirName = Path.GetFileName(currentPath).ToLowerInvariant();

                    // Skip unwanted directories
                    if (_config.SkipDirectories.Contains(dirName) || dirName.StartsWith(".")) continue;

                    // Check if this is a priority directory
                    if (priorityDirNames.Contains(dirName))
                    {
                        priorityPaths.Add(currentPath);
                    }
                    else
                    {
                        regularPaths.Add(currentPath);
                    }

                    progress.DirectoriesScanned++;
                    if (progress.DirectoriesScanned % 1000 == 0)
                    {
                        ReportProgress(progress);
                    }

                    // Add subdirectories to stack (limit depth to prevent infinite loops)
                    try
                    {
                        var subDirs = Directory.GetDirectories(currentPath);
                        foreach (var subDir in subDirs.Take(100)) // Limit to prevent issues with directories containing too many subdirs
                        {
                            stack.Push(subDir);
                        }
                    }
                    catch (UnauthorizedAccessException) { }
                    catch (Exception ex)
                    {
                        _logger?.LogDebug(ex, $"Error accessing directory {currentPath}");
                    }
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, $"Error collecting directories from {rootPath}");
            }
        }

        private async Task ScanDirectoryBatchAsync(
            IEnumerable<string> directories,
            List<ProjectInfo> projects,
            HashSet<string> foundProjectRoots,
            ScanProgress progress,
            CancellationToken cancellationToken)
        {
            var tasks = directories.Select(dir => ScanDirectoryAsync(dir, projects, foundProjectRoots, progress, cancellationToken));
            await Task.WhenAll(tasks);
        }

        private async Task ScanDirectoryAsync(
            string path,
            List<ProjectInfo> projects,
            HashSet<string> foundProjectRoots,
            ScanProgress progress,
            CancellationToken cancellationToken)
        {
            await Task.Run(() =>
            {
                try
                {
                    var projectInfo = CheckForProject(path);
                    if (projectInfo != null && !foundProjectRoots.Contains(path))
                    {
                        foundProjectRoots.Add(path);
                        lock (projects)
                        {
                            projects.Add(projectInfo);
                            progress.ProjectsFound = projects.Count;
                        }
                        ReportProgress(progress);
                    }
                }
                catch (Exception ex)
                {
                    _logger?.LogDebug(ex, $"Error scanning directory {path}");
                }
            }, cancellationToken);
        }

        private ProjectInfo? CheckForProject(string path)
        {
            // This is a simplified version - we'll integrate with the existing ProjectScanner logic
            try
            {
                var dirInfo = new DirectoryInfo(path);
                if (!dirInfo.Exists) return null;

                // Check for common project files
                var projectFiles = new[]
                {
                    "*.csproj", "*.sln", "*.slnx", "package.json", "requirements.txt",
                    "setup.py", "pyproject.toml", "Pipfile", "pom.xml", "build.gradle",
                    "go.mod", "Cargo.toml", "composer.json", "Gemfile", "Package.swift",
                    "Podfile", "pubspec.yaml", "Dockerfile", "docker-compose.yml"
                };

                foreach (var pattern in projectFiles)
                {
                    var files = Directory.GetFiles(path, pattern);
                    if (files.Length > 0)
                    {
                        return CreateProjectInfo(path, files, pattern);
                    }
                }

                // Check for Python projects with multiple .py files
                var pyFiles = Directory.GetFiles(path, "*.py");
                if (pyFiles.Length >= 3)
                {
                    return CreateProjectInfo(path, pyFiles, "Python");
                }

                return null;
            }
            catch
            {
                return null;
            }
        }

        private ProjectInfo CreateProjectInfo(string projectPath, string[] files, string markerType)
        {
            var dirInfo = new DirectoryInfo(projectPath);
            var projectType = DetermineProjectType(markerType, files);
            var category = DetermineCategory(projectType, files);
            var tags = DetermineTags(projectType, files);

            return new ProjectInfo
            {
                Name = dirInfo.Name,
                Path = projectPath,
                ProjectType = projectType,
                Category = category,
                Tags = tags.Take(4).ToList(),
                LastModified = dirInfo.LastWriteTime,
                LastIndexed = DateTime.Now,
                IsActive = (DateTime.Now - dirInfo.LastWriteTime).TotalDays <= 7,
                IconText = GetIconText(projectType),
                IconColor = GetIconColor(projectType)
            };
        }

        private string DetermineProjectType(string markerType, string[] files)
        {
            var extension = Path.GetExtension(markerType).ToLowerInvariant();
            var fileName = Path.GetFileName(markerType).ToLowerInvariant();

            return extension switch
            {
                ".csproj" => ".NET",
                ".sln" => ".NET Solution",
                ".slnx" => ".NET Solution",
                _ => fileName switch
                {
                    "package.json" => "Node.js",
                    "requirements.txt" => "Python",
                    "setup.py" => "Python",
                    "pyproject.toml" => "Python",
                    "pipfile" => "Python",
                    "pom.xml" => "Java/Maven",
                    "build.gradle" => "Java/Gradle",
                    "go.mod" => "Go",
                    "cargo.toml" => "Rust",
                    "composer.json" => "PHP",
                    "gemfile" => "Ruby",
                    "package.swift" => "Swift",
                    "podfile" => "iOS",
                    "pubspec.yaml" => "Flutter",
                    "dockerfile" => "Docker",
                    "docker-compose.yml" => "Docker",
                    "python" => "Python",
                    _ => "Unknown"
                }
            };
        }

        private string DetermineCategory(string projectType, string[] files)
        {
            return projectType.ToLowerInvariant() switch
            {
                var pt when pt.Contains(".net") || pt.Contains("c#") => "Desktop",
                var pt when pt.Contains("node.js") || pt.Contains("react") || pt.Contains("vue") || pt.Contains("angular") => "Web",
                var pt when pt.Contains("flutter") || pt.Contains("ios") || pt.Contains("android") => "Mobile",
                var pt when pt.Contains("docker") => "Cloud",
                var pt when pt.Contains("python") || pt.Contains("java") || pt.Contains("go") || pt.Contains("rust") =>
                    files.Any(f => Path.GetFileName(f).ToLowerInvariant().Contains("web")) ? "Web" : "Desktop",
                _ => "Other"
            };
        }

        private List<string> DetermineTags(string projectType, string[] files)
        {
            var tags = new List<string> { projectType };

            // Add technology-specific tags based on files
            var fileNames = files.Select(Path.GetFileName).Select(f => f?.ToLowerInvariant()).Where(f => !string.IsNullOrEmpty(f));

            if (fileNames.Contains("tsconfig.json")) tags.Add("TypeScript");
            if (fileNames.Contains("webpack.config.js")) tags.Add("Webpack");
            if (fileNames.Contains("babel.config.js")) tags.Add("Babel");
            if (fileNames.Contains("jest.config.js")) tags.Add("Jest");
            if (fileNames.Contains("dockerfile")) tags.Add("Docker");

            return tags.Distinct().ToList();
        }

        private string GetIconText(string projectType)
        {
            return projectType.ToLowerInvariant() switch
            {
                var pt when pt.Contains(".net") || pt.Contains("c#") => "C#",
                var pt when pt.Contains("node.js") => "JS",
                var pt when pt.Contains("python") => "Py",
                var pt when pt.Contains("react") => "</>",
                var pt when pt.Contains("vue") => "V",
                var pt when pt.Contains("angular") => "A",
                var pt when pt.Contains("go") => "Go",
                var pt when pt.Contains("rust") => "Rs",
                var pt when pt.Contains("java") => "Jv",
                var pt when pt.Contains("flutter") => "Fl",
                var pt when pt.Contains("docker") => "Dk",
                _ => "P"
            };
        }

        private string GetIconColor(string projectType)
        {
            return projectType.ToLowerInvariant() switch
            {
                var pt when pt.Contains(".net") || pt.Contains("c#") => "#512BD4",
                var pt when pt.Contains("node.js") => "#F7DF1E",
                var pt when pt.Contains("python") => "#3776AB",
                var pt when pt.Contains("react") => "#61DAFB",
                var pt when pt.Contains("vue") => "#42B883",
                var pt when pt.Contains("angular") => "#DD0031",
                var pt when pt.Contains("go") => "#00ADD8",
                var pt when pt.Contains("rust") => "#DEA584",
                var pt when pt.Contains("java") => "#ED8B00",
                var pt when pt.Contains("flutter") => "#02569B",
                var pt when pt.Contains("docker") => "#2496ED",
                _ => "#6B7280"
            };
        }

        private List<DriveInfo> GetDrivesToScan()
        {
            return DriveInfo.GetDrives()
                .Where(d => d.IsReady && (d.DriveType == DriveType.Fixed || d.DriveType == DriveType.Removable))
                .ToList();
        }

        private void ReportProgress(ScanProgress progress)
        {
            ProgressChanged?.Invoke(this, progress);
        }
    }
}
