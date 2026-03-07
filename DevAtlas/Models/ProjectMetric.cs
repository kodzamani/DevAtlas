namespace DevAtlas.Models;

/// <summary>
/// Represents a project metric (lines of code, file count, etc.)
/// </summary>
public class ProjectMetric
{
    public string ProjectName { get; set; } = string.Empty;
    public string ProjectType { get; set; } = string.Empty;
    public int Value { get; set; }
}
