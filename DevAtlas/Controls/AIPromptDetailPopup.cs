using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using DevAtlas.Models;
using DevAtlas.Services;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace DevAtlas.Controls;

/// <summary>
/// Popup showing the full AI prompt content with copy support.
/// </summary>
public partial class AIPromptDetailPopup : UserControl, INotifyPropertyChanged
{
    private readonly AIPromptLibraryService _library;
    private readonly EventHandler _languageChangedHandler;
    private CancellationTokenSource? _copyResetToken;
    private AIPromptDefinition? _currentPrompt;
    private bool _isCopied;

    public AIPromptDetailPopup()
    {
        InitializeComponent();
        _library = AIPromptLibraryService.Instance;
        DataContext = this;
        _languageChangedHandler = (_, _) => RefreshLocalization();
        LanguageManager.Instance.LanguageChanged += _languageChangedHandler;
        Unloaded += OnUnloaded;
    }

    public new event PropertyChangedEventHandler? PropertyChanged;

    public string Title => _currentPrompt is null ? string.Empty : _library.GetString(_currentPrompt.TitleKey);

    public string Description => _currentPrompt is null ? string.Empty : _library.GetString(_currentPrompt.DescriptionKey);

    public string Prompt => _currentPrompt?.Prompt ?? string.Empty;

    public string WordCountLabel => _currentPrompt is null
        ? string.Empty
        : _library.Format("aiprompts.wordCount", _currentPrompt.WordCount);

    public string CopyLabel => _library.GetString(IsCopied ? "aiprompts.copied" : "aiprompts.copy");

    public bool IsCopied
    {
        get => _isCopied;
        private set
        {
            if (_isCopied == value)
            {
                return;
            }

            _isCopied = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CopyLabel));
        }
    }

    public void Show(AIPromptDefinition prompt)
    {
        _currentPrompt = prompt;
        IsCopied = false;
        RefreshLocalization();
        IsVisible = true;
    }

    public void Hide()
    {
        _copyResetToken?.Cancel();
        _copyResetToken?.Dispose();
        _copyResetToken = null;
        _currentPrompt = null;
        IsCopied = false;
        IsVisible = false;
        RefreshLocalization();
    }

    private async void CopyPrompt_Click(object sender, RoutedEventArgs e)
    {
        if (_currentPrompt is null)
        {
            return;
        }

        await (TopLevel.GetTopLevel(this)?.Clipboard?.SetTextAsync(_currentPrompt.Prompt) ?? Task.CompletedTask);

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

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Hide();
    }

    private void Overlay_MouseLeftButtonDown(object sender, PointerPressedEventArgs e)
    {
        if (e.Source == this || e.Source is Grid { Parent: AIPromptDetailPopup })
        {
            Hide();
        }
    }

    private void PopupContent_MouseLeftButtonDown(object sender, PointerPressedEventArgs e)
    {
        e.Handled = true;
    }

    private void RefreshLocalization()
    {
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(Description));
        OnPropertyChanged(nameof(Prompt));
        OnPropertyChanged(nameof(WordCountLabel));
        OnPropertyChanged(nameof(CopyLabel));
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        LanguageManager.Instance.LanguageChanged -= _languageChangedHandler;
        Unloaded -= OnUnloaded;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
