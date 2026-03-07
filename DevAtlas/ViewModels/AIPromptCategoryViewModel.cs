using DevAtlas.Models;
using DevAtlas.Services;
using System.Collections.ObjectModel;

namespace DevAtlas.ViewModels;

/// <summary>
/// Localized category view model for the AI prompt grid.
/// </summary>
public sealed class AIPromptCategoryViewModel : ViewModelBase
{
    private readonly AIPromptLibraryService _library;

    public AIPromptCategoryViewModel(AIPromptCategoryDefinition definition, AIPromptLibraryService library)
    {
        Definition = definition;
        _library = library;
        Prompts = new ObservableCollection<AIPromptCardViewModel>(
            Definition.Prompts.Select(prompt => new AIPromptCardViewModel(prompt, _library)));
    }

    public AIPromptCategoryDefinition Definition { get; }

    public ObservableCollection<AIPromptCardViewModel> Prompts { get; }

    public string Title => _library.GetString(Definition.TitleKey);

    public string IconGlyph => Definition.IconName switch
    {
        "checkmark.shield" => "\u2713",
        "speedometer" => "\u23F1",
        "square.stack.3d.up" => "\u25A3",
        "testtube.2" => "\u2697",
        "book.closed" => "\u2261",
        "ladybug" => "\u2022",
        "network" => "\u21C4",
        "figure.wave" => "\u25CE",
        "globe" => "\u25CC",
        "arrow.triangle.branch" => "\u21C6",
        "shippingbox" => "\u25A3",
        "wand.and.stars" => "\u2726",
        _ => "\u2022"
    };

    public void RefreshLocalization()
    {
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(IconGlyph));

        foreach (var prompt in Prompts)
        {
            prompt.RefreshLocalization();
        }
    }
}
