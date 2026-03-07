using DevAtlas.Models;
using System.Text.RegularExpressions;

namespace DevAtlas.Services.UnusedCodeAnalyzer
{
    /// <summary>
    /// Regex-based unused code analyzer for Dart/Flutter projects.
    /// Detects unused classes, mixins, extensions, enums, typedefs, functions, variables,
    /// private members, Flutter widgets, and unused pubspec packages/assets.
    /// </summary>
    public class DartUnusedAnalyzer : ILanguageAnalyzer
    {
        public string[] SupportedExtensions => new[] { ".dart" };
        public string LanguageName => "Dart";

        // Regex patterns
        private static readonly Regex ClassRegex = new(
            @"(?:abstract\s+)?class\s+([a-zA-Z_][a-zA-Z0-9_]*)", RegexOptions.Compiled);
        private static readonly Regex MixinRegex = new(
            @"mixin\s+([a-zA-Z_][a-zA-Z0-9_]*)", RegexOptions.Compiled);
        private static readonly Regex ExtensionRegex = new(
            @"extension\s+(?:[a-zA-Z_][a-zA-Z0-9_]*\s+)?on\s+([a-zA-Z_][a-zA-Z0-9_]*)", RegexOptions.Compiled);
        private static readonly Regex EnumRegex = new(
            @"enum\s+([a-zA-Z_][a-zA-Z0-9_]*)", RegexOptions.Compiled);
        private static readonly Regex TypedefRegex = new(
            @"typedef\s+(?:[a-zA-Z_][a-zA-Z0-9_<>,\s]*\s+)?([a-zA-Z_][a-zA-Z0-9_]*)\s*=", RegexOptions.Compiled);
        private static readonly Regex TopLevelVarRegex = new(
            @"(?:static\s+)?(?:final|const|var)\s+(?:<[^>]+>\s+)?([a-zA-Z_][a-zA-Z0-9_]*)\s*=", RegexOptions.Compiled);
        private static readonly Regex PrivateMethodRegex = new(
            @"(?:void|int|double|String|bool|dynamic|var|final|Widget|Stream|Future|static)?\s*(?:\?\s*)?\s+_([a-zA-Z_][a-zA-Z0-9_]*)\s*\(", RegexOptions.Compiled);
        private static readonly Regex PrivateVarRegex = new(
            @"(?:final|const|var)\s+_([a-zA-Z_][a-zA-Z0-9_]*)", RegexOptions.Compiled);
        private static readonly Regex StaticMethodRegex = new(
            @"static\s+(?:void|int|double|String|bool|dynamic|Widget|Stream|Future)?\s*(?:\?\s*)?\s+([a-zA-Z_][a-zA-Z0-9_]*)\s*\(", RegexOptions.Compiled);

        // Flutter-specific patterns
        private static readonly Regex StatelessWidgetRegex = new(
            @"class\s+([a-zA-Z_][a-zA-Z0-9_]*)\s+extends\s+StatelessWidget", RegexOptions.Compiled);
        private static readonly Regex StatefulWidgetRegex = new(
            @"class\s+([a-zA-Z_][a-zA-Z0-9_]*)\s+extends\s+StatefulWidget", RegexOptions.Compiled);
        private static readonly Regex ChangeNotifierRegex = new(
            @"class\s+([a-zA-Z_][a-zA-Z0-9_]*)\s+extends\s+ChangeNotifier", RegexOptions.Compiled);

        // Dart built-in types to skip
        private static readonly HashSet<string> DartBuiltInTypes = new()
        {
            "String", "int", "double", "num", "bool", "dynamic", "void", "Never",
            "Object", "Function", "Symbol", "Type", "Null",
            "List", "Map", "Set", "Iterable", "Collection", "Queue", "Stack",
            "Future", "Stream", "FutureOr", "StreamSubscription",
            "Widget", "BuildContext", "State", "StatefulWidget", "StatelessWidget",
            "Key", "Element", "RenderObject", "RenderObjectWidget",
            "AppBar", "Scaffold", "Container", "Text", "Image", "Icon",
            "Center", "Column", "Row", "Padding",
            "TextStyle", "BoxDecoration", "Border", "EdgeInsets",
            "Navigator", "Route", "MaterialPageRoute",
            "Theme", "ThemeData", "Colors", "Icons",
            "ChangeNotifier", "Provider", "Consumer", "Selector",
            "ChangeNotifierProvider", "FutureProvider", "StreamProvider",
            "InheritedWidget", "InheritedModel", "ProxyWidget",
            "main", "runApp", "print", "toString", "hashCode",
            "context", "mounted", "widget"
        };

