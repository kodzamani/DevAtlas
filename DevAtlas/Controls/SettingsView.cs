using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using DevAtlas.Enums;
using DevAtlas.ViewModels;

namespace DevAtlas.Controls;

public partial class SettingsView : UserControl
{
    private SettingsViewModel? _vm;

    private static readonly Dictionary<AppLanguage, (string Flag, string Name)> LanguageInfo = new()
    {
        [AppLanguage.English] = ("\uD83C\uDDFA\uD83C\uDDF8", "English"),
        [AppLanguage.Turkish] = ("\uD83C\uDDF9\uD83C\uDDF7", "T\u00FCrk\u00E7e"),
        [AppLanguage.German] = ("\uD83C\uDDE9\uD83C\uDDEA", "Deutsch"),
        [AppLanguage.French] = ("\uD83C\uDDEB\uD83C\uDDF7", "Fran\u00E7ais"),
        [AppLanguage.Italian] = ("\uD83C\uDDEE\uD83C\uDDF9", "Italiano"),
        [AppLanguage.Japanese] = ("\uD83C\uDDEF\uD83C\uDDF5", "\u65E5\u672C\u8A9E"),
        [AppLanguage.Korean] = ("\uD83C\uDDF0\uD83C\uDDF7", "\uD55C\uAD6D\uC5B4"),
        [AppLanguage.ChineseSimplified] = ("\uD83C\uDDE8\uD83C\uDDF3", "\u4E2D\u6587(\u7B80\u4F53)")
    };

    private static readonly Dictionary<AppAccentColor, Color> AccentColors = new()
    {
        [AppAccentColor.Blue] = Color.FromRgb(59, 130, 246),
        [AppAccentColor.Purple] = Color.FromRgb(139, 92, 246),
        [AppAccentColor.Pink] = Color.FromRgb(236, 72, 153),
        [AppAccentColor.Red] = Color.FromRgb(239, 68, 68),
        [AppAccentColor.Orange] = Color.FromRgb(249, 115, 22),
        [AppAccentColor.Yellow] = Color.FromRgb(234, 179, 8),
        [AppAccentColor.Green] = Color.FromRgb(34, 197, 94),
        [AppAccentColor.Teal] = Color.FromRgb(20, 184, 166),
        [AppAccentColor.Indigo] = Color.FromRgb(99, 102, 241),
        [AppAccentColor.Cyan] = Color.FromRgb(6, 182, 212)
    };

    public SettingsView()
    {
        InitializeComponent();
        PropertyChanged += (s, e) => { if (e.Property == DataContextProperty) OnDataContextChanged(s, e); };
        Loaded += OnLoaded;
    }

