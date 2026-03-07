namespace DevAtlas.Models;

/// <summary>
/// Represents an npm script from package.json
/// </summary>
public class NpmScript
{
    public string Name { get; set; } = string.Empty;
    public string Command { get; set; } = string.Empty;
}
