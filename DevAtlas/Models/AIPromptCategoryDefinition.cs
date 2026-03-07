namespace DevAtlas.Models;

/// <summary>
/// Immutable prompt category grouping parsed from the Mac reference implementation.
/// </summary>
public sealed record AIPromptCategoryDefinition(
    string TitleKey,
    string IconName,
    IReadOnlyList<AIPromptDefinition> Prompts);
