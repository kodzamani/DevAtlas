using DevAtlas.Models;

namespace DevAtlas.Services
{
    public class ProjectScanner
    {
        private List<string> _excludePaths;

        // Extensions that indicate a project (matched against file extension)
        // Static readonly collections are intentionally kept in memory for performance
        private static readonly HashSet<string> _extensionMarkers = new(StringComparer.OrdinalIgnoreCase)
        {
            ".csproj", ".fsproj", ".vbproj", ".sln", ".slnx"
        };

        // Filenames that indicate a project (matched against filename)
        private static readonly HashSet<string> _filenameMarkers = new(StringComparer.OrdinalIgnoreCase)
        {
            // Node.js / JavaScript
            "package.json",
            // Python
            "requirements.txt", "setup.py", "pyproject.toml", "Pipfile",
            "main.py", "app.py", "__init__.py", "__main__.py",
            // Java
            "pom.xml", "build.gradle", "build.gradle.kts",
            // Go
            "go.mod",
            // Rust
            "Cargo.toml",
            // PHP
            "composer.json",
            // Ruby
            "Gemfile",
            // Swift / iOS
            "Package.swift", "Podfile",
            // Flutter
            "pubspec.yaml",
            // React / Next.js
            "next.config.js", "next.config.mjs", "next.config.ts",
            // Vue
            "vue.config.js", "vite.config.js", "vite.config.ts",
            // Angular
            "angular.json",
            // Docker
            "Dockerfile", "docker-compose.yml", "docker-compose.yaml",
        };

        private static readonly Dictionary<string, string> _projectTypeByExtension = new(StringComparer.OrdinalIgnoreCase)
        {
            { ".csproj", ".NET" },
            { ".fsproj", "F#" },
            { ".vbproj", "VB.NET" },
            { ".sln", ".NET Solution" },
            { ".slnx", ".NET Solution" },
        };

        private static readonly Dictionary<string, string> _projectTypeByFilename = new(StringComparer.OrdinalIgnoreCase)
        {
            { "package.json", "Node.js" },
            { "go.mod", "Go" },
            { "Cargo.toml", "Rust" },
            { "pom.xml", "Java/Maven" },
            { "build.gradle", "Java/Gradle" },
            { "build.gradle.kts", "Java/Gradle" },
            { "composer.json", "PHP" },
            { "Gemfile", "Ruby" },
            { "requirements.txt", "Python" },
            { "pyproject.toml", "Python" },
            { "Pipfile", "Python" },
            { "setup.py", "Python" },
            { "main.py", "Python" },
            { "app.py", "Python" },
            { "__init__.py", "Python" },
            { "__main__.py", "Python" },
            { "pubspec.yaml", "Flutter" },
            { "Podfile", "iOS" },
            { "Package.swift", "Swift" },
            { "angular.json", "Angular" },
            { "next.config.js", "Next.js" },
            { "next.config.mjs", "Next.js" },
            { "next.config.ts", "Next.js" },
            { "vue.config.js", "Vue" },
            { "vite.config.js", "Vite" },
            { "vite.config.ts", "Vite" },
            { "Dockerfile", "Docker" },
            { "docker-compose.yml", "Docker" },
            { "docker-compose.yaml", "Docker" },
        };

        private static readonly Dictionary<string, string[]> _technologyTags = new(StringComparer.OrdinalIgnoreCase)
        {
            { "package.json", new[] { "JavaScript", "Node.js" } },
            { "tsconfig.json", new[] { "TypeScript" } },
            { "next.config.js", new[] { "React", "Next.js" } },
            { "next.config.mjs", new[] { "React", "Next.js" } },
            { "next.config.ts", new[] { "React", "Next.js" } },
            { "angular.json", new[] { "Angular", "TypeScript" } },
            { "vue.config.js", new[] { "Vue" } },
            { "vite.config.js", new[] { "Vite" } },
            { "vite.config.ts", new[] { "Vite", "TypeScript" } },
            { "go.mod", new[] { "Go" } },
            { "Cargo.toml", new[] { "Rust" } },
            { "pom.xml", new[] { "Java", "Maven" } },
            { "build.gradle", new[] { "Java", "Gradle" } },
            { "build.gradle.kts", new[] { "Java", "Gradle", "Kotlin" } },
            { "composer.json", new[] { "PHP" } },
            { "requirements.txt", new[] { "Python" } },
            { "pyproject.toml", new[] { "Python" } },
            { "Pipfile", new[] { "Python" } },
            { "setup.py", new[] { "Python" } },
            { "main.py", new[] { "Python" } },
            { "app.py", new[] { "Python" } },
            { "__init__.py", new[] { "Python" } },
            { "__main__.py", new[] { "Python" } },
            { "Gemfile", new[] { "Ruby" } },
            { "pubspec.yaml", new[] { "Flutter", "Dart" } },
            { "Podfile", new[] { "iOS", "CocoaPods" } },
            { "Dockerfile", new[] { "Docker" } },
            { "docker-compose.yml", new[] { "Docker", "Compose" } },
            { "docker-compose.yaml", new[] { "Docker", "Compose" } },
        };

