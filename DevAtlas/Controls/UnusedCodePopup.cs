using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using DevAtlas.Models;
using DevAtlas.Services.UnusedCodeAnalyzer;

namespace DevAtlas.Controls
{
    /// <summary>
    /// Popup that shows unused code analysis results with type/name/location/hints table
    /// </summary>
    public partial class UnusedCodePopup : UserControl
    {
        private readonly UnusedCodeAnalyzerService _analyzer;
        private CancellationTokenSource? _analysisCts;
        private string _currentProjectPath = string.Empty;
        private List<UnusedCodeResult> _currentResults = new();

        public UnusedCodePopup()
        {
            InitializeComponent();
            _analyzer = new UnusedCodeAnalyzerService();
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

        /// <summary>
        /// Shows the popup and starts analyzing the given project for unused code
        /// </summary>
        public async void Show(string projectName, string projectPath)
        {
            HeaderProjectName.Text = projectName;
            _currentProjectPath = projectPath;
            _currentResults = new List<UnusedCodeResult>();
            LoadingPanel.IsVisible = true;
            ResultsPanel.IsVisible = false;
            IsVisible = true;
            SetGeneratePromptButtonEnabled(false);

            // Cancel any previous analysis
            _analysisCts?.Cancel();
            _analysisCts?.Dispose();
            _analysisCts = new CancellationTokenSource();

            try
            {
                var results = await _analyzer.AnalyzeAsync(projectPath, _analysisCts.Token);
                if (_analysisCts.Token.IsCancellationRequested) return;

                PopulateResults(results);
            }
            catch (OperationCanceledException)
            {
                // Analysis was cancelled
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Unused code analysis error: {ex.Message}");
                LoadingPanel.IsVisible = false;
            }
        }

        /// <summary>
        /// Hides the popup
        /// </summary>
        public void Hide()
        {
            _analysisCts?.Cancel();
            _currentProjectPath = string.Empty;
            _currentResults = new List<UnusedCodeResult>();
            SetGeneratePromptButtonEnabled(false);
            IsVisible = false;
        }

        private void PopulateResults(List<UnusedCodeResult> results)
        {
            _currentResults = results.ToList();

            // Calculate stats
            int totalIssues = results.Count;
            int variableCount = results.Count(r =>
                r.Kind == "variable" || r.Kind == "field" || r.Kind == "property" || r.Kind == "var");
            int methodCount = results.Count(r =>
                r.Kind == "method" || r.Kind == "function" || r.Kind == "arrow function" || r.Kind == "static method");
            int typeCount = results.Count(r =>
                r.Kind == "class" || r.Kind == "struct" || r.Kind == "enum" || r.Kind == "interface" ||
                r.Kind == "protocol" || r.Kind == "extension" || r.Kind == "mixin" || r.Kind == "typedef" ||
                r.Kind == "StatelessWidget" || r.Kind == "StatefulWidget" || r.Kind == "ChangeNotifier" ||
                r.Kind == "InheritedWidget" || r.Kind == "package" || r.Kind == "asset");

            StatTotalIssues.Text = totalIssues.ToString("N0");
            StatVariables.Text = variableCount.ToString("N0");
            StatMethods.Text = methodCount.ToString("N0");
            StatTypes.Text = typeCount.ToString("N0");

            // Summary text
            SummaryText.Text = $"Automatically generated analysis of unused declarations in the project. Found {totalIssues} potential unused items.";

            if (totalIssues == 0)
            {
                NoIssuesPanel.IsVisible = true;
                TableHeader.IsVisible = false;
                TableSeparator.IsVisible = false;
                ResultsListControl.IsVisible = false;
            }
            else
            {
                NoIssuesPanel.IsVisible = false;
                TableHeader.IsVisible = true;
                TableSeparator.IsVisible = true;
                ResultsListControl.IsVisible = true;

                // Build list items
                var items = results.Select(r => new UnusedCodeListItem
                {
                    Kind = r.Kind,
                    Name = r.Name,
                    Location = r.Location,
                    HintsText = string.Join(", ", r.Hints),
                    KindBackground = GetKindBackground(r.Kind),
                    KindForeground = GetKindForeground(r.Kind)
                }).ToList();

                ResultsListControl.ItemsSource = items;
            }

            LoadingPanel.IsVisible = false;
            ResultsPanel.IsVisible = true;
            SetGeneratePromptButtonEnabled(true);
        }

        private SolidColorBrush GetKindBackground(string kind)
        {
            return kind switch
            {
                "variable" or "field" or "property" or "var" =>
                    new Avalonia.Media.SolidColorBrush(Color.FromArgb(30, 245, 158, 11)),      // amber
                "method" or "function" or "arrow function" or "static method" =>
                    new Avalonia.Media.SolidColorBrush(Color.FromArgb(30, 239, 68, 68)),       // red
                "class" or "struct" or "protocol" or "mixin" or "typedef"
                    or "StatelessWidget" or "StatefulWidget" or "ChangeNotifier" or "InheritedWidget" =>
                    new Avalonia.Media.SolidColorBrush(Color.FromArgb(30, 139, 92, 246)),      // purple
                "enum" =>
                    new Avalonia.Media.SolidColorBrush(Color.FromArgb(30, 16, 185, 129)),      // green
                "interface" or "extension" =>
                    new Avalonia.Media.SolidColorBrush(Color.FromArgb(30, 59, 130, 246)),      // blue
                "package" or "asset" =>
                    new Avalonia.Media.SolidColorBrush(Color.FromArgb(30, 236, 72, 153)),      // pink
                _ => new Avalonia.Media.SolidColorBrush(Color.FromArgb(20, 107, 114, 128))     // gray
            };
        }

        private SolidColorBrush GetKindForeground(string kind)
        {
            return kind switch
            {
                "variable" or "field" or "property" or "var" =>
                    new Avalonia.Media.SolidColorBrush(Color.FromRgb(245, 158, 11)),
                "method" or "function" or "arrow function" or "static method" =>
                    new Avalonia.Media.SolidColorBrush(Color.FromRgb(239, 68, 68)),
                "class" or "struct" or "protocol" or "mixin" or "typedef"
                    or "StatelessWidget" or "StatefulWidget" or "ChangeNotifier" or "InheritedWidget" =>
                    new Avalonia.Media.SolidColorBrush(Color.FromRgb(139, 92, 246)),
                "enum" =>
                    new Avalonia.Media.SolidColorBrush(Color.FromRgb(16, 185, 129)),
                "interface" or "extension" =>
                    new Avalonia.Media.SolidColorBrush(Color.FromRgb(59, 130, 246)),
                "package" or "asset" =>
                    new Avalonia.Media.SolidColorBrush(Color.FromRgb(236, 72, 153)),
                _ => new Avalonia.Media.SolidColorBrush(Color.FromRgb(107, 114, 128))
            };
        }

        private void CloseButton_Click(object sender, PointerPressedEventArgs e)
        {
            Hide();
        }

        private async void GenerateRemovalPrompt_Click(object sender, PointerPressedEventArgs e)
        {
            try
            {
                var prompt = GenerateRemovalPrompt(_currentResults, _currentProjectPath);
                if (string.IsNullOrWhiteSpace(prompt))
                {
                    return;
                }

                await (TopLevel.GetTopLevel(this)?.Clipboard?.SetTextAsync(prompt) ?? Task.CompletedTask);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"GenerateRemovalPrompt error: {ex.Message}");
            }
        }

