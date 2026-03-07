namespace DevAtlas.Models;

/// <summary>
/// Represents a selectable git project filter option.
/// </summary>
public class GitProjectFilterOption
{
    public string DisplayName { get; set; } = string.Empty;
    public string? ProjectName { get; set; }
    public bool IsAllProjects => string.IsNullOrWhiteSpace(ProjectName);
}
