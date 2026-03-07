using DevAtlas.Enums;
using DevAtlas.Services;
using System.Windows.Input;

namespace DevAtlas.ViewModels;

/// <summary>
/// ViewModel for the multi-step onboarding wizard.
/// </summary>
public class OnboardingViewModel : ViewModelBase
{
    private readonly LanguageManager _langManager;
    private OnboardingPage _currentPage = OnboardingPage.Welcome;
    private bool _isPresented;

    public OnboardingViewModel()
    {
        _langManager = LanguageManager.Instance;
        _isPresented = !_langManager.HasCompletedOnboarding;

        NextCommand = new RelayCommand(NextPage, () => !IsLastPage);
        PreviousCommand = new RelayCommand(PreviousPage, () => !IsFirstPage);
        SkipCommand = new RelayCommand(SkipOnboarding);
        CompleteCommand = new RelayCommand(CompleteOnboarding);
        GoToPageCommand = new RelayCommand(o =>
        {
            if (o is OnboardingPage page)
                CurrentPage = page;
            else if (o is int index && Enum.IsDefined(typeof(OnboardingPage), index))
                CurrentPage = (OnboardingPage)index;
        });
    }

    public OnboardingPage CurrentPage
    {
        get => _currentPage;
        set
        {
            if (SetProperty(ref _currentPage, value))
            {
                OnPropertyChanged(nameof(CurrentPageIndex));
                OnPropertyChanged(nameof(ProgressValue));
                OnPropertyChanged(nameof(IsFirstPage));
                OnPropertyChanged(nameof(IsLastPage));
                OnPropertyChanged(nameof(PageTitle));
                OnPropertyChanged(nameof(PageSubtitle));
                OnPropertyChanged(nameof(PageIcon));
            }
        }
    }

    public bool IsPresented
    {
        get => _isPresented;
        set => SetProperty(ref _isPresented, value);
    }

    public int CurrentPageIndex => (int)_currentPage;
    public int TotalPages => Enum.GetValues<OnboardingPage>().Length;
    public double ProgressValue => (double)(CurrentPageIndex + 1) / TotalPages;
    public bool IsFirstPage => _currentPage == OnboardingPage.Welcome;
    public bool IsLastPage => _currentPage == OnboardingPage.Appearance;

    public string PageTitle => _currentPage switch
    {
        OnboardingPage.Welcome => L("OnboardingWelcomeTitle"),
        OnboardingPage.LanguageSelection => L("SettingsLanguage"),
        OnboardingPage.Features => L("OnboardingFeaturesTitle"),
        OnboardingPage.QuickActions => L("OnboardingQuickActionsTitle"),
        OnboardingPage.StatsNotebook => L("OnboardingStatsNotebookTitle"),
        OnboardingPage.Appearance => L("OnboardingAppearanceTitle"),
        _ => ""
    };

    public string PageSubtitle => _currentPage switch
    {
        OnboardingPage.Welcome => L("OnboardingWelcomeSubtitle"),
        OnboardingPage.LanguageSelection => L("SettingsSelectLanguage"),
        OnboardingPage.Features => L("OnboardingFeaturesSubtitle"),
        OnboardingPage.QuickActions => L("OnboardingQuickActionsSubtitle"),
        OnboardingPage.StatsNotebook => L("OnboardingStatsNotebookSubtitle"),
        OnboardingPage.Appearance => L("OnboardingAppearanceSubtitle"),
        _ => ""
    };

    public string PageIcon => _currentPage switch
    {
        OnboardingPage.Welcome => "\uD83D\uDC4B",
        OnboardingPage.LanguageSelection => "\uD83C\uDF10",
        OnboardingPage.Features => "\uD83D\uDCC1",
        OnboardingPage.QuickActions => "\u26A1",
        OnboardingPage.StatsNotebook => "\uD83D\uDCCA",
        OnboardingPage.Appearance => "\uD83C\uDFA8",
        _ => "\uD83D\uDC4B"
    };

    public ICommand NextCommand { get; }
    public ICommand PreviousCommand { get; }
    public ICommand SkipCommand { get; }
    public ICommand CompleteCommand { get; }
    public ICommand GoToPageCommand { get; }

    // Settings access for appearance page
    public AppLanguage SelectedLanguage
    {
        get => _langManager.SelectedLanguage;
        set
        {
            _langManager.SelectedLanguage = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(PageTitle));
            OnPropertyChanged(nameof(PageSubtitle));
        }
    }

    public AppThemeMode SelectedThemeMode
    {
        get => _langManager.ThemeMode;
        set
        {
            _langManager.ThemeMode = value;
            OnPropertyChanged();
        }
    }

    public AppAccentColor SelectedAccentColor
    {
        get => _langManager.AccentColor;
        set
        {
            _langManager.AccentColor = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(SelectedAccentColorValue));
        }
    }

    public Avalonia.Media.Color SelectedAccentColorValue => _langManager.GetAccentColorValue();

    public IEnumerable<AppLanguage> AvailableLanguages => Enum.GetValues<AppLanguage>();
    public IEnumerable<AppAccentColor> AvailableAccentColors => Enum.GetValues<AppAccentColor>();

    private void NextPage()
    {
        if ((int)_currentPage < TotalPages - 1)
            CurrentPage = (OnboardingPage)((int)_currentPage + 1);
    }

    private void PreviousPage()
    {
        if ((int)_currentPage > 0)
            CurrentPage = (OnboardingPage)((int)_currentPage - 1);
    }

    private void CompleteOnboarding()
    {
        _langManager.HasCompletedOnboarding = true;
        IsPresented = false;
    }

    private void SkipOnboarding() => CompleteOnboarding();

    public void ShowOnboarding()
    {
        CurrentPage = OnboardingPage.Welcome;
        IsPresented = true;
    }

    public void ResetOnboarding()
    {
        _langManager.HasCompletedOnboarding = false;
        CurrentPage = OnboardingPage.Welcome;
        IsPresented = true;
    }

    public void RefreshLocalization()
    {
        OnPropertyChanged(nameof(PageTitle));
        OnPropertyChanged(nameof(PageSubtitle));
        OnPropertyChanged(nameof(PageIcon));
    }

    private string L(string key) => _langManager.GetString(key);
}
