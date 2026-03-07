using DevAtlas.Models;
using System.Text.RegularExpressions;

namespace DevAtlas.Services.UnusedCodeAnalyzer
{
    /// <summary>
    /// Regex-based unused code analyzer for Swift projects.
    /// Detects unused classes, structs, enums, protocols, extensions, private vars and funcs.
    /// </summary>
    public class SwiftUnusedAnalyzer : ILanguageAnalyzer
    {
        public string[] SupportedExtensions => new[] { ".swift" };
        public string LanguageName => "Swift";

        private static readonly Regex ClassRegex = new(
            @"(?:open|public|internal|fileprivate|private)?\s*(?:final\s+)?class\s+([a-zA-Z_][a-zA-Z0-9_]*)",
            RegexOptions.Compiled);

        private static readonly Regex StructRegex = new(
            @"(?:open|public|internal|fileprivate|private)?\s*struct\s+([a-zA-Z_][a-zA-Z0-9_]*)",
            RegexOptions.Compiled);

        private static readonly Regex EnumRegex = new(
            @"(?:open|public|internal|fileprivate|private)?\s*enum\s+([a-zA-Z_][a-zA-Z0-9_]*)",
            RegexOptions.Compiled);

        private static readonly Regex ProtocolRegex = new(
            @"(?:open|public|internal|fileprivate|private)?\s*protocol\s+([a-zA-Z_][a-zA-Z0-9_]*)",
            RegexOptions.Compiled);

        private static readonly Regex ExtensionRegex = new(
            @"(?:open|public|internal|fileprivate|private)?\s*extension\s+([a-zA-Z_][a-zA-Z0-9_]*)",
            RegexOptions.Compiled);

        private static readonly Regex PrivateVarRegex = new(
            @"private\s+(?:var|let)\s+([a-zA-Z0-9_]+)",
            RegexOptions.Compiled);

        private static readonly Regex PrivateFuncRegex = new(
            @"private\s+func\s+([a-zA-Z0-9_]+)",
            RegexOptions.Compiled);

        // Swift built-in types to skip
        private static readonly HashSet<string> SwiftBuiltInTypes = new()
        {
            "String", "Int", "Int8", "Int16", "Int32", "Int64",
            "UInt", "UInt8", "UInt16", "UInt32", "UInt64",
            "Float", "Double", "Character", "Unicode",
            "Array", "Dictionary", "Set", "Optional", "Bool",
            "Any", "AnyObject", "AnyClass", "Self", "Type",
            "Error", "Void", "Never", "Result", "Range",
            "ClosedRange", "Sequence", "Iterator", "Collection",
            "BidirectionalCollection", "RandomAccessCollection",
            "Equatable", "Comparable", "Hashable", "Codable",
            "Encodable", "Decodable", "Identifiable", "ObservableObject",
            "Published", "State", "Binding", "ObservedObject",
            "View", "App", "Scene", "WindowGroup", "AppDelegate"
        };

