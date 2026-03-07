namespace DevAtlas.Models;

using System.Collections.ObjectModel;

/// <summary>
/// Represents a week column in the contributions heatmap.
/// </summary>
public class GitContributionHeatmapWeek
{
    public ObservableCollection<GitContributionHeatmapCell> Days { get; } = [];
}
