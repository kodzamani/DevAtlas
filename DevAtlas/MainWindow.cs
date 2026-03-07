using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using DevAtlas.Controls;
using DevAtlas.Enums;
using DevAtlas.Models;
using DevAtlas.Services;
using DevAtlas.ViewModels;
using MsBox.Avalonia;
using MsBox.Avalonia.Enums;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace DevAtlas
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window, IDisposable
    {
        private readonly ProjectScanner _scanner;
        private readonly ProjectIndex _index;
        private readonly CodeEditorDetector _editorDetector;
        private readonly ProjectAnalyzerService _projectAnalyzer;
        private ProjectStatisticsService _statisticsService;
        public ObservableCollection<ProjectInfo> Projects { get; } = new();
        public ObservableCollection<ProjectInfo> FilteredProjects { get; } = new();
        public ObservableCollection<ProjectGroup> ProjectGroups { get; } = new();
        public ObservableCollection<ProjectListEntry> ProjectListEntries { get; } = new();
        private CancellationTokenSource? _scanCancellationToken;
        private CancellationTokenSource? _statusStatisticsCancellationToken;
        private CancellationTokenSource? _detailStatisticsCancellationToken;
        private string _currentFilter = "All";
        private string _searchText = "";
        private bool _isGridView = true;
        private ProjectInfo? _currentDetailProject;
        private Process? _runningProcess;
        private bool _isProjectRunning = false;
        private bool _isClosing = false;
        private EventHandler? _processExitedHandler;
        private bool _disposed = false;
        private bool _isDarkMode = false;
        private string _currentTab = "Atlas";
        private readonly EventHandler _accentColorChangedHandler;
        private readonly EventHandler _languageChangedHandler;
        private readonly EventHandler<AppThemeMode> _settingsViewThemeChangedHandler;
        private readonly EventHandler<AppLanguage> _settingsViewLanguageChangedHandler;
        private readonly EventHandler _settingsViewTourRequestedHandler;
        private readonly EventHandler<AppThemeMode> _onboardingViewThemeChangedHandler;

        // Dependency management
        private readonly DependencyDetectorService _dependencyDetector = new();
        private readonly PackageUpdateCheckerService _packageUpdateChecker = new();
        private CancellationTokenSource? _dependencyCancellationToken;
        private List<DependencySection>? _currentDependencySections;

        // ViewModels
        private readonly StatsViewModel _statsViewModel = new();
        private readonly AIPromptsViewModel _aiPromptsViewModel = new();
        private readonly SettingsViewModel _settingsViewModel = new();
        private readonly OnboardingViewModel _onboardingViewModel = new();
        private const double ProjectGridCardFootprint = 272;
        private int _gridProjectsPerRow = 1;

        public MainWindow()
        {
            InitializeComponent();

            _accentColorChangedHandler = (_, _) => UpdateAccentColorResources();
            _languageChangedHandler = (_, _) => Dispatcher.UIThread.Post(UpdateLanguageResources);
            _settingsViewThemeChangedHandler = (_, mode) => ApplyThemeFromMode(mode);
            _settingsViewLanguageChangedHandler = (_, _) => Dispatcher.UIThread.Post(UpdateLanguageResources);
            _settingsViewTourRequestedHandler = (_, _) => _onboardingViewModel.ShowOnboarding();
            _onboardingViewThemeChangedHandler = (_, mode) => ApplyThemeFromMode(mode);

            _scanner = new ProjectScanner(LanguageManager.Instance.GetExcludePaths());
            _index = new ProjectIndex();
            _editorDetector = new CodeEditorDetector();
            _projectAnalyzer = new ProjectAnalyzerService();
            _statisticsService = new ProjectStatisticsService(_projectAnalyzer);

            DataContext = this;

            _scanner.ProgressChanged += Scanner_ProgressChanged;
            EditorSelectorPopupControl.EditorSelected += EditorSelectorPopup_EditorSelected;
            ProjectAnalyzePopupControl.FindUnusedClicked += ProjectAnalyzePopup_FindUnusedClicked;
            RunScriptPopupControl.ScriptSelected += RunScriptPopup_ScriptSelected;
            KeyDown += Window_PreviewKeyDown;

            // Wire up new view DataContexts
            StatsViewControl.DataContext = _statsViewModel;
            AIPromptsViewControl.DataContext = _aiPromptsViewModel;
            SettingsViewControl.DataContext = _settingsViewModel;
            OnboardingViewControl.DataContext = _onboardingViewModel;

            // Wire up settings/onboarding events
            AIPromptsViewControl.PromptExpandRequested += AIPromptsViewControl_PromptExpandRequested;
            SettingsViewControl.ThemeChanged += _settingsViewThemeChangedHandler;
            SettingsViewControl.LanguageChanged += _settingsViewLanguageChangedHandler;
            SettingsViewControl.TourRequested += _settingsViewTourRequestedHandler;
            _settingsViewModel.SettingsSaved += SettingsViewModel_SettingsSaved;
            OnboardingViewControl.ThemeChanged += _onboardingViewThemeChangedHandler;
            LanguageManager.Instance.AccentColorChanged += _accentColorChangedHandler;
            LanguageManager.Instance.LanguageChanged += _languageChangedHandler;

            // Persist TotalLines/TotalFiles to the project index after first analysis
            // so subsequent Stats page loads are fast (no re-scan needed)
            _statsViewModel.ProjectStatisticsPopulated += async (_, _) =>
            {
                try { await _index.SaveProjectsAsync(Projects.ToList()); } catch { }
            };

            TabAIPromptsText.Text = _aiPromptsViewModel.SidebarLabel;

            Opened += MainWindow_Loaded;
            Closing += MainWindow_Closing;
        }

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
                    // Clear collections to release references
                    Projects.Clear();
                    FilteredProjects.Clear();
                    ProjectGroups.Clear();
                    ProjectListEntries.Clear();

                    // Clear index cache to release memory
                    _index?.ClearCache();

                    // Unsubscribe from events
                    if (_scanner != null)
                    {
                        _scanner.ProgressChanged -= Scanner_ProgressChanged;
                    }
                    if (EditorSelectorPopupControl != null)
                    {
                        EditorSelectorPopupControl.EditorSelected -= EditorSelectorPopup_EditorSelected;
                    }
                    if (AIPromptsViewControl != null)
                    {
                        AIPromptsViewControl.PromptExpandRequested -= AIPromptsViewControl_PromptExpandRequested;
                    }
                    if (ProjectAnalyzePopupControl != null)
                    {
                        ProjectAnalyzePopupControl.FindUnusedClicked -= ProjectAnalyzePopup_FindUnusedClicked;
                    }
                    if (RunScriptPopupControl != null)
                    {
                        RunScriptPopupControl.ScriptSelected -= RunScriptPopup_ScriptSelected;
                    }
                    KeyDown -= Window_PreviewKeyDown;
                    LanguageManager.Instance.AccentColorChanged -= _accentColorChangedHandler;
                    LanguageManager.Instance.LanguageChanged -= _languageChangedHandler;
                    Opened -= MainWindow_Loaded;
                    Closing -= MainWindow_Closing;

                    // Unsubscribe from SettingsView events
                    if (SettingsViewControl != null)
                    {
                        SettingsViewControl.ThemeChanged -= _settingsViewThemeChangedHandler;
                        SettingsViewControl.LanguageChanged -= _settingsViewLanguageChangedHandler;
                        SettingsViewControl.TourRequested -= _settingsViewTourRequestedHandler;
                    }

                    // Unsubscribe from OnboardingView events
                    if (OnboardingViewControl != null)
                    {
                        OnboardingViewControl.ThemeChanged -= _onboardingViewThemeChangedHandler;
                    }

                    // Unsubscribe from ViewModel events
                    if (_settingsViewModel != null)
                    {
                        _settingsViewModel.SettingsSaved -= SettingsViewModel_SettingsSaved;
                    }

                    // Unsubscribe from process exited event
                    if (_runningProcess != null && _processExitedHandler != null)
                    {
                        _runningProcess.Exited -= _processExitedHandler;
                    }

                    // Cancel and dispose cancellation tokens
                    _scanCancellationToken?.Cancel();
                    _scanCancellationToken?.Dispose();
                    _statusStatisticsCancellationToken?.Cancel();
                    _statusStatisticsCancellationToken?.Dispose();
                    _detailStatisticsCancellationToken?.Cancel();
                    _detailStatisticsCancellationToken?.Dispose();
                    _dependencyCancellationToken?.Cancel();
                    _dependencyCancellationToken?.Dispose();
                    _searchDebounceToken?.Cancel();
                    _searchDebounceToken?.Dispose();

                    // Dispose statistics service
                    _statisticsService?.Dispose();
                    _packageUpdateChecker?.Dispose();
                    _statsViewModel.Dispose();
                    _aiPromptsViewModel.Dispose();

                    // Dispose running process
                    _runningProcess?.Dispose();

                    // Dispose scanner and index if they implement IDisposable
                    if (_scanner is IDisposable disposableScanner)
                    {
                        disposableScanner.Dispose();
                    }
                    if (_index is IDisposable disposableIndex)
                    {
                        disposableIndex.Dispose();
                    }
                }

                _disposed = true;
            }
        }

        /// <summary>
        /// Optimizes memory usage by clearing unused resources
        /// </summary>
        public void OptimizeMemory()
        {
            // Clear filtered projects when not needed
            if (string.IsNullOrEmpty(_searchText) && _currentFilter == "All")
            {
                FilteredProjects.Clear();
                foreach (var p in Projects)
                {
                    FilteredProjects.Add(p);
                }
            }

            // Clear index cache to free memory
            _index?.ClearCache();

            // Suggest garbage collection
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Optimized);
        }

        private void Window_PreviewKeyDown(object? sender, KeyEventArgs e)
        {
            if (e.Key == Key.F && (e.KeyModifiers & KeyModifiers.Control) == KeyModifiers.Control)
            {
                SearchBox.Focus();
                e.Handled = true;
            }
            else if (e.Key == Key.Escape)
            {
                // Priority: close popup first, then onboarding, then detail view
                if (UnusedCodePopupControl.IsVisible)
                {
                    UnusedCodePopupControl.Hide();
                    e.Handled = true;
                }
                else if (ProjectAnalyzePopupControl.IsVisible)
                {
                    ProjectAnalyzePopupControl.Hide();
                    e.Handled = true;
                }
                else if (EditorSelectorPopupControl.IsVisible)
                {
                    EditorSelectorPopupControl.Hide();
                    e.Handled = true;
                }
                else if (RunScriptPopupControl.IsVisible)
                {
                    RunScriptPopupControl.Hide();
                    e.Handled = true;
                }
                else if (AIPromptDetailPopupControl.IsVisible)
                {
                    AIPromptDetailPopupControl.Hide();
                    e.Handled = true;
                }
                else if (_onboardingViewModel.IsPresented)
                {
                    _onboardingViewModel.SkipCommand.Execute(null);
                    e.Handled = true;
                }
                else if (ProjectDetailPanel.IsVisible)
                {
                    _ = CloseDetailViewAsync();
                    e.Handled = true;
                }
            }
            else if (e.Key == Key.Enter)
            {
                if (ProjectDetailPanel.IsVisible && _currentDetailProject != null)
                {
                    var editors = _editorDetector.DetectInstalledEditors();
                    var defaultEditor = editors.FirstOrDefault(ed => ed.Name == "vscode") ?? editors.FirstOrDefault();
                    if (defaultEditor != null && !string.IsNullOrEmpty(defaultEditor.FullPath))
                    {
                        ProjectRunner.OpenInEditor(defaultEditor.FullPath, _currentDetailProject.Path);
                        e.Handled = true;
                    }
                }
            }
        }

        private async void MainWindow_Loaded(object? sender, EventArgs e)
        {
            // Load theme from LanguageManager (unified settings)
            var langMgr = LanguageManager.Instance;
            var themeMode = langMgr.ThemeMode;
            _isDarkMode = themeMode switch
            {
                AppThemeMode.Dark => true,
                AppThemeMode.Light => false,
                AppThemeMode.System => IsSystemDarkMode(),
                _ => false
            };

            // Also fallback to old theme.txt if LanguageManager hasn't been configured yet
            if (themeMode == AppThemeMode.Light)
            {
                try
                {
                    var themeFile = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "theme.txt");
                    if (File.Exists(themeFile))
                    {
                        var oldTheme = File.ReadAllText(themeFile).Trim();
                        if (oldTheme == "Dark")
                        {
                            _isDarkMode = true;
                            langMgr.ThemeMode = AppThemeMode.Dark;
                        }
                    }
                }
                catch { }
            }

            ApplyTheme(_isDarkMode);

            // Check if we need to rescan or can load from index
            if (await _index.NeedsRescanAsync())
            {
                await RunScanAsync();
            }
            else
            {
                // Load from cache
                var projects = await _index.LoadProjectsAsync();
                var excludePaths = LanguageManager.Instance.GetExcludePaths();
                Projects.Clear();
                foreach (var p in projects)
                {
                    // Skip projects whose paths are currently excluded
                    if (IsProjectExcluded(p.Path, excludePaths)) continue;

                    // Re-derive category from cached data (category may be missing from old cache)
                    if (p.Category == "Other" || string.IsNullOrEmpty(p.Category))
                    {
                        string[] files;
                        try
                        {
                            files = Directory.Exists(p.Path) ? Directory.GetFiles(p.Path) : Array.Empty<string>();
                        }
                        catch
                        {
                            files = Array.Empty<string>();
                        }
                        p.Category = ProjectScanner.DetectCategory(p.ProjectType, p.Tags, files);
                    }
                    Projects.Add(p);
                }
                UpdateProjectCount();

                // Optimize memory after loading - clear cache since we have projects in UI
                _index.ClearCache();

                // Suggest garbage collection to reclaim memory
                GC.Collect(GC.MaxGeneration, GCCollectionMode.Optimized);

                // Initialize all SVG icons and update tab/sidebar selection on startup
                Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
                {
                    UpdateAllIcons(_isDarkMode);
                });
            }
        }

        private async void MainWindow_Closing(object? sender, WindowClosingEventArgs e)
        {
            // Stop any running process before closing
            if (_runningProcess != null && _isProjectRunning && !_isClosing)
            {
                // Cancel the default close so we can do async cleanup
                e.Cancel = true;
                _isClosing = true;

                try
                {
                    // Kill the process tree
                    await KillProcessTreeAsync(_runningProcess.Id);

                    // Try to kill by process name as a fallback
                    await KillProcessByNameAsync("npm.exe");
                    await KillProcessByNameAsync("node.exe");

                    // Unsubscribe from process exited event before disposing
                    if (_processExitedHandler != null)
                    {
                        _runningProcess.Exited -= _processExitedHandler;
                    }
                    _runningProcess?.Dispose();
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Error during closing: {ex.Message}");
                }
                finally
                {
                    _runningProcess = null;
                    _isProjectRunning = false;

                    // Now close the window for real
                    if (Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop)
                        desktop.Shutdown();
                }
            }
            else
            {
                // Dispose resources when closing
                Dispose();
            }
        }

        private async Task RunScanAsync()
        {
            _scanCancellationToken?.Cancel();
            _scanCancellationToken?.Dispose();
            _scanCancellationToken = new CancellationTokenSource();
            var token = _scanCancellationToken.Token;

            ScanningOverlay.Reset();
            ScanningOverlay.Show();

            try
            {
                var projects = await _scanner.ScanAllDrivesAsync(token);

                if (!token.IsCancellationRequested)
                {
                    await _index.SaveProjectsAsync(projects);
                    Projects.Clear();
                    foreach (var p in projects)
                    {
                        Projects.Add(p);
                    }
                    UpdateProjectCount();

                    // Optimize memory after scan - clear cache since we have projects in UI
                    _index.ClearCache();

                    // Suggest garbage collection to reclaim memory
                    GC.Collect(GC.MaxGeneration, GCCollectionMode.Optimized);

                    // Ensure newly scanned items get correct icon variants
                    Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        UpdateAllIcons(_isDarkMode);
                    });
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                await ErrorDialogService.ShowExceptionAsync(ex, "Project scan failed.");
            }
            finally
            {
                ScanningOverlay.Hide();
            }
        }

        private void Scanner_ProgressChanged(object? sender, ScanProgress e)
        {
            ScanningOverlay.UpdateProgress(
                e.CurrentDrive,
                e.CurrentPath,
                e.ProgressPercentage,
                e.Status
            );
        }

        private async void RescanButton_Click(object sender, PointerPressedEventArgs e)
        {
            // Close detail view if open when rescanning
            await CloseDetailViewAsync();

            _index.ClearCache();
            await RunScanAsync();
        }

        private async void SettingsViewModel_SettingsSaved(object? sender, EventArgs e)
        {
            // Close detail view if open when settings are saved
            await CloseDetailViewAsync();

            // Update scanner with new exclude paths
            var excludePaths = LanguageManager.Instance.GetExcludePaths();
            _scanner.UpdateExcludePaths(excludePaths);

            // Clear cache and run scan
            _index.ClearCache();
            await RunScanAsync();
        }

        private void AIPromptsViewControl_PromptExpandRequested(object? sender, AIPromptRequestedEventArgs e)
        {
            AIPromptDetailPopupControl.Show(e.Prompt);
        }

        private static bool IsProjectExcluded(string projectPath, List<string> excludePaths)
        {
            if (excludePaths == null || excludePaths.Count == 0) return false;
            var normalizedProject = projectPath.Replace('/', '\\').TrimEnd('\\');
            foreach (var excludePath in excludePaths)
            {
                if (string.IsNullOrWhiteSpace(excludePath)) continue;
                var normalizedExclude = excludePath.Replace('/', '\\').TrimEnd('\\');
                if (normalizedProject.StartsWith(normalizedExclude, StringComparison.OrdinalIgnoreCase))
                {
                    if (normalizedProject.Length == normalizedExclude.Length ||
                        (normalizedProject.Length > normalizedExclude.Length && normalizedProject[normalizedExclude.Length] == '\\'))
                        return true;
                }
            }
            return false;
        }

        private void UpdateProjectCount()
        {
            var count = Projects.Count;
            SidebarProjectCountText.Text = count.ToString();

            // Update category counts
            SidebarWebCount.Text = Projects.Count(p => p.Category == "Web").ToString();
            SidebarDesktopCount.Text = Projects.Count(p => p.Category == "Desktop").ToString();
            SidebarMobileCount.Text = Projects.Count(p => p.Category == "Mobile").ToString();
            SidebarCloudCount.Text = Projects.Count(p => p.Category == "Cloud").ToString();

            // Show/hide status display based on whether projects are loaded
            StatusDisplayControl.IsVisible = Projects.Count > 0;

            ApplyFilter();
        }

        private void RefreshMainContentVisibility()
        {
            var isAtlas = _currentTab == "Atlas";
            var isStats = _currentTab == "Stats";
            var isAIPrompts = _currentTab == "AIPrompts";
            var isSettings = _currentTab == "Settings";
            var isDetailOpen = ProjectDetailPanel.IsVisible;
            var isEmpty = FilteredProjects.Count == 0;

            if (isDetailOpen)
            {
                ProjectListGrid.IsVisible = false;
                EmptyStatePanel.IsVisible = false;
                SetStatsViewActive(false);
                AIPromptsViewControl.IsVisible = false;
                SettingsViewControl.IsVisible = false;
                return;
            }

            ProjectListGrid.IsVisible = isAtlas && !isEmpty;
            EmptyStatePanel.IsVisible = isAtlas && isEmpty;
            SetStatsViewActive(isStats);
            AIPromptsViewControl.IsVisible = isAIPrompts;
            SettingsViewControl.IsVisible = isSettings;
        }

        private void SetStatsViewActive(bool isActive)
        {
            if (!ReferenceEquals(StatsViewControl.DataContext, _statsViewModel))
            {
                StatsViewControl.DataContext = _statsViewModel;
            }

            StatsViewControl.IsVisible = isActive;
        }

        private void ApplyFilter()
        {
            FilteredProjects.Clear();

            IEnumerable<ProjectInfo> filtered = _currentFilter == "All"
                ? Projects
                : Projects.Where(p => p.Category == _currentFilter);

            // Apply search text filter
            if (!string.IsNullOrWhiteSpace(_searchText))
            {
                var searchLower = _searchText.ToLower();
                filtered = filtered.Where(p =>
                    p.Name.ToLower().Contains(searchLower) ||
                    p.Path.ToLower().Contains(searchLower) ||
                    p.ProjectType.ToLower().Contains(searchLower) ||
                    p.Tags.Any(t => t.ToLower().Contains(searchLower)));
            }

            // Sort by LastModified in descending order (most recently modified first)
            filtered = filtered.OrderByDescending(p => p.LastModified);

            foreach (var p in filtered)
            {
                FilteredProjects.Add(p);
            }

            // Build time-based groups
            ProjectGroups.Clear();
            var groups = ProjectGroup.GroupByLastModified(FilteredProjects);
            foreach (var g in groups)
            {
                ProjectGroups.Add(g);
            }

            UpdateProjectListEntries();

            // Update breadcrumb
            var categoryText = _currentFilter == "All" ? LanguageManager.Instance["MessageAllLocations"] : _currentFilter;
            BreadcrumbCategoryText.Text = categoryText;
            FilteredCountText.Text = FilteredProjects.Count.ToString();

            // Update status display only when filter is applied (not in real-time)
            // pass the current filtered set so statistics reflect the sidebar/search filter
            _ = UpdateStatusDisplayAsync(FilteredProjects);

            RefreshMainContentVisibility();

            // Re-apply dark mode icons to newly rendered DataTemplate items
            if (_isDarkMode)
            {
                Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
                {
                    SwapSvgIconsInTree(this, true);
                });
            }
        }

        private CancellationTokenSource? _searchDebounceToken;
        private readonly TimeSpan _searchDebounceDelay = TimeSpan.FromMilliseconds(300);

        private async void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (sender is TextBox textBox)
            {
                _searchText = textBox.Text;

                // Close detail view if open when search text changes
                await CloseDetailViewAsync();

                // Show/hide placeholder
                if (SearchPlaceholder != null)
                {
                    SearchPlaceholder.IsVisible = string.IsNullOrEmpty(_searchText);
                }

                // Debounce search to avoid recalculating on every keystroke
                _searchDebounceToken?.Cancel();
                _searchDebounceToken = new CancellationTokenSource();
                try
                {
                    await Task.Delay(_searchDebounceDelay, _searchDebounceToken.Token);
                    ApplyFilter();
                }
                catch (TaskCanceledException)
                {
                    // Debounce cancelled, ignore
                }
            }
        }

        private async void SidebarFilter_Click(object sender, PointerPressedEventArgs e)
        {
            if (sender is Border border && border.Tag is string filter)
            {
                // Close detail view if open when navigation changes
                await CloseDetailViewAsync();

                _currentFilter = filter;
                UpdateSidebarSelection();
                ApplyFilter();
            }
        }

        private void UpdateSidebarSelection()
        {
            // Reset all sidebar items
            SidebarAllProjects.Background = null;
            SidebarWeb.Background = null;
            SidebarDesktop.Background = null;
            SidebarMobile.Background = null;
            SidebarCloud.Background = null;

            // Set selected item background
            var selected = _currentFilter switch
            {
                "All" => SidebarAllProjects,
                "Web" => SidebarWeb,
                "Desktop" => SidebarDesktop,
                "Mobile" => SidebarMobile,
                "Cloud" => SidebarCloud,
                _ => SidebarAllProjects
            };

            selected.Bind(Border.BackgroundProperty, this.GetResourceObservable("Bg.SidebarActive"));

            // Tint category icons for selected filter
            try
            {
                var accent = (IBrush)this.FindResource("Accent.Primary")!;
                var hover = (IBrush)this.FindResource("Bg.Hover")!;
                var badge = (IBrush)this.FindResource("Bg.Badge")!;
                var secondary = (IBrush)this.FindResource("Text.Secondary")!;
                var activeText = (IBrush)this.FindResource("Text.SidebarActive")!;
                var isAllSelected = _currentFilter == "All";
                var allProjectsBg = isAllSelected ? accent : hover;
                var allProjectsLightCell = isAllSelected
                    ? new SolidColorBrush(Color.FromArgb(191, 255, 255, 255))
                    : secondary;
                var allProjectsDarkCell = isAllSelected
                    ? new SolidColorBrush(Color.FromArgb(147, 255, 255, 255))
                    : secondary;
                SidebarWebIconBorder.Background = _currentFilter == "Web" ? accent : Brushes.Transparent;
                SidebarDesktopIconBorder.Background = _currentFilter == "Desktop" ? accent : Brushes.Transparent;
                SidebarMobileIconBorder.Background = _currentFilter == "Mobile" ? accent : Brushes.Transparent;
                SidebarCloudIconBorder.Background = _currentFilter == "Cloud" ? accent : Brushes.Transparent;
                SidebarAllProjectsIconBorder.Background = allProjectsBg;
                SidebarAllProjectsText.Foreground = isAllSelected ? activeText : secondary;
                SidebarAllProjectsBadge.Background = isAllSelected ? badge : Brushes.Transparent;
                SidebarProjectCountText.Foreground = isAllSelected ? activeText : secondary;
                SidebarAllProjectsCell1.Background = allProjectsLightCell;
                SidebarAllProjectsCell2.Background = allProjectsDarkCell;
                SidebarAllProjectsCell3.Background = allProjectsDarkCell;
                SidebarAllProjectsCell4.Background = allProjectsLightCell;

                // Ensure category icons are reset when All is selected.
                if (_currentFilter == "All")
                {
                    SidebarWebIconBorder.Background = Brushes.Transparent;
                    SidebarDesktopIconBorder.Background = Brushes.Transparent;
                    SidebarMobileIconBorder.Background = Brushes.Transparent;
                    SidebarCloudIconBorder.Background = Brushes.Transparent;
                }
                // Swap svg sources for selected category icons
                try
                {
                    SetSvgSelection(SidebarWebSvg, _currentFilter == "Web");
                    SetSvgSelection(SidebarDesktopSvg, _currentFilter == "Desktop");
                    SetSvgSelection(SidebarMobileSvg, _currentFilter == "Mobile");
                    SetSvgSelection(SidebarCloudSvg, _currentFilter == "Cloud");
                }
                catch { }
            }
            catch { }
        }

        private void ViewToggle_Click(object sender, PointerPressedEventArgs e)
        {
            if (sender is Border border && border.Tag is string view)
            {
                _isGridView = view == "Grid";
                UpdateViewToggleButtons();
                UpdateItemsPanel();
            }
        }

        private void UpdateViewToggleButtons()
        {
            if (_isGridView)
            {
                ViewGridBtn.Bind(Border.BackgroundProperty, this.GetResourceObservable("Border.Normal"));
                ViewListBtn.Background = null;
            }
            else
            {
                ViewGridBtn.Background = null;
                ViewListBtn.Bind(Border.BackgroundProperty, this.GetResourceObservable("Border.Normal"));
            }
        }

        private void UpdateItemsPanel()
        {
            UpdateProjectListEntries();
        }

        private void ProjectListScrollViewer_SizeChanged(object? sender, SizeChangedEventArgs e)
        {
            if (!_isGridView || !e.WidthChanged)
            {
                return;
            }

            var nextProjectsPerRow = CalculateProjectsPerRow(e.NewSize.Width);
            if (nextProjectsPerRow == _gridProjectsPerRow)
            {
                return;
            }

            _gridProjectsPerRow = nextProjectsPerRow;
            UpdateProjectListEntries();
        }

        private void UpdateProjectListEntries()
        {
            if (_isGridView)
            {
                _gridProjectsPerRow = CalculateProjectsPerRow(ProjectListScrollViewer.Bounds.Width);
            }

            ProjectListEntries.Clear();

            foreach (var group in ProjectGroups)
            {
                ProjectListEntries.Add(new ProjectGroupHeaderEntry
                {
                    GroupName = group.GroupName,
                    Icon = group.Icon,
                    Count = group.Count
                });

                if (_isGridView)
                {
                    foreach (var chunk in group.Projects.Chunk(_gridProjectsPerRow))
                    {
                        ProjectListEntries.Add(new ProjectGridRowEntry
                        {
                            Projects = chunk.ToArray()
                        });
                    }
                }
                else
                {
                    foreach (var project in group.Projects)
                    {
                        ProjectListEntries.Add(new ProjectListItemEntry
                        {
                            Project = project
                        });
                    }
                }
            }
        }

        private static int CalculateProjectsPerRow(double availableWidth)
        {
            var usableWidth = Math.Max(availableWidth - 32, ProjectGridCardFootprint);
            return Math.Max(1, (int)Math.Floor(usableWidth / ProjectGridCardFootprint));
        }
        private void Header_PointerPressed(object sender, PointerPressedEventArgs e)
        {
            if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            {
                BeginMoveDrag(e);
            }
        }

        private void MinimizeButton_Click(object sender, PointerPressedEventArgs e)
        {
            WindowState = WindowState.Minimized;
        }

        private void MaximizeButton_Click(object sender, PointerPressedEventArgs e)
        {
            if (WindowState == WindowState.Maximized)
            {
                WindowState = WindowState.Normal;
            }
            else
            {
                WindowState = WindowState.Maximized;
            }
        }

        private void CloseButton_Click(object sender, PointerPressedEventArgs e)
        {
            Close();
        }

        private void ProjectCard_Click(object sender, PointerPressedEventArgs e)
        {
            // Walk up the visual tree to find the Border with DataContext
            var element = sender as Control;
            if (element?.DataContext is ProjectInfo project)
            {
                ShowProjectDetail(project);
            }
        }

        private ProjectInfo? GetProjectFromContextMenu(object sender)
        {
            if (sender is MenuItem menuItem && menuItem.DataContext is ProjectInfo project)
            {
                return project;
            }
            return null;
        }

        private void ContextMenu_OpenTerminal_Click(object sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            if (GetProjectFromContextMenu(sender) is ProjectInfo project)
            {
                try { PlatformHelper.OpenTerminal(project.Path); } catch { }
            }
        }

        private void ContextMenu_OpenExplorer_Click(object sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            if (GetProjectFromContextMenu(sender) is ProjectInfo project)
            {
                try { Process.Start(new ProcessStartInfo(PlatformHelper.GetFileManagerCommand(), project.Path) { UseShellExecute = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) }); } catch { }
            }
        }

        private void ContextMenu_Properties_Click(object sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            if (GetProjectFromContextMenu(sender) is ProjectInfo project)
            {
                ShowProjectDetail(project);
            }
        }

        private async void ShowProjectDetail(ProjectInfo project)
        {
            _currentDetailProject = project;

            // Check if project is runnable
            bool isRunnable = ProjectRunner.IsRunnableProject(project.Path);

            // Check if project is a C#/.NET Core project for Visual Studio button
            // initial assessment based on project type string
            bool isDotNetCoreProject = IsDotNetCoreProject(project.ProjectType);

            // some .NET Core projects may not have a friendly project type yet, so also
            // look for a .csproj/.sln file in the folder hierarchy
            if (!isDotNetCoreProject && ProjectHasDotNetFile(project.Path))
            {
                isDotNetCoreProject = true;
            }

            // Hide/show Run Project button based on runnability
            RunProjectBorder.IsVisible = isRunnable;

            // Hide/show Visual Studio button based on project type (or file heuristic) and VS installation
            bool hasVisualStudio = !string.IsNullOrEmpty(FindVisualStudioPath());
            DetailOpenVSButton.IsVisible = (isDotNetCoreProject && hasVisualStudio);

            // Adjust UniformGrid columns: always has Open Editor, Analyze, Terminal, Explorer + conditionally Run + conditionally VS
            int columnCount = 4; // Open Editor, Analyze, Terminal, Explorer
            if (isRunnable) columnCount++;
            if (isDotNetCoreProject && hasVisualStudio) columnCount++;
            QuickActionsGrid.Columns = columnCount;

            // Reset run button state
            _isProjectRunning = false;
            UpdateRunButton();

            // Hide the project list and show detail panel
            ProjectListGrid.IsVisible = false;
            ProjectDetailPanel.IsVisible = true;

            // Populate detail panel
            DetailProjectName.Text = project.Name;
            DetailProjectPath.Text = project.Path;
            DetailProjectType.Text = project.ProjectType;
            DetailActiveBadge.IsVisible = project.IsActive;
            DetailGitBranch.Text = project.GitBranch ?? "N/A";
            DetailGitBranchPanel.IsVisible = !string.IsNullOrEmpty(project.GitBranch);

            // Icon
            DetailIconText.Text = project.IconText;
            try
            {
                DetailIconBorder.Background = new SolidColorBrush(Color.Parse(project.IconColor ?? "#6B7280"));
            }
            catch
            {
                DetailIconBorder.Background = new SolidColorBrush(Color.FromRgb(107, 114, 128));
            }

            // Tech Stack tags - initially show without line counts, then load async
            DetailTechTags.ItemsSource = project.Tags.Select(t => new Models.TechStackItem { Name = t, Lines = 0 }).ToList();
            DetailTechTotalLines.Text = ""; // clear until loaded

            // Ensure detail panel SVGs are initialized every time (light/dark).
            Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
            {
                SwapSvgIconsInTree(ProjectDetailPanel, _isDarkMode);
            });

            // Load project statistics asynchronously to prevent UI hanging
            await LoadProjectStatisticsAsync(project);

            // Load tech stack line counts asynchronously
            await LoadTechStackLinesAsync(project);

            // Load dependencies asynchronously
            await LoadDependenciesAsync(project);
        }

        private async Task LoadTechStackLinesAsync(ProjectInfo project)
        {
            try
            {
                var techItems = await _projectAnalyzer.GetTechStackWithLinesAsync(project.Path, project.Tags);
                if (_currentDetailProject == project)
                {
                    DetailTechTags.ItemsSource = techItems;
                    int total = project.TotalLines ?? 0;
                    DetailTechTotalLines.Text = total > 0 ? $"{total:N0} total lines" : string.Empty;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error loading tech stack lines: {ex.Message}");
            }
        }

        /// <summary>
        /// Loads project dependencies and checks for available updates.
        /// </summary>
        private async Task LoadDependenciesAsync(ProjectInfo project)
        {
            // Cancel any previous dependency loading
            _dependencyCancellationToken?.Cancel();
            _dependencyCancellationToken?.Dispose();
            _dependencyCancellationToken = new CancellationTokenSource();
            var token = _dependencyCancellationToken.Token;

            try
            {
                // Show loading state
                DependenciesSectionPanel.IsVisible = true;
                DependenciesLoadingPanel.IsVisible = true;
                DependencySectionsControl.ItemsSource = null;
                _currentDependencySections = null;

                // Detect dependencies
                var sections = await _dependencyDetector.DetectDependenciesAsync(project.Path, token);

                if (token.IsCancellationRequested || _currentDetailProject != project)
                    return;

                _currentDependencySections = sections;

                if (sections.Count > 0)
                {
                    DependencySectionsControl.ItemsSource = sections;
                    DependenciesLoadingPanel.IsVisible = false;

                    // Check for updates asynchronously (background, non-blocking)
                    _ = CheckDependencyUpdatesAsync(sections, project, token);
                }
                else
                {
                    // No dependencies found, hide the section
                    DependenciesSectionPanel.IsVisible = false;
                }
            }
            catch (OperationCanceledException)
            {
                // Cancelled, ignore
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error loading dependencies: {ex.Message}");
                DependenciesSectionPanel.IsVisible = false;
            }
        }

        /// <summary>
        /// Checks for available updates for all detected dependencies.
        /// </summary>
        private async Task CheckDependencyUpdatesAsync(List<DependencySection> sections, ProjectInfo project, CancellationToken token)
        {
            try
            {
                await _packageUpdateChecker.CheckForUpdatesAsync(sections, () =>
                {
                    // Refresh the UI when a batch completes
                    if (!token.IsCancellationRequested && _currentDetailProject == project)
                    {
                        Avalonia.Threading.Dispatcher.UIThread.Invoke(() =>
                        {
                            // Re-bind to refresh update badges
                            DependencySectionsControl.ItemsSource = null;
                            DependencySectionsControl.ItemsSource = _currentDependencySections;
                        });
                    }
                }, token);
            }
            catch (OperationCanceledException)
            {
                // Cancelled
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error checking dependency updates: {ex.Message}");
            }
        }

        /// <summary>
        /// Toggle expand/collapse for a dependency section header.
        /// </summary>
        private void DependencySection_Toggle(object sender, PointerPressedEventArgs e)
        {
            if (sender is Control element && element.DataContext is DependencySection section)
            {
                section.IsExpanded = !section.IsExpanded;
                // element is the header Border; its Parent is the enclosing StackPanel
                if (element.Parent is StackPanel sectionPanel)
                {
                    // Toggle groups ItemsControl
                    for (int i = 1; i < sectionPanel.Children.Count; i++)
                    {
                        if (sectionPanel.Children[i] is ItemsControl groupsControl)
                            groupsControl.IsVisible = section.IsExpanded;
                    }
                    // Flip the arrow on the section header (down / right)
                    if (element is Border headerBorder && headerBorder.Child is Grid headerGrid)
                    {
                        foreach (Control child in headerGrid.Children)
                        {
                            if (child is TextBlock tb && (tb.Text == "\u25BC" || tb.Text == "\u25B6") && Grid.GetColumn(tb) == 4)
                            {
                                tb.Text = section.IsExpanded ? "\u25BC" : "\u25B6";
                                break;
                            }
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Toggle expand/collapse for a dependency group (sub-project).
        /// </summary>
        private void DependencyGroup_Toggle(object sender, PointerPressedEventArgs e)
        {
            if (sender is Control element && element.DataContext is DependencyGroup group)
            {
                group.IsExpanded = !group.IsExpanded;
                if (element.Parent is StackPanel groupPanel)
                {
                    // Toggle the package table StackPanel
                    for (int i = 1; i < groupPanel.Children.Count; i++)
                    {
                        if (groupPanel.Children[i] is StackPanel tablePanel)
                            tablePanel.IsVisible = group.IsExpanded;
                    }
                    // Flip the arrow on the group header (right / down)
                    if (element is Border headerBorder && headerBorder.Child is Grid headerGrid)
                    {
                        foreach (Control child in headerGrid.Children)
                        {
                            if (child is TextBlock tb && (tb.Text == "\u25B6" || tb.Text == "\u25BC"))
                            {
                                tb.Text = group.IsExpanded ? "\u25BC" : "\u25B6";
                                break;
                            }
                        }
                    }
                }
            }
        }

        private async Task LoadProjectStatisticsAsync(ProjectInfo project)
        {
            // Cancel any previous statistics loading
            _detailStatisticsCancellationToken?.Cancel();
            _detailStatisticsCancellationToken?.Dispose();
            _detailStatisticsCancellationToken = new CancellationTokenSource();
            var token = _detailStatisticsCancellationToken.Token;

            await Task.Run(() =>
            {
                try
                {
                    if (Directory.Exists(project.Path))
                    {
                        long totalSize = 0;
                        int fileCount = 0;
                        int totalLines = 0;
                        List<FileInfo> includedFileInfos = new();

                        try
                        {
                            var includedFiles = _projectAnalyzer.GetIncludedProjectFilesAsync(project.Path, token).GetAwaiter().GetResult();
                            includedFileInfos = includedFiles
                                .Select(filePath =>
                                {
                                    try
                                    {
                                        return new FileInfo(filePath);
                                    }
                                    catch
                                    {
                                        return null;
                                    }
                                })
                                .Where(file => file != null)
                                .Cast<FileInfo>()
                                .ToList();

                            var analysis = _projectAnalyzer.AnalyzeProjectAsync(project.Path, token).GetAwaiter().GetResult();
                            fileCount = analysis?.TotalFiles ?? 0;
                            totalLines = analysis?.TotalLines ?? 0;
                            project.TotalFiles = fileCount;
                            project.TotalLines = totalLines;
                            totalSize = includedFileInfos.Sum(file => file.Length);
                        }
                        catch (OperationCanceledException)
                        {
                            return;
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"Error calculating project statistics: {ex.Message}");
                            totalSize = 0;
                            fileCount = 0;
                            totalLines = 0;
                            includedFileInfos = new List<FileInfo>();
                        }

                        // Check for cancellation
                        if (token.IsCancellationRequested)
                            return;

                        var sizeText = FormatSize(totalSize);
                        var fileCountText = fileCount.ToString("N0");
                        var langText = GetPrimaryLanguage(project);
                        var frameworkText = project.ProjectType;
                        var linesText = totalLines > 0 ? totalLines.ToString("N0") : "0";

                        // Recent Activity - get recently modified files
                        List<RecentFileInfo> recentFiles = new List<RecentFileInfo>();
                        try
                        {
                            // Check for cancellation before expensive operation
                            if (token.IsCancellationRequested)
                                return;

                            recentFiles = includedFileInfos
                                .OrderByDescending(f => f.LastWriteTime)
                                .Take(5)
                                .Select(f => new RecentFileInfo
                                {
                                    FileName = GetRelativePath(project.Path, f.FullName),
                                    Extension = f.Extension.TrimStart('.').ToUpper(),
                                    ModifiedAgo = GetTimeAgo(f.LastWriteTime)
                                })
                                .ToList();
                        }
                        catch (OperationCanceledException)
                        {
                            // Task was cancelled, return early
                            return;
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"Error getting recent files: {ex.Message}");
                        }

                        // Update UI on the main thread
                        if (!token.IsCancellationRequested)
                        {
                            Avalonia.Threading.Dispatcher.UIThread.Invoke(() =>
                            {
                                DetailStatSize.Text = sizeText;
                                DetailStatFiles.Text = fileCountText;
                                DetailStatLang.Text = langText;
                                DetailStatFramework.Text = frameworkText;
                                DetailStatLines.Text = linesText;
                                DetailRecentFiles.ItemsSource = recentFiles;
                            });
                        }
                    }
                    else
                    {
                        Avalonia.Threading.Dispatcher.UIThread.Invoke(() =>
                        {
                            DetailStatSize.Text = "N/A";
                            DetailStatFiles.Text = "N/A";
                            DetailStatLang.Text = "N/A";
                            DetailStatFramework.Text = project.ProjectType;
                            DetailStatLines.Text = "N/A";
                            DetailRecentFiles.ItemsSource = null;
                        });
                    }
                }
                catch (OperationCanceledException)
                {
                }
                catch
                {
                    Avalonia.Threading.Dispatcher.UIThread.Invoke(() =>
                    {
                        DetailStatSize.Text = "N/A";
                        DetailStatFiles.Text = "N/A";
                        DetailStatLang.Text = "N/A";
                        DetailStatFramework.Text = project.ProjectType;
                        DetailStatLines.Text = "N/A";
                    });
                }
            });
        }

        /// <summary>
        /// Closes the detail view and stops any running processes
        /// </summary>
        private async Task CloseDetailViewAsync()
        {
            // Only proceed if detail view is visible
            if (!ProjectDetailPanel!.IsVisible)
                return;

            // Stop any running process
            if (_isProjectRunning)
            {
                await StopProjectAsync();
            }

            // Cancel dependency loading
            _dependencyCancellationToken?.Cancel();
            _dependencyCancellationToken?.Dispose();
            _dependencyCancellationToken = null;
            _currentDependencySections = null;

            // Cancel detail statistics/loading work
            _detailStatisticsCancellationToken?.Cancel();
            _detailStatisticsCancellationToken?.Dispose();
            _detailStatisticsCancellationToken = null;

            // Dispose statistics service to release resources
            _statisticsService?.Dispose();
            _statisticsService = new ProjectStatisticsService(_projectAnalyzer);

            // Reset dependencies UI
            DependencySectionsControl.ItemsSource = null;
            DependenciesSectionPanel.IsVisible = false;

            // Hide detail view and show list view
            _currentDetailProject = null;
            ProjectDetailPanel.IsVisible = false;
            RefreshMainContentVisibility();
        }

        private async void DetailBackButton_Click(object sender, PointerPressedEventArgs e)
        {
            await CloseDetailViewAsync();
        }

        private void DetailOpenEditor_Click(object sender, PointerPressedEventArgs e)
        {
            EditorSelectorPopupControl.Show();
        }

        private void DetailAnalyzeProject_Click(object sender, PointerPressedEventArgs e)
        {
            if (_currentDetailProject != null)
            {
                if (UnusedCodePopupControl.IsVisible)
                {
                    UnusedCodePopupControl.Hide();
                }

                ProjectAnalyzePopupControl.Show(_currentDetailProject.Name, _currentDetailProject.Path);
            }
        }

        private void EditorSelectorPopup_EditorSelected(object? sender, CodeEditor editor)
        {
            var path = DetailProjectPath.Text;
            ProjectRunner.OpenInEditor(editor.FullPath, path);
        }

        private void ProjectAnalyzePopup_FindUnusedClicked(object? sender, FindUnusedEventArgs e)
        {
            // Find the project info for the given path
            var project = Projects.FirstOrDefault(p => p.Path.Equals(e.ProjectPath, StringComparison.OrdinalIgnoreCase));
            if (project != null)
            {
                if (ProjectAnalyzePopupControl.IsVisible)
                {
                    ProjectAnalyzePopupControl.Hide();
                }

                UnusedCodePopupControl.Show(project.Name, project.Path);
            }
        }

        private async void RunScriptPopup_ScriptSelected(object? sender, ScriptRunEventArgs e)
        {
            // If already running, stop it first
            if (_isProjectRunning)
            {
                await StopProjectAsync();
            }

            try
            {
                // Show running state immediately
                _isProjectRunning = true;
                UpdateRunButton();

                _runningProcess = ProjectRunner.StartDevServerWithOutput(e.ProjectPath, e.ScriptName,
                    output => { },
                    error => { });

                if (_runningProcess != null)
                {
                    _processExitedHandler = (s, args) =>
                    {
                        if (!_isClosing)
                        {
                            Avalonia.Threading.Dispatcher.UIThread.Invoke(() =>
                            {
                                _isProjectRunning = false;
                                UpdateRunButton();
                            });
                        }
                    };
                    _runningProcess.EnableRaisingEvents = true;
                    _runningProcess.Exited += _processExitedHandler;
                }
                else
                {
                    _isProjectRunning = false;
                    UpdateRunButton();
                    var box1 = MessageBoxManager.GetMessageBoxStandard(LanguageManager.Instance["MessageError"], LanguageManager.Instance["MessageFailedToStartDevServer"], ButtonEnum.Ok, MsBox.Avalonia.Enums.Icon.Error);
                    await box1.ShowAsync();
                }
            }
            catch (Exception ex)
            {
                _isProjectRunning = false;
                UpdateRunButton();
                var box2 = MessageBoxManager.GetMessageBoxStandard(LanguageManager.Instance["MessageError"], string.Format(LanguageManager.Instance["MessageFailedToRunProject"], ex.Message), ButtonEnum.Ok, MsBox.Avalonia.Enums.Icon.Error);
                await box2.ShowAsync();
            }
        }

        private string? FindSolutionFile(string projectPath)
        {
            try
            {
                // Look for .sln or .slnx files in the project directory
                var solutionFiles = Directory.GetFiles(projectPath, "*.sln")
                    .Concat(Directory.GetFiles(projectPath, "*.slnx"))
                    .ToArray();

                if (solutionFiles.Length > 0)
                {
                    // Return the first solution file found
                    return solutionFiles[0];
                }

                // If no solution files found in the root, search subdirectories (one level deep)
                var subdirectories = Directory.GetDirectories(projectPath);
                foreach (var subDir in subdirectories)
                {
                    try
                    {
                        var subSolutionFiles = Directory.GetFiles(subDir, "*.sln")
                            .Concat(Directory.GetFiles(subDir, "*.slnx"))
                            .ToArray();

                        if (subSolutionFiles.Length > 0)
                        {
                            return subSolutionFiles[0];
                        }
                    }
                    catch
                    {
                        // Skip directories we can't access
                        continue;
                    }
                }

                return null;
            }
            catch
            {
                return null;
            }
        }

        private void DetailOpenVisualStudio_Click(object sender, PointerPressedEventArgs e)
        {
            var path = DetailProjectPath.Text;

            // Find Visual Studio installation
            var visualStudioPath = FindVisualStudioPath();
            if (!string.IsNullOrEmpty(visualStudioPath))
            {
                // Try to find a solution file first
                var solutionFile = FindSolutionFile(path);
                if (!string.IsNullOrEmpty(solutionFile))
                {
                    ProjectRunner.OpenInEditor(visualStudioPath, solutionFile);
                }
                else
                {
                    // Fallback to opening the folder if no solution file found
                    ProjectRunner.OpenInEditor(visualStudioPath, path);
                }
            }
        }

        private void DetailOpenTerminal_Click(object sender, PointerPressedEventArgs e)
        {
            var path = DetailProjectPath.Text;
            try { PlatformHelper.OpenTerminal(path); } catch { }
        }

        /// <summary>
        /// Checks if the project type string represents a C#/.NET project.
        /// This is intentionally broad since the scanner may return simple values
        /// such as ".NET", "C#", "Console" or similar.
        /// </summary>
        private static bool IsDotNetCoreProject(string? projectType)
        {
            if (string.IsNullOrEmpty(projectType))
                return false;

            return projectType.Contains(".NET", StringComparison.OrdinalIgnoreCase) ||
                   projectType.Contains("C#", StringComparison.OrdinalIgnoreCase) ||
                   projectType.Contains("ASP.NET", StringComparison.OrdinalIgnoreCase) ||
                   projectType.Contains("WPF", StringComparison.OrdinalIgnoreCase) ||
                   projectType.Contains("WinForms", StringComparison.OrdinalIgnoreCase) ||
                   projectType.Contains("Blazor", StringComparison.OrdinalIgnoreCase) ||
                   projectType.Contains("MAUI", StringComparison.OrdinalIgnoreCase) ||
                   projectType.Contains("Console", StringComparison.OrdinalIgnoreCase) ||
                   projectType.Contains("Worker", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Finds Visual Studio installation path by checking hardcoded common locations.
        /// Falls back to vswhere.exe if available.
        /// </summary>
        private static string FindVisualStudioPath()
        {
            try
            {
                // Dynamically scan all VS version folders (supports 2019, 2022, 2025/18, and future versions)
                string[] editions = { "Enterprise", "Professional", "Community" };
                string[] programFolders = {
                    Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                    Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86)
                };

                // Collect all candidate paths, preferring newest versions first
                var candidates = new List<(string path, int sortOrder)>();

                foreach (var programFolder in programFolders)
                {
                    var vsRoot = Path.Combine(programFolder, "Microsoft Visual Studio");
                    if (!Directory.Exists(vsRoot)) continue;

                    try
                    {
                        foreach (var versionDir in Directory.GetDirectories(vsRoot))
                        {
                            var versionName = Path.GetFileName(versionDir);
                            // Parse version folder name as number for sorting (e.g. "18" -> 18, "2022" -> 2022)
                            int versionNum = int.TryParse(versionName, out var v) ? v : 0;

                            foreach (var edition in editions)
                            {
                                var devenvPath = Path.Combine(versionDir, edition, "Common7", "IDE", "devenv.exe");
                                if (File.Exists(devenvPath))
                                {
                                    candidates.Add((devenvPath, versionNum));
                                }
                            }
                        }
                    }
                    catch { /* access denied etc. */ }
                }

                // Return the newest version found (highest version number)
                if (candidates.Count > 0)
                {
                    return candidates.OrderByDescending(c => c.sortOrder).First().path;
                }

                // try vswhere executable if it's on PATH
                try
                {
                    var psi = new ProcessStartInfo("vswhere.exe")
                    {
                        Arguments = "-latest -products * -requires Microsoft.Component.MSBuild -find **\\devenv.exe",
                        RedirectStandardOutput = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    };
                    using var proc = Process.Start(psi);
                    string output = proc?.StandardOutput.ReadToEnd() ?? string.Empty;
                    proc?.WaitForExit(2000);
                    if (!string.IsNullOrWhiteSpace(output) && File.Exists(output.Trim()))
                        return output.Trim();
                }
                catch { /* ignore vswhere failure */ }

                return string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        /// <summary>
        /// Determines whether the given folder contains at least one .csproj or .sln
        /// file at the top level. Used as a fallback when project type is unknown.
        /// </summary>
        private static bool ProjectHasDotNetFile(string projectPath)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(projectPath) || !Directory.Exists(projectPath))
                    return false;

                // look for .csproj, .sln, .slnx, .fsproj, .vbproj files without recursing deeply
                string[] dotNetPatterns = { "*.csproj", "*.sln", "*.slnx", "*.fsproj", "*.vbproj" };
                foreach (var pattern in dotNetPatterns)
                {
                    if (Directory.EnumerateFiles(projectPath, pattern, SearchOption.TopDirectoryOnly).Any())
                        return true;
                }
            }
            catch { }

            return false;
        }

        private void DetailRunProject_Click(object sender, PointerPressedEventArgs e)
        {
            if (_currentDetailProject == null)
                return;

            // If already running, stop it
            if (_isProjectRunning)
            {
                _ = StopProjectAsync();
                return;
            }

            // Show the script selection popup
            RunScriptPopupControl.Show(_currentDetailProject.Path, _currentDetailProject.Name);
        }

        private async Task StopProjectAsync()
        {
            if (_runningProcess != null && !_runningProcess.HasExited)
            {
                try
                {
                    await KillProcessTreeAsync(_runningProcess.Id);

                    // Unsubscribe from event to avoid memory leaks
                    if (_processExitedHandler != null)
                    {
                        _runningProcess.Exited -= _processExitedHandler;
                    }

                    _runningProcess.Dispose();
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Failed to stop process: {ex.Message}");

                    try
                    {
                        await KillProcessByNameAsync("npm.exe");
                        await KillProcessByNameAsync("node.exe");
                    }
                    catch { }
                }
                finally
                {
                    _runningProcess = null;
                }
            }

            _isProjectRunning = false;
            UpdateRunButton();
        }

        private async Task KillProcessTreeAsync(int processId)
        {
            await Task.Run(() =>
            {
                try
                {
                    if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                    {
                        var psi = new ProcessStartInfo
                        {
                            FileName = "taskkill",
                            Arguments = $"/T /F /PID {processId}",
                            CreateNoWindow = true,
                            UseShellExecute = false
                        };
                        using var p = Process.Start(psi);
                        p?.WaitForExit(3000);
                    }
                    else
                    {
                        var psi = new ProcessStartInfo
                        {
                            FileName = "kill",
                            Arguments = $"-9 {processId}",
                            CreateNoWindow = true,
                            UseShellExecute = false
                        };
                        using var p = Process.Start(psi);
                        p?.WaitForExit(3000);
                    }
                }
                catch { }
            });
        }

        private async Task KillProcessByNameAsync(string processName)
        {
            await Task.Run(() =>
            {
                try
                {
                    var processes = Process.GetProcessesByName(Path.GetFileNameWithoutExtension(processName));
                    foreach (var process in processes)
                    {
                        try { process.Kill(true); } catch { }
                    }
                }
                catch { }
            });
        }

        private void UpdateRunButton()
        {
            if (_isProjectRunning)
            {
                RunProjectIcon.Text = "\u25A0";
                RunProjectIcon.Foreground = new SolidColorBrush(Color.Parse("#EF4444")); // Red-500
                RunProjectText.Text = LanguageManager.Instance["RunProjectStop"];
                RunProjectSubtext.Text = LanguageManager.Instance["RunProjectServerRunning"];
                RunProjectBorder.BorderBrush = new SolidColorBrush(Color.Parse("#FCA5A5")); // Red-300
                RunProjectBorder.Background = new SolidColorBrush(Color.Parse("#FEF2F2")); // Red-50
            }
            else
            {
                RunProjectIcon.Text = "\u25B6";
                RunProjectIcon.Bind(TextBlock.ForegroundProperty, this.GetResourceObservable("Text.Success"));
                RunProjectText.Text = LanguageManager.Instance["RunProjectStart"];
                RunProjectSubtext.Text = LanguageManager.Instance["RunProjectStartDevServer"];
                RunProjectBorder.Bind(Border.BorderBrushProperty, this.GetResourceObservable("Border.Normal"));
                RunProjectBorder.Bind(Border.BackgroundProperty, this.GetResourceObservable("Bg.Card"));
            }
        }

        private void DetailOpenExplorer_Click(object sender, PointerPressedEventArgs e)
        {
            var path = DetailProjectPath.Text;
            try { Process.Start(new ProcessStartInfo(PlatformHelper.GetFileManagerCommand(), path) { UseShellExecute = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) }); } catch { }
        }

        private string FormatSize(long bytes)
        {
            string[] suf = { "B", "KB", "MB", "GB", "TB", "PB", "EB" };
            if (bytes == 0) return "0" + suf[0];
            long bytesTemp = Math.Abs(bytes);
            int place = Convert.ToInt32(Math.Floor(Math.Log(bytesTemp, 1024)));
            double num = Math.Round(bytesTemp / Math.Pow(1024, place), 1);
            return (Math.Sign(bytes) * num).ToString() + suf[place];
        }

        private string GetPrimaryLanguage(ProjectInfo project)
        {
            if (project.Tags.Contains("JavaScript") || project.Tags.Contains("TypeScript"))
                return project.Tags.FirstOrDefault(t => t == "TypeScript" || t == "JavaScript") ?? "JavaScript";

            if (project.Tags.Contains("C#"))
                return "C#";

            if (project.Tags.Contains("Python"))
                return "Python";

            if (project.ProjectType == "React" || project.ProjectType == "Next.js" || project.ProjectType == "Vue" || project.ProjectType == "Angular")
                return "JavaScript/TypeScript";

            if (project.ProjectType == "WPF" || project.ProjectType == "ASP.NET Core" || project.ProjectType == "WinForms")
                return "C#";

            return project.Tags.FirstOrDefault() ?? "Unknown";
        }

        private string GetRelativePath(string rootPath, string fullPath)
        {
            if (fullPath.StartsWith(rootPath))
            {
                var relPath = fullPath.Substring(rootPath.Length).TrimStart(System.IO.Path.DirectorySeparatorChar);
                if (relPath.Length > 40)
                    return "..." + relPath.Substring(relPath.Length - 37);
                return relPath;
            }
            return fullPath;
        }

        private string GetTimeAgo(DateTime time)
        {
            var span = DateTime.Now - time;

            if (span.TotalMinutes < 1) return "just now";
            if (span.TotalHours < 1) return $"{(int)span.TotalMinutes}m ago";
            if (span.TotalDays < 1) return $"{(int)span.TotalHours}h ago";
            if (span.TotalDays < 30) return $"{(int)span.TotalDays}d ago";

            return time.ToString("MMM dd, yyyy");
        }

        private void ApplyTheme(bool isDark)
        {
            // Swap Resource Dictionaries using Avalonia ResourceInclude
            var themeUri = new Uri(isDark ? "avares://DevAtlas/Themes/Dark.axaml" : "avares://DevAtlas/Themes/Light.axaml");
            var dict = new Avalonia.Markup.Xaml.Styling.ResourceInclude(themeUri) { Source = themeUri };

            Application.Current!.Resources.MergedDictionaries.Clear();
            Application.Current!.Resources.MergedDictionaries.Add(dict);

            // Update accent color resources dynamically
            UpdateAccentColorResources();

            // Update Toggle UI
            if (DarkModeThumb != null && DarkModeBorder != null)
            {
                DarkModeThumb.HorizontalAlignment = isDark ? Avalonia.Layout.HorizontalAlignment.Right : Avalonia.Layout.HorizontalAlignment.Left;
                DarkModeThumb.Margin = isDark ? new Thickness(0, 0, 2, 0) : new Thickness(2, 0, 0, 0);
                DarkModeBorder.Background = isDark
                    ? new SolidColorBrush(LanguageManager.Instance.GetAccentColorValue())
                    : new SolidColorBrush(Color.Parse("#D1D5DB"));
            }

            // Also reload XAML colors that might be manually set if any
            UpdateViewToggleButtons();
            UpdateAllIcons(isDark);
            UpdateTabSelection();
            UpdateSidebarSelection();

            Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
            {
                SettingsViewControl?.RefreshThemeVisuals();
            });
        }

        private void UpdateLanguageResources()
        {
            // Force WPF to re-evaluate all language bindings by raising PropertyChanged
            // This will update all text elements bound to LanguageManager
            LanguageManager.Instance.RefreshBindings();

            TabAIPromptsText.Text = _aiPromptsViewModel.SidebarLabel;
            _aiPromptsViewModel.RefreshLocalization();
            _onboardingViewModel.RefreshLocalization();

            // Also update manually set language strings in code-behind
            UpdateBreadcrumbLanguage();
            ApplyFilter();
            _statsViewModel.RefreshLocalization();
            SettingsViewControl?.RefreshThemeVisuals();
            OnboardingViewControl?.RefreshLocalization();
        }

        private void UpdateBreadcrumbLanguage()
        {
            // Update breadcrumb
            if (BreadcrumbCategoryText != null)
            {
                var categoryText = _currentFilter == "All"
                    ? LanguageManager.Instance["MessageAllLocations"]
                    : _currentFilter;
                BreadcrumbCategoryText.Text = categoryText;
            }
        }

        private void UpdateAccentColorResources()
        {
            var accentColor = LanguageManager.Instance.GetAccentColorValue();
            var accentBrush = new SolidColorBrush(accentColor);

            // Create derived accent variants used by the shared resource keys.
            var accentColorLight = BlendWithWhite(accentColor, 0.9);
            var accentBrushLight = new SolidColorBrush(accentColorLight);

            var accentColorLighter = BlendWithWhite(accentColor, 0.75);
            var accentBrushLighter = new SolidColorBrush(accentColorLighter);

            var accentColorDark = BlendWithBlack(accentColor, 0.7);
            var accentBrushDark = new SolidColorBrush(accentColorDark);

            var accentColorHover = BlendWithBlack(accentColor, 0.12);
            var accentBrushHover = new SolidColorBrush(accentColorHover);

            // Always override app-level resource keys. ContainsKey does not account for merged dictionaries,
            // which caused updates to be skipped and left the UI in the default blue accent.
            if (Application.Current?.Resources is { } resources)
            {
                resources["Text.SidebarActive"] = accentBrush;
                resources["Bg.SidebarActive"] = _isDarkMode ? accentBrushDark : accentBrushLight;
                resources["Accent.Primary"] = accentBrush;
                resources["Accent.Light"] = accentBrushLighter;
                resources["Accent.Lighter"] = accentBrushLight;
                resources["Accent.PrimaryHover"] = accentBrushHover;
                resources["Bg.Badge"] = _isDarkMode ? accentBrushDark : accentBrushLight;
            }

            if (DarkModeBorder != null)
            {
                DarkModeBorder.Background = _isDarkMode
                    ? accentBrush
                    : new SolidColorBrush(Color.Parse("#D1D5DB"));
            }

            UpdateTabSelection();
            UpdateSidebarSelection();
        }

        private static Color BlendWithWhite(Color color, double ratio)
        {
            ratio = Math.Clamp(ratio, 0, 1);
            return Color.FromRgb(
                (byte)Math.Round(color.R + (255 - color.R) * ratio),
                (byte)Math.Round(color.G + (255 - color.G) * ratio),
                (byte)Math.Round(color.B + (255 - color.B) * ratio));
        }

        private static Color BlendWithBlack(Color color, double ratio)
        {
            ratio = Math.Clamp(ratio, 0, 1);
            var factor = 1 - ratio;
            return Color.FromRgb(
                (byte)Math.Round(color.R * factor),
                (byte)Math.Round(color.G * factor),
                (byte)Math.Round(color.B * factor));
        }

        private void ApplyThemeFromMode(AppThemeMode mode)
        {
            bool isDark = mode switch
            {
                AppThemeMode.Dark => true,
                AppThemeMode.Light => false,
                AppThemeMode.System => IsSystemDarkMode(),
                _ => false
            };
            _isDarkMode = isDark;
            ApplyTheme(isDark);

            // Also save via LanguageManager
            LanguageManager.Instance.ThemeMode = mode;
        }

        private static bool IsSystemDarkMode()
        {
            try
            {
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    var psi = new ProcessStartInfo
                    {
                        FileName = "reg",
                        Arguments = "query HKCU\\Software\\Microsoft\\Windows\\CurrentVersion\\Themes\\Personalize /v AppsUseLightTheme",
                        RedirectStandardOutput = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    };
                    using var proc = Process.Start(psi);
                    var output = proc?.StandardOutput.ReadToEnd() ?? "";
                    proc?.WaitForExit(2000);
                    return output.Contains("0x0");
                }
                else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                {
                    var psi = new ProcessStartInfo
                    {
                        FileName = "defaults",
                        Arguments = "read -g AppleInterfaceStyle",
                        RedirectStandardOutput = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    };
                    using var proc = Process.Start(psi);
                    var output = proc?.StandardOutput.ReadToEnd() ?? "";
                    proc?.WaitForExit(2000);
                    return output.Trim().Equals("Dark", StringComparison.OrdinalIgnoreCase);
                }
                else
                {
                    // Linux: check common desktop environment settings
                    var psi = new ProcessStartInfo
                    {
                        FileName = "gsettings",
                        Arguments = "get org.gnome.desktop.interface gtk-theme",
                        RedirectStandardOutput = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    };
                    using var proc = Process.Start(psi);
                    var output = proc?.StandardOutput.ReadToEnd() ?? "";
                    proc?.WaitForExit(2000);
                    return output.Contains("dark", StringComparison.OrdinalIgnoreCase);
                }
            }
            catch { return false; }
        }

        private async void TabNavigation_Click(object sender, PointerPressedEventArgs e)
        {
            if (sender is Border border && border.Tag is string tab)
            {
                // Clear stats data when leaving Stats tab to free memory
                if (_currentTab == "Stats" && tab != "Stats")
                {
                    _statsViewModel.ClearStatsData();
                }

                // Close detail view if open
                await CloseDetailViewAsync();
                AIPromptDetailPopupControl.Hide();

                _currentTab = tab;
                UpdateTabSelection();

                // Show/hide content areas
                var isAtlas = tab == "Atlas";
                var isStats = tab == "Stats";
                var isAIPrompts = tab == "AIPrompts";

                ProjectDetailPanel.IsVisible = false;
                RefreshMainContentVisibility();

                // Show explorer section only for Atlas tab
                ExplorerSection.IsVisible = isAtlas;

                if (isAtlas)
                {
                    ApplyFilter(); // Re-apply to show empty state if needed
                }
                else if (isStats)
                {
                    _ = _statsViewModel.EnsureStatsLoadedAsync(Projects.ToList());
                }
                else if (isAIPrompts)
                {
                    _aiPromptsViewModel.RefreshLocalization();
                    SetStatsViewActive(false);
                }
                else
                {
                    SetStatsViewActive(false);
                }
            }
        }

        private void UpdateTabSelection()
        {
            // Reset all tabs
            TabAtlas.Background = null;
            TabStats.Background = null;
            TabAIPrompts.Background = null;
            TabSettings.Background = null;

            var selected = _currentTab switch
            {
                "Atlas" => TabAtlas,
                "Stats" => TabStats,
                "AIPrompts" => TabAIPrompts,
                "Settings" => TabSettings,
                _ => TabAtlas
            };

            selected.Background = (IBrush)this.FindResource("Bg.SidebarActive")!;

            // Update text foreground
            SetTabTextForeground(TabAtlas, _currentTab == "Atlas");
            SetTabTextForeground(TabStats, _currentTab == "Stats");
            SetTabTextForeground(TabAIPrompts, _currentTab == "AIPrompts");
            SetTabTextForeground(TabSettings, _currentTab == "Settings");

            // Tint the icon background for the selected tab to accent color (small rounded square)
            try
            {
                var accent = (IBrush)this.FindResource("Accent.Primary")!;
                TabAtlasIconBorder.Background = _currentTab == "Atlas" ? accent : Brushes.Transparent;
                TabStatsIconBorder.Background = _currentTab == "Stats" ? accent : Brushes.Transparent;
                TabAIPromptsIconBorder.Background = _currentTab == "AIPrompts" ? accent : Brushes.Transparent;
                TabSettingsIconBorder.Background = _currentTab == "Settings" ? accent : Brushes.Transparent;
            }
            catch { }

            // Swap svg sources to dark variants for active tabs
            try
            {
                SetSvgSelection(TabAtlasSvg, _currentTab == "Atlas");
                SetSvgSelection(TabStatsSvg, _currentTab == "Stats");
                SetSvgSelection(TabAIPromptsSvg, _currentTab == "AIPrompts");
                SetSvgSelection(TabSettingsSvg, _currentTab == "Settings");
            }
            catch { }
        }

        private void SetSvgSelection(Image svgImage, bool isSelected)
        {
            if (svgImage == null) return;
            var basePath = GetBaseSvgPath(svgImage);
            if (string.IsNullOrEmpty(basePath)) return;

            var shouldUseDarkVariant = isSelected || _isDarkMode;
            var target = shouldUseDarkVariant ? ToDarkVariant(basePath) : ToLightVariant(basePath);
            SetSvgImageSource(svgImage, target);
        }

        private static string GetImageSourcePath(Image image)
        {
            if (image.Source is Avalonia.Svg.Skia.SvgImage svgImg && svgImg.Source != null)
                return svgImg.Source.ToString() ?? "";
            return image.Tag?.ToString() ?? "";
        }

        private static string GetBaseSvgPath(Image image)
        {
            var tagPath = image.Tag?.ToString();
            var sourcePath = string.IsNullOrWhiteSpace(tagPath)
                ? GetImageSourcePath(image)
                : tagPath;

            return ToLightVariant(sourcePath);
        }

        private static string ToDarkVariant(string sourcePath)
        {
            if (string.IsNullOrWhiteSpace(sourcePath))
            {
                return sourcePath;
            }

            if (sourcePath.Contains("mobile_dark.svg", StringComparison.OrdinalIgnoreCase) ||
                sourcePath.EndsWith("_dark.svg", StringComparison.OrdinalIgnoreCase))
            {
                return sourcePath;
            }

            if (sourcePath.Contains("phone.svg", StringComparison.OrdinalIgnoreCase))
            {
                return ReplaceFirstIgnoreCase(sourcePath, "phone.svg", "mobile_dark.svg");
            }

            return sourcePath.EndsWith(".svg", StringComparison.OrdinalIgnoreCase)
                ? sourcePath[..^4] + "_dark.svg"
                : sourcePath;
        }

        private static string ToLightVariant(string sourcePath)
        {
            if (string.IsNullOrWhiteSpace(sourcePath))
            {
                return sourcePath;
            }

            if (sourcePath.Contains("mobile_dark.svg", StringComparison.OrdinalIgnoreCase))
            {
                return ReplaceFirstIgnoreCase(sourcePath, "mobile_dark.svg", "phone.svg");
            }

            return sourcePath.EndsWith("_dark.svg", StringComparison.OrdinalIgnoreCase)
                ? sourcePath[..^9] + ".svg"
                : sourcePath;
        }

        private static string ReplaceFirstIgnoreCase(string input, string oldValue, string newValue)
        {
            var index = input.IndexOf(oldValue, StringComparison.OrdinalIgnoreCase);
            if (index < 0)
            {
                return input;
            }

            return input[..index] + newValue + input[(index + oldValue.Length)..];
        }

        private static void SetSvgImageSource(Image image, string targetSource)
        {
            if (image == null || string.IsNullOrWhiteSpace(targetSource))
                return;

            try
            {
                var uriStr = targetSource.StartsWith("avares://") ? targetSource : $"avares://DevAtlas/{targetSource.TrimStart('/')}";
                var source = Avalonia.Svg.Skia.SvgSource.Load(uriStr, null);
                if (source != null)
                    image.Source = new Avalonia.Svg.Skia.SvgImage { Source = source };
            }
            catch { }
        }

        private void SetTabTextForeground(Border tab, bool isActive)
        {
            if (tab.Child is Grid grid)
            {
                foreach (var child in grid.Children)
                {
                    if (child is TextBlock tb)
                    {
                        tb.Foreground = isActive
                            ? (IBrush)this.FindResource("Text.SidebarActive")!
                            : (IBrush)this.FindResource("Text.Secondary")!;
                    }
                }
            }
        }

        private void UpdateAllIcons(bool isDark)
        {
            // Walk entire visual tree to swap ALL SVG icons (including those inside DataTemplates)
            SwapSvgIconsInTree(this, isDark);
            UpdateTabSelection();
            UpdateSidebarSelection();

            // Also schedule a delayed update to catch items rendered after layout
            Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
            {
                SwapSvgIconsInTree(this, isDark);
                UpdateTabSelection();
                UpdateSidebarSelection();
            });
        }

        private void SwapSvgIconsInTree(Avalonia.Visual parent, bool isDark)
        {
            var children = parent.GetVisualChildren();
            foreach (var child in children)
            {
                if (child is Image imageControl)
                {
                    var currentSource = GetImageSourcePath(imageControl);
                    if (string.IsNullOrEmpty(currentSource)) { SwapSvgIconsInTree(child, isDark); continue; }
                    if (currentSource.Contains("/Assets/Icons/Settings/", StringComparison.OrdinalIgnoreCase)) { SwapSvgIconsInTree(child, isDark); continue; }

                    var basePath = GetBaseSvgPath(imageControl);
                    var targetPath = isDark ? ToDarkVariant(basePath) : ToLightVariant(basePath);
                    SetSvgImageSource(imageControl, targetPath);
                }
                SwapSvgIconsInTree(child, isDark);
            }
        }

        private void DarkModeToggle_Click(object sender, PointerPressedEventArgs e)
        {
            _isDarkMode = !_isDarkMode;
            ApplyTheme(_isDarkMode);

            // Save preference via LanguageManager
            LanguageManager.Instance.ThemeMode = _isDarkMode ? AppThemeMode.Dark : AppThemeMode.Light;

            // Also save legacy preference for backwards compatibility
            try
            {
                File.WriteAllText(System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "theme.txt"), _isDarkMode ? "Dark" : "Light");
            }
            catch { }
        }
        private async Task UpdateStatusDisplayAsync(IEnumerable<ProjectInfo>? projects = null)
        {
            try
            {
                // Cancel any previous statistics calculation
                _statusStatisticsCancellationToken?.Cancel();
                _statusStatisticsCancellationToken?.Dispose();
                _statusStatisticsCancellationToken = new CancellationTokenSource();
                var token = _statusStatisticsCancellationToken.Token;

                // choose projects to calculate
                var listToCalculate = (projects == null) ? Projects : projects;

                // Calculate statistics for the requested collection
                var (statistics, isFromCache) = await _statisticsService.CalculateStatisticsAsync(listToCalculate, token);

                if (token.IsCancellationRequested)
                    return;

                // Update UI on main thread
                // Only show loading state if we're calculating fresh data
                Avalonia.Threading.Dispatcher.UIThread.Invoke(() =>
                {
                    if (!isFromCache)
                    {
                        StatusDisplayControl.IsLoading = true;
                        StatusDisplayControl.ProjectCount = 0;
                        StatusDisplayControl.FileCount = 0;
                        StatusDisplayControl.LinesCount = 0;
                    }

                    StatusDisplayControl.IsLoading = false;
                    StatusDisplayControl.ProjectCount = statistics.ProjectCount;
                    StatusDisplayControl.FileCount = statistics.FileCount;
                    StatusDisplayControl.LinesCount = statistics.LinesCount;
                });
            }
            catch (OperationCanceledException)
            {
                // Expected when filter/search changes quickly.
            }
            catch (Exception ex)
            {
                // Hide loading state on error
                Avalonia.Threading.Dispatcher.UIThread.Invoke(() =>
                {
                    StatusDisplayControl.IsLoading = false;
                });
                System.Diagnostics.Debug.WriteLine($"Error updating status display: {ex.Message}");
            }
        }
    }
}