        // Category detection: Web, Desktop, Mobile, Cloud
        private static readonly HashSet<string> _webProjectTypes = new(StringComparer.OrdinalIgnoreCase)
        {
            "Node.js", "Next.js", "Angular", "Vue", "Vite", "React"
        };

        private static readonly HashSet<string> _webProjectFiles = new(StringComparer.OrdinalIgnoreCase)
        {
            "next.config.js", "next.config.mjs", "next.config.ts",
            "angular.json", "vue.config.js", "vite.config.js", "vite.config.ts",
            "nuxt.config.js", "nuxt.config.ts", "gatsby-config.js", "svelte.config.js"
        };

        private static readonly HashSet<string> _desktopProjectTypes = new(StringComparer.OrdinalIgnoreCase)
        {
            ".NET", ".NET Solution", "F#", "VB.NET"
        };

        private static readonly HashSet<string> _desktopProjectFiles = new(StringComparer.OrdinalIgnoreCase)
        {
            // Electron/Tauri desktop apps
            "electron-builder.yml", "electron-builder.json", "tauri.conf.json"
        };

        private static readonly HashSet<string> _mobileProjectTypes = new(StringComparer.OrdinalIgnoreCase)
        {
            "Flutter", "iOS", "Swift", "Android"
        };

        private static readonly HashSet<string> _mobileProjectFiles = new(StringComparer.OrdinalIgnoreCase)
        {
            "pubspec.yaml", "Podfile", "AndroidManifest.xml"
        };

        private static readonly HashSet<string> _cloudProjectTypes = new(StringComparer.OrdinalIgnoreCase)
        {
            "Docker"
        };

        private static readonly HashSet<string> _cloudProjectFiles = new(StringComparer.OrdinalIgnoreCase)
        {
            "Dockerfile", "docker-compose.yml", "docker-compose.yaml",
            "serverless.yml", "serverless.yaml", "terraform.tf", "main.tf",
            "cloudformation.yaml", "cloudformation.yml", "kubernetes.yaml", "k8s.yaml"
        };

        private static readonly HashSet<string> _skipDirectories = new(StringComparer.OrdinalIgnoreCase)
        {
            "node_modules", "bin", "obj", ".svn", ".hg",
            "windows", "program files", "program files (x86)",
            "programdata", "$recycle.bin", "system volume information",
            "windows.old", "perflogs", ".nuget", "packages",
            ".vs", ".idea", ".vscode", "dist", "out",
            "target", "vendor", "__pycache__", ".venv", "venv",
            "env", ".tox", ".mypy_cache", ".pytest_cache",
            ".cache", ".tmp", "temp", "tmp",
            "recovery", "msocache", "intel", "nvidia",
            "amd", "drivers", "boot", "go", "plugins", "pkg",
            // explicitly skip flutter project directories
            "flutter"
        };

        // Unix system paths that should not be traversed during project scanning.
        // These locations are either virtual file systems, OS internals, or usually unrelated to user projects.
        private static readonly string[] _unixSystemPathPrefixes =
        {
            "/proc",
            "/sys",
            "/dev",
            "/run",
            "/snap",
            "/tmp",
            "/lost+found",
            "/usr",
            "/bin",
            "/sbin",
            "/lib",
            "/lib64",
            "/boot",
            "/etc",
            "/var"
        };

        public event EventHandler<ScanProgress>? ProgressChanged;

        /// <summary>
        /// Initializes a new instance of the ProjectScanner class.
        /// </summary>
        /// <param name="excludePaths">List of directory paths to exclude during scanning</param>
        public ProjectScanner(List<string>? excludePaths = null)
        {
            _excludePaths = excludePaths ?? new List<string>();
        }

