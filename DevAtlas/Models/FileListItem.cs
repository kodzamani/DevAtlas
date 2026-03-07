using Avalonia.Media;

namespace DevAtlas.Models;

public class FileListItem
{
    public string RowNumber { get; set; } = "";
    public string RelativePath { get; set; } = "";
    public string Extension { get; set; } = "";
    public int Lines { get; set; }
    public string LinesFormatted { get; set; } = "";
    public int BarWidth { get; set; }
    public Color BarColor { get; set; }
}