        private string GenerateRemovalPrompt(IReadOnlyList<UnusedCodeResult> results, string projectPath)
        {
            if (results.Count == 0)
            {
                return $"""
                Review the codebase at {projectPath} and confirm there are no clearly unused declarations left to remove.
                """;
            }

            var normalizedProjectPath = string.IsNullOrWhiteSpace(projectPath)
                ? HeaderProjectName.Text
                : projectPath;
            var projectName = Path.GetFileName(Path.TrimEndingDirectorySeparator(normalizedProjectPath));
            var groupedResults = results
                .GroupBy(result => result.Kind.ToLowerInvariant())
                .ToDictionary(group => group.Key, group => group.ToList());
            var preferredOrder = new[]
            {
                "class", "struct", "interface", "protocol", "enum",
                "function", "method", "property", "variable", "typealias"
            };
            var sections = new List<string>();

            foreach (var kind in preferredOrder)
            {
                if (!groupedResults.TryGetValue(kind, out var items) || items.Count == 0)
                {
                    continue;
                }

                sections.Add(BuildPromptSection(kind, items));
            }

            var remainingKinds = groupedResults.Keys
                .Where(kind => !preferredOrder.Contains(kind))
                .OrderBy(kind => kind);

            foreach (var kind in remainingKinds)
            {
                var items = groupedResults[kind];
                if (items.Count == 0)
                {
                    continue;
                }

                sections.Add(BuildPromptSection(items[0].Kind, items));
            }

            var sectionsText = string.Join(Environment.NewLine + Environment.NewLine, sections);

            return $"""
            SYSTEM ROLE:
            You are a senior refactoring agent operating in deterministic cleanup mode. Your job is to remove verified dead code while preserving runtime behavior.

            PROJECT:
            Name: "{projectName}"
            Path: "{normalizedProjectPath}"

            GOAL:
            For EACH symbol listed below, perform a full-project static reference search across the repository. If there are ZERO references, remove it.

            EXECUTION RULES:
            1) Perform a global reference search across ALL files (source, tests, configs, scripts), including barrel exports (index.ts), re-exports, and registry/config wiring.
            2) If a symbol has zero references (or only self-references) -> DELETE the declaration.
            3) After deletion, remove now-dead imports, exports, and any barrel-file entries that referenced the symbol.
            4) IMPORTANT: Exported status does NOT imply usage. If an exported symbol has zero references in this repo, it MUST be removed (and its export removed too).
            5) ONLY keep a symbol if you find concrete evidence of non-static usage, such as:
               - string-based lookup (e.g., accessing by name)
               - framework conventions requiring the symbol name
               - generated code hooks
               If kept, you MUST provide the exact file and line(s) proving this usage.
            6) Do not ask for confirmation. Apply the cleanup immediately.
            7) Do not invent usage. If you cannot find references, treat it as unused and remove it.

            POTENTIALLY UNUSED ITEMS (inspect one-by-one):
            {sectionsText}

            AFTER CLEANUP, OUTPUT:
            - Removed symbols (name + file)
            - Modified files
            - Preserved/risky items + exact evidence (file:line)
            - Follow-ups: run typecheck/lint/tests/build and report failures
            """;
        }