        /// <summary>
        /// Updates the list of exclude paths used during scanning.
        /// </summary>
        /// <param name="excludePaths">List of directory paths to exclude during scanning</param>
        public void UpdateExcludePaths(List<string>? excludePaths)
        {
            _excludePaths = excludePaths ?? new List<string>();
        }

        public async Task<List<ProjectInfo>> ScanAllDrivesAsync(CancellationToken cancellationToken = default)
        {
            var projects = new List<ProjectInfo>();
            var progress = new ScanProgress { IsScanning = true, Status = "Starting scan..." };

            var drives = DriveInfo.GetDrives()
                .Where(d => d.IsReady && (d.DriveType == DriveType.Fixed || d.DriveType == DriveType.Removable))
                .ToList();

            progress.TotalDrives = drives.Count;
            ReportProgress(progress);

            foreach (var drive in drives)
            {
                if (cancellationToken.IsCancellationRequested) break;

                progress.CurrentDrive = drive.Name;
                progress.Status = $"Scanning drive {drive.Name}...";
                ReportProgress(progress);

                try
                {
                    var driveProjects = await ScanDriveAsync(drive, progress, cancellationToken);
                    projects.AddRange(driveProjects);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Error scanning drive {drive.Name}: {ex.Message}");
                }

                progress.ProcessedDrives++;
                progress.ProgressPercentage = (double)progress.ProcessedDrives / progress.TotalDrives * 100;
                ReportProgress(progress);
            }

            progress.IsScanning = false;
            progress.Status = $"Scan complete. Found {projects.Count} projects.";
            ReportProgress(progress);

            return projects;
        }

        private async Task<List<ProjectInfo>> ScanDriveAsync(DriveInfo drive, ScanProgress progress, CancellationToken cancellationToken)
        {
            var projects = new List<ProjectInfo>();
            var foundProjectRoots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var visitedDirectories = new HashSet<string>(GetPathComparer());

            await Task.Run(() =>
            {
                try
                {
                    ScanDirectory(drive.RootDirectory.FullName, projects, foundProjectRoots, visitedDirectories, progress, 0, 100, cancellationToken);
                }
                catch (OperationCanceledException) { }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Error in ScanDirectory: {ex.Message}");
                }
            }, cancellationToken);

            return projects;
        }

