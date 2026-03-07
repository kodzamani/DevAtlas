namespace DevAtlas.Configuration
{
    public class ScalabilityConfiguration
    {
        /// <summary>
        /// Maximum number of projects to cache in memory
        /// </summary>
        public int MaxCachedProjects { get; set; } = 1000;

        /// <summary>
        /// Maximum number of concurrent scan operations
        /// </summary>
        public int MaxConcurrentScans { get; set; } = Math.Max(2, Environment.ProcessorCount - 1);

        /// <summary>
        /// Enable parallel scanning for improved performance
        /// </summary>
        public bool EnableParallelScanning { get; set; } = true;

        /// <summary>
        /// Enable incremental scanning to avoid full rescans
        /// </summary>
        public bool EnableIncrementalScanning { get; set; } = true;

        /// <summary>
        /// Interval between automatic scans
        /// </summary>
        public TimeSpan ScanInterval { get; set; } = TimeSpan.FromHours(24);

        /// <summary>
        /// High priority directories to scan first
        /// </summary>
        public List<string> PriorityDirectories { get; set; } = new()
        {
            "Users",
            "Projects",
            "Development",
            "Source",
            "Code",
            "Repos"
        };

        /// <summary>
        /// Maximum scan depth for directory traversal
        /// </summary>
        public int MaxScanDepth { get; set; } = 10;

        /// <summary>
        /// Page size for virtualized project loading
        /// </summary>
        public int ProjectPageSize { get; set; } = 50;

        /// <summary>
        /// Enable UI virtualization for large project lists
        /// </summary>
        public bool EnableUIVirtualization { get; set; } = true;

        /// <summary>
        /// Cache expiration time for project data
        /// </summary>
        public TimeSpan CacheExpiration { get; set; } = TimeSpan.FromHours(1);

        /// <summary>
        /// Enable database indexing for better query performance
        /// </summary>
        public bool EnableDatabaseIndexing { get; set; } = true;

        /// <summary>
        /// Maximum memory usage in MB before cache cleanup
        /// </summary>
        public int MaxMemoryUsageMB { get; set; } = 512;

        /// <summary>
        /// Enable performance monitoring
        /// </summary>
        public bool EnablePerformanceMonitoring { get; set; } = true;

        /// <summary>
        /// Directories to skip during scanning
        /// </summary>
        public List<string> SkipDirectories { get; set; } = new()
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
            // Linux/macOS system locations (avoid scanning virtual/system file trees)
            "proc", "sys", "dev", "run", "snap", "tmp", "usr", "lib", "lib64", "sbin", "etc", "var", "lost+found",
            // exclude flutter projects from scans
            "flutter"
        };
    }
}
