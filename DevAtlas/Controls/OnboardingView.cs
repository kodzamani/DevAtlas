using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using DevAtlas.Enums;
using DevAtlas.Services;
using DevAtlas.ViewModels;
using System.ComponentModel;

namespace DevAtlas.Controls;

public partial class OnboardingView : UserControl
{
    private OnboardingViewModel? _vm;

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

    public OnboardingView()
    {
        InitializeComponent();
        PropertyChanged += (s, e) => { if (e.Property == DataContextProperty) OnDataContextChanged(s, e); };
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        LanguageManager.Instance.LanguageChanged += OnLanguageChanged;
    }

    private void OnDataContextChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (_vm != null)
        {
            _vm.PropertyChanged -= Vm_PropertyChanged;
        }

        _vm = e.NewValue as OnboardingViewModel;

        if (_vm != null)
        {
            _vm.PropertyChanged += Vm_PropertyChanged;
        }
    }

    private void Vm_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(OnboardingViewModel.CurrentPage) or nameof(OnboardingViewModel.SelectedLanguage) or nameof(OnboardingViewModel.SelectedThemeMode))
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(UpdatePageVisibility);
            return;
        }

        if (e.PropertyName == nameof(OnboardingViewModel.SelectedAccentColor))
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                BuildAccentColors();
                BuildLanguageList();
                UpdateThemeButtons();
                UpdatePageDots();
            });
        }
    }

    private void OnLoaded(object? sender, RoutedEventArgs e)
    {
        _vm = DataContext as OnboardingViewModel;
        if (_vm != null)
        {
            _vm.PropertyChanged -= Vm_PropertyChanged;
            _vm.PropertyChanged += Vm_PropertyChanged;
        }

        UpdatePageVisibility();
    }

    private void OnUnloaded(object? sender, RoutedEventArgs e)
    {
        if (_vm != null)
        {
            _vm.PropertyChanged -= Vm_PropertyChanged;
        }

        LanguageManager.Instance.LanguageChanged -= OnLanguageChanged;
        Loaded -= OnLoaded;
        Unloaded -= OnUnloaded;
    }

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(RefreshLocalization);
    }

    public void RefreshLocalization()
    {
        _vm?.RefreshLocalization();
        BuildLanguageList();
        UpdatePageVisibility();
    }

    private void UpdatePageVisibility()
    {
        if (_vm == null)
        {
            return;
        }

        var page = _vm.CurrentPage;

        PageWelcome.IsVisible = page == OnboardingPage.Welcome;
        PageLanguage.IsVisible = page == OnboardingPage.LanguageSelection;
        PageFeatures.IsVisible = page == OnboardingPage.Features;
        PageQuickActions.IsVisible = page == OnboardingPage.QuickActions;
        PageStats.IsVisible = page == OnboardingPage.StatsNotebook;
        PageAppearance.IsVisible = page == OnboardingPage.Appearance;

        UpdatePageDots();

        PrevBtn.IsVisible = !_vm.IsFirstPage;
        var lm = LanguageManager.Instance;
        NextBtnText.Text = _vm.IsLastPage
            ? $"{lm.GetString("OnboardingGetStarted")} \u2713"
            : $"{lm.GetString("CommonNext")} \u2192";

        if (page == OnboardingPage.LanguageSelection)
        {
            BuildLanguageList();
        }
        else if (page == OnboardingPage.Appearance)
        {
            BuildAccentColors();
            UpdateThemeButtons();
        }
    }

    private void UpdatePageDots()
    {
        if (_vm == null)
        {
            return;
        }

        var dots = new[] { Dot0, Dot1, Dot2, Dot3, Dot4, Dot5 };
        for (var i = 0; i < dots.Length; i++)
        {
            dots[i].Fill = i == (int)_vm.CurrentPage
                ? GetBrushResource("Accent.Primary", Brushes.DodgerBlue)
                : GetBrushResource("Border.Normal", Brushes.LightGray);
        }
    }

    private void BuildLanguageList()
    {
        if (_vm == null)
        {
            return;
        }

        OnboardingLanguageList.Children.Clear();

        foreach (var lang in Enum.GetValues<AppLanguage>())
        {
            var isSelected = _vm.SelectedLanguage == lang;
            var accentBrush = new SolidColorBrush(_vm.SelectedAccentColorValue);

            var row = new Border
            {
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(14, 8),
                Margin = new Thickness(0, 0, 0, 4),
                Cursor = new Cursor(StandardCursorType.Hand),
                Background = isSelected ? accentBrush : Brushes.Transparent,
                Tag = lang
            };

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var flagText = new TextBlock
            {
                Text = LanguageManager.GetLanguageFlag(lang),
                FontSize = 16,
                Margin = new Thickness(0, 0, 10, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = isSelected ? Brushes.White : GetBrushResource("Text.Primary", Brushes.Black)
            };
            Grid.SetColumn(flagText, 0);

            var nameText = new TextBlock
            {
                Text = LanguageManager.GetLanguageDisplayName(lang),
                FontSize = 12,
                FontWeight = isSelected ? FontWeight.SemiBold : FontWeight.Normal,
                Foreground = isSelected ? Brushes.White : GetBrushResource("Text.Primary", Brushes.Black),
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(nameText, 1);

            grid.Children.Add(flagText);
            grid.Children.Add(nameText);

            if (isSelected)
            {
                var check = new TextBlock
                {
                    Text = "\u2713",
                    FontSize = 13,
                    FontWeight = FontWeight.Bold,
                    Foreground = Brushes.White,
                    VerticalAlignment = VerticalAlignment.Center
                };
                Grid.SetColumn(check, 2);
                grid.Children.Add(check);
            }

            row.Child = grid;
            row.PointerPressed += OBLanguage_Click;
            OnboardingLanguageList.Children.Add(row);
        }
    }

    private void BuildAccentColors()
    {
        if (_vm == null)
        {
            return;
        }

        OBAccentPanel.Children.Clear();

        foreach (var (color, value) in AccentColors)
        {
            var isSelected = _vm.SelectedAccentColor == color;
            var border = new Border
            {
                Width = 32,
                Height = 32,
                CornerRadius = new CornerRadius(16),
                Margin = new Thickness(4),
                Cursor = new Cursor(StandardCursorType.Hand),
                Background = new SolidColorBrush(value),
                BorderThickness = new Thickness(3),
                BorderBrush = isSelected ? new SolidColorBrush(_vm.SelectedAccentColorValue) : Brushes.Transparent,
                Tag = color
            };

            if (isSelected)
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

            border.PointerPressed += OBAccent_Click;
            OBAccentPanel.Children.Add(border);
        }
    }

    private void UpdateThemeButtons()
    {
        if (_vm == null)
        {
            return;
        }

        var isLight = _vm.SelectedThemeMode == AppThemeMode.Light;
        var accentBrush = new SolidColorBrush(_vm.SelectedAccentColorValue);

        OBThemeLight.BorderBrush = isLight ? accentBrush : Brushes.Transparent;
        OBThemeDark.BorderBrush = !isLight ? accentBrush : Brushes.Transparent;
        OBThemeLight.Background = isLight ? accentBrush : GetBrushResource("Bg.Muted", Brushes.LightGray);
        OBThemeDark.Background = !isLight ? accentBrush : GetBrushResource("Bg.Muted", Brushes.LightGray);
    }

    private static IBrush GetBrushResource(string key, IBrush fallback)
    {
        if (Application.Current?.Resources.TryGetResource(key, null, out var resource) == true &&
            resource is IBrush brush)
        {
            return brush;
        }

        return fallback;
    }

    private void Next_Click(object? sender, PointerPressedEventArgs e)
    {
        if (_vm == null)
        {
            return;
        }

        if (_vm.IsLastPage)
        {
            _vm.CompleteCommand.Execute(null);
            OnboardingCompleted?.Invoke(this, EventArgs.Empty);
            return;
        }

        _vm.NextCommand.Execute(null);
    }

    private void Prev_Click(object? sender, PointerPressedEventArgs e)
    {
        _vm?.PreviousCommand.Execute(null);
    }

    private void Skip_Click(object? sender, PointerPressedEventArgs e)
    {
        _vm?.SkipCommand.Execute(null);
        OnboardingCompleted?.Invoke(this, EventArgs.Empty);
    }

    private void Dot_Click(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Control control || control.Tag is not string tag || !int.TryParse(tag, out var index))
        {
            return;
        }

        _vm?.GoToPageCommand.Execute(index);
    }

    private void OBLanguage_Click(object? sender, PointerPressedEventArgs e)
    {
        if (_vm == null || sender is not Control control || control.Tag is not AppLanguage language)
        {
            return;
        }

        _vm.SelectedLanguage = language;
        BuildLanguageList();
        LanguageChanged?.Invoke(this, language);
    }

    private void OBTheme_Click(object? sender, PointerPressedEventArgs e)
    {
        if (_vm == null || sender is not Control control || control.Tag is not string tag)
        {
            return;
        }

        var mode = tag == "Dark" ? AppThemeMode.Dark : AppThemeMode.Light;
        _vm.SelectedThemeMode = mode;
        UpdateThemeButtons();
        ThemeChanged?.Invoke(this, mode);
    }

    private void OBAccent_Click(object? sender, PointerPressedEventArgs e)
    {
        if (_vm == null || sender is not Control control || control.Tag is not AppAccentColor accentColor)
        {
            return;
        }

        _vm.SelectedAccentColor = accentColor;
        BuildAccentColors();
        AccentColorChanged?.Invoke(this, accentColor);
    }

    public event EventHandler? OnboardingCompleted;
    public event EventHandler<AppThemeMode>? ThemeChanged;
    public event EventHandler<AppAccentColor>? AccentColorChanged;
    public event EventHandler<AppLanguage>? LanguageChanged;
}