        public List<UnusedCodeResult> Analyze(string projectPath)
        {
            var results = new List<UnusedCodeResult>();

            // Dart regex analysis
            results.AddRange(DartRegexAnalysis(projectPath));

            // Pubspec analysis
            results.AddRange(AnalyzePubspec(projectPath));

            return results;
        }

        private List<UnusedCodeResult> AnalyzePubspec(string projectPath)
        {
            var results = new List<UnusedCodeResult>();
            var pubspecPath = Path.Combine(projectPath, "pubspec.yaml");

            if (!File.Exists(pubspecPath)) return results;

            string pubspecContent;
            try { pubspecContent = File.ReadAllText(pubspecPath); }
            catch { return results; }

            var dependencies = ParsePubspecDependencies(pubspecContent);
            var assets = ParsePubspecAssets(pubspecContent);

            // Collect all Dart file contents
            var allDartContent = new System.Text.StringBuilder();
            foreach (var file in GetDartFiles(projectPath))
            {
                try { allDartContent.AppendLine(File.ReadAllText(file)); }
                catch { }
            }
            var combinedDart = allDartContent.ToString();

            // Check unused packages
            var skipPackages = new HashSet<string> { "flutter", "flutter_test", "meta", "collection", "async" };
            foreach (var package in dependencies)
            {
                if (skipPackages.Contains(package)) continue;
                var importPattern = $"package:{package}";
                if (!combinedDart.Contains(importPattern))
                {
                    results.Add(new UnusedCodeResult
                    {
                        Kind = "package",
                        Name = package,
                        Location = "pubspec.yaml",
                        Hints = new List<string> { "Package defined but not imported in any Dart file" }
                    });
                }
            }

            // Check unused assets
            foreach (var assetPath in assets)
            {
                var fullAssetPath = Path.Combine(projectPath, assetPath.Replace('/', Path.DirectorySeparatorChar));
                bool isReferenced = false;

                if (Directory.Exists(fullAssetPath))
                {
                    try
                    {
                        foreach (var file in Directory.GetFiles(fullAssetPath, "*", SearchOption.AllDirectories))
                        {
                            var fileName = Path.GetFileName(file);
                            if (combinedDart.Contains(fileName) || combinedDart.Contains(assetPath))
                            {
                                isReferenced = true;
                                break;
                            }
                        }
                    }
                    catch { }
                }
                else if (File.Exists(fullAssetPath))
                {
                    var fileName = Path.GetFileName(assetPath);
                    isReferenced = combinedDart.Contains(fileName) || combinedDart.Contains(assetPath);
                }

                if (!isReferenced)
                {
                    var nameWithoutExt = Path.GetFileNameWithoutExtension(assetPath);
                    if (combinedDart.Contains(nameWithoutExt))
                        isReferenced = true;
                }

                if (!isReferenced)
                {
                    results.Add(new UnusedCodeResult
                    {
                        Kind = "asset",
                        Name = assetPath,
                        Location = "pubspec.yaml",
                        Hints = new List<string> { "Asset defined but not referenced in code" }
                    });
                }
            }

            return results;
        }

        private List<string> ParsePubspecDependencies(string content)
        {
            var dependencies = new List<string>();
            var skipPackages = new HashSet<string> { "flutter", "flutter_test", "sdk" };
            var lines = content.Split('\n');
            bool inDeps = false;

            foreach (var line in lines)
            {
                var trimmed = line.TrimStart();

                if (trimmed.StartsWith("dependencies:") || trimmed.StartsWith("dev_dependencies:"))
                {
                    inDeps = true;
                    continue;
                }

                if (inDeps && !line.StartsWith(" ") && !line.StartsWith("\t") && !string.IsNullOrWhiteSpace(trimmed) && !trimmed.StartsWith("#"))
                {
                    if (trimmed.EndsWith(":") && !trimmed.StartsWith("dependencies") && !trimmed.StartsWith("dev_dependencies"))
                        inDeps = false;
                }

                if (inDeps && !string.IsNullOrWhiteSpace(trimmed) && !trimmed.StartsWith("#"))
                {
                    if (line.StartsWith("  ") && !line.StartsWith("    "))
                    {
                        var colonIndex = trimmed.IndexOf(':');
                        if (colonIndex > 0)
                        {
                            var packageName = trimmed.Substring(0, colonIndex).Trim();
                            if (!string.IsNullOrEmpty(packageName) && !skipPackages.Contains(packageName) && !packageName.Contains(" "))
                                dependencies.Add(packageName);
                        }
                    }
                }
            }

            return dependencies;
        }

