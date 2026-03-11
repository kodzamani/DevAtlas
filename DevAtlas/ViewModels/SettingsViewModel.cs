using Avalonia.Media;
using DevAtlas.Enums;
using DevAtlas.Services;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Reflection;
using System.Windows.Input;

namespace DevAtlas.ViewModels;

/// <summary>
/// ViewModel for the Settings page.
/// Manages appearance, language, and project scanning preferences.
/// </summary>
public class SettingsViewModel : ViewModelBase, IDisposable
{
    private readonly LanguageManager _langManager;
    private readonly PropertyChangedEventHandler _propertyChangedHandler;
    private bool _disposed = false;

    public SettingsViewModel()
    {
        _langManager = LanguageManager.Instance;
        _propertyChangedHandler = (_, e) =>
        {
            OnPropertyChanged(nameof(SelectedLanguage));
            OnPropertyChanged(nameof(SelectedThemeMode));
            OnPropertyChanged(nameof(SelectedAccentColor));
            OnPropertyChanged(nameof(AccentBrush));
            // Also refresh all localized labels when language changes
            OnPropertyChanged(nameof(SettingsTitle));
            OnPropertyChanged(nameof(AppearanceLabel));
            OnPropertyChanged(nameof(LanguageLabel));
            OnPropertyChanged(nameof(AboutLabel));
            OnPropertyChanged(nameof(ThemeLabel));
            OnPropertyChanged(nameof(AccentColorLabel));
            OnPropertyChanged(nameof(LightLabel));
            OnPropertyChanged(nameof(DarkLabel));
            OnPropertyChanged(nameof(SystemLabel));
            OnPropertyChanged(nameof(AboutDescription));
            OnPropertyChanged(nameof(VersionLabel));
            OnPropertyChanged(nameof(CopyrightLabel));
            OnPropertyChanged(nameof(SelectLanguageLabel));
            OnPropertyChanged(nameof(WslProjectsLabel));
            OnPropertyChanged(nameof(WslProjectsDescription));
        };
        _langManager.PropertyChanged += _propertyChangedHandler;

        SetLanguageCommand = new RelayCommand(o =>
        {
            if (o is AppLanguage lang)
                SelectedLanguage = lang;
        });

        SetThemeCommand = new RelayCommand(o =>
        {
            if (o is AppThemeMode mode)
                SelectedThemeMode = mode;
        });

        SetAccentColorCommand = new RelayCommand(o =>
        {
            if (o is AppAccentColor color)
                SelectedAccentColor = color;
        });

        AddExcludePathCommand = new RelayCommand(path =>
        {
            if (path is string pathStr && !string.IsNullOrWhiteSpace(pathStr))
            {
                var trimmed = pathStr.Trim();
                if (!ExcludePaths.Any(p => string.Equals(p, trimmed, StringComparison.OrdinalIgnoreCase)))
                {
                    ExcludePaths.Add(trimmed);
                    _langManager.SetExcludePaths(ExcludePaths.ToList());
                    SettingsSaved?.Invoke(this, EventArgs.Empty);
                }
            }
        });

        RemoveExcludePathCommand = new RelayCommand(path =>
        {
            if (path is string pathToRemove)
            {
                ExcludePaths.Remove(pathToRemove);
                _langManager.SetExcludePaths(ExcludePaths.ToList());
                SettingsSaved?.Invoke(this, EventArgs.Empty);
            }
        });

        // Load existing exclude paths
        LoadExcludePaths();
    }

    public AppLanguage SelectedLanguage
    {
        get => _langManager.SelectedLanguage;
        set
        {
            _langManager.SelectedLanguage = value;
            OnPropertyChanged();
            // Notify all localized property changes
            OnPropertyChanged(nameof(SettingsTitle));
            OnPropertyChanged(nameof(AppearanceLabel));
            OnPropertyChanged(nameof(LanguageLabel));
            OnPropertyChanged(nameof(AboutLabel));
            OnPropertyChanged(nameof(ThemeLabel));
            OnPropertyChanged(nameof(AccentColorLabel));
            OnPropertyChanged(nameof(LightLabel));
            OnPropertyChanged(nameof(DarkLabel));
            OnPropertyChanged(nameof(SystemLabel));
            OnPropertyChanged(nameof(AboutDescription));
            OnPropertyChanged(nameof(VersionLabel));
            OnPropertyChanged(nameof(CopyrightLabel));
            OnPropertyChanged(nameof(SelectLanguageLabel));
            OnPropertyChanged(nameof(WslProjectsLabel));
            OnPropertyChanged(nameof(WslProjectsDescription));
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
            OnPropertyChanged(nameof(AccentBrush));
        }
    }