        private static string BuildPromptSection(string kind, IReadOnlyList<UnusedCodeResult> items)
        {
            var title = GetPromptSectionTitle(kind, items.Count);
            var lines = items.Select(item =>
            {
                var hints = string.Join(", ", item.Hints);
                return $"- {item.Name} ({item.Location}){(string.IsNullOrWhiteSpace(hints) ? string.Empty : $" - {hints}")}";
            });

            return $"{title}:{Environment.NewLine}{string.Join(Environment.NewLine, lines)}";
        }

        private static string GetPromptSectionTitle(string kind, int count)
        {
            var normalizedKind = kind.ToLowerInvariant();
            var singularTitle = normalizedKind switch
            {
                "class" => "Class",
                "struct" => "Struct",
                "interface" => "Interface",
                "protocol" => "Protocol",
                "enum" => "Enum",
                "function" => "Function",
                "method" => "Method",
                "property" => "Property",
                "variable" => "Variable",
                "typealias" => "Typealias",
                _ => string.IsNullOrWhiteSpace(kind)
                    ? "Item"
                    : char.ToUpperInvariant(kind[0]) + kind[1..]
            };

            if (count == 1)
            {
                return singularTitle;
            }

            return normalizedKind switch
            {
                "class" => "Classes",
                "property" => "Properties",
                _ => singularTitle + "s"
            };
        }

        private void SetGeneratePromptButtonEnabled(bool isEnabled)
        {
            GeneratePromptButton.IsEnabled = isEnabled;
            GeneratePromptButton.Opacity = isEnabled ? 1.0 : 0.5;
            GeneratePromptButton.Cursor = isEnabled ? new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand) : Avalonia.Input.Cursor.Default;
        }

        private void Overlay_MouseLeftButtonDown(object sender, PointerPressedEventArgs e)
        {
            if ((e as PointerPressedEventArgs)?.Source == this)
            {
                Hide();
            }
        }

        private void PopupContent_MouseLeftButtonDown(object sender, PointerPressedEventArgs e)
        {
            e.Handled = true;
        }
    }
}
