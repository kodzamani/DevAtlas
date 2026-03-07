using DevAtlas.Configuration;
using DevAtlas.Models;
using DevAtlas.Services;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace DevAtlas.Data
{
    public class DataMigrationService
    {
        private readonly DevAtlasDbContext _dbContext;
        private readonly ScalabilityConfiguration _config;
        private readonly FeatureFlags _featureFlags;
        private readonly ILogger<DataMigrationService>? _logger;
        private readonly string _jsonIndexPath;

        public DataMigrationService(
            DevAtlasDbContext dbContext,
            ScalabilityConfiguration config,
            FeatureFlags featureFlags,
            ILogger<DataMigrationService>? logger = null)
        {
            _dbContext = dbContext;
            _config = config;
            _featureFlags = featureFlags;
            _logger = logger;

            var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var appFolder = Path.Combine(appDataPath, "DevAtlas");
            _jsonIndexPath = Path.Combine(appFolder, "project_index.json");
        }

        public async Task<MigrationResult> MigrateIfNeededAsync()
        {
            var result = new MigrationResult();

            try
            {
                // Check if database exists and is healthy
                if (await _dbContext.IsDatabaseHealthyAsync())
                {
                    var projectCount = await _dbContext.GetProjectCountAsync();
                    _logger?.LogInformation($"Database exists with {projectCount} projects");

                    if (projectCount > 0)
                    {
                        result.Status = MigrationStatus.AlreadyMigrated;
                        result.Message = "Database already exists and contains projects";
                        return result;
                    }
                }

                // Check if JSON data exists for migration
                if (!File.Exists(_jsonIndexPath))
                {
                    result.Status = MigrationStatus.NoDataToMigrate;
                    result.Message = "No JSON data found for migration";
                    return result;
                }

                // Perform migration
                result = await MigrateFromJsonAsync();
            }
            catch (Exception ex)
            {
                result.Status = MigrationStatus.Failed;
                result.Message = $"Migration failed: {ex.Message}";
                result.Exception = ex;
                _logger?.LogError(ex, "Data migration failed");
            }

            return result;
        }

        private async Task<MigrationResult> MigrateFromJsonAsync()
        {
            var result = new MigrationResult
            {
                Status = MigrationStatus.InProgress,
                Message = "Starting migration from JSON to SQLite"
            };

            _logger?.LogInformation("Starting migration from JSON to SQLite");

            try
            {
                // Ensure database is created
                await _dbContext.Database.EnsureCreatedAsync();

                // Read JSON data
                var jsonContent = await File.ReadAllTextAsync(_jsonIndexPath);
                var indexData = JsonSerializer.Deserialize<ProjectIndexData>(jsonContent, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                if (indexData?.Projects == null || !indexData.Projects.Any())
                {
                    result.Status = MigrationStatus.NoDataToMigrate;
                    result.Message = "No projects found in JSON data";
                    return result;
                }

                var projectsToMigrate = indexData.Projects;
                result.TotalProjects = projectsToMigrate.Count;

                _logger?.LogInformation($"Found {projectsToMigrate.Count} projects to migrate");

                // Migrate projects in batches to avoid memory issues
                const int batchSize = 100;
                var migratedCount = 0;

                for (int i = 0; i < projectsToMigrate.Count; i += batchSize)
                {
                    var batch = projectsToMigrate.Skip(i).Take(batchSize).ToList();
                    await MigrateBatchAsync(batch);
                    migratedCount += batch.Count;

                    result.MigratedProjects = migratedCount;
                    _logger?.LogInformation($"Migrated {migratedCount}/{projectsToMigrate.Count} projects");

                    // Small delay to prevent overwhelming the system
                    await Task.Delay(10);
                }

                // Create backup of original JSON file
                if (_featureFlags.MaintainJsonCompatibility)
                {
                    var backupPath = _jsonIndexPath + ".backup";
                    File.Copy(_jsonIndexPath, backupPath, overwrite: true);
                    _logger?.LogInformation($"Created backup at {backupPath}");
                }

                result.Status = MigrationStatus.Completed;
                result.Message = $"Successfully migrated {migratedCount} projects from JSON to SQLite";
                _logger?.LogInformation(result.Message);

                return result;
            }
            catch (Exception ex)
            {
                result.Status = MigrationStatus.Failed;
                result.Message = $"Migration failed: {ex.Message}";
                result.Exception = ex;
                _logger?.LogError(ex, "Migration failed");
                ErrorDialogService.ShowException(ex, "Data migration failed.");
                return result;
            }
        }

        private async Task MigrateBatchAsync(List<ProjectInfo> projectInfos)
        {
            var projects = new List<Project>();
            var allTags = new Dictionary<string, ProjectTag>();

            foreach (var projectInfo in projectInfos)
            {
                var project = new Project
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

                // Process tags
                if (projectInfo.Tags != null)
                {
                    foreach (var tagName in projectInfo.Tags)
                    {
                        if (!allTags.TryGetValue(tagName, out var tag))
                        {
                            tag = new ProjectTag
                            {
                                Name = tagName,
                                Color = GetTagColor(tagName)
                            };
                            allTags[tagName] = tag;
                        }
                        project.Tags.Add(tag);
                    }
                }

                projects.Add(project);
            }

            // Add tags first to avoid foreign key issues
            var uniqueTags = allTags.Values.Distinct().ToList();
            await _dbContext.Tags.AddRangeAsync(uniqueTags);
            await _dbContext.SaveChangesAsync();

            // Add projects
            await _dbContext.Projects.AddRangeAsync(projects);
            await _dbContext.SaveChangesAsync();
        }

        private string GetTagColor(string tagName)
        {
            return tagName.ToLower() switch
            {
                "c#" => "#512BD4",
                "javascript" => "#F7DF1E",
                "typescript" => "#3178C6",
                "python" => "#3776AB",
                "react" => "#61DAFB",
                "vue" => "#42B883",
                "angular" => "#DD0031",
                "node.js" => "#339933",
                "docker" => "#2496ED",
                "rust" => "#DEA584",
                "go" => "#00ADD8",
                "java" => "#ED8B00",
                "php" => "#777BB4",
                "ruby" => "#CC342D",
                "flutter" => "#02569B",
                "swift" => "#FA7343",
                _ => "#6B7280"
            };
        }

        public async Task<bool> ValidateMigrationAsync()
        {
            try
            {
                var dbProjectCount = await _dbContext.GetProjectCountAsync();

                if (!File.Exists(_jsonIndexPath))
                {
                    _logger?.LogInformation("No JSON file exists for comparison");
                    return dbProjectCount > 0;
                }

                var jsonContent = await File.ReadAllTextAsync(_jsonIndexPath);
                var indexData = JsonSerializer.Deserialize<ProjectIndexData>(jsonContent, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                var jsonProjectCount = indexData?.Projects?.Count ?? 0;

                _logger?.LogInformation($"Database: {dbProjectCount} projects, JSON: {jsonProjectCount} projects");

                return dbProjectCount >= jsonProjectCount;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Migration validation failed");
                return false;
            }
        }

        public async Task CleanupJsonBackupAsync()
        {
            if (!_featureFlags.MaintainJsonCompatibility)
            {
                var backupPath = _jsonIndexPath + ".backup";
                if (File.Exists(backupPath))
                {
                    File.Delete(backupPath);
                    _logger?.LogInformation("Cleaned up JSON backup file");
                }
            }
        }
    }
}
