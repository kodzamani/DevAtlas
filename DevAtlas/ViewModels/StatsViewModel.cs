using Avalonia.Media;
using Avalonia.Threading;
using DevAtlas.Models;
using DevAtlas.Services;
using LiveChartsCore;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using SkiaSharp;
using System.Collections.ObjectModel;
using System.Globalization;

namespace DevAtlas.ViewModels;

/// <summary>
/// ViewModel for the Statistics dashboard view.
/// Provides Git activity data, project metrics, and chart series for LiveCharts2.
/// </summary>
public class StatsViewModel : ViewModelBase, IDisposable
{
    private readonly ProjectAnalyzerService _analyzer;
    private DateRangeFilter _dateRange = DateRangeFilter.Month;
    private bool _isCalculating = true;
    private List<ProjectInfo> _currentProjects = [];
    private List<GitDailyStat> _allGitStats = [];
    private CancellationTokenSource? _refreshCts;
    private CancellationTokenSource? _gitStatsCts;
    private GitProjectFilterOption? _selectedGitProjectFilter;
    private bool _isGitProjectFilterOpen;
    private readonly EventHandler _themeModeChangedHandler;
    private readonly EventHandler _languageChangedHandler;
    private readonly EventHandler _accentColorChangedHandler;
    private int _refreshGeneration;
    private bool _disposed = false;

    public StatsViewModel()
    {
        _analyzer = new ProjectAnalyzerService();
        _themeModeChangedHandler = (_, _) =>
        {
            UpdateHeatmapLegendBrushes();
            RefreshHeatmapCellBrushes();
        };
        _languageChangedHandler = (_, _) => HandleLanguageChanged();
        _accentColorChangedHandler = (_, _) => HandleAccentColorChanged();
        LanguageManager.Instance.ThemeModeChanged += _themeModeChangedHandler;
        LanguageManager.Instance.LanguageChanged += _languageChangedHandler;
        LanguageManager.Instance.AccentColorChanged += _accentColorChangedHandler;
        UpdateHeatmapLegendBrushes();
    }

    // Summary stats
    private int _projectsAnalyzed;
    private long _trackedCodeLines;
    private int _gitAdditions;
    private int _gitDeletions;

    public DateRangeFilter DateRange
    {
        get => _dateRange;
        set
        {
            if (SetProperty(ref _dateRange, value))
            {
                OnPropertyChanged(nameof(DateRangeDisplay));
                _ = LoadGitStatsAsync();
            }
        }
    }

    public string DateRangeDisplay => _dateRange.DisplayName();

    public bool IsCalculating
    {
        get => _isCalculating;
        set => SetProperty(ref _isCalculating, value);
    }

    public int ProjectsAnalyzed
    {
        get => _projectsAnalyzed;
        set => SetProperty(ref _projectsAnalyzed, value);
    }

    public long TrackedCodeLines
    {
        get => _trackedCodeLines;
        set => SetProperty(ref _trackedCodeLines, value);
    }

    public int GitAdditions
    {
        get => _gitAdditions;
        set => SetProperty(ref _gitAdditions, value);
    }

    public int GitDeletions
    {
        get => _gitDeletions;
        set => SetProperty(ref _gitDeletions, value);
    }

    public GitProjectFilterOption? SelectedGitProjectFilter
    {
        get => _selectedGitProjectFilter;
        set
        {
            if (SetProperty(ref _selectedGitProjectFilter, value))
            {
                IsGitProjectFilterOpen = false;
                RebuildGitVisuals();
                OnPropertyChanged(nameof(SelectedGitProjectFilterDisplayName));
            }
        }
    }

    public bool IsGitProjectFilterOpen
    {
        get => _isGitProjectFilterOpen;
        set => SetProperty(ref _isGitProjectFilterOpen, value);
    }

