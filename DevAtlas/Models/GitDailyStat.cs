namespace DevAtlas.Models;

/// <summary>
/// Represents daily Git statistics for a specific project.
/// </summary>
public class GitDailyStat
{
    public DateTime Date { get; set; }
    public string ProjectName { get; set; } = string.Empty;
    public int Additions { get; set; }
    public int Deletions { get; set; }
    public int Commits { get; set; }
    public int TotalChanges => Additions + Deletions;
}
