using DevAtlas.Models;

namespace DevAtlas.Models;

public class NpmScriptUi : NpmScript
{
    public bool IsDev { get; set; }

    public bool IsHighlighted { get; set; }

    public string BadgeText { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public string KindGlyph { get; set; } = string.Empty;
}