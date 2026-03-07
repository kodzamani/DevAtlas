using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using DevAtlas.Models;
using DevAtlas.Services;

namespace DevAtlas.Controls
{
    /// <summary>
    /// Popup that shows detailed project code analysis with line counts, language breakdown, and file list
    /// </summary>
    public partial class ProjectAnalyzePopup : UserControl
    {
        private readonly ProjectAnalyzerService _analyzer;
        private CancellationTokenSource? _analysisCts;
        private string? _currentProjectPath;

        /// <summary>
        /// Event fired when the user clicks the "Find Unuseds" button
        /// </summary>
        public event EventHandler<FindUnusedEventArgs>? FindUnusedClicked;

        public ProjectAnalyzePopup()
        {
            InitializeComponent();
            _analyzer = new ProjectAnalyzerService();
            SetHeaderIcon(PopupHeaderIcon);
        }

        private static void SetHeaderIcon(Image image)
        {
            try
            {
                var source = Avalonia.Svg.Skia.SvgSource.Load("avares://DevAtlas/Assets/Icons/calculate_code.svg", null);
                if (source != null)
                    image.Source = new Avalonia.Svg.Skia.SvgImage { Source = source };
            }
            catch
            {
            }
        }

        private void FindUnusedsButton_Click(object sender, PointerPressedEventArgs e)
        {
            if (_currentProjectPath != null)
            {
                FindUnusedClicked?.Invoke(this, new FindUnusedEventArgs(_currentProjectPath));
            }
        }

        /// <summary>
        /// Shows the popup and starts analyzing the given project
        /// </summary>
        public async void Show(string projectName, string projectPath)
        {
            _currentProjectPath = projectPath;
            HeaderProjectName.Text = projectName;
            LoadingPanel.IsVisible = true;
            ResultsPanel.IsVisible = false;
            IsVisible = true;

            // Cancel any previous analysis
            _analysisCts?.Cancel();
            _analysisCts?.Dispose();
            _analysisCts = new CancellationTokenSource();

            try
            {
                var result = await _analyzer.AnalyzeProjectAsync(projectPath, _analysisCts.Token);
                if (_analysisCts.Token.IsCancellationRequested) return;

                PopulateResults(result);
            }
            catch (OperationCanceledException)
            {
                // Analysis was cancelled
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Analysis error: {ex.Message}");
                LoadingPanel.IsVisible = false;
            }
        }

        /// <summary>
        /// Hides the popup
        /// </summary>
        public void Hide()
        {
            _analysisCts?.Cancel();
            IsVisible = false;
        }

        private void PopulateResults(ProjectAnalysisResult result)
        {
            // Stats
            StatTotalFiles.Text = result.TotalFiles.ToString("N0");
            StatTotalLines.Text = result.TotalLines.ToString("N0");
            StatAvgLines.Text = result.AvgLinesPerFile.ToString("N0");
            StatLargestFile.Text = $"{result.LargestFileLines:N0} lines";
            AnalyzedFileCountText.Text = $"{result.TotalFiles:N0} files analyzed";

            // Language bar
            BuildLanguageBar(result);

            // Language legend
            var legendItems = result.Languages.Where(l => l.Percentage >= 0.5).Select(l => new LanguageLegendItem
            {
                Name = l.Name,
                PercentageText = $"{l.Percentage:F1}%",
                Color = ParseColor(l.Color)
            }).ToList();
            LanguageLegend.ItemsSource = legendItems;

            // File list
            int maxLines = result.Files.Count > 0 ? result.Files[0].Lines : 1;
            var fileItems = result.Files.Select((f, i) => new FileListItem
            {
                RowNumber = (i + 1).ToString(),
                RelativePath = f.RelativePath,
                Extension = $".{f.Extension}",
                Lines = f.Lines,
                LinesFormatted = f.Lines.ToString("N0"),
                BarWidth = Math.Max(4, (int)(40.0 * f.Lines / Math.Max(maxLines, 1))),
                BarColor = GetColorForExtension(f.Extension, result.Languages)
            }).ToList();
            FileListControl.ItemsSource = fileItems;

            LoadingPanel.IsVisible = false;
            ResultsPanel.IsVisible = true;
        }

