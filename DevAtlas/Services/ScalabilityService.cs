using DevAtlas.Configuration;
using DevAtlas.Data;
using DevAtlas.Enums;
using DevAtlas.Models;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace DevAtlas.Services
{
    public class ScalabilityService
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ScalabilityConfiguration _config;
        private readonly FeatureFlags _featureFlags;
        private readonly ILogger<ScalabilityService>? _logger;
        private readonly SemaphoreSlim _initializationLock = new(1, 1);

        private DevAtlasDbContext? _dbContext;
        private ScalableProjectIndex? _projectIndex;
        private ParallelProjectScanner? _parallelScanner;
        private IncrementalScanner? _incrementalScanner;
        private DataMigrationService? _migrationService;

        private bool _isInitialized = false;

        public event EventHandler<List<ProjectInfo>>? ProjectsUpdated;
        public event EventHandler<ScanProgress>? ScanProgress;
        public event EventHandler<string>? StatusChanged;

        public ScalabilityService(
            IServiceProvider serviceProvider,
            ScalabilityConfiguration config,
            FeatureFlags featureFlags,
            ILogger<ScalabilityService>? logger = null)
        {
            _serviceProvider = serviceProvider;
            _config = config;
            _featureFlags = featureFlags;
            _logger = logger;
        }

        public async Task InitializeAsync()
        {
            if (_isInitialized) return;

            await _initializationLock.WaitAsync();
            try
            {
                if (_isInitialized) return;

                StatusChanged?.Invoke(this, "Initializing scalability services...");

                // Initialize database context
                _dbContext = _serviceProvider.GetRequiredService<DevAtlasDbContext>();
                await _dbContext.Database.EnsureCreatedAsync();

                // Initialize migration service
                _migrationService = new DataMigrationService(_dbContext, _config, _featureFlags, null);

                // Perform migration if needed
                if (_featureFlags.EnableSqliteDatabase)
                {
                    StatusChanged?.Invoke(this, "Migrating data to new database format...");
                    var migrationResult = await _migrationService.MigrateIfNeededAsync();

                    if (migrationResult.Status == MigrationStatus.Failed)
                    {
                        var migrationMessage = $"Data migration failed: {migrationResult.Message}";
                        StatusChanged?.Invoke(this, migrationMessage);
                        _logger?.LogError(migrationMessage);
                        ErrorDialogService.ShowError(migrationMessage, LanguageManager.Instance["MessageInitializationError"]);
                        return;
                    }

                    _logger?.LogInformation($"Migration completed: {migrationResult.Message}");
                }

                // Initialize project index
                _projectIndex = new ScalableProjectIndex(
                    _dbContext,
                    _config,
                    _featureFlags,
                    _serviceProvider.GetRequiredService<IMemoryCache>(),
                    null);

                _projectIndex.ProjectsUpdated += (sender, projects) => ProjectsUpdated?.Invoke(this, projects);

                // Initialize scanners
                _parallelScanner = new ParallelProjectScanner(_dbContext, _config, null);
                _parallelScanner.ProgressChanged += (sender, progress) => ScanProgress?.Invoke(this, progress);

                if (_config.EnableIncrementalScanning && _featureFlags.EnableRealTimeMonitoring)
                {
                    _incrementalScanner = new IncrementalScanner(_dbContext, _config, null);
                    _incrementalScanner.ProgressChanged += (sender, progress) => ScanProgress?.Invoke(this, progress);
                    _incrementalScanner.ProjectsChanged += (sender, projects) => ProjectsUpdated?.Invoke(this, projects);

                    await _incrementalScanner.StartAsync();
                }

                _isInitialized = true;
                StatusChanged?.Invoke(this, "Scalability services initialized successfully");
                _logger?.LogInformation("Scalability services initialized successfully");
            }
            catch (Exception ex)
            {
                StatusChanged?.Invoke(this, $"Failed to initialize scalability services: {ex.Message}");
                _logger?.LogError(ex, "Failed to initialize scalability services");
                ErrorDialogService.ShowException(ex, "Failed to initialize scalability services.");
                return;
            }
            finally
            {
                _initializationLock.Release();
            }
        }

        public async Task ShutdownAsync()
        {
            if (!_isInitialized) return;

            StatusChanged?.Invoke(this, "Shutting down scalability services...");

            try
            {
                if (_incrementalScanner != null)
                {
                    await _incrementalScanner.StopAsync();
                }

                if (_dbContext != null)
                {
                    await _dbContext.DisposeAsync();
                }

                _isInitialized = false;
                StatusChanged?.Invoke(this, "Scalability services shut down successfully");
                _logger?.LogInformation("Scalability services shut down successfully");
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error during scalability services shutdown");
            }
        }

        public async Task<List<ProjectInfo>> LoadProjectsAsync(int page = 0, int pageSize = 0)
        {
            if (!EnsureInitialized(nameof(LoadProjectsAsync)))
            {
                return new List<ProjectInfo>();
            }

            return await _projectIndex!.LoadProjectsAsync(page, pageSize);
        }

        public async Task<PagedResult<ProjectInfo>> GetProjectsPagedAsync(int pageNumber, int pageSize)
        {
            if (!EnsureInitialized(nameof(GetProjectsPagedAsync)))
            {
                return new PagedResult<ProjectInfo>();
            }

            return await _projectIndex!.GetProjectsPagedAsync(pageNumber, pageSize);
        }

        public async Task<List<ProjectInfo>> SearchProjectsAsync(string searchTerm)
        {
            if (!EnsureInitialized(nameof(SearchProjectsAsync)))
            {
                return new List<ProjectInfo>();
            }

            return await _projectIndex!.SearchProjectsAsync(searchTerm);
        }

        public async Task<List<ProjectInfo>> ScanAllDrivesAsync(CancellationToken cancellationToken = default)
        {
            if (!EnsureInitialized(nameof(ScanAllDrivesAsync)))
            {
                return new List<ProjectInfo>();
            }

            StatusChanged?.Invoke(this, "Starting full project scan...");

            var projects = await _parallelScanner!.ScanAllDrivesAsync(cancellationToken);

            // Save to database
            await _projectIndex!.SaveProjectsAsync(projects);

            StatusChanged?.Invoke(this, $"Scan completed: {projects.Count} projects found");

            return projects;
        }

        public async Task<IncrementalScanResult> ScanForChangesAsync()
        {
            if (!EnsureInitialized(nameof(ScanForChangesAsync)))
            {
                return new IncrementalScanResult
                {
                    Status = ScanStatus.Failed,
                    Message = "Scalability service is not initialized."
                };
            }

            if (_incrementalScanner == null)
            {
                const string message = "Incremental scanning is not enabled.";
                StatusChanged?.Invoke(this, message);
                _logger?.LogWarning(message);
                ErrorDialogService.ShowError(message);
                return new IncrementalScanResult
                {
                    Status = ScanStatus.Failed,
                    Message = message
                };
            }

            StatusChanged?.Invoke(this, "Starting incremental scan...");

            var result = await _incrementalScanner.ScanForChangesAsync();

            StatusChanged?.Invoke(this, result.Message);

            return result;
        }

        public async Task<bool> NeedsRescanAsync()
        {
            if (!EnsureInitialized(nameof(NeedsRescanAsync)))
            {
                return false;
            }

            return await _projectIndex!.NeedsRescanAsync();
        }

        public async Task<int> GetProjectCountAsync()
        {
            if (!EnsureInitialized(nameof(GetProjectCountAsync)))
            {
                return 0;
            }

            return await _projectIndex!.GetProjectCountAsync();
        }

        public async Task<Dictionary<string, int>> GetProjectStatisticsAsync()
        {
            if (!EnsureInitialized(nameof(GetProjectStatisticsAsync)))
            {
                return new Dictionary<string, int>();
            }

            return await _projectIndex!.GetProjectStatisticsAsync();
        }

        public async Task AddProjectAsync(ProjectInfo project)
        {
            if (!EnsureInitialized(nameof(AddProjectAsync)))
            {
                return;
            }

            await _projectIndex!.AddProjectAsync(project);
        }

        public async Task UpdateProjectAsync(ProjectInfo project)
        {
            if (!EnsureInitialized(nameof(UpdateProjectAsync)))
            {
                return;
            }

            await _projectIndex!.UpdateProjectAsync(project);
        }

        public async Task RemoveProjectAsync(string projectId)
        {
            if (!EnsureInitialized(nameof(RemoveProjectAsync)))
            {
                return;
            }

            await _projectIndex!.RemoveProjectAsync(projectId);
        }

        public void ClearCache()
        {
            if (!EnsureInitialized(nameof(ClearCache)))
            {
                return;
            }

            _projectIndex!.ClearCache();
        }

        public ScalabilityConfiguration GetConfiguration()
        {
            return _config;
        }

        public FeatureFlags GetFeatureFlags()
        {
            return _featureFlags;
        }

        public async Task<bool> IsDatabaseHealthyAsync()
        {
            if (_dbContext == null) return false;
            return await _dbContext.IsDatabaseHealthyAsync();
        }

        public async Task<MigrationResult> ValidateMigrationAsync()
        {
            if (_migrationService == null)
            {
                return new MigrationResult { Status = MigrationStatus.NotStarted, Message = "Migration service not initialized" };
            }

            var isValid = await _migrationService.ValidateMigrationAsync();
            return new MigrationResult
            {
                Status = isValid ? MigrationStatus.Completed : MigrationStatus.Failed,
                Message = isValid ? "Migration validation successful" : "Migration validation failed"
            };
        }

        public async Task CleanupAsync()
        {
            if (_migrationService != null)
            {
                await _migrationService.CleanupJsonBackupAsync();
            }
        }

        private bool EnsureInitialized(string operationName)
        {
            if (_isInitialized)
            {
                return true;
            }

            var message = $"Scalability service is not initialized. Operation: {operationName}";
            StatusChanged?.Invoke(this, message);
            _logger?.LogWarning(message);
            ErrorDialogService.ShowError(message, LanguageManager.Instance["MessageInitializationError"]);
            return false;
        }

        // Factory method for easy setup
        public static ScalabilityService CreateDefault(IServiceProvider serviceProvider)
        {
            var config = LoadConfiguration();
            var featureFlags = LoadFeatureFlags();
            var logger = serviceProvider.GetService<ILogger<ScalabilityService>>();

            return new ScalabilityService(serviceProvider, config, featureFlags, logger);
        }

        private static ScalabilityConfiguration LoadConfiguration()
        {
            var configuration = new ConfigurationBuilder()
                .SetBasePath(AppDomain.CurrentDomain.BaseDirectory)
                .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
                .AddJsonFile($"appsettings.{Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT") ?? "Production"}.json", optional: true)
                .Build();

            var config = new ScalabilityConfiguration();
            configuration.GetSection("Scalability").Bind(config);

            return config;
        }

        private static FeatureFlags LoadFeatureFlags()
        {
            var configuration = new ConfigurationBuilder()
                .SetBasePath(AppDomain.CurrentDomain.BaseDirectory)
                .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
                .AddJsonFile($"appsettings.{Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT") ?? "Production"}.json", optional: true)
                .Build();

            var featureFlags = new FeatureFlags();
            configuration.GetSection("FeatureFlags").Bind(featureFlags);

            return featureFlags;
        }
    }
}
