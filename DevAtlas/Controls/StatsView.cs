using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using DevAtlas.Models;
using DevAtlas.Services;
using DevAtlas.ViewModels;

namespace DevAtlas.Controls;

public partial class StatsView : UserControl
{
    private StatsViewModel? _vm;
    private readonly EventHandler _accentColorChangedHandler;

    public StatsView()
    {
        InitializeComponent();
        PropertyChanged += OnPropertyChanged;
        _accentColorChangedHandler = (_, _) => RefreshRangeButtonsForCurrentRange();
        LanguageManager.Instance.AccentColorChanged += _accentColorChangedHandler;
        Unloaded += OnUnloaded;
    }

    private void OnPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == DataContextProperty)
        {
            OnDataContextChanged(sender, e);
        }
        else if (e.Property == IsVisibleProperty)
        {
            if (e.NewValue is false)
            {
                LanguageManager.Instance.AccentColorChanged -= _accentColorChangedHandler;
            }
            else if (e.NewValue is true)
            {
                LanguageManager.Instance.AccentColorChanged -= _accentColorChangedHandler;
                LanguageManager.Instance.AccentColorChanged += _accentColorChangedHandler;
            }
        }
    }

    private void OnDataContextChanged(object sender, AvaloniaPropertyChangedEventArgs e)
    {
        _vm = e.NewValue as StatsViewModel;
        RefreshRangeButtonsForCurrentRange();
    }

    private void DateRange_Click(object sender, Avalonia.Input.PointerPressedEventArgs e)
    {
        if (sender is not Control el || el.Tag is not string tag || _vm is null) return;

        var range = tag switch
        {
            "Week" => DateRangeFilter.Week,
            "Month" => DateRangeFilter.Month,
            "Year" => DateRangeFilter.Year,
            "AllTime" => DateRangeFilter.AllTime,
            _ => DateRangeFilter.Month
        };

        _vm.SetDateRange(range);
        UpdateRangeButtons(tag);
    }

    private void UpdateRangeButtons(string activeTag)
    {
        var buttons = new[] { RangeWeek, RangeMonth, RangeYear, RangeAll };
        var tags = new[] { "Week", "Month", "Year", "AllTime" };

        for (int i = 0; i < buttons.Length; i++)
        {
            var isActive = tags[i] == activeTag;
            buttons[i].Background = isActive
                ? (IBrush)this.FindResource("Accent.Primary")
                : (IBrush)this.FindResource("Bg.Muted");

            if (buttons[i].Child is TextBlock tb)
            {
                tb.Foreground = isActive
                    ? (IBrush)this.FindResource("Text.Inverse")
                    : (IBrush)this.FindResource("Text.Secondary");
            }
        }
    }

    private void RefreshRangeButtonsForCurrentRange()
    {
        if (_vm is null)
        {
            return;
        }

        var activeTag = _vm.DateRange switch
        {
            DateRangeFilter.Week => "Week",
            DateRangeFilter.Month => "Month",
            DateRangeFilter.Year => "Year",
            DateRangeFilter.AllTime => "AllTime",
            _ => "Month"
        };

        UpdateRangeButtons(activeTag);
    }

    private void OnUnloaded(object? sender, RoutedEventArgs e)
    {
        LanguageManager.Instance.AccentColorChanged -= _accentColorChangedHandler;
        Unloaded -= OnUnloaded;
    }

    private void GitProjectFilterToggle_Click(object sender, RoutedEventArgs e)
    {
        if (_vm is null) return;
        _vm.IsGitProjectFilterOpen = !_vm.IsGitProjectFilterOpen;
    }

    private void GitProjectFilterPopup_Closed(object sender, EventArgs e)
    {
        if (_vm is null) return;
        _vm.IsGitProjectFilterOpen = false;
    }

    private void GitProjectFilterSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_vm is null || sender is not ListBox listBox || listBox.SelectedItem is not GitProjectFilterOption option) return;
        _vm.SelectedGitProjectFilter = option;
    }
}
