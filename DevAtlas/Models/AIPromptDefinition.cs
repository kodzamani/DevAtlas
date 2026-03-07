using System.Text.RegularExpressions;

namespace DevAtlas.Models;

/// <summary>
/// Immutable prompt definition parsed from the Mac reference implementation.
/// </summary>
public sealed record AIPromptDefinition(string TitleKey, string DescriptionKey, string Prompt)
{
    private static readonly Regex WordRegex = new(@"\S+", RegexOptions.Compiled);

    public int WordCount => WordRegex.Matches(Prompt).Count;
}