        public List<UnusedCodeResult> Analyze(string projectPath)
        {
            var results = new List<UnusedCodeResult>();
            var files = GetSwiftFiles(projectPath);

            // Collect all file contents for cross-file analysis
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
                var structs = new List<(string name, int line)>();
                var enums = new List<(string name, int line)>();
                var protocols = new List<(string name, int line)>();
                var extensions = new List<(string name, int line)>();
                var privateVars = new List<(string name, int line)>();
                var privateFuncs = new List<(string name, int line)>();

                for (int i = 0; i < lines.Length; i++)
                {
                    var line = lines[i];
                    var lineNumber = i + 1;
                    var trimmed = line.TrimStart();

                    if (string.IsNullOrWhiteSpace(trimmed) ||
                        trimmed.StartsWith("//") ||
                        trimmed.StartsWith("/*") ||
                        trimmed.StartsWith("*") ||
                        trimmed.StartsWith("@") ||
                        trimmed.StartsWith("import ") ||
                        trimmed.StartsWith("#if") ||
                        trimmed.StartsWith("#else") ||
                        trimmed.StartsWith("#endif"))
                        continue;

                    // Class
                    var classMatch = ClassRegex.Match(line);
                    if (classMatch.Success)
                    {
                        var name = classMatch.Groups[1].Value;
                        if (!SwiftBuiltInTypes.Contains(name) && !name.StartsWith("_") && !name.StartsWith("UI") && !name.StartsWith("NS"))
                            classes.Add((name, lineNumber));
                    }

                    // Struct
                    var structMatch = StructRegex.Match(line);
                    if (structMatch.Success)
                    {
                        var name = structMatch.Groups[1].Value;
                        if (!SwiftBuiltInTypes.Contains(name) && !name.StartsWith("_") && !name.StartsWith("UI") && !name.StartsWith("NS"))
                            structs.Add((name, lineNumber));
                    }

                    // Enum
                    var enumMatch = EnumRegex.Match(line);
                    if (enumMatch.Success)
                    {
                        var name = enumMatch.Groups[1].Value;
                        if (!SwiftBuiltInTypes.Contains(name) && !name.StartsWith("_"))
                            enums.Add((name, lineNumber));
                    }

                    // Protocol
                    var protoMatch = ProtocolRegex.Match(line);
                    if (protoMatch.Success)
                    {
                        var name = protoMatch.Groups[1].Value;
                        if (!SwiftBuiltInTypes.Contains(name) && !name.StartsWith("_"))
                            protocols.Add((name, lineNumber));
                    }

                    // Extension
                    var extMatch = ExtensionRegex.Match(line);
                    if (extMatch.Success)
                    {
                        var name = extMatch.Groups[1].Value;
                        if (!SwiftBuiltInTypes.Contains(name) && !name.StartsWith("_"))
                            extensions.Add((name, lineNumber));
                    }

                    // Private var/let
                    var varMatch = PrivateVarRegex.Match(line);
                    if (varMatch.Success)
                        privateVars.Add((varMatch.Groups[1].Value, lineNumber));

                    // Private func
                    var funcMatch = PrivateFuncRegex.Match(line);
                    if (funcMatch.Success)
                        privateFuncs.Add((funcMatch.Groups[1].Value, lineNumber));
                }

                // Cross-file: classes
                foreach (var cls in classes)
                    CheckCrossFileUsage(results, combinedContent, cls.name, cls.line, shortFile, "class", "Class appears unused across project");

                // Cross-file: structs
                foreach (var str in structs)
                    CheckCrossFileUsage(results, combinedContent, str.name, str.line, shortFile, "struct", "Struct appears unused across project");

                // Cross-file: enums
                foreach (var enm in enums)
                    CheckCrossFileUsage(results, combinedContent, enm.name, enm.line, shortFile, "enum", "Enum appears unused across project");

                // Cross-file: protocols
                foreach (var proto in protocols)
                    CheckCrossFileUsage(results, combinedContent, proto.name, proto.line, shortFile, "protocol", "Protocol appears unused across project");

                // Cross-file: extensions
                foreach (var ext in extensions)
                    CheckCrossFileUsage(results, combinedContent, ext.name, ext.line, shortFile, "extension", "Extension appears unused across project");

                // Local: private vars
                foreach (var v in privateVars)
                {
                    var usageCount = content.Split(new[] { v.name }, StringSplitOptions.None).Length - 1;
                    if (usageCount <= 1)
                    {
                        results.Add(new UnusedCodeResult
                        {
                            Kind = "var",
                            Name = v.name,
                            Location = $"{shortFile}:{v.line}",
                            Hints = new List<string> { "Private property is unused" }
                        });
                    }
                }

                // Local: private funcs
                foreach (var f in privateFuncs)
                {
                    var usageCount = content.Split(new[] { f.name }, StringSplitOptions.None).Length - 1;
                    if (usageCount <= 1)
                    {
                        results.Add(new UnusedCodeResult
                        {
                            Kind = "function",
                            Name = f.name,
                            Location = $"{shortFile}:{f.line}",
                            Hints = new List<string> { "Private function is unused" }
                        });
                    }
                }
            }

            return results;
        }

        private void CheckCrossFileUsage(List<UnusedCodeResult> results, string combinedContent,
            string name, int line, string shortFile, string kind, string hint)
        {
            try
            {
                var regex = new Regex($@"\b{Regex.Escape(name)}\b");
                if (regex.Matches(combinedContent).Count <= 1)
                {
                    results.Add(new UnusedCodeResult
                    {
                        Kind = kind,
                        Name = name,
                        Location = $"{shortFile}:{line}",
                        Hints = new List<string> { hint }
                    });
                }
            }
            catch { }
        }

        private List<string> GetSwiftFiles(string path)
        {
            var files = new List<string>();
            var excludeDirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "Pods", ".build", "DerivedData", ".git", "build"
            };

            var stack = new Stack<string>();
            stack.Push(path);

            while (stack.Count > 0)
            {
                var dir = stack.Pop();
                try
                {
                    foreach (var file in Directory.GetFiles(dir, "*.swift"))
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
