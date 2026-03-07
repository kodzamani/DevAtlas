using DevAtlas.Data;
using DevAtlas.Models;
using System.Globalization;
using System.Text.RegularExpressions;

namespace DevAtlas.Services;

/// <summary>
/// Loads AI prompt definitions from constant class and resolves text via resx resources.
/// </summary>
public sealed class AIPromptLibraryService
{
    private static readonly Lazy<AIPromptLibraryService> LazyInstance = new(() => new AIPromptLibraryService());

    private static readonly Regex FormatArgumentRegex = new(
        "%d|%@",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private readonly Lazy<IReadOnlyList<AIPromptCategoryDefinition>> _definitions;

    private AIPromptLibraryService()
    {
        _definitions = new Lazy<IReadOnlyList<AIPromptCategoryDefinition>>(LoadDefinitions);
    }

    public static AIPromptLibraryService Instance => LazyInstance.Value;

    public IReadOnlyList<AIPromptCategoryDefinition> GetDefinitions() => _definitions.Value;

    public string GetString(string key)
    {
        return LanguageManager.Instance.GetString(key);
    }

    public string Format(string key, params object[] args)
    {
        var template = NormalizeFormatString(GetString(key));
        return string.Format(CultureInfo.CurrentCulture, template, args);
    }

    private static IReadOnlyList<AIPromptCategoryDefinition> LoadDefinitions()
    {
        // Direct access to constant class - no file parsing needed
        return AIPromptsConstants.Categories;
    }

    private static string NormalizeFormatString(string template)
    {
        var argumentIndex = 0;
        return FormatArgumentRegex.Replace(template, _ => $"{{{argumentIndex++}}}");
    }
}
