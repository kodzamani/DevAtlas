using DevAtlas.Enums;

namespace DevAtlas.Models;

public class IncrementalScanResult
{
    public ScanStatus Status { get; set; }
    public string Message { get; set; } = "";
    public DateTime StartTime { get; set; }
    public DateTime EndTime { get; set; }
    public List<ProjectInfo> NewProjects { get; set; } = new();
    public List<ProjectInfo> ModifiedProjects { get; set; } = new();
    public List<ProjectInfo> DeletedProjects { get; set; } = new();
    public Exception? Exception { get; set; }

    public List<ProjectInfo> AllChanges => NewProjects.Concat(ModifiedProjects).ToList();

    public bool HasChanges => NewProjects.Any() || ModifiedProjects.Any() || DeletedProjects.Any();

    public TimeSpan Duration => EndTime - StartTime;
}