    // Chart data
    public ObservableCollection<ISeries> GitActivitySeries { get; } = [];
    public ObservableCollection<ISeries> CodeLinesSeries { get; } = [];
    public ObservableCollection<ISeries> ProjectTypesSeries { get; } = [];
    public ObservableCollection<ISeries> FileCountSeries { get; } = [];
    public ObservableCollection<GitProjectFilterOption> GitProjectFilters { get; } = [];
    public ObservableCollection<GitContributionHeatmapWeek> GitHeatmapWeeks { get; } = [];

    /// <summary>
    /// Fires after project file statistics (TotalLines/TotalFiles) have been populated.
    /// Subscribe in MainWindow to persist results to the index for faster subsequent loads.
    /// </summary>
    public event EventHandler<IReadOnlyList<ProjectInfo>>? ProjectStatisticsPopulated;

    public List<Axis> GitActivityXAxes { get; private set; } = [new Axis { Labels = [] }];
    public List<Axis> GitActivityYAxes { get; private set; } = [new Axis { MinLimit = 0 }];
    public List<Axis> CodeLinesXAxes { get; private set; } = [new Axis { Labels = [] }];
    public List<Axis> CodeLinesYAxes { get; private set; } = [new Axis { MinLimit = 0 }];
    public List<Axis> FileCountXAxes { get; private set; } = [new Axis { MinLimit = 0 }];
    public List<Axis> FileCountYAxes { get; private set; } = [new Axis { Labels = [] }];

    // Raw data
    public ObservableCollection<GitDailyStat> GitDailyStats { get; } = [];
    public ObservableCollection<ProjectMetric> ProjectMetrics { get; } = [];
    public ObservableCollection<ProjectMetric> ProjectFileMetrics { get; } = [];
    public ObservableCollection<ProjectMetric> ProjectTypeMetrics { get; } = [];

    private static readonly SKColor AdditionsColor = new(34, 197, 94);   // Green
    private static readonly SKColor DeletionsColor = new(239, 68, 68);   // Red
    public ObservableCollection<IBrush> HeatmapLegendBrushes { get; } = [];

    public string SelectedGitProjectFilterDisplayName =>
        !string.IsNullOrWhiteSpace(SelectedGitProjectFilter?.DisplayName)
            ? SelectedGitProjectFilter.DisplayName
            : LocalizedAllProjectsLabel();
    public bool HasFilteredGitActivity => SelectedGitProjectFilter?.ProjectName is not string selectedProjectName ||
                                          string.IsNullOrWhiteSpace(selectedProjectName)
        ? _allGitStats.Count != 0
        : _allGitStats.Any(stat => string.Equals(stat.ProjectName, selectedProjectName, StringComparison.OrdinalIgnoreCase));
    public bool HasGitHeatmapData => GitHeatmapWeeks.SelectMany(week => week.Days).Any(day => !day.IsPlaceholder && day.Contributions > 0);