        private void BuildLanguageBar(ProjectAnalysisResult result)
        {
            LanguageBar.ColumnDefinitions.Clear();
            LanguageBar.Children.Clear();

            if (result.Languages.Count == 0) return;

            int col = 0;
            foreach (var lang in result.Languages)
            {
                if (lang.Percentage < 0.3) continue;

                var colDef = new ColumnDefinition
                {
                    Width = new GridLength(lang.Percentage, GridUnitType.Star)
                };
                LanguageBar.ColumnDefinitions.Add(colDef);

                var bar = new Border
                {
                    Background = new Avalonia.Media.SolidColorBrush(ParseColor(lang.Color)),
                    CornerRadius = new CornerRadius(
                        col == 0 ? 4 : 0,
                        col == result.Languages.Count(l => l.Percentage >= 0.3) - 1 ? 4 : 0,
                        col == result.Languages.Count(l => l.Percentage >= 0.3) - 1 ? 4 : 0,
                        col == 0 ? 4 : 0)
                };
                Avalonia.Controls.ToolTip.SetTip(bar, $"{lang.Name}: {lang.Percentage:F1}% ({lang.TotalLines:N0} lines)");
                Grid.SetColumn(bar, col);
                LanguageBar.Children.Add(bar);
                col++;
            }
        }

        private Color ParseColor(string colorHex)
        {
            try
            {
                return Color.Parse(colorHex);
            }
            catch
            {
                return Color.FromRgb(107, 114, 128);
            }
        }

        private Color GetColorForExtension(string extension, List<LanguageBreakdown> languages)
        {
            // Map extension to language name
            var ext = extension.StartsWith('.') ? extension[1..] : extension;
            var langMapping = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "cs", "C#" }, { "js", "JavaScript" }, { "jsx", "JavaScript" },
                { "ts", "TypeScript" }, { "tsx", "TypeScript" },
                { "html", "HTML" }, { "htm", "HTML" }, { "css", "CSS" },
                { "scss", "SCSS" }, { "py", "Python" }, { "java", "Java" },
                { "kt", "Kotlin" }, { "c", "C" }, { "cpp", "C++" }, { "h", "C" },
                { "go", "Go" }, { "rs", "Rust" }, { "rb", "Ruby" },
                { "php", "PHP" }, { "swift", "Swift" }, { "dart", "Dart" },
                { "xml", "XML" }, { "xaml", "XAML" }, { "vue", "Vue" },
                { "svelte", "Svelte" }, { "md", "Markdown" }, { "sql", "SQL" },
                { "yml", "YAML" }, { "yaml", "YAML" }, { "razor", "Razor" },
                { "cshtml", "Razor" }, { "sh", "Shell" }, { "ps1", "PowerShell" },
            };

            ext = extension.TrimStart('.');
            if (langMapping.TryGetValue(ext, out var langName))
            {
                var lang = languages.FirstOrDefault(l => l.Name.Equals(langName, StringComparison.OrdinalIgnoreCase));
                if (lang != null)
                    return ParseColor(lang.Color);
            }

            return Color.FromRgb(107, 114, 128);
        }

        private void CloseButton_Click(object sender, PointerPressedEventArgs e)
        {
            Hide();
        }

        private void Overlay_MouseLeftButtonDown(object sender, PointerPressedEventArgs e)
        {
            // Close when clicking the dark overlay background
            if ((e as PointerPressedEventArgs)?.Source == this)
            {
                Hide();
            }
        }

        private void PopupContent_MouseLeftButtonDown(object sender, PointerPressedEventArgs e)
        {
            // Prevent overlay click-to-close when clicking inside the popup content
            e.Handled = true;
        }
    }
}