        private List<string> ParsePubspecAssets(string content)
        {
            var assets = new List<string>();
            bool inAssets = false;

            foreach (var line in content.Split('\n'))
            {
                var trimmed = line.TrimStart();

                if (trimmed.StartsWith("assets:"))
                {
                    inAssets = true;
                    continue;
                }

                if (inAssets)
                {
                    if (string.IsNullOrWhiteSpace(trimmed) || trimmed.StartsWith("#")) continue;

                    if (trimmed.StartsWith("- "))
                    {
                        var assetPath = trimmed.Substring(2).Trim().Replace("\"", "").Replace("'", "");
                        var commentIndex = assetPath.IndexOf('#');
                        if (commentIndex >= 0)
                            assetPath = assetPath.Substring(0, commentIndex).Trim();
                        if (!string.IsNullOrEmpty(assetPath))
                            assets.Add(assetPath);
                    }
                    else
                    {
                        inAssets = false;
                    }
                }
            }

            return assets;
        }

        private List<UnusedCodeResult> DartRegexAnalysis(string projectPath)
        {
            var results = new List<UnusedCodeResult>();
            var files = GetDartFiles(projectPath);

            var allFileContents = new Dictionary<string, string>();
            foreach (var file in files)
            {
                try { allFileContents[file] = File.ReadAllText(file); }
                catch { }
            }

            var combinedContent = string.Join("\n", allFileContents.Values);

            foreach (var file in files)
            {
                if (!allFileContents.TryGetValue(file, out var content)) continue;
                var lines = content.Split('\n');
                var shortFile = Path.GetFileName(file);

                var classes = new List<(string name, int line)>();
                var mixins = new List<(string name, int line)>();
                var extensions = new List<(string name, int line)>();
                var enums = new List<(string name, int line)>();
                var typedefs = new List<(string name, int line)>();
                var topLevelVars = new List<(string name, int line)>();
                var privateMethods = new List<(string name, int line)>();
                var privateVars = new List<(string name, int line)>();
                var staticMethods = new List<(string name, int line)>();
                var statelessWidgets = new List<(string name, int line)>();
                var statefulWidgets = new List<(string name, int line)>();
                var changeNotifiers = new List<(string name, int line)>();

                for (int i = 0; i < lines.Length; i++)
                {
                    var line = lines[i];
                    var lineNumber = i + 1;
                    var trimmed = line.TrimStart();

                    if (string.IsNullOrWhiteSpace(trimmed) ||
                        trimmed.StartsWith("//") || trimmed.StartsWith("/*") || trimmed.StartsWith("*") ||
                        trimmed.StartsWith("@") || trimmed.StartsWith("import ") ||
                        trimmed.StartsWith("export ") || trimmed.StartsWith("part "))
                        continue;

                    TryMatch(ClassRegex, line, name => { if (!DartBuiltInTypes.Contains(name) && !name.StartsWith("_")) classes.Add((name, lineNumber)); });
                    TryMatch(MixinRegex, line, name => { if (!DartBuiltInTypes.Contains(name) && !name.StartsWith("_")) mixins.Add((name, lineNumber)); });
                    TryMatch(ExtensionRegex, line, name => { if (!DartBuiltInTypes.Contains(name)) extensions.Add((name, lineNumber)); });
                    TryMatch(EnumRegex, line, name => { if (!DartBuiltInTypes.Contains(name) && !name.StartsWith("_")) enums.Add((name, lineNumber)); });
                    TryMatch(TypedefRegex, line, name => { if (!DartBuiltInTypes.Contains(name) && !name.StartsWith("_")) typedefs.Add((name, lineNumber)); });
                    TryMatch(TopLevelVarRegex, line, name => { if (!DartBuiltInTypes.Contains(name) && !name.StartsWith("_")) topLevelVars.Add((name, lineNumber)); });
                    TryMatch(PrivateMethodRegex, line, name => { if (!DartBuiltInTypes.Contains(name)) privateMethods.Add((name, lineNumber)); });
                    TryMatch(PrivateVarRegex, line, name => { if (!DartBuiltInTypes.Contains(name)) privateVars.Add((name, lineNumber)); });
                    TryMatch(StaticMethodRegex, line, name => { if (!DartBuiltInTypes.Contains(name)) staticMethods.Add((name, lineNumber)); });
                    TryMatch(StatelessWidgetRegex, line, name => { if (!DartBuiltInTypes.Contains(name)) statelessWidgets.Add((name, lineNumber)); });
                    TryMatch(StatefulWidgetRegex, line, name => { if (!DartBuiltInTypes.Contains(name)) statefulWidgets.Add((name, lineNumber)); });
                    TryMatch(ChangeNotifierRegex, line, name => { if (!DartBuiltInTypes.Contains(name)) changeNotifiers.Add((name, lineNumber)); });
                }

                // Cross-file checks
                foreach (var cls in classes) CheckCrossFile(results, combinedContent, cls, shortFile, "class", "Class appears unused across project");
                foreach (var mix in mixins) CheckMixinUsage(results, combinedContent, mix, shortFile);
                foreach (var ext in extensions) CheckCrossFile(results, combinedContent, ext, shortFile, "extension", "Extension appears unused across project");
                foreach (var enm in enums) CheckCrossFile(results, combinedContent, enm, shortFile, "enum", "Enum appears unused across project");
                foreach (var td in typedefs) CheckCrossFile(results, combinedContent, td, shortFile, "typedef", "Typedef appears unused across project");
                foreach (var tv in topLevelVars) CheckCrossFile(results, combinedContent, tv, shortFile, "variable", "Top-level variable appears unused");
                foreach (var sw in statelessWidgets) CheckCrossFile(results, combinedContent, sw, shortFile, "StatelessWidget", "StatelessWidget appears unused across project");
                foreach (var fw in statefulWidgets) CheckCrossFile(results, combinedContent, fw, shortFile, "StatefulWidget", "StatefulWidget appears unused across project");

                // Local file checks
                foreach (var pm in privateMethods) CheckLocalUsage(results, content, $"_?{pm.name}\\s*\\(", pm, shortFile, "method", "Private method appears unused");
                foreach (var pv in privateVars) CheckLocalUsage(results, content, $"_?{pv.name}\\b", pv, shortFile, "variable", "Private variable appears unused");
                foreach (var sm in staticMethods) CheckLocalUsage(results, content, $@"\b{Regex.Escape(sm.name)}\s*\(", sm, shortFile, "static method", "Static method appears unused");
            }

            return results;
        }

