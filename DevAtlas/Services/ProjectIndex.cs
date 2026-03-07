using DevAtlas.Data;
using DevAtlas.Models;
using System.Text.Json;

namespace DevAtlas.Services
{
    public class ProjectIndex
    {
        private const int MaxCacheSize = 200; // Reduced from 1000 to minimize memory usage
        private readonly string _indexPath;
        private List<ProjectInfo> _cachedProjects = new();
        private DateTime _lastIndexed = DateTime.MinValue;

        private static readonly JsonSerializerOptions _jsonOptions = new()
        {
            WriteIndented = false, // Compact JSON to reduce file size
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        public event EventHandler<List<ProjectInfo>>? ProjectsUpdated;

        public ProjectIndex()
        {
            var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var appFolder = Path.Combine(appDataPath, "DevAtlas");

            if (!Directory.Exists(appFolder))
            {
                Directory.CreateDirectory(appFolder);
            }

            _indexPath = Path.Combine(appFolder, "project_index.json");
        }

        public async Task<List<ProjectInfo>> LoadProjectsAsync()
        {
            if (_cachedProjects.Any())
            {
                return _cachedProjects;
            }

            if (!File.Exists(_indexPath))
            {
                return new List<ProjectInfo>();
            }

            try
            {
                // Use streaming to read file more efficiently
                using var fileStream = File.OpenRead(_indexPath);
                using var streamReader = new StreamReader(fileStream);
                var json = await streamReader.ReadToEndAsync();

                var indexData = JsonSerializer.Deserialize<ProjectIndexData>(json, _jsonOptions);

                if (indexData != null)
                {
                    // Limit loaded projects to cache size to reduce memory
                    var allProjects = indexData.Projects ?? new List<ProjectInfo>();
                    _cachedProjects = allProjects.Count > MaxCacheSize
                        ? allProjects.Take(MaxCacheSize).ToList()
                        : allProjects;
                    _lastIndexed = indexData.LastIndexed;
                    // Ensure C#/.NET projects in cache have a default tag when missing
                    try
                    {
                        var updated = false;
                        foreach (var proj in _cachedProjects)
                        {
                            var hasTags = proj.Tags != null && proj.Tags.Any();
                            var looksLikeDotNet = !string.IsNullOrEmpty(proj.ProjectType) && proj.ProjectType.IndexOf(".NET", StringComparison.OrdinalIgnoreCase) >= 0;
                            var hasCsprojFile = false;
                            try
                            {
                                if (!string.IsNullOrEmpty(proj.Path) && Directory.Exists(proj.Path))
                                {
                                    hasCsprojFile = Directory.EnumerateFiles(proj.Path, "*.csproj").Any();
                                }
                            }
                            catch { }

                            if (!hasTags && (looksLikeDotNet || hasCsprojFile))
                            {
                                proj.Tags = proj.Tags ?? new List<string>();
                                proj.Tags.Add("C# .NET Core");
                                updated = true;
                            }
                        }

                        if (updated)
                        {
                            // Persist updated index so next app start has tags
                            _ = SaveProjectsAsync(_cachedProjects);
                        }
                    }
                    catch { }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error loading project index: {ex.Message}");
                _cachedProjects = new List<ProjectInfo>();
            }

            return _cachedProjects;
        }

        public async Task SaveProjectsAsync(List<ProjectInfo> projects)
        {
            var indexData = new ProjectIndexData
            {
                Projects = projects,
                LastIndexed = DateTime.Now,
                Version = 1
            };

            try
            {
                // Use streaming to write file more efficiently
                var json = JsonSerializer.Serialize(indexData, _jsonOptions);
                using var fileStream = File.OpenWrite(_indexPath);
                fileStream.SetLength(0); // Clear existing content
                using var streamWriter = new StreamWriter(fileStream);
                await streamWriter.WriteAsync(json);
                await streamWriter.FlushAsync();

                // Limit cache size to prevent unbounded growth
                _cachedProjects = projects.Count > MaxCacheSize
                    ? projects.Take(MaxCacheSize).ToList()
                    : projects;

                _lastIndexed = indexData.LastIndexed;
                ProjectsUpdated?.Invoke(this, _cachedProjects);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error saving project index: {ex.Message}");
                ErrorDialogService.ShowException(ex, "Project index could not be saved.");
            }
        }

        public async Task AddProjectAsync(ProjectInfo project)
        {
            var projects = await LoadProjectsAsync();

            var existingIndex = projects.FindIndex(p => p.Path == project.Path);
            if (existingIndex >= 0)
            {
                projects[existingIndex] = project;
            }
            else
            {
                projects.Add(project);
            }

            await SaveProjectsAsync(projects);
        }

        public async Task RemoveProjectAsync(string projectId)
        {
            var projects = await LoadProjectsAsync();
            projects.RemoveAll(p => p.Id == projectId);
            await SaveProjectsAsync(projects);
        }

        public async Task UpdateProjectAsync(ProjectInfo project)
        {
            var projects = await LoadProjectsAsync();
            var index = projects.FindIndex(p => p.Id == project.Id);

            if (index >= 0)
            {
                projects[index] = project;
                await SaveProjectsAsync(projects);
            }
        }

        public async Task<bool> NeedsRescanAsync()
        {
            if (!File.Exists(_indexPath))
                return true;

            var projects = await LoadProjectsAsync();

            // Rescan if no projects or last scan was more than 24 hours ago
            if (!projects.Any())
                return true;

            return (DateTime.Now - _lastIndexed).TotalHours > 24;
        }

        public DateTime GetLastIndexedTime()
        {
            return _lastIndexed;
        }

        public async Task<int> GetProjectCountAsync()
        {
            var projects = await LoadProjectsAsync();
            return projects.Count;
        }

        public void ClearCache()
        {
            _cachedProjects = new List<ProjectInfo>();
        }
    }
}
