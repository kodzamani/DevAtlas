namespace DevAtlas.Models;

public class ProjectStatistics
{
    public int ProjectCount { get; set; }
    public long FileCount { get; set; }
    public long LinesCount { get; set; }
    public DateTime CalculatedAt { get; set; }
}