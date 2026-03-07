namespace DevAtlas.Models;

using Avalonia.Media;
using System.ComponentModel;
using System.Runtime.CompilerServices;

/// <summary>
/// Represents a single day cell in the contributions heatmap.
/// </summary>
public class GitContributionHeatmapCell : INotifyPropertyChanged
{
    private IBrush _fill = Brushes.Transparent;

    public DateTime Date { get; set; }
    public int Contributions { get; set; }
    public bool IsPlaceholder { get; set; }
    public IBrush Fill
    {
        get => _fill;
        set
        {
            if (ReferenceEquals(_fill, value)) return;
            _fill = value;
            OnPropertyChanged();
        }
    }
    public string DayLabel { get; set; } = string.Empty;
    public string ContributionLabel { get; set; } = string.Empty;
    public string DateLabel { get; set; } = string.Empty;

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
