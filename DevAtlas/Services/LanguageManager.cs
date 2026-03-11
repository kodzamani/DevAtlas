using DevAtlas.Enums;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.Json;

namespace DevAtlas.Services;

/// <summary>
/// Manages localization, appearance, and scanning preferences for the application.
/// Provides reactive property change notifications for WPF data binding.
/// </summary>
public sealed class LanguageManager : INotifyPropertyChanged
{
    private static readonly Lazy<LanguageManager> _instance = new(() => new LanguageManager());
    public static LanguageManager Instance => _instance.Value;

    public event PropertyChangedEventHandler? PropertyChanged;
    public event EventHandler? LanguageChanged;
    public event EventHandler? AccentColorChanged;
    public event EventHandler? ThemeModeChanged;

    private AppLanguage _selectedLanguage;
    private AppAccentColor _accentColor;
    private AppThemeMode _themeMode;
    private bool _hasCompletedOnboarding;
    private bool _includeWslProjects;
    private List<string> _excludePaths = new();

    private static readonly string SettingsFolder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DevAtlas");
    private static readonly string SettingsFile = Path.Combine(SettingsFolder, "settings.json");

    private LanguageManager()
    {
        LoadSettings();
        ApplyCulture();
    }

    private void LoadSettings()
    {
        try
        {
            if (File.Exists(SettingsFile))
            {
                var json = File.ReadAllText(SettingsFile);
                var settings = JsonSerializer.Deserialize<AppSettings>(json);
                if (settings != null)
                {
                    _selectedLanguage = Enum.TryParse<AppLanguage>(settings.Language, out var lang) ? lang : AppLanguage.English;
                    _accentColor = Enum.TryParse<AppAccentColor>(settings.AccentColor, out var accent) ? accent : AppAccentColor.Blue;
                    _themeMode = Enum.TryParse<AppThemeMode>(settings.ThemeMode, out var theme) ? theme : AppThemeMode.Light;
                    _hasCompletedOnboarding = settings.HasCompletedOnboarding;
                    _includeWslProjects = settings.IncludeWslProjects;
                    _excludePaths = settings.ExcludePaths ?? new List<string>();
                    return;
                }
            }
        }
        catch { /* Use defaults */ }

        _selectedLanguage = AppLanguage.English;
        _accentColor = AppAccentColor.Blue;
        _themeMode = AppThemeMode.Light;
        _hasCompletedOnboarding = false;
        _includeWslProjects = false;
        _excludePaths = new List<string>();
    }