    private void OnDataContextChanged(object sender, AvaloniaPropertyChangedEventArgs e)
    {
        // Clean up previous ViewModel subscription if exists
        if (_vm != null)
            _vm.PropertyChanged -= OnViewModelPropertyChanged;

        _vm = e.NewValue as SettingsViewModel;

        if (_vm != null)
            _vm.PropertyChanged += OnViewModelPropertyChanged;
    }

    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SettingsViewModel.SelectedLanguage))
        {
            // Rebuild both lists when language selection changes
            BuildLanguageList();
            BuildAccentColors();
            UpdateThemeButtons();
        }
        else if (e.PropertyName == nameof(SettingsViewModel.SelectedAccentColor) ||
            e.PropertyName == nameof(SettingsViewModel.AccentBrush))
        {
            // Rebuild both accent colors and language list when accent color changes
            BuildAccentColors();
            BuildLanguageList();
            UpdateThemeButtons();
        }
        else if (e.PropertyName == nameof(SettingsViewModel.SelectedThemeMode))
        {
            // Rebuild language list when theme changes to update text colors
            BuildLanguageList();
            UpdateThemeButtons();
        }
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _vm = DataContext as SettingsViewModel;
        if (_vm != null)
        {
            // Only subscribe if not already subscribed
            _vm.PropertyChanged -= OnViewModelPropertyChanged;
            _vm.PropertyChanged += OnViewModelPropertyChanged;
        }
        BuildAccentColors();
        BuildLanguageList();
        UpdateThemeButtons();
    }

    private void BuildAccentColors()
    {
        AccentColorPanel.Children.Clear();
        foreach (var (color, value) in AccentColors)
        {
            var border = new Border
            {
                Width = 32,
                Height = 32,
                CornerRadius = new CornerRadius(16),
                Margin = new Thickness(0, 0, 8, 8),
                Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand),
                Background = new Avalonia.Media.SolidColorBrush(value),
                BorderThickness = new Thickness(3),
                BorderBrush = _vm?.SelectedAccentColor == color
                    ? _vm?.AccentBrush
                    : Brushes.Transparent,
                Tag = color
            };

            // Checkmark for selected
            if (_vm?.SelectedAccentColor == color)
            {
                border.Child = new TextBlock
                {
                    Text = "\u2713",
                    Foreground = Brushes.White,
                    FontSize = 14,
                    FontWeight = FontWeight.Bold,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                };
            }

            border.PointerPressed += AccentColor_Click;
            AccentColorPanel.Children.Add(border);
        }
    }

    private void BuildLanguageList()
    {
        LanguageList.Children.Clear();
        foreach (var (lang, (flag, name)) in LanguageInfo)
        {
            var isSelected = _vm?.SelectedLanguage == lang;

            var row = new Border
            {
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(14, 10, 14, 10),
                Margin = new Thickness(0, 0, 0, 4),
                Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand),
                Background = Brushes.Transparent,
                Tag = lang
            };

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var flagText = new TextBlock
            {
                Text = flag,
                FontSize = 18,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 12, 0)
            };
            flagText.Bind(TextBlock.ForegroundProperty, this.GetResourceObservable("Text.Primary"));
            Grid.SetColumn(flagText, 0);

            var nameText = new TextBlock
            {
                Text = name,
                FontSize = 13,
                FontWeight = isSelected ? FontWeight.SemiBold : FontWeight.Normal,
                VerticalAlignment = VerticalAlignment.Center
            };
            nameText.Bind(TextBlock.ForegroundProperty, this.GetResourceObservable("Text.Primary"));
            Grid.SetColumn(nameText, 1);

            grid.Children.Add(flagText);
            grid.Children.Add(nameText);

            if (isSelected)
            {
                // Circular checkmark with accent color background
                var checkBorder = new Border
                {
                    Width = 20,
                    Height = 20,
                    CornerRadius = new CornerRadius(10),
                    Background = _vm?.AccentBrush,
                    Child = new TextBlock
                    {
                        Text = "\u2713",
                        FontSize = 11,
                        FontWeight = FontWeight.Bold,
                        Foreground = Brushes.White,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center,
                        Margin = new Thickness(0, 0, 0, 1)
                    }
                };
                Grid.SetColumn(checkBorder, 2);
                grid.Children.Add(checkBorder);
            }

            row.Child = grid;
            row.PointerPressed += Language_Click;
            LanguageList.Children.Add(row);
        }
    }

    private void UpdateThemeButtons()
    {
        if (_vm is null) return;

        var buttons = new[] { ThemeLightBtn, ThemeDarkBtn, ThemeSystemBtn };
        var icons = new[] { ThemeLightIcon, ThemeDarkIcon, ThemeSystemIcon };
        var modes = new[] { AppThemeMode.Light, AppThemeMode.Dark, AppThemeMode.System };
        var iconSourcesNormal = new[]
        {
            "/Assets/Icons/Settings/settings_light_mode.svg",
            "/Assets/Icons/Settings/settings_dark_mode.svg",
            "/Assets/Icons/Settings/settings_system.svg"
        };
        var iconSourcesSelected = new[]
        {
            "/Assets/Icons/Settings/settings_light_mode_dark.svg",
            "/Assets/Icons/Settings/settings_dark_mode_dark.svg",
            "/Assets/Icons/Settings/settings_system_dark.svg"
        };
        var useDarkIcons = IsEffectiveDarkMode();

        for (int i = 0; i < buttons.Length; i++)
        {
            var isActive = _vm.SelectedThemeMode == modes[i];
            buttons[i].BorderBrush = isActive
                ? _vm?.AccentBrush
                : Brushes.Transparent;
            buttons[i].Background = isActive
                ? _vm?.AccentBrush
                : (IBrush)this.FindResource("Bg.Muted");

            // Update text color based on selection state
            var stackPanel = (StackPanel)buttons[i].Child;
            var textBlock = (TextBlock)stackPanel.Children[1];
            textBlock.Foreground = isActive
                ? Brushes.White
                : (IBrush)this.FindResource("Text.Primary");

            // Selected buttons always use the high-contrast icon variant.
            // In dark theme, inactive buttons also need the dark variant because their background is dark.
            var iconSource = isActive || useDarkIcons
                ? iconSourcesSelected[i]
                : iconSourcesNormal[i];
            var svgUri = iconSource.StartsWith("avares://") ? iconSource : $"avares://DevAtlas{iconSource}";
            var svgSrc = Avalonia.Svg.Skia.SvgSource.Load(svgUri, null);
            icons[i].Source = svgSrc != null ? new Avalonia.Svg.Skia.SvgImage { Source = svgSrc } : null;
        }
    }

    private bool IsEffectiveDarkMode()
    {
        if (_vm is null) return false;

        return _vm.SelectedThemeMode switch
        {
            AppThemeMode.Dark => true,
            AppThemeMode.Light => false,
            AppThemeMode.System => IsSystemDarkMode(),
            _ => false
        };
    }

    private static bool IsSystemDarkMode()
    {
        try
        {
            if (System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Windows))
            {
                var psi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "reg",
                    Arguments = "query HKCU\\Software\\Microsoft\\Windows\\CurrentVersion\\Themes\\Personalize /v AppsUseLightTheme",
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using var proc = System.Diagnostics.Process.Start(psi);
                var output = proc?.StandardOutput.ReadToEnd() ?? "";
                proc?.WaitForExit(2000);
                return output.Contains("0x0");
            }
            else if (System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.OSX))
            {
                var psi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "defaults",
                    Arguments = "read -g AppleInterfaceStyle",
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using var proc = System.Diagnostics.Process.Start(psi);
                var output = proc?.StandardOutput.ReadToEnd() ?? "";
                proc?.WaitForExit(2000);
                return output.Trim().Equals("Dark", StringComparison.OrdinalIgnoreCase);
            }
            else
            {
                var psi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "gsettings",
                    Arguments = "get org.gnome.desktop.interface gtk-theme",
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using var proc = System.Diagnostics.Process.Start(psi);
                var output = proc?.StandardOutput.ReadToEnd() ?? "";
                proc?.WaitForExit(2000);
                return output.Contains("dark", StringComparison.OrdinalIgnoreCase);
            }
        }
        catch { return false; }
    }

    private void ThemeBtn_Click(object sender, PointerPressedEventArgs e)
    {
        if (sender is not Control el || el.Tag is not string tag || _vm is null) return;

        var mode = tag switch
        {
            "Light" => AppThemeMode.Light,
            "Dark" => AppThemeMode.Dark,
            "System" => AppThemeMode.System,
            _ => AppThemeMode.Light
        };

        _vm.SelectedThemeMode = mode;

        UpdateThemeButtons();

        // Raise event for MainWindow to apply theme
        ThemeChanged?.Invoke(this, mode);

        Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
        {
            UpdateThemeButtons();
        });
    }

    public void RefreshThemeVisuals()
    {
        if (!IsLoaded)
            return;

        BuildAccentColors();
        BuildLanguageList();
        UpdateThemeButtons();
    }

    private void AccentColor_Click(object sender, PointerPressedEventArgs e)
    {
        if (sender is not Control el || el.Tag is not AppAccentColor color || _vm is null) return;

        _vm.SelectedAccentColor = color;
        BuildAccentColors();

        AccentColorChanged?.Invoke(this, color);
    }

    private void Language_Click(object sender, PointerPressedEventArgs e)
    {
        if (sender is not Control el || el.Tag is not AppLanguage lang || _vm is null) return;

        _vm.SelectedLanguage = lang;
        BuildLanguageList();

        LanguageChanged?.Invoke(this, lang);
    }

    private void ShowTourButton_Click(object sender, PointerPressedEventArgs e)
    {
        TourRequested?.Invoke(this, EventArgs.Empty);
    }

    private async void BrowseExcludePathButton_Click(object sender, RoutedEventArgs e)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel == null) return;

        var folders = await topLevel.StorageProvider.OpenFolderPickerAsync(new Avalonia.Platform.Storage.FolderPickerOpenOptions
        {
            Title = "Select a folder to exclude from project scanning",
            AllowMultiple = false
        });

        if (folders.Count > 0)
        {
            var path = folders[0].Path.LocalPath;
            if (!string.IsNullOrEmpty(path))
                _vm?.AddExcludePathCommand.Execute(path);
        }
    }

    // Events for MainWindow integration
    public event EventHandler<AppThemeMode>? ThemeChanged;
    public event EventHandler<AppAccentColor>? AccentColorChanged;
    public event EventHandler<AppLanguage>? LanguageChanged;
    public event EventHandler? TourRequested;
}
