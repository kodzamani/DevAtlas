using DevAtlas.Services;
using System.Collections.ObjectModel;

namespace DevAtlas.ViewModels;

/// <summary>
/// ViewModel backing the AI prompt library screen.
/// </summary>
public sealed class AIPromptsViewModel : ViewModelBase, IDisposable
{
    private readonly AIPromptLibraryService _library;
    private readonly EventHandler _languageChangedHandler;
    private bool _disposed;

    public AIPromptsViewModel()
    {
        _library = AIPromptLibraryService.Instance;
        Categories = new ObservableCollection<AIPromptCategoryViewModel>(
            _library.GetDefinitions().Select(definition => new AIPromptCategoryViewModel(definition, _library)));

        _languageChangedHandler = (_, _) => RefreshLocalization();
        LanguageManager.Instance.LanguageChanged += _languageChangedHandler;
    }

    public ObservableCollection<AIPromptCategoryViewModel> Categories { get; }

    public string Title => _library.GetString("aiprompts.title");

    public string Subtitle => _library.Format(
        "aiprompts.subtitle",
        Categories.Count,
        Categories.Sum(category => category.Prompts.Count));

    public string Description => _library.GetString("aiprompts.description");

    public string SidebarLabel => _library.GetString("tab.aiPrompts");

    public void RefreshLocalization()
    {
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(Subtitle));
        OnPropertyChanged(nameof(Description));
        OnPropertyChanged(nameof(SidebarLabel));

        foreach (var category in Categories)
        {
            category.RefreshLocalization();
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        LanguageManager.Instance.LanguageChanged -= _languageChangedHandler;
        _disposed = true;
    }
}
