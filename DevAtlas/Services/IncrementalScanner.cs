using DevAtlas.Configuration;
using DevAtlas.Data;
using DevAtlas.Enums;
using DevAtlas.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace DevAtlas.Services
{
    public class IncrementalScanner : IDisposable
    {
        private readonly DevAtlasDbContext _dbContext;
        private readonly ScalabilityConfiguration _config;
        private readonly ILogger<IncrementalScanner>? _logger;
        private readonly FileSystemWatcher[] _fileSystemWatchers;
        private readonly Timer _scanTimer;
        private readonly SemaphoreSlim _scanLock = new(1, 1);
        private bool _disposed = false;

        public event EventHandler<ScanProgress>? ProgressChanged;
        public event EventHandler<List<ProjectInfo>>? ProjectsChanged;

        public IncrementalScanner(
            DevAtlasDbContext dbContext,
            ScalabilityConfiguration config,
            ILogger<IncrementalScanner>? logger = null)
        {
            _dbContext = dbContext;
            _config = config;
            _logger = logger;

            // Initialize timer for periodic scans
            _scanTimer = new Timer(PerformPeriodicScan, null, Timeout.Infinite, Timeout.Infinite);

            // Initialize file system watchers for common project directories
            _fileSystemWatchers = new FileSystemWatcher[Environment.GetLogicalDrives().Length];
        }

        public async Task StartAsync()
        {
            if (!_config.EnableIncrementalScanning) return;

            _logger?.LogInformation("Starting incremental scanner");

            // Start file system watchers
            await StartFileSystemWatchersAsync();

            // Start periodic scan timer
            _scanTimer.Change(TimeSpan.FromMinutes(30), _config.ScanInterval);
        }

        public async Task StopAsync()
        {
            _logger?.LogInformation("Stopping incremental scanner");

            // Stop file system watchers
            foreach (var watcher in _fileSystemWatchers)
            {
                if (watcher != null)
                {
                    watcher.EnableRaisingEvents = false;
                    watcher.Dispose();
                }
            }

            // Stop timer
            await _scanTimer.DisposeAsync();
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
                    // Stop file system watchers
                    if (_fileSystemWatchers != null)
                    {
                        foreach (var watcher in _fileSystemWatchers)
                        {
                            if (watcher != null)
                            {
                                watcher.EnableRaisingEvents = false;
                                watcher.Dispose();
                            }
                        }
                    }

                    // Dispose timer
                    _scanTimer?.Dispose();

                    // Dispose semaphore
                    _scanLock?.Dispose();
                }
                _disposed = true;
            }
        }

        public async Task<IncrementalScanResult> ScanForChangesAsync()
        {
            if (!await _scanLock.WaitAsync(TimeSpan.FromSeconds(30)))
            {
                _logger?.LogWarning("Incremental scan lock timeout - scan already in progress");
                return new IncrementalScanResult { Status = ScanStatus.Busy };
            }

            try
            {
                var result = new IncrementalScanResult
                {
                    Status = ScanStatus.Running,
                    StartTime = DateTime.Now
                };

                var progress = new ScanProgress
                {
                    IsScanning = true,
                    Status = "Starting incremental scan..."
                };

                ReportProgress(progress);

                // Get last scan time
                var lastScanTime = await GetLastScanTimeAsync();
                if (lastScanTime == DateTime.MinValue)
                {
                    result.Status = ScanStatus.NoPreviousScan;
                    result.Message = "No previous scan found - perform full scan first";
                    return result;
                }

                // Scan for new, modified, and deleted projects
                var scanTasks = new List<Task>
                {
                    ScanForNewProjectsAsync(lastScanTime, result, progress),
                    ScanForModifiedProjectsAsync(lastScanTime, result, progress),
                    ScanForDeletedProjectsAsync(result, progress)
                };

                await Task.WhenAll(scanTasks);

                result.EndTime = DateTime.Now;
                result.Status = ScanStatus.Completed;
                result.Message = $"Incremental scan completed: {result.NewProjects.Count} new, {result.ModifiedProjects.Count} modified, {result.DeletedProjects.Count} deleted";

                progress.Status = result.Message;
                progress.IsScanning = false;
                ReportProgress(progress);

                // Notify of changes
                if (result.HasChanges)
                {
                    ProjectsChanged?.Invoke(this, result.AllChanges);
                }

                _logger?.LogInformation(result.Message);
                return result;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Incremental scan failed");
                return new IncrementalScanResult
                {
                    Status = ScanStatus.Failed,
                    Message = ex.Message,
                    Exception = ex
                };
            }
            finally
            {
                _scanLock.Release();
            }
        }

        private async Task StartFileSystemWatchersAsync()
        {
            var drives = DriveInfo.GetDrives()
                .Where(d => d.IsReady && d.DriveType == DriveType.Fixed)
                .ToList();

            for (int i = 0; i < drives.Count && i < _fileSystemWatchers.Length; i++)
            {
                try
                {
                    var drive = drives[i];
                    var watcher = new FileSystemWatcher(drive.RootDirectory.FullName)
                    {
                        IncludeSubdirectories = true,
                        NotifyFilter = NotifyFilters.DirectoryName |
                                       NotifyFilters.FileName |
                                       NotifyFilters.LastWrite |
                                       NotifyFilters.Size
                    };

                    // Set up event handlers
                    watcher.Created += OnFileSystemChanged;
                    watcher.Deleted += OnFileSystemChanged;
                    watcher.Renamed += OnFileSystemChanged;
                    watcher.Changed += OnFileSystemChanged;

                    // Filter to important directories
                    watcher.Filter = "*.*"; // We'll filter in the event handler

                    watcher.EnableRaisingEvents = true;
                    _fileSystemWatchers[i] = watcher;

                    _logger?.LogDebug($"Started file system watcher for {drive.Name}");
                }
                catch (Exception ex)
                {
                    _logger?.LogWarning(ex, $"Failed to start file system watcher for drive {drives[i].Name}");
                }
            }
        }

        private void OnFileSystemChanged(object sender, FileSystemEventArgs e)
        {
            try
            {
                var path = e.FullPath;
                var dirName = Path.GetDirectoryName(path);
                var fileName = Path.GetFileName(path);

                // Skip if this is not a project-related change
                if (!IsProjectRelatedChange(dirName, fileName))
                    return;

                _logger?.LogDebug($"File system change detected: {e.ChangeType} - {path}");

                // Debounce rapid changes - schedule a scan after a delay
                _ = Task.Delay(TimeSpan.FromSeconds(5)).ContinueWith(async _ =>
                {
                    try
                    {
                        await ScanForChangesAsync();
                    }
                    catch (Exception ex)
                    {
                        _logger?.LogError(ex, "Error in debounced incremental scan");
                    }
                });
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, $"Error handling file system change: {e.FullPath}");
            }
        }

        private bool IsProjectRelatedChange(string? dirName, string fileName)
        {
            if (string.IsNullOrEmpty(dirName) || string.IsNullOrEmpty(fileName))
                return false;

            // Check for project file changes
            var projectFiles = new[]
            {
                ".csproj", ".sln", ".slnx", "package.json", "requirements.txt",
                "setup.py", "pyproject.toml", "Pipfile", "pom.xml", "build.gradle",
                "go.mod", "Cargo.toml", "composer.json", "Gemfile", "Package.swift",
                "Podfile", "pubspec.yaml", "Dockerfile", "docker-compose.yml"
            };

            return projectFiles.Any(pf =>
                fileName.EndsWith(pf, StringComparison.OrdinalIgnoreCase) ||
                fileName.Equals(pf, StringComparison.OrdinalIgnoreCase));
        }

        private async Task ScanForNewProjectsAsync(DateTime lastScanTime, IncrementalScanResult result, ScanProgress progress)
        {
            progress.Status = "Scanning for new projects...";
            ReportProgress(progress);

            var drives = DriveInfo.GetDrives()
                .Where(d => d.IsReady && (d.DriveType == DriveType.Fixed || d.DriveType == DriveType.Removable))
                .ToList();

            foreach (var drive in drives)
            {
                var newProjects = await ScanDriveForNewProjectsAsync(drive, lastScanTime, progress);
                result.NewProjects.AddRange(newProjects);
            }

            if (result.NewProjects.Any())
            {
                await _dbContext.Projects.AddRangeAsync(result.NewProjects.Select(p => ConvertToProject(p)));
                await _dbContext.SaveChangesAsync();
            }
        }

        private async Task<List<ProjectInfo>> ScanDriveForNewProjectsAsync(DriveInfo drive, DateTime lastScanTime, ScanProgress progress)
        {
            var newProjects = new List<ProjectInfo>();
            var existingPaths = await _dbContext.Projects
                .Where(p => p.Path.StartsWith(drive.RootDirectory.FullName, StringComparison.OrdinalIgnoreCase))
                .Select(p => p.Path)
                .ToListAsync();

            var foundProjectRoots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            existingPaths.ForEach(p => foundProjectRoots.Add(p));

            await Task.Run(() =>
            {
                try
                {
                    ScanDirectoryForNewProjects(drive.RootDirectory.FullName, newProjects, foundProjectRoots, lastScanTime, progress, 0);
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, $"Error scanning drive {drive.Name} for new projects");
                }
            });

            return newProjects;
        }

        private void ScanDirectoryForNewProjects(string path, List<ProjectInfo> newProjects,
            HashSet<string> existingPaths, DateTime lastScanTime, ScanProgress progress, int depth)
        {
            if (depth > _config.MaxScanDepth) return;

            try
            {
                var dirInfo = new DirectoryInfo(path);
                if (!dirInfo.Exists || dirInfo.LastWriteTime <= lastScanTime) return;

                // Skip if this is already a known project
                if (existingPaths.Contains(path)) return;

                // Check if this is a new project
                var projectInfo = CheckForProject(path);
                if (projectInfo != null)
                {
                    newProjects.Add(projectInfo);
                    progress.ProjectsFound++;
                    ReportProgress(progress);
                    return; // Don't scan subdirectories of a project
                }

                // Recursively scan subdirectories
                var subDirs = Directory.GetDirectories(path);
                foreach (var subDir in subDirs)
                {
                    var dirName = Path.GetFileName(subDir);
                    if (_config.SkipDirectories.Any(d => string.Equals(d, dirName, StringComparison.OrdinalIgnoreCase)) || dirName.StartsWith(".")) continue;

                    ScanDirectoryForNewProjects(subDir, newProjects, existingPaths, lastScanTime, progress, depth + 1);
                }
            }
            catch (UnauthorizedAccessException) { }
            catch (Exception ex)
            {
                _logger?.LogDebug(ex, $"Error scanning directory {path} for new projects");
            }
        }

        private async Task ScanForModifiedProjectsAsync(DateTime lastScanTime, IncrementalScanResult result, ScanProgress progress)
        {
            progress.Status = "Scanning for modified projects...";
            ReportProgress(progress);

            var modifiedProjects = await _dbContext.Projects
                .Where(p => p.LastModified > lastScanTime && p.LastIndexed <= lastScanTime)
                .ToListAsync();

            foreach (var project in modifiedProjects)
            {
                if (Directory.Exists(project.Path))
                {
                    var dirInfo = new DirectoryInfo(project.Path);
                    var updatedProjectInfo = CheckForProject(project.Path);

                    if (updatedProjectInfo != null)
                    {
                        // Update project with new information
                        project.Name = updatedProjectInfo.Name;
                        project.ProjectType = updatedProjectInfo.ProjectType;
                        project.Category = updatedProjectInfo.Category;
                        project.LastIndexed = DateTime.Now;
                        project.IsActive = (DateTime.Now - dirInfo.LastWriteTime).TotalDays <= 7;

                        result.ModifiedProjects.Add(ConvertToProjectInfo(project));
                    }
                }
            }

            if (modifiedProjects.Any())
            {
                await _dbContext.SaveChangesAsync();
            }
        }

        private async Task ScanForDeletedProjectsAsync(IncrementalScanResult result, ScanProgress progress)
        {
            progress.Status = "Scanning for deleted projects...";
            ReportProgress(progress);

            var allProjects = await _dbContext.Projects.ToListAsync();
            var deletedProjects = allProjects.Where(p => !Directory.Exists(p.Path)).ToList();

            foreach (var project in deletedProjects)
            {
                result.DeletedProjects.Add(ConvertToProjectInfo(project));
            }

            if (deletedProjects.Any())
            {
                _dbContext.Projects.RemoveRange(deletedProjects);
                await _dbContext.SaveChangesAsync();
            }
        }

        private ProjectInfo? CheckForProject(string path)
        {
            // This is a simplified version - we should reuse the logic from ParallelProjectScanner
            try
            {
                var dirInfo = new DirectoryInfo(path);
                if (!dirInfo.Exists) return null;

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
            // Simplified implementation - reuse logic from ParallelProjectScanner
            var dirInfo = new DirectoryInfo(projectPath);

            return new ProjectInfo
            {
                Name = dirInfo.Name,
                Path = projectPath,
                ProjectType = "Unknown", // Simplified
                Category = "Other",
                Tags = new List<string>(),
                LastModified = dirInfo.LastWriteTime,
                LastIndexed = DateTime.Now,
                IsActive = (DateTime.Now - dirInfo.LastWriteTime).TotalDays <= 7,
                IconText = "P",
                IconColor = "#6B7280"
            };
        }

        private Project ConvertToProject(ProjectInfo projectInfo)
        {
            return new Project
            {
                Name = projectInfo.Name,
                Path = projectInfo.Path,
                ProjectType = projectInfo.ProjectType,
                Category = projectInfo.Category,
                LastModified = projectInfo.LastModified,
                LastIndexed = projectInfo.LastIndexed,
                IsActive = projectInfo.IsActive,
                GitBranch = projectInfo.GitBranch,
                IconText = projectInfo.IconText,
                IconColor = projectInfo.IconColor
            };
        }

        private ProjectInfo ConvertToProjectInfo(Project project)
        {
            return new ProjectInfo
            {
                Id = project.Id.ToString(),
                Name = project.Name,
                Path = project.Path,
                ProjectType = project.ProjectType,
                Category = project.Category,
                Tags = project.Tags?.Select(t => t.Name).ToList() ?? new List<string>(),
                LastModified = project.LastModified,
                LastIndexed = project.LastIndexed,
                IsActive = project.IsActive,
                GitBranch = project.GitBranch,
                IconText = project.IconText,
                IconColor = project.IconColor
            };
        }

        private async Task<DateTime> GetLastScanTimeAsync()
        {
            var lastScan = await _dbContext.ScanHistory
                .OrderByDescending(h => h.ScanStartTime)
                .FirstOrDefaultAsync();

            return lastScan?.ScanStartTime ?? DateTime.MinValue;
        }

        private async void PerformPeriodicScan(object? state)
        {
            try
            {
                _logger?.LogDebug("Performing periodic incremental scan");
                await ScanForChangesAsync();
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error in periodic incremental scan");
            }
        }

        private void ReportProgress(ScanProgress progress)
        {
            ProgressChanged?.Invoke(this, progress);
        }
    }
}