        private void ScanDirectory(string path, List<ProjectInfo> projects, HashSet<string> foundProjectRoots,
            HashSet<string> visitedDirectories, ScanProgress progress, int depth, int maxDepth, CancellationToken cancellationToken)
        {
            if (depth > maxDepth) return;
            if (cancellationToken.IsCancellationRequested) return;

            var normalizedPath = NormalizePathForComparison(path);
            if (!string.IsNullOrEmpty(normalizedPath) && !visitedDirectories.Add(normalizedPath))
            {
                return;
            }

            try
            {
                // Skip hidden and system directories
                var dirInfo = new DirectoryInfo(path);
                if (!dirInfo.Exists) return;

                var attrs = dirInfo.Attributes;
                if ((attrs & FileAttributes.Hidden) != 0 && depth > 0) return;
                if ((attrs & FileAttributes.System) != 0 && depth > 0) return;
                if ((attrs & FileAttributes.ReparsePoint) != 0 && depth > 0) return;

                // Skip common non-project directories
                var dirName = dirInfo.Name;
                if (ShouldSkipDirectory(dirName, path)) return;
            }
            catch
            {
                return;
            }

            progress.CurrentPath = path;
            progress.DirectoriesScanned++;

            if (progress.DirectoriesScanned % 100 == 0)
            {
                ReportProgress(progress);
            }

            try
            {
                // Check if this directory is a project root
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
                    return; // Don't scan subdirectories of a project
                }

                // Recursively scan subdirectories
                string[] subDirs;
                try
                {
                    subDirs = Directory.GetDirectories(path);
                }
                catch
                {
                    return;
                }

                foreach (var subDir in subDirs)
                {
                    if (cancellationToken.IsCancellationRequested) return;

                    var dirName = Path.GetFileName(subDir);
                    if (string.IsNullOrEmpty(dirName) || ShouldSkipDirectory(dirName, subDir))
                        continue;

                    try
                    {
                        ScanDirectory(subDir, projects, foundProjectRoots, visitedDirectories, progress, depth + 1, maxDepth, cancellationToken);
                    }
                    catch (OperationCanceledException) { return; }
                    catch (UnauthorizedAccessException) { }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Error scanning {subDir}: {ex.Message}");
                    }
                }
            }
            catch (OperationCanceledException) { return; }
            catch (UnauthorizedAccessException) { }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error in ScanDirectory for {path}: {ex.Message}");
            }
        }

        private bool ShouldSkipDirectory(string dirName, string fullPath)
        {
            // Check directory name against built-in skip list
            if (_skipDirectories.Contains(dirName) || dirName.StartsWith("."))
                return true;

            // Skip Linux/macOS system roots and descendants
            if (IsUnixSystemPath(fullPath))
                return true;

            // Check full path against user-defined exclude paths
            foreach (var excludePath in _excludePaths)
            {
                if (string.IsNullOrWhiteSpace(excludePath))
                    continue;

                var normalizedExcludePath = NormalizePathForComparison(excludePath);
                var normalizedFullPath = NormalizePathForComparison(fullPath);
                if (IsPathSameOrChild(normalizedFullPath, normalizedExcludePath))
                    return true;
            }

            return false;
        }

        private static bool IsUnixSystemPath(string path)
        {
            if (!(OperatingSystem.IsLinux() || OperatingSystem.IsMacOS()))
            {
                return false;
            }

            var normalizedPath = NormalizePathForComparison(path);
            if (string.IsNullOrEmpty(normalizedPath))
            {
                return false;
            }

            foreach (var prefix in _unixSystemPathPrefixes)
            {
                if (IsPathSameOrChild(normalizedPath, prefix))
                {
                    return true;
                }
            }

            return false;
        }

        private static string NormalizePathForComparison(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return string.Empty;
            }

            try
            {
                path = Path.GetFullPath(path);
            }
            catch
            {
                // Keep original path when full-path resolution fails.
            }

            path = path.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);

            if (path.Length > 1)
            {
                path = path.TrimEnd(Path.DirectorySeparatorChar);

                // Keep Windows drive roots normalized as "X:\" for correct boundary checks.
                if (OperatingSystem.IsWindows() && path.Length == 2 && path[1] == ':')
                {
                    path += Path.DirectorySeparatorChar;
                }
            }

            return path;
        }

        private static bool IsPathSameOrChild(string path, string basePath)
        {
            if (string.IsNullOrEmpty(path) || string.IsNullOrEmpty(basePath))
            {
                return false;
            }

            var comparison = GetPathComparison();
            if (!path.StartsWith(basePath, comparison))
            {
                return false;
            }

            if (path.Length == basePath.Length)
            {
                return true;
            }

            if (basePath[^1] == Path.DirectorySeparatorChar)
            {
                return true;
            }

            var nextChar = path[basePath.Length];
            return nextChar == Path.DirectorySeparatorChar;
        }

        private static StringComparison GetPathComparison()
        {
            return OperatingSystem.IsLinux() || OperatingSystem.IsMacOS()
                ? StringComparison.Ordinal
                : StringComparison.OrdinalIgnoreCase;
        }

        private static StringComparer GetPathComparer()
        {
            return OperatingSystem.IsLinux() || OperatingSystem.IsMacOS()
                ? StringComparer.Ordinal
                : StringComparer.OrdinalIgnoreCase;
        }

        private ProjectInfo? CheckForProject(string path)
        {
            try
            {
                string[] files;
                string[] dirs;
                try
                {
                    files = Directory.GetFiles(path);
                    dirs = Directory.GetDirectories(path);
                }
                catch
                {
                    return null;
                }

                string? matchedMarker = null;
                bool isExtensionMatch = false;

                // Check files for project markers
                foreach (var file in files)
                {
                    var fileName = Path.GetFileName(file);
                    var ext = Path.GetExtension(file);

                    // Check by extension (e.g. .csproj, .sln)
                    if (!string.IsNullOrEmpty(ext) && _extensionMarkers.Contains(ext))
                    {
                        matchedMarker = ext;
                        isExtensionMatch = true;
                        break;
                    }

                    // Check by filename (e.g. package.json, Cargo.toml)
                    if (_filenameMarkers.Contains(fileName))
                    {
                        matchedMarker = fileName;
                        isExtensionMatch = false;
                        break;
                    }
                }

                // Check for directory-based markers (like .xcodeproj)
                if (matchedMarker == null)
                {
                    foreach (var dir in dirs)
                    {
                        var subDirName = Path.GetFileName(dir);
                        if (subDirName.EndsWith(".xcodeproj", StringComparison.OrdinalIgnoreCase) ||
                            subDirName.EndsWith(".xcworkspace", StringComparison.OrdinalIgnoreCase))
                        {
                            matchedMarker = subDirName;
                            break;
                        }
                    }
                }

                if (matchedMarker != null)
                {
                    return CreateProjectInfo(path, files, matchedMarker, isExtensionMatch);
                }

                // Check for Python project with .py files but no marker files
                var pyFiles = files.Where(f => f.EndsWith(".py", StringComparison.OrdinalIgnoreCase)).ToList();
                if (pyFiles.Count >= 2) // At least 2 Python files suggests a project
                {
                    // Check if there's a main.py, app.py, __init__.py, or __main__.py
                    bool hasMainFile = pyFiles.Any(f =>
                    {
                        var name = Path.GetFileName(f);
                        return name.Equals("main.py", StringComparison.OrdinalIgnoreCase) ||
                               name.Equals("app.py", StringComparison.OrdinalIgnoreCase) ||
                               name.Equals("__init__.py", StringComparison.OrdinalIgnoreCase) ||
                               name.Equals("__main__.py", StringComparison.OrdinalIgnoreCase);
                    });

                    if (hasMainFile)
                    {
                        return CreateProjectInfo(path, files, "main.py", false);
                    }

                    // Or if there are 3+ .py files, treat it as a project
                    if (pyFiles.Count >= 3)
                    {
                        return CreateProjectInfo(path, files, "main.py", false);
                    }
                }

                // Check if it's a git repo with recognizable content
                bool hasGit = dirs.Any(d => Path.GetFileName(d).Equals(".git", StringComparison.OrdinalIgnoreCase));
                if (hasGit)
                {
                    bool hasReadme = files.Any(f => Path.GetFileName(f).Equals("README.md", StringComparison.OrdinalIgnoreCase));
                    if (hasReadme)
                    {
                        return CreateProjectInfo(path, files, ".git", false);
                    }
                }

                return null;
            }
            catch
            {
                return null;
            }
        }

        private ProjectInfo CreateProjectInfo(string projectPath, string[] files, string markerType, bool isExtensionMatch)
        {
            var dirInfo = new DirectoryInfo(projectPath);
            var tags = new List<string>();

            // Determine project type
            string projectType = "Unknown";
            if (isExtensionMatch)
            {
                _projectTypeByExtension.TryGetValue(markerType, out projectType!);
                projectType ??= "Unknown";

                // For extension markers, also add tags from the extension
                if (_technologyTags.TryGetValue(markerType, out var extTags))
                {
                    tags.AddRange(extTags);
                }
            }
            else
            {
                _projectTypeByFilename.TryGetValue(markerType, out projectType!);
                projectType ??= "Unknown";
            }

            // Get technology tags from all files in the directory
            try
            {
                foreach (var file in files)
                {
                    var fileName = Path.GetFileName(file);
                    if (_technologyTags.TryGetValue(fileName, out var fileTags))
                    {
                        tags.AddRange(fileTags);
                    }
                }
            }
            catch { }

            // Detect git branch
            string? gitBranch = null;
            try
            {
                var gitHeadPath = Path.Combine(projectPath, ".git", "HEAD");
                if (File.Exists(gitHeadPath))
                {
                    var headContent = File.ReadAllText(gitHeadPath).Trim();
                    if (headContent.StartsWith("ref: refs/heads/"))
                    {
                        gitBranch = headContent.Substring("ref: refs/heads/".Length);
                    }
                }
            }
            catch { }

            // Determine if project is active (modified within last 7 days)
            bool isActive = (DateTime.Now - dirInfo.LastWriteTime).TotalDays <= 7;

            // Determine category (Web, Desktop, Mobile, Cloud, Other)
            string category = DetectCategory(projectType, tags, files);

            // Ensure C#/.NET projects without tags get a default tag so UI cards keep consistent height
            try
            {
                var hasAnyTag = tags.Any();
                var hasCsprojFile = files.Any(f => f.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase));
                if (!hasAnyTag && (projectType?.Contains(".NET", StringComparison.OrdinalIgnoreCase) == true || hasCsprojFile))
                {
                    tags.Add("C# .NET Core");
                }
            }
            catch { }

            // Determine icon
            var (iconText, iconColor) = GetProjectIcon(projectType, tags);

            return new ProjectInfo
            {
                Name = dirInfo.Name,
                Path = projectPath,
                ProjectType = projectType,
                Category = category,
                Tags = tags.Distinct().Take(4).ToList(),
                LastModified = dirInfo.LastWriteTime,
                LastIndexed = DateTime.Now,
                IsActive = isActive,
                GitBranch = gitBranch,
                IconText = iconText,
                IconColor = iconColor
            };
        }

        public static string DetectCategory(string projectType, List<string> tags, string[] files)
        {
            // Check by project type
            if (_webProjectTypes.Contains(projectType))
                return "Web";
            if (_desktopProjectTypes.Contains(projectType))
                return "Desktop";
            if (_mobileProjectTypes.Contains(projectType))
                return "Mobile";
            if (_cloudProjectTypes.Contains(projectType))
                return "Cloud";

            // Check for Python web frameworks
            if (projectType == "Python" || tags.Contains("Python"))
            {
                // Check requirements.txt for web frameworks
                var requirementsPath = files.FirstOrDefault(f =>
                    Path.GetFileName(f).Equals("requirements.txt", StringComparison.OrdinalIgnoreCase));
                if (requirementsPath != null)
                {
                    try
                    {
                        var content = File.ReadAllText(requirementsPath).ToLower();
                        if (content.Contains("django") || content.Contains("flask") ||
                            content.Contains("fastapi") || content.Contains("streamlit") ||
                            content.Contains("tornado") || content.Contains("bottle"))
                        {
                            return "Web";
                        }
                    }
                    catch { }
                }

                // Check for common Python web framework files
                foreach (var file in files)
                {
                    var fileName = Path.GetFileName(file).ToLower();
                    if (fileName == "manage.py" || fileName == "wsgi.py" ||
                        fileName == "asgi.py" || fileName.Contains("flask") ||
                        fileName.Contains("fastapi"))
                    {
                        return "Web";
                    }
                }
            }

            // Check by tags
            foreach (var tag in tags)
            {
                if (_webProjectTypes.Contains(tag))
                    return "Web";
                if (_desktopProjectTypes.Contains(tag))
                    return "Desktop";
                if (_mobileProjectTypes.Contains(tag))
                    return "Mobile";
                if (_cloudProjectTypes.Contains(tag))
                    return "Cloud";
            }

            // Check by files
            foreach (var file in files)
            {
                var fileName = Path.GetFileName(file);
                if (_webProjectFiles.Contains(fileName))
                    return "Web";
                if (_desktopProjectFiles.Contains(fileName))
                    return "Desktop";
                if (_mobileProjectFiles.Contains(fileName))
                    return "Mobile";
                if (_cloudProjectFiles.Contains(fileName))
                    return "Cloud";
            }

            // Check for common web patterns in package.json projects
            if (tags.Contains("JavaScript") || tags.Contains("Node.js") || tags.Contains("TypeScript"))
            {
                // Default JS/TS projects to Web unless they have desktop/mobile indicators
                return "Web";
            }

            return "Other";
        }

        private static (string iconText, string iconColor) GetProjectIcon(string projectType, List<string> tags)
        {
            if (tags.Contains("React") || tags.Contains("Next.js"))
                return ("</>", "#2563EB");
            if (tags.Contains("Vue"))
                return ("V", "#42B883");
            if (tags.Contains("Angular"))
                return ("A", "#DD0031");
            if (tags.Contains("TypeScript"))
                return ("TS", "#3178C6");
            if (tags.Contains("Python"))
                return ("Py", "#3776AB");
            if (tags.Contains("Go"))
                return ("Go", "#00ADD8");
            if (tags.Contains("Rust"))
                return ("Rs", "#DEA584");
            if (tags.Contains("Java"))
                return ("Jv", "#ED8B00");
            if (tags.Contains("PHP"))
                return ("PHP", "#777BB4");
            if (tags.Contains("Ruby"))
                return ("Rb", "#CC342D");
            if (tags.Contains("Flutter") || tags.Contains("Dart"))
                return ("Fl", "#02569B");
            if (tags.Contains("iOS") || tags.Contains("Swift"))
                return ("iS", "#FA7343");
            if (tags.Contains("Docker"))
                return ("Dk", "#2496ED");
            if (projectType.Contains(".NET") || tags.Contains("C#"))
                return ("C#", "#512BD4");
            if (tags.Contains("JavaScript") || tags.Contains("Node.js"))
                return ("JS", "#F7DF1E");

            return ("P", "#6B7280");
        }

        private void ReportProgress(ScanProgress progress)
        {
            ProgressChanged?.Invoke(this, progress);
        }
    }
}