    /// <summary>
    /// Clears all stats data to free memory when leaving the Stats tab.
    /// </summary>
    public void ClearStatsData()
    {
        CancelAndDispose(ref _refreshCts);
        CancelAndDispose(ref _gitStatsCts);
        Interlocked.Increment(ref _refreshGeneration);

        _currentProjects = [];
        _allGitStats = [];
        _selectedGitProjectFilter = null;
        _isGitProjectFilterOpen = false;

        GitActivitySeries.Clear();
        CodeLinesSeries.Clear();
        ProjectTypesSeries.Clear();
        FileCountSeries.Clear();
        GitProjectFilters.Clear();
        GitHeatmapWeeks.Clear();
        GitDailyStats.Clear();
        ProjectMetrics.Clear();
        ProjectFileMetrics.Clear();
        ProjectTypeMetrics.Clear();

        ProjectsAnalyzed = 0;
        TrackedCodeLines = 0;
        GitAdditions = 0;
        GitDeletions = 0;
        IsCalculating = false;

        GitActivityXAxes = [new Axis { Labels = [] }];
        GitActivityYAxes = [new Axis { MinLimit = 0 }];
        CodeLinesXAxes = [new Axis { Labels = [] }];
        CodeLinesYAxes = [new Axis { MinLimit = 0 }];
        FileCountXAxes = [new Axis { MinLimit = 0 }];
        FileCountYAxes = [new Axis { Labels = [] }];

        OnPropertyChanged(nameof(SelectedGitProjectFilter));
        OnPropertyChanged(nameof(SelectedGitProjectFilterDisplayName));
        OnPropertyChanged(nameof(GitActivityXAxes));
        OnPropertyChanged(nameof(GitActivityYAxes));
        OnPropertyChanged(nameof(CodeLinesXAxes));
        OnPropertyChanged(nameof(CodeLinesYAxes));
        OnPropertyChanged(nameof(FileCountXAxes));
        OnPropertyChanged(nameof(FileCountYAxes));
        OnPropertyChanged(nameof(HasFilteredGitActivity));
        OnPropertyChanged(nameof(HasGitHeatmapData));
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
                LanguageManager.Instance.ThemeModeChanged -= _themeModeChangedHandler;
                LanguageManager.Instance.LanguageChanged -= _languageChangedHandler;
                LanguageManager.Instance.AccentColorChanged -= _accentColorChangedHandler;

                CancelAndDispose(ref _refreshCts);
                CancelAndDispose(ref _gitStatsCts);

                if (_analyzer is IDisposable disposableAnalyzer)
                {
                    disposableAnalyzer.Dispose();
                }
            }
            _disposed = true;
        }
    }

    public async Task RefreshStatsAsync(List<ProjectInfo> projects)
    {
        var (generation, cancellationToken) = BeginRefreshOperation();

        try
        {
            IsCalculating = true;
            _currentProjects = projects;
            BuildGitProjectFilters();

            await PopulateProjectStatisticsAsync(cancellationToken);
            if (!IsRefreshCurrent(generation, cancellationToken))
            {
                return;
            }

            // Notify listeners so persisted TotalLines/TotalFiles can be saved to disk
            ProjectStatisticsPopulated?.Invoke(this, _currentProjects);

            await CalculateProjectMetricsAsync(cancellationToken);
            if (!IsRefreshCurrent(generation, cancellationToken))
            {
                return;
            }

            // Show populated charts/cards as soon as core metrics are ready.
            IsCalculating = false;
            await LoadGitStatsAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            if (generation == _refreshGeneration)
            {
                IsCalculating = false;
            }
        }
    }

    public async Task EnsureStatsLoadedAsync(List<ProjectInfo> projects)
    {
        if (_currentProjects.Count != 0 &&
            AreSameProjectSet(_currentProjects, projects) &&
            (ProjectMetrics.Count > 0 || GitDailyStats.Count > 0 || GitProjectFilters.Count > 0))
        {
            return;
        }

        await RefreshStatsAsync(projects);
    }

    public void RefreshLocalization()
    {
        HandleLanguageChanged();
    }

    /// <summary>
    /// Analyzes each project to populate TotalLines and TotalFiles properties
    /// </summary>
    private async Task PopulateProjectStatisticsAsync(CancellationToken cancellationToken)
    {
        var projectsToAnalyze = _currentProjects
            .Where(project => !project.TotalLines.HasValue || !project.TotalFiles.HasValue)
            .ToList();

        if (projectsToAnalyze.Count == 0)
        {
            return;
        }

        var maxConcurrency = Math.Clamp(Environment.ProcessorCount, 2, 8);
        using var throttler = new SemaphoreSlim(maxConcurrency);

        var tasks = projectsToAnalyze.Select(async project =>
        {
            await throttler.WaitAsync(cancellationToken);
            try
            {
                var (totalFiles, totalLines) = await _analyzer.AnalyzeProjectSummaryAsync(project.Path, cancellationToken);
                project.TotalLines = totalLines;
                project.TotalFiles = totalFiles;
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error analyzing project {project.Name}: {ex.Message}");
                project.TotalLines = 0;
                project.TotalFiles = 0;
            }
            finally
            {
                throttler.Release();
            }
        });

        await Task.WhenAll(tasks);
    }

    private async Task LoadGitStatsAsync(CancellationToken cancellationToken = default)
    {
        if (_currentProjects.Count == 0)
        {
            GitActivitySeries.Clear();
            GitHeatmapWeeks.Clear();
            GitDailyStats.Clear();
            GitProjectFilters.Clear();
            GitAdditions = 0;
            GitDeletions = 0;
            OnPropertyChanged(nameof(HasFilteredGitActivity));
            OnPropertyChanged(nameof(HasGitHeatmapData));
            return;
        }

        using var gitCts = BeginGitStatsOperation(cancellationToken);
        var ct = gitCts.Token;
        var ownsLoadingState = !cancellationToken.CanBeCanceled;

        if (ownsLoadingState)
        {
            IsCalculating = true;
        }

        try
        {
            var stats = await GitStatsService.Shared.FetchGitStatsAsync(_currentProjects, _dateRange, ct);

            if (ct.IsCancellationRequested) return;

            _allGitStats = stats;
            GitDailyStats.Clear();
            foreach (var s in stats) GitDailyStats.Add(s);

            GitAdditions = stats.Sum(s => s.Additions);
            GitDeletions = stats.Sum(s => s.Deletions);

            RebuildGitVisuals();
        }
        catch (OperationCanceledException) { }
        finally
        {
            if (ownsLoadingState)
            {
                IsCalculating = false;
            }
        }
    }

    private void RebuildGitVisuals()
    {
        var filteredStats = GetFilteredGitStats();
        BuildGitActivityChart(filteredStats);
        BuildGitHeatmap(filteredStats);
        OnPropertyChanged(nameof(HasFilteredGitActivity));
        OnPropertyChanged(nameof(HasGitHeatmapData));
    }

    private IReadOnlyList<GitDailyStat> GetFilteredGitStats()
    {
        var selectedProjectName = SelectedGitProjectFilter?.ProjectName;
        return string.IsNullOrWhiteSpace(selectedProjectName)
            ? _allGitStats
            : _allGitStats
                .Where(stat => string.Equals(stat.ProjectName, selectedProjectName, StringComparison.OrdinalIgnoreCase))
                .ToList();
    }

    private void BuildGitProjectFilters()
    {
        var currentProjectName = SelectedGitProjectFilter?.ProjectName;
        var options = new List<GitProjectFilterOption>
        {
            new()
            {
                DisplayName = LocalizedAllProjectsLabel(),
                ProjectName = null
            }
        };

        options.AddRange(_currentProjects
            .Select(project =>
            {
                if (!string.IsNullOrWhiteSpace(project.Name))
                {
                    return project.Name.Trim();
                }

                if (!string.IsNullOrWhiteSpace(project.Path))
                {
                    var folderName = Path.GetFileName(project.Path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
                    if (!string.IsNullOrWhiteSpace(folderName))
                    {
                        return folderName;
                    }
                }

                return "Unnamed Project";
            })
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(name => name, StringComparer.CurrentCultureIgnoreCase)
            .Select(name => new GitProjectFilterOption
            {
                DisplayName = name,
                ProjectName = name
            }));

        GitProjectFilters.Clear();
        foreach (var option in options)
        {
            GitProjectFilters.Add(option);
        }

        SelectedGitProjectFilter = GitProjectFilters.FirstOrDefault(option =>
            string.Equals(option.ProjectName, currentProjectName, StringComparison.OrdinalIgnoreCase))
            ?? GitProjectFilters.FirstOrDefault();
    }

    private void BuildGitActivityChart(IReadOnlyList<GitDailyStat> stats)
    {
        var byDate = stats
            .GroupBy(s => s.Date.Date)
            .OrderBy(g => g.Key)
            .ToList();

        var labels = byDate.Select(g => g.Key.ToString("MM/dd")).ToArray();
        var additions = byDate.Select(g => (double)g.Sum(s => s.Additions)).ToArray();
        var deletions = byDate.Select(g => (double)g.Sum(s => s.Deletions)).ToArray();

        GitActivitySeries.Clear();
        GitActivitySeries.Add(new ColumnSeries<double>
        {
            Values = additions,
            Name = LanguageManager.Instance["StatsAdditions"],
            Fill = new SolidColorPaint(AdditionsColor),
            MaxBarWidth = 12,
            Padding = 2
        });
        GitActivitySeries.Add(new ColumnSeries<double>
        {
            Values = deletions,
            Name = LanguageManager.Instance["StatsDeletions"],
            Fill = new SolidColorPaint(DeletionsColor),
            MaxBarWidth = 12,
            Padding = 2
        });

        GitActivityXAxes = [new Axis
        {
            Labels = labels,
            LabelsRotation = 45,
            TextSize = 10,
            LabelsPaint = new SolidColorPaint(new SKColor(107, 114, 128))
        }];
        GitActivityYAxes = [new Axis
        {
            MinLimit = 0,
            TextSize = 10,
            LabelsPaint = new SolidColorPaint(new SKColor(107, 114, 128))
        }];

        OnPropertyChanged(nameof(GitActivityXAxes));
        OnPropertyChanged(nameof(GitActivityYAxes));
    }

    private void BuildGitHeatmap(IReadOnlyList<GitDailyStat> stats)
    {
        GitHeatmapWeeks.Clear();

        var today = DateTime.Today;
        var weeksToDisplay = _dateRange is DateRangeFilter.Year or DateRangeFilter.AllTime ? 53
            : _dateRange == DateRangeFilter.Month ? 6
            : 2;
        var heatmapStart = StartOfWeek(today.AddDays(-(weeksToDisplay * 7) + 1), DayOfWeek.Monday);
        var heatmapEnd = heatmapStart.AddDays((weeksToDisplay * 7) - 1);

        var contributionsByDate = stats
            .GroupBy(s => s.Date.Date)
            .ToDictionary(group => group.Key, group => group.Sum(entry => entry.Commits > 0 ? entry.Commits : 1));
        var maxContributions = contributionsByDate.Values.DefaultIfEmpty(0).Max();

        for (var weekIndex = 0; weekIndex < weeksToDisplay; weekIndex++)
        {
            var week = new GitContributionHeatmapWeek();
            var weekStart = heatmapStart.AddDays(weekIndex * 7);

            for (var dayIndex = 0; dayIndex < 7; dayIndex++)
            {
                var date = weekStart.AddDays(dayIndex);
                var isPlaceholder = date > today || date > heatmapEnd;
                var contributions = !isPlaceholder && contributionsByDate.TryGetValue(date, out var count) ? count : 0;

                week.Days.Add(new GitContributionHeatmapCell
                {
                    Date = date,
                    Contributions = contributions,
                    IsPlaceholder = isPlaceholder,
                    Fill = GetHeatmapBrush(contributions, maxContributions, isPlaceholder),
                    DayLabel = GetWeekdayLabel(date.DayOfWeek),
                    ContributionLabel = BuildContributionLabel(contributions),
                    DateLabel = date.ToString("MMM d, yyyy", CultureInfo.CurrentCulture)
                });
            }

            GitHeatmapWeeks.Add(week);
        }
    }

    private void RefreshHeatmapCellBrushes()
    {
        var maxContributions = GitHeatmapWeeks
            .SelectMany(week => week.Days)
            .Where(day => !day.IsPlaceholder)
            .Select(day => day.Contributions)
            .DefaultIfEmpty(0)
            .Max();

        foreach (var day in GitHeatmapWeeks.SelectMany(week => week.Days))
        {
            day.Fill = GetHeatmapBrush(day.Contributions, maxContributions, day.IsPlaceholder);
        }

        OnPropertyChanged(nameof(GitHeatmapWeeks));
    }

    private async Task CalculateProjectMetricsAsync(CancellationToken cancellationToken)
    {
        var projects = _currentProjects.ToList();

        var metrics = await Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();

            var codeMetrics = projects
                .Where(p => (p.TotalLines ?? 0) > 0)
                .Select(p => new ProjectMetric
                {
                    ProjectName = p.Name,
                    ProjectType = p.ProjectType,
                    Value = p.TotalLines ?? 0
                })
                .OrderByDescending(m => m.Value)
                .ToList();

            var fileMetrics = projects
                .Where(p => (p.TotalFiles ?? 0) > 0)
                .Select(p => new ProjectMetric
                {
                    ProjectName = p.Name,
                    ProjectType = p.ProjectType,
                    Value = p.TotalFiles ?? 0
                })
                .OrderByDescending(m => m.Value)
                .ToList();

            var typeMetrics = projects
                .GroupBy(p => p.ProjectType)
                .Select(g => new ProjectMetric
                {
                    ProjectName = g.Key,
                    ProjectType = g.Key,
                    Value = g.Count()
                })
                .OrderByDescending(m => m.Value)
                .ToList();

            var trackedCodeLines = codeMetrics.Sum(m => (long)m.Value);
            return (codeMetrics, fileMetrics, typeMetrics, trackedCodeLines, ProjectCount: projects.Count);
        }, cancellationToken);

        cancellationToken.ThrowIfCancellationRequested();

        await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return;
            }

            ProjectsAnalyzed = metrics.ProjectCount;
            TrackedCodeLines = metrics.trackedCodeLines;

            ProjectMetrics.Clear();
            foreach (var metric in metrics.codeMetrics) ProjectMetrics.Add(metric);

            ProjectFileMetrics.Clear();
            foreach (var metric in metrics.fileMetrics) ProjectFileMetrics.Add(metric);

            ProjectTypeMetrics.Clear();
            foreach (var metric in metrics.typeMetrics) ProjectTypeMetrics.Add(metric);

            BuildCodeLinesChart(metrics.codeMetrics);
            BuildProjectTypesChart(metrics.typeMetrics);
            BuildFileCountChart(metrics.fileMetrics);
        });
    }

    private void BuildCodeLinesChart(List<ProjectMetric> metrics)
    {
        var top10 = metrics.Take(10).ToList();
        var labels = top10.Select(m => m.ProjectName).ToArray();
        var values = top10.Select(m => (double)m.Value).ToArray();

        CodeLinesSeries.Clear();
        CodeLinesSeries.Add(new RowSeries<double>
        {
            Values = values,
            Name = LanguageManager.Instance["StatsLinesOfCode"],
            Fill = new SolidColorPaint(GetAccentSkColor()),
            MaxBarWidth = 20,
            Padding = 4
        });

        CodeLinesXAxes = [new Axis
        {
            MinLimit = 0,
            TextSize = 10,
            LabelsPaint = new SolidColorPaint(new SKColor(107, 114, 128))
        }];
        CodeLinesYAxes = [new Axis
        {
            Labels = labels,
            TextSize = 11,
            LabelsPaint = new SolidColorPaint(new SKColor(107, 114, 128))
        }];

        OnPropertyChanged(nameof(CodeLinesXAxes));
        OnPropertyChanged(nameof(CodeLinesYAxes));
    }

    private void BuildProjectTypesChart(List<ProjectMetric> metrics)
    {
        var colors = new SKColor[]
        {
            GetAccentSkColor(), new(139, 92, 246), new(236, 72, 153),
            new(249, 115, 22), new(34, 197, 94), new(6, 182, 212),
            new(234, 179, 8), new(99, 102, 241)
        };

        ProjectTypesSeries.Clear();
        for (int i = 0; i < metrics.Count && i < colors.Length; i++)
        {
            ProjectTypesSeries.Add(new PieSeries<int>
            {
                Values = [metrics[i].Value],
                Name = metrics[i].ProjectType,
                Fill = new SolidColorPaint(colors[i % colors.Length]),
                MaxRadialColumnWidth = 40
            });
        }
    }

    private void BuildFileCountChart(List<ProjectMetric> metrics)
    {
        var top10 = metrics.Take(10).ToList();

        FileCountSeries.Clear();
        FileCountSeries.Add(new RowSeries<double>
        {
            Values = top10.Select(m => (double)m.Value).ToArray(),
            Name = LanguageManager.Instance["StatsFileCount"],
            Fill = new SolidColorPaint(GetSecondarySkColor()),
            MaxBarWidth = 20,
            Padding = 4
        });

        FileCountXAxes = [new Axis
        {
            MinLimit = 0,
            TextSize = 10,
            LabelsPaint = new SolidColorPaint(new SKColor(107, 114, 128))
        }];
        FileCountYAxes = [new Axis
        {
            Labels = top10.Select(m => m.ProjectName).ToArray(),
            TextSize = 11,
            LabelsPaint = new SolidColorPaint(new SKColor(107, 114, 128))
        }];

        OnPropertyChanged(nameof(FileCountXAxes));
        OnPropertyChanged(nameof(FileCountYAxes));
    }

    public void SetDateRange(DateRangeFilter range)
    {
        DateRange = range;
    }

    private static IBrush CreateBrush(byte red, byte green, byte blue)
    {
        var brush = new SolidColorBrush(Color.FromRgb(red, green, blue));
        return brush;
    }

    private static DateTime StartOfWeek(DateTime date, DayOfWeek startOfWeek)
    {
        var diff = (7 + (date.DayOfWeek - startOfWeek)) % 7;
        return date.AddDays(-diff).Date;
    }

    private static string GetWeekdayLabel(DayOfWeek dayOfWeek)
    {
        var dayName = CultureInfo.CurrentCulture.DateTimeFormat.GetAbbreviatedDayName(dayOfWeek);
        return dayName.Length <= 3 ? dayName : dayName[..3];
    }

    private static IBrush GetHeatmapBrush(int contributions, int maxContributions, bool isPlaceholder)
    {
        if (isPlaceholder)
        {
            return Brushes.Transparent;
        }

        var palette = GetHeatmapPalette();

        if (contributions <= 0 || maxContributions <= 0)
        {
            return palette[0];
        }

        var ratio = (double)contributions / maxContributions;
        if (ratio >= 0.75) return palette[4];
        if (ratio >= 0.5) return palette[3];
        if (ratio >= 0.25) return palette[2];
        return palette[1];
    }

    private static string BuildContributionLabel(int contributions)
    {
        var languageManager = LanguageManager.Instance;
        var key = contributions == 1 ? "StatsContribution" : "StatsContributions";
        return $"{contributions:N0} {languageManager[key]}";
    }

    private void UpdateHeatmapLegendBrushes()
    {
        var palette = GetHeatmapPalette();
        HeatmapLegendBrushes.Clear();

        foreach (var brush in palette)
        {
            HeatmapLegendBrushes.Add(brush);
        }
    }

    private static IReadOnlyList<IBrush> GetHeatmapPalette()
    {
        return IsDarkThemeActive()
            ?
            [
                CreateBrush(31, 41, 55),
                CreateBrush(187, 247, 208),
                CreateBrush(134, 239, 172),
                CreateBrush(34, 197, 94),
                CreateBrush(21, 128, 61)
            ]
            :
            [
                CreateBrush(229, 231, 235),
                CreateBrush(187, 247, 208),
                CreateBrush(134, 239, 172),
                CreateBrush(34, 197, 94),
                CreateBrush(21, 128, 61)
            ];
    }

    private static bool IsDarkThemeActive()
    {
        if (Avalonia.Application.Current?.Resources.TryGetResource("Bg.Window", null, out var bgRes) != true || bgRes is not SolidColorBrush backgroundBrush)
        {
            return false;
        }

        var color = backgroundBrush.Color;
        var luminance = (0.2126 * color.R) + (0.7152 * color.G) + (0.0722 * color.B);
        return luminance < 128;
    }

    private void HandleLanguageChanged()
    {
        var selectedProjectName = SelectedGitProjectFilter?.ProjectName;

        OnPropertyChanged(nameof(DateRangeDisplay));
        BuildGitProjectFilters();
        SelectedGitProjectFilter = GitProjectFilters.FirstOrDefault(option =>
            string.Equals(option.ProjectName, selectedProjectName, StringComparison.OrdinalIgnoreCase))
            ?? GitProjectFilters.FirstOrDefault();

        if (ProjectMetrics.Count > 0)
        {
            BuildCodeLinesChart(ProjectMetrics.ToList());
        }

        if (ProjectFileMetrics.Count > 0)
        {
            BuildFileCountChart(ProjectFileMetrics.ToList());
        }

        if (ProjectTypeMetrics.Count > 0)
        {
            BuildProjectTypesChart(ProjectTypeMetrics.ToList());
        }

        RebuildGitVisuals();
    }

    private void HandleAccentColorChanged()
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (ProjectMetrics.Count > 0)
            {
                BuildCodeLinesChart(ProjectMetrics.ToList());
            }

            if (ProjectFileMetrics.Count > 0)
            {
                BuildFileCountChart(ProjectFileMetrics.ToList());
            }

            if (ProjectTypeMetrics.Count > 0)
            {
                BuildProjectTypesChart(ProjectTypeMetrics.ToList());
            }
        });
    }

    private static SKColor GetAccentSkColor()
    {
        var accent = LanguageManager.Instance.GetAccentColorValue();
        return new SKColor(accent.R, accent.G, accent.B);
    }

    private static SKColor GetSecondarySkColor()
    {
        var accent = LanguageManager.Instance.GetAccentColorValue();
        var light = Color.FromRgb(
            (byte)Math.Min(255, accent.R + (255 - accent.R) * 0.25),
            (byte)Math.Min(255, accent.G + (255 - accent.G) * 0.25),
            (byte)Math.Min(255, accent.B + (255 - accent.B) * 0.25));

        return new SKColor(light.R, light.G, light.B);
    }

    private static bool AreSameProjectSet(IReadOnlyList<ProjectInfo> first, IReadOnlyList<ProjectInfo> second)
    {
        if (first.Count != second.Count)
        {
            return false;
        }

        var firstPaths = first
            .Select(project => NormalizePath(project.Path))
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();

        var secondPaths = second
            .Select(project => NormalizePath(project.Path))
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();

        return firstPaths.SequenceEqual(secondPaths, StringComparer.Ordinal);
    }

    private static string NormalizePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return string.Empty;
        }

        return path.Trim().Replace('\\', '/').ToUpperInvariant();
    }

    private static string LocalizedAllProjectsLabel()
    {
        var label = LanguageManager.Instance["StatsAllProjects"];
        return string.IsNullOrWhiteSpace(label) ? "All Projects" : label;
    }

    private (int Generation, CancellationToken Token) BeginRefreshOperation()
    {
        CancelAndDispose(ref _refreshCts);
        _refreshCts = new CancellationTokenSource();
        var generation = Interlocked.Increment(ref _refreshGeneration);
        return (generation, _refreshCts.Token);
    }

    private CancellationTokenSource BeginGitStatsOperation(CancellationToken cancellationToken)
    {
        CancelAndDispose(ref _gitStatsCts);
        _gitStatsCts = cancellationToken.CanBeCanceled
            ? CancellationTokenSource.CreateLinkedTokenSource(cancellationToken)
            : new CancellationTokenSource();
        return _gitStatsCts;
    }

    private bool IsRefreshCurrent(int generation, CancellationToken cancellationToken)
    {
        return generation == _refreshGeneration && !cancellationToken.IsCancellationRequested;
    }

    private static void CancelAndDispose(ref CancellationTokenSource? cancellationTokenSource)
    {
        if (cancellationTokenSource is null)
        {
            return;
        }

        try
        {
            cancellationTokenSource.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }

        cancellationTokenSource.Dispose();
        cancellationTokenSource = null;
    }
}