        private void TryMatch(Regex regex, string line, Action<string> onMatch)
        {
            var match = regex.Match(line);
            if (match.Success)
                onMatch(match.Groups[1].Value);
        }

        private void CheckCrossFile(List<UnusedCodeResult> results, string combined,
            (string name, int line) item, string shortFile, string kind, string hint)
        {
            try
            {
                var regex = new Regex($@"\b{Regex.Escape(item.name)}\b");
                if (regex.Matches(combined).Count <= 1)
                {
                    results.Add(new UnusedCodeResult
                    {
                        Kind = kind,
                        Name = item.name,
                        Location = $"{shortFile}:{item.line}",
                        Hints = new List<string> { hint }
                    });
                }
            }
            catch { }
        }

        private void CheckMixinUsage(List<UnusedCodeResult> results, string combined,
            (string name, int line) item, string shortFile)
        {
            try
            {
                var regex = new Regex($@"with\s+{Regex.Escape(item.name)}\b");
                if (regex.Matches(combined).Count == 0)
                {
                    results.Add(new UnusedCodeResult
                    {
                        Kind = "mixin",
                        Name = item.name,
                        Location = $"{shortFile}:{item.line}",
                        Hints = new List<string> { "Mixin appears unused across project" }
                    });
                }
            }
            catch { }
        }

        private void CheckLocalUsage(List<UnusedCodeResult> results, string content,
            string pattern, (string name, int line) item, string shortFile, string kind, string hint)
        {
            try
            {
                var regex = new Regex(pattern);
                if (regex.Matches(content).Count <= 1)
                {
                    results.Add(new UnusedCodeResult
                    {
                        Kind = kind,
                        Name = item.name,
                        Location = $"{shortFile}:{item.line}",
                        Hints = new List<string> { hint }
                    });
                }
            }
            catch { }
        }

        private List<string> GetDartFiles(string path)
        {
            var files = new List<string>();
            var excludeDirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "node_modules", ".pub-cache", "build", ".dart_tool", ".git",
                "ios", "android", "web", "linux", "macos", "windows"
            };

            var stack = new Stack<string>();
            stack.Push(path);

            while (stack.Count > 0)
            {
                var dir = stack.Pop();
                try
                {
                    foreach (var file in Directory.GetFiles(dir, "*.dart"))
                        files.Add(file);

                    foreach (var subDir in Directory.GetDirectories(dir))
                    {
                        var dirName = Path.GetFileName(subDir);
                        if (!excludeDirs.Contains(dirName) && !dirName.StartsWith("."))
                            stack.Push(subDir);
                    }
                }
                catch { }
            }

            return files;
        }
    }
}