    public SolidColorBrush AccentBrush => new(_langManager.GetAccentColorValue());

    public bool IncludeWslProjects
    {
        get => _langManager.IncludeWslProjects;
        set
        {
            if (_langManager.IncludeWslProjects == value)
            {
                return;
            }

            _langManager.IncludeWslProjects = value;
            OnPropertyChanged();
            SettingsSaved?.Invoke(this, EventArgs.Empty);
        }
    }

    public bool ShowWslProjectsOption => OperatingSystem.IsWindows();

    // Exclude Paths
    public ObservableCollection<string> ExcludePaths { get; } = new();

    // Localized labels
    public string SettingsTitle => L("SettingsTitle");
    public string AppearanceLabel => L("SettingsAppearance");
    public string LanguageLabel => L("SettingsLanguage");
    public string AboutLabel => L("SettingsAbout");
    public string ThemeLabel => L("SettingsTheme");
    public string AccentColorLabel => L("SettingsAccentColor");
    public string LightLabel => L("SettingsLight");
    public string DarkLabel => L("SettingsDark");
    public string SystemLabel => L("SettingsSystem");
    public string AboutDescription => L("SettingsAboutDescription");
    public string VersionLabel => L("SettingsVersion");
    public string AppVersion => Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "N/A";
    public string CopyrightLabel => L("SettingsCopyright");
    public string SelectLanguageLabel => L("SettingsSelectLanguage");
    public string WslProjectsLabel => L("SettingsWslProjects");
    public string WslProjectsDescription => L("SettingsWslProjectsDescription");

    public ICommand SetLanguageCommand { get; }
    public ICommand SetThemeCommand { get; }
    public ICommand SetAccentColorCommand { get; }
    public ICommand AddExcludePathCommand { get; }
    public ICommand RemoveExcludePathCommand { get; }

    /// <summary>
    /// Event raised when settings are saved.
    /// </summary>
    public event EventHandler? SettingsSaved;

    public IEnumerable<AppLanguage> AvailableLanguages => Enum.GetValues<AppLanguage>();
    public IEnumerable<AppThemeMode> AvailableThemes => Enum.GetValues<AppThemeMode>();
    public IEnumerable<AppAccentColor> AvailableAccentColors => Enum.GetValues<AppAccentColor>();

    private void LoadExcludePaths()
    {
        ExcludePaths.Clear();
        foreach (var path in _langManager.GetExcludePaths())
        {
            ExcludePaths.Add(path);
        }
    }

    private string L(string key) => _langManager.GetString(key);

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!_disposed)
        {
            if (disposing)
            {
                // Unsubscribe from LanguageManager events
                _langManager.PropertyChanged -= _propertyChangedHandler;
            }
            _disposed = true;
        }
    }

    /// <summary>
    /// Gets accent color display info.
    /// </summary>
    public static Color GetAccentColorValue(AppAccentColor color) => color switch
    {
        AppAccentColor.Blue => Color.FromRgb(59, 130, 246),
        AppAccentColor.Purple => Color.FromRgb(139, 92, 246),
        AppAccentColor.Pink => Color.FromRgb(236, 72, 153),
        AppAccentColor.Red => Color.FromRgb(239, 68, 68),
        AppAccentColor.Orange => Color.FromRgb(249, 115, 22),
        AppAccentColor.Yellow => Color.FromRgb(234, 179, 8),
        AppAccentColor.Green => Color.FromRgb(34, 197, 94),
        AppAccentColor.Teal => Color.FromRgb(20, 184, 166),
        AppAccentColor.Indigo => Color.FromRgb(99, 102, 241),
        AppAccentColor.Cyan => Color.FromRgb(6, 182, 212),
        _ => Color.FromRgb(59, 130, 246)
    };
}