    private void SaveSettings()
    {
        try
        {
            Directory.CreateDirectory(SettingsFolder);
            var settings = new AppSettings
            {
                Language = _selectedLanguage.ToString(),
                AccentColor = _accentColor.ToString(),
                ThemeMode = _themeMode.ToString(),
                HasCompletedOnboarding = _hasCompletedOnboarding,
                IncludeWslProjects = _includeWslProjects,
                ExcludePaths = _excludePaths
            };
            File.WriteAllText(SettingsFile, JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { /* Silently fail */ }
    }

    private class AppSettings
    {
        public string Language { get; set; } = "English";
        public string AccentColor { get; set; } = "Blue";
        public string ThemeMode { get; set; } = "Light";
        public bool HasCompletedOnboarding { get; set; }
        public bool IncludeWslProjects { get; set; }
        public List<string>? ExcludePaths { get; set; }
    }

    public AppLanguage SelectedLanguage
    {
        get => _selectedLanguage;
        set
        {
            if (_selectedLanguage == value) return;
            _selectedLanguage = value;
            SaveSettings();
            ApplyCulture();
            RaiseLocalizationPropertyChanged();
            LanguageChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public AppAccentColor AccentColor
    {
        get => _accentColor;
        set
        {
            if (_accentColor == value) return;
            _accentColor = value;
            SaveSettings();
            OnPropertyChanged();
            AccentColorChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public AppThemeMode ThemeMode
    {
        get => _themeMode;
        set
        {
            if (_themeMode == value) return;
            _themeMode = value;
            SaveSettings();
            OnPropertyChanged();
            ThemeModeChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public bool HasCompletedOnboarding
    {
        get => _hasCompletedOnboarding;
        set
        {
            if (_hasCompletedOnboarding == value) return;
            _hasCompletedOnboarding = value;
            SaveSettings();
            OnPropertyChanged();
        }
    }

    public bool IncludeWslProjects
    {
        get => _includeWslProjects;
        set
        {
            if (_includeWslProjects == value) return;
            _includeWslProjects = value;
            SaveSettings();
            OnPropertyChanged();
        }
    }

    /// <summary>
    /// Gets the list of exclude paths for project scanning.
    /// </summary>
    public List<string> GetExcludePaths() => _excludePaths;

    /// <summary>
    /// Sets the list of exclude paths and saves to settings.
    /// </summary>
    public void SetExcludePaths(List<string> paths)
    {
        _excludePaths = paths ?? new List<string>();
        SaveSettings();
    }

    public CultureInfo CurrentCulture => new(GetCultureCode(_selectedLanguage));

    private static readonly System.Resources.ResourceManager _resourceManager =
        new("DevAtlas.Resources.Strings", typeof(LanguageManager).Assembly);

    /// <summary>
    /// Gets a localized string by key from the resource manager.
    /// </summary>
    public string GetString(string key)
    {
        try
        {
            return _resourceManager.GetString(key, CurrentCulture) ?? key;
        }
        catch
        {
            return key;
        }
    }

    /// <summary>
    /// Shorthand indexer for localized strings.
    /// </summary>
    public string this[string key] => GetString(key);

    public static string GetCultureCode(AppLanguage language) => language switch
    {
        AppLanguage.English => "en",
        AppLanguage.Turkish => "tr",
        AppLanguage.German => "de",
        AppLanguage.Japanese => "ja",
        AppLanguage.ChineseSimplified => "zh-Hans",
        AppLanguage.Korean => "ko",
        AppLanguage.Italian => "it",
        AppLanguage.French => "fr",
        _ => "en"
    };

    public static string GetLanguageDisplayName(AppLanguage language) => language switch
    {
        AppLanguage.English => "English",
        AppLanguage.Turkish => "T\u00FCrk\u00E7e",
        AppLanguage.German => "Deutsch",
        AppLanguage.Japanese => "\u65E5\u672C\u8A9E",
        AppLanguage.ChineseSimplified => "\u7B80\u4F53\u4E2D\u6587",
        AppLanguage.Korean => "\uD55C\uAD6D\uC5B4",
        AppLanguage.Italian => "Italiano",
        AppLanguage.French => "Fran\u00E7ais",
        _ => "English"
    };

    public static string GetLanguageNativeName(AppLanguage language) => GetLanguageDisplayName(language);

    public static string GetLanguageFlag(AppLanguage language) => language switch
    {
        AppLanguage.English => "\uD83C\uDDFA\uD83C\uDDF8",
        AppLanguage.Turkish => "\uD83C\uDDF9\uD83C\uDDF7",
        AppLanguage.German => "\uD83C\uDDE9\uD83C\uDDEA",
        AppLanguage.Japanese => "\uD83C\uDDEF\uD83C\uDDF5",
        AppLanguage.ChineseSimplified => "\uD83C\uDDE8\uD83C\uDDF3",
        AppLanguage.Korean => "\uD83C\uDDF0\uD83C\uDDF7",
        AppLanguage.Italian => "\uD83C\uDDEE\uD83C\uDDF9",
        AppLanguage.French => "\uD83C\uDDEB\uD83C\uDDF7",
        _ => "\uD83C\uDF10"
    };

    public Avalonia.Media.Color GetAccentColorValue() => _accentColor switch
    {
        AppAccentColor.Blue => Avalonia.Media.Color.FromRgb(59, 130, 246),
        AppAccentColor.Purple => Avalonia.Media.Color.FromRgb(139, 92, 246),
        AppAccentColor.Pink => Avalonia.Media.Color.FromRgb(236, 72, 153),
        AppAccentColor.Red => Avalonia.Media.Color.FromRgb(239, 68, 68),
        AppAccentColor.Orange => Avalonia.Media.Color.FromRgb(249, 115, 22),
        AppAccentColor.Yellow => Avalonia.Media.Color.FromRgb(234, 179, 8),
        AppAccentColor.Green => Avalonia.Media.Color.FromRgb(34, 197, 94),
        AppAccentColor.Teal => Avalonia.Media.Color.FromRgb(20, 184, 166),
        AppAccentColor.Indigo => Avalonia.Media.Color.FromRgb(99, 102, 241),
        AppAccentColor.Cyan => Avalonia.Media.Color.FromRgb(6, 182, 212),
        _ => Avalonia.Media.Color.FromRgb(59, 130, 246)
    };

    private void ApplyCulture()
    {
        var culture = CurrentCulture;
        CultureInfo.DefaultThreadCurrentCulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;
        Thread.CurrentThread.CurrentUICulture = culture;
        Thread.CurrentThread.CurrentCulture = culture;
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    /// <summary>
    /// Forces refresh of all bindings by raising PropertyChanged.
    /// null = all properties changed, which updates both direct and indexer bindings in Avalonia.
    /// </summary>
    public void RefreshBindings()
    {
        RaiseLocalizationPropertyChanged();
    }

    private void RaiseLocalizationPropertyChanged()
    {
        OnPropertyChanged(nameof(SelectedLanguage));
        OnPropertyChanged(nameof(CurrentCulture));

        // Notify indexer-based bindings across Avalonia binding implementations.
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item"));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));

        // Fallback full-refresh notifications.
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
    }
}
