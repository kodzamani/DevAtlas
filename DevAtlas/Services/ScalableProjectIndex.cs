using DevAtlas.Configuration;
using DevAtlas.Data;
using DevAtlas.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace DevAtlas.Services
{
    public class ScalableProjectIndex
    {
        private readonly DevAtlasDbContext _dbContext;
        private readonly ScalabilityConfiguration _config;
        private readonly FeatureFlags _featureFlags;
        private readonly IMemoryCache _cache;
        private readonly ILogger<ScalableProjectIndex>? _logger;
        private readonly SemaphoreSlim _cacheLock = new(1, 1);

        public event EventHandler<List<ProjectInfo>>? ProjectsUpdated;

        public ScalableProjectIndex(
            DevAtlasDbContext dbContext,
            ScalabilityConfiguration config,
            FeatureFlags featureFlags,
            IMemoryCache cache,
            ILogger<ScalableProjectIndex>? logger = null)
        {
            _dbContext = dbContext;
            _config = config;
            _featureFlags = featureFlags;
            _cache = cache;
            _logger = logger;
        }

        public async Task<List<ProjectInfo>> LoadProjectsAsync(int page = 0, int pageSize = 0)
        {
            if (pageSize == 0) pageSize = _config.ProjectPageSize;

            var cacheKey = $"projects_page_{page}_size_{pageSize}";

            if (_cache.TryGetValue(cacheKey, out List<ProjectInfo>? cachedProjects))
            {
                _logger?.LogDebug($"Cache hit for {cacheKey}");
                return cachedProjects ?? new List<ProjectInfo>();
            }

            try
            {
                var projects = await _dbContext.GetProjectsPagedAsync(page, pageSize);
                var projectInfos = ConvertToProjectInfos(projects);

                var cacheOptions = new MemoryCacheEntryOptions
                {
                    AbsoluteExpirationRelativeToNow = _config.CacheExpiration,
                    SlidingExpiration = TimeSpan.FromMinutes(30),
                    Size = projectInfos.Count
                };

                await _cacheLock.WaitAsync();
                try
                {
                    _cache.Set(cacheKey, projectInfos, cacheOptions);
                }
                finally
                {
                    _cacheLock.Release();
                }

                _logger?.LogInformation($"Loaded {projectInfos.Count} projects for page {page}");
                return projectInfos;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, $"Error loading projects for page {page}");
                return new List<ProjectInfo>();
            }
        }

        public async Task<PagedResult<ProjectInfo>> GetProjectsPagedAsync(int pageNumber, int pageSize)
        {
            try
            {
                var totalCount = await _dbContext.GetProjectCountAsync();
                var projects = await LoadProjectsAsync(pageNumber, pageSize);

                return new PagedResult<ProjectInfo>
                {
                    Items = projects,
                    TotalCount = totalCount,
                    PageNumber = pageNumber,
                    PageSize = pageSize
                };
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error getting paged projects");
                return new PagedResult<ProjectInfo>();
            }
        }

        public async Task<List<ProjectInfo>> SearchProjectsAsync(string searchTerm)
        {
            var cacheKey = $"search_{searchTerm.GetHashCode()}";

            if (_cache.TryGetValue(cacheKey, out List<ProjectInfo>? cachedResults))
            {
                return cachedResults ?? new List<ProjectInfo>();
            }

            try
            {
                var projects = await _dbContext.SearchProjectsAsync(searchTerm);
                var projectInfos = ConvertToProjectInfos(projects);

                var cacheOptions = new MemoryCacheEntryOptions
                {
                    AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(15),
                    Size = projectInfos.Count
                };

                _cache.Set(cacheKey, projectInfos, cacheOptions);

                _logger?.LogInformation($"Search for '{searchTerm}' returned {projectInfos.Count} results");
                return projectInfos;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, $"Error searching projects for term '{searchTerm}'");
                return new List<ProjectInfo>();
            }
        }

        public async Task SaveProjectsAsync(List<ProjectInfo> projectInfos)
        {
            if (!projectInfos.Any()) return;

            try
            {
                await _cacheLock.WaitAsync();
                try
                {
                    // Clear relevant cache entries
                    ClearProjectCache();

                    var projects = ConvertFromProjectInfos(projectInfos);

                    // Use upsert pattern - update existing, insert new
                    foreach (var project in projects)
                    {
                        var existingProject = await _dbContext.Projects
                            .Include(p => p.Tags)
                            .FirstOrDefaultAsync(p => p.Path == project.Path);

                        if (existingProject != null)
                        {
                            // Update existing project
                            existingProject.Name = project.Name;
                            existingProject.ProjectType = project.ProjectType;
                            existingProject.Category = project.Category;
                            existingProject.LastModified = project.LastModified;
                            existingProject.LastIndexed = project.LastIndexed;
                            existingProject.IsActive = project.IsActive;
                            existingProject.GitBranch = project.GitBranch;
                            existingProject.IconText = project.IconText;
                            existingProject.IconColor = project.IconColor;

                            // Update tags
                            UpdateProjectTags(existingProject, project.Tags?.Select(t => t.Name).ToList());
                        }
                        else
                        {
                            // Add new project
                            _dbContext.Projects.Add(project);
                        }
                    }

                    await _dbContext.SaveChangesAsync();

                    // Update cache with new data
                    var cacheKey = "projects_page_0_size_" + _config.ProjectPageSize;
                    var updatedProjects = ConvertToProjectInfos(projects);
                    _cache.Set(cacheKey, updatedProjects, new MemoryCacheEntryOptions
                    {
                        AbsoluteExpirationRelativeToNow = _config.CacheExpiration,
                        Size = updatedProjects.Count
                    });

                    ProjectsUpdated?.Invoke(this, updatedProjects);
                    _logger?.LogInformation($"Saved {projects.Count} projects to database");
                }
                finally
                {
                    _cacheLock.Release();
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error saving projects to database");
                ErrorDialogService.ShowException(ex, "Projects could not be saved to the database.");
            }
        }

        public async Task AddProjectAsync(ProjectInfo projectInfo)
        {
            try
            {
                await _cacheLock.WaitAsync();
                try
                {
                    ClearProjectCache();

                    var existingProject = await _dbContext.GetProjectByPathAsync(projectInfo.Path);

                    if (existingProject != null)
                    {
                        // Update existing
                        UpdateProjectFromInfo(existingProject, projectInfo);
                        await _dbContext.SaveChangesAsync();
                    }
                    else
                    {
                        // Add new
                        var project = ConvertFromProjectInfo(projectInfo);
                        _dbContext.Projects.Add(project);
                        await _dbContext.SaveChangesAsync();
                    }

                    ProjectsUpdated?.Invoke(this, new List<ProjectInfo> { projectInfo });
                    _logger?.LogInformation($"Added/updated project: {projectInfo.Name}");
                }
                finally
                {
                    _cacheLock.Release();
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, $"Error adding project {projectInfo.Name}");
                ErrorDialogService.ShowException(ex, $"Project '{projectInfo.Name}' could not be added.");
            }
        }

        public async Task RemoveProjectAsync(string projectId)
        {
            try
            {
                await _cacheLock.WaitAsync();
                try
                {
                    ClearProjectCache();

                    var project = await _dbContext.Projects.FindAsync(int.Parse(projectId));
                    if (project != null)
                    {
                        _dbContext.Projects.Remove(project);
                        await _dbContext.SaveChangesAsync();

                        _logger?.LogInformation($"Removed project with ID: {projectId}");
                    }
                }
                finally
                {
                    _cacheLock.Release();
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, $"Error removing project with ID: {projectId}");
                ErrorDialogService.ShowException(ex, $"Project with ID {projectId} could not be removed.");
            }
        }

        public async Task UpdateProjectAsync(ProjectInfo projectInfo)
        {
            await AddProjectAsync(projectInfo); // Reuse the add logic which handles updates
        }

        public async Task<bool> NeedsRescanAsync()
        {
            try
            {
                // Check if we have any projects
                var projectCount = await _dbContext.GetProjectCountAsync();
                if (projectCount == 0) return true;

                // Check last scan time
                var lastScan = await _dbContext.ScanHistory
                    .OrderByDescending(h => h.ScanStartTime)
                    .FirstOrDefaultAsync();

                if (lastScan == null) return true;

                // Check if last scan was more than the configured interval ago
                return DateTime.Now - lastScan.ScanStartTime > _config.ScanInterval;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error checking if rescan is needed");
                return true; // Default to rescan if there's an error
            }
        }

        public async Task<DateTime> GetLastIndexedTime()
        {
            try
            {
                var lastScan = await _dbContext.ScanHistory
                    .OrderByDescending(h => h.ScanStartTime)
                    .FirstOrDefaultAsync();

                return lastScan?.ScanStartTime ?? DateTime.MinValue;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error getting last indexed time");
                return DateTime.MinValue;
            }
        }

        public async Task<int> GetProjectCountAsync()
        {
            try
            {
                return await _dbContext.GetProjectCountAsync();
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error getting project count");
                return 0;
            }
        }

        public void ClearCache()
        {
            _cacheLock.Wait();
            try
            {
                _cache.Remove("projects_page_0_size_" + _config.ProjectPageSize);
                _logger?.LogInformation("Cleared project cache");
            }
            finally
            {
                _cacheLock.Release();
            }
        }

        private void ClearProjectCache()
        {
            // Clear all project-related cache entries
            var keysToRemove = new List<string>();

            if (_cache is MemoryCache memoryCache)
            {
                // This is a bit of a hack, but necessary since IMemoryCache doesn't expose enumeration
                // In a production scenario, we might want to implement a more sophisticated cache key management
                for (int i = 0; i < 100; i++) // Assume max 100 pages cached
                {
                    keysToRemove.Add($"projects_page_{i}_size_{_config.ProjectPageSize}");
                }
            }

            foreach (var key in keysToRemove)
            {
                _cache.Remove(key);
            }
        }

        private List<ProjectInfo> ConvertToProjectInfos(List<Project> projects)
        {
            return projects.Select(p => new ProjectInfo
            {
                Id = p.Id.ToString(),
                Name = p.Name,
                Path = p.Path,
                ProjectType = p.ProjectType,
                Category = p.Category,
                Tags = p.Tags?.Select(t => t.Name).ToList() ?? new List<string>(),
                LastModified = p.LastModified,
                LastIndexed = p.LastIndexed,
                IsActive = p.IsActive,
                GitBranch = p.GitBranch,
                IconText = p.IconText,
                IconColor = p.IconColor
            }).ToList();
        }

        private List<Project> ConvertFromProjectInfos(List<ProjectInfo> projectInfos)
        {
            return projectInfos.Select(pi => ConvertFromProjectInfo(pi)).ToList();
        }

        private Project ConvertFromProjectInfo(ProjectInfo projectInfo)
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

        private void UpdateProjectFromInfo(Project project, ProjectInfo projectInfo)
        {
            project.Name = projectInfo.Name;
            project.ProjectType = projectInfo.ProjectType;
            project.Category = projectInfo.Category;
            project.LastModified = projectInfo.LastModified;
            project.LastIndexed = projectInfo.LastIndexed;
            project.IsActive = projectInfo.IsActive;
            project.GitBranch = projectInfo.GitBranch;
            project.IconText = projectInfo.IconText;
            project.IconColor = projectInfo.IconColor;

            UpdateProjectTags(project, projectInfo.Tags);
        }

        private void UpdateProjectTags(Project project, List<string>? tagNames)
        {
            if (tagNames == null || !tagNames.Any()) return;

            // Clear existing tags
            project.Tags.Clear();

            // Add new tags
            foreach (var tagName in tagNames)
            {
                var tag = _dbContext.Tags.FirstOrDefault(t => t.Name == tagName);
                if (tag == null)
                {
                    tag = new ProjectTag
                    {
                        Name = tagName,
                        Color = GetTagColor(tagName)
                    };
                    _dbContext.Tags.Add(tag);
                }
                project.Tags.Add(tag);
            }
        }

        private string GetTagColor(string tagName)
        {
            return tagName.ToLowerInvariant() switch
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

        public async Task<Dictionary<string, int>> GetProjectStatisticsAsync()
        {
            try
            {
                var stats = new Dictionary<string, int>
                {
                    ["Total"] = await _dbContext.Projects.CountAsync(),
                    ["Active"] = await _dbContext.Projects.CountAsync(p => p.IsActive),
                    ["Web"] = await _dbContext.Projects.CountAsync(p => p.Category == "Web"),
                    ["Desktop"] = await _dbContext.Projects.CountAsync(p => p.Category == "Desktop"),
                    ["Mobile"] = await _dbContext.Projects.CountAsync(p => p.Category == "Mobile"),
                    ["Cloud"] = await _dbContext.Projects.CountAsync(p => p.Category == "Cloud"),
                    ["Other"] = await _dbContext.Projects.CountAsync(p => p.Category == "Other")
                };

                return stats;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error getting project statistics");
                return new Dictionary<string, int>();
            }
        }
    }
}
