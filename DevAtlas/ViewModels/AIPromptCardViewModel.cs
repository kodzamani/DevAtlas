using DevAtlas.Models;
using DevAtlas.Services;

namespace DevAtlas.ViewModels;

/// <summary>
/// Localized prompt card view model with transient copy feedback state.
/// </summary>
public sealed class AIPromptCardViewModel : ViewModelBase
{
    private readonly AIPromptLibraryService _library;
    private CancellationTokenSource? _copyResetToken;
    private bool _isCopied;

    public AIPromptCardViewModel(AIPromptDefinition definition, AIPromptLibraryService library)
    {
        Definition = definition;
        _library = library;
    }

    public AIPromptDefinition Definition { get; }

    public string Title => _library.GetString(Definition.TitleKey);

    public string Description => _library.GetString(Definition.DescriptionKey);

    public string Prompt => Definition.Prompt;

    public string CopyLabel => _library.GetString(IsCopied ? "aiprompts.copied" : "aiprompts.copy");

    public string ExpandLabel => _library.GetString("aiprompts.expand");

    public string WordCountLabel => _library.Format("aiprompts.wordCount", Definition.WordCount);

    public bool IsCopied
    {
        get => _isCopied;
        private set
        {
            if (!SetProperty(ref _isCopied, value))
            {
                return;
            }

            OnPropertyChanged(nameof(CopyLabel));
        }
    }

    public async Task FlashCopiedAsync()
    {
        _copyResetToken?.Cancel();
        _copyResetToken?.Dispose();
        _copyResetToken = new CancellationTokenSource();
        var token = _copyResetToken.Token;

        IsCopied = true;

        try
        {
            await Task.Delay(TimeSpan.FromSeconds(2), token);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        if (!token.IsCancellationRequested)
        {
            IsCopied = false;
        }
    }

    public void RefreshLocalization()
    {
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(Description));
        OnPropertyChanged(nameof(CopyLabel));
        OnPropertyChanged(nameof(ExpandLabel));
        OnPropertyChanged(nameof(WordCountLabel));
    }
}
