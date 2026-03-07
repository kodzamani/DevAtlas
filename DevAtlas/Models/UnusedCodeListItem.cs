using Avalonia.Media;

namespace DevAtlas.Models;

public class UnusedCodeListItem
{
    public string Kind { get; set; } = "";
    public string Name { get; set; } = "";
    public string Location { get; set; } = "";
    public string HintsText { get; set; } = "";
    public SolidColorBrush KindBackground { get; set; } = new(Colors.Transparent);
    public SolidColorBrush KindForeground { get; set; } = new(Colors.Gray);
}