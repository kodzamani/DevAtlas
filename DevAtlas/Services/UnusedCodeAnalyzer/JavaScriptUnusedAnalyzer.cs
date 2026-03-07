using DevAtlas.Models;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace DevAtlas.Services.UnusedCodeAnalyzer
{
    /// <summary>
    /// Regex and compiler-backed unused code analyzer for JavaScript/TypeScript projects.
    /// Mirrors the Swift-side analyzer behavior for source collection, project-wide references,
    /// exported symbol handling, and TypeScript compiler diagnostics.
    /// </summary>
    public class JavaScriptUnusedAnalyzer : ILanguageAnalyzer
    {
        public string[] SupportedExtensions => new[] { ".js", ".jsx", ".ts", ".tsx", ".mjs", ".cjs", ".mts", ".cts" };
        public string LanguageName => "JavaScript";

        private static readonly HashSet<string> SourceExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".js", ".jsx", ".ts", ".tsx", ".mjs", ".cjs", ".mts", ".cts"
        };

        private static readonly HashSet<string> ReferenceExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".js", ".jsx", ".ts", ".tsx", ".mjs", ".cjs", ".mts", ".cts",
            ".vue", ".svelte", ".astro", ".mdx", ".md", ".html"
        };

        private static readonly string[] IgnoredDirectories =
        {
            "node_modules/",
            ".next/",
            ".nuxt/",
            "dist/",
            "build/",
            "coverage/",
            ".turbo/",
            ".cache/",
            "storybook-static/"
        };

        private static readonly HashSet<string> IgnoredSymbolNames = new(StringComparer.Ordinal)
        {
            "undefined",
            "NaN",
            "arguments"
        };

        private static readonly Regex ArrowFunctionRegex = new(
            @"(?:export\s+default\s+|export\s+)?(?:const|let|var)\s+([a-zA-Z_$][a-zA-Z0-9_$]*)\s*=\s*(?:async\s+)?(?:\([^)]*\)|[a-zA-Z_$][a-zA-Z0-9_$]*)\s*=>",
            RegexOptions.Compiled);

        private static readonly Regex FunctionRegex = new(
            @"(?:export\s+default\s+|export\s+)?(?:async\s+)?function\s+([a-zA-Z_$][a-zA-Z0-9_$]*)\s*\(",
            RegexOptions.Compiled);

        private static readonly Regex ClassRegex = new(
            @"(?:export\s+default\s+|export\s+)?(?:abstract\s+)?class\s+([a-zA-Z_$][a-zA-Z0-9_$]*)\b",
            RegexOptions.Compiled);

        private static readonly Regex InterfaceRegex = new(
            @"(?:export\s+)?interface\s+([a-zA-Z_$][a-zA-Z0-9_$]*)\b",
            RegexOptions.Compiled);

        private static readonly Regex TypeAliasRegex = new(
            @"(?:export\s+)?type\s+([a-zA-Z_$][a-zA-Z0-9_$]*)\b",
            RegexOptions.Compiled);

        private static readonly Regex EnumRegex = new(
            @"(?:export\s+)?(?:const\s+)?enum\s+([a-zA-Z_$][a-zA-Z0-9_$]*)\b",
            RegexOptions.Compiled);

        private static readonly Regex VariableRegex = new(
            @"(?:export\s+)?(?:const|let|var)\s+([a-zA-Z_$][a-zA-Z0-9_$]*)\s*=",
            RegexOptions.Compiled);

        private static readonly Regex[] DiagnosticFormats =
        {
            new Regex(@"(.+)\((\d+),(\d+)\):\s*(?:error|warning)\s+TS(\d+):\s+(.+)", RegexOptions.Compiled),
            new Regex(@"(.+):(\d+):(\d+)\s*-\s*(?:error|warning)\s+TS(\d+):\s+(.+)", RegexOptions.Compiled)
        };

        private static readonly Regex[] ExportListRegexes =
        {
            new Regex(@"export\s+(?:type\s+)?\{([^}]*)\}", RegexOptions.Compiled),
            new Regex(@"module\.exports\s*=\s*\{([^}]*)\}", RegexOptions.Compiled)
        };

        private static readonly Regex[] DirectExportRegexes =
        {
            new Regex(@"export\s+default\s+([a-zA-Z_$][a-zA-Z0-9_$]*)\b", RegexOptions.Compiled),
            new Regex(@"exports\.[a-zA-Z_$][a-zA-Z0-9_$]*\s*=\s*([a-zA-Z_$][a-zA-Z0-9_$]*)", RegexOptions.Compiled),
            new Regex(@"module\.exports\.[a-zA-Z_$][a-zA-Z0-9_$]*\s*=\s*([a-zA-Z_$][a-zA-Z0-9_$]*)", RegexOptions.Compiled),
            new Regex(@"module\.exports\s*=\s*([a-zA-Z_$][a-zA-Z0-9_$]*)\b", RegexOptions.Compiled)
        };

        private static readonly HashSet<string> SupportedDiagnosticCodes = new(StringComparer.Ordinal)
        {
            "6133",
            "6192",
            "6196"
        };

        public List<UnusedCodeResult> Analyze(string projectPath)
        {
            var sourceFiles = CollectSourceFiles(projectPath);
            if (sourceFiles.Count == 0)
            {
                return new List<UnusedCodeResult>();
            }

            var searchableProjectContent = CollectSearchableProjectContent(projectPath);
            var compilerResults = RunTypeScriptCompilerAnalysis(projectPath, sourceFiles);
            var regexResults = JavaScriptRegexAnalysis(sourceFiles, searchableProjectContent);

            return Deduplicate(compilerResults.Concat(regexResults));
        }

        private List<UnusedCodeResult> JavaScriptRegexAnalysis(
            IReadOnlyList<SourceFile> sourceFiles,
            string searchableProjectContent)
        {
            var results = new List<UnusedCodeResult>();

            foreach (var sourceFile in sourceFiles)
            {
                if (sourceFile.IsDeclarationFile)
                {
                    continue;
                }

                var exportedNames = CollectExportedNames(sourceFile.Content);
                var declarations = new List<Declaration>();

                for (int index = 0; index < sourceFile.Lines.Count; index++)
                {
                    var line = sourceFile.Lines[index];
                    var trimmed = line.Trim();
                    if (!ShouldAnalyze(trimmed))
                    {
                        continue;
                    }

                    var lineNumber = index + 1;

                    if (TryBuildDeclaration(line, lineNumber, sourceFile.RelativePath, exportedNames, ArrowFunctionRegex, "function", declarations))
                    {
                        continue;
                    }

                    if (TryBuildDeclaration(line, lineNumber, sourceFile.RelativePath, exportedNames, FunctionRegex, "function", declarations))
                    {
                        continue;
                    }

                    if (TryBuildDeclaration(line, lineNumber, sourceFile.RelativePath, exportedNames, ClassRegex, "class", declarations))
                    {
                        continue;
                    }

                    if (TryBuildDeclaration(line, lineNumber, sourceFile.RelativePath, exportedNames, InterfaceRegex, "interface", declarations))
                    {
                        continue;
                    }

                    if (TryBuildDeclaration(line, lineNumber, sourceFile.RelativePath, exportedNames, TypeAliasRegex, "typealias", declarations))
                    {
                        continue;
                    }

                    if (TryBuildDeclaration(line, lineNumber, sourceFile.RelativePath, exportedNames, EnumRegex, "enum", declarations))
                    {
                        continue;
                    }

                    var variableName = FirstCapture(line, VariableRegex);
                    if (!string.IsNullOrEmpty(variableName)
                        && ShouldTrackDeclaration(variableName)
                        && !LooksLikeFunctionOrClassAssignment(line))
                    {
                        var exported = IsExported(variableName, line, exportedNames);
                        declarations.Add(new Declaration(
                            "variable",
                            variableName,
                            $"{sourceFile.RelativePath}:{lineNumber}",
                            new List<string> { HintFor("variable", exported) },
                            exported ? SearchScope.Project : SearchScope.File));
                    }
                }

                foreach (var declaration in declarations)
                {
                    var searchableContent = declaration.SearchScope == SearchScope.Project
                        ? searchableProjectContent
                        : sourceFile.SearchableContent;

                    try
                    {
                        var usageRegex = new Regex(@"\b" + Regex.Escape(declaration.Name) + @"\b", RegexOptions.Compiled);
                        if (usageRegex.Matches(searchableContent).Count <= 1)
                        {
                            results.Add(new UnusedCodeResult
                            {
                                Kind = declaration.Kind,
                                Name = declaration.Name,
                                Location = declaration.Location,
                                Hints = declaration.Hints
                            });
                        }
                    }
                    catch
                    {
                    }
                }
            }

            return Deduplicate(results);
        }

        private bool TryBuildDeclaration(
            string line,
            int lineNumber,
            string relativePath,
            HashSet<string> exportedNames,
            Regex regex,
            string kind,
            List<Declaration> declarations)
        {
            var name = FirstCapture(line, regex);
            if (string.IsNullOrEmpty(name) || !ShouldTrackDeclaration(name))
            {
                return false;
            }

            var exported = IsExported(name, line, exportedNames);
            declarations.Add(new Declaration(
                kind,
                name,
                $"{relativePath}:{lineNumber}",
                new List<string> { HintFor(kind, exported) },
                exported ? SearchScope.Project : SearchScope.File));

            return true;
        }

        private List<SourceFile> CollectSourceFiles(string projectPath)
        {
            var sourceFiles = new List<SourceFile>();

            foreach (var file in EnumerateProjectFiles(projectPath))
            {
                var extension = Path.GetExtension(file);
                if (!SourceExtensions.Contains(extension))
                {
                    continue;
                }

                string content;
                try
                {
                    content = File.ReadAllText(file);
                }
                catch
                {
                    continue;
                }

                var relativePath = Path.GetRelativePath(projectPath, file).Replace('\\', '/');
                var isDeclarationFile = relativePath.EndsWith(".d.ts", StringComparison.OrdinalIgnoreCase);

                sourceFiles.Add(new SourceFile(
                    StandardizePath(file),
                    relativePath,
                    content,
                    isDeclarationFile ? string.Empty : StripComments(content),
                    content.Split('\n').ToList(),
                    isDeclarationFile));
            }

            return sourceFiles;
        }

        private string CollectSearchableProjectContent(string projectPath)
        {
            var contents = new List<string>();

            foreach (var file in EnumerateProjectFiles(projectPath))
            {
                var extension = Path.GetExtension(file);
                if (!ReferenceExtensions.Contains(extension))
                {
                    continue;
                }

                var relativePath = Path.GetRelativePath(projectPath, file).Replace('\\', '/');
                if (relativePath.EndsWith(".d.ts", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                try
                {
                    contents.Add(StripComments(File.ReadAllText(file)));
                }
                catch
                {
                }
            }

            return string.Join("\n", contents);
        }

        private IEnumerable<string> EnumerateProjectFiles(string projectPath)
        {
            var stack = new Stack<string>();
            stack.Push(projectPath);

            while (stack.Count > 0)
            {
                var directory = stack.Pop();

                string[] files;
                try
                {
                    files = Directory.GetFiles(directory);
                }
                catch
                {
                    continue;
                }

                foreach (var file in files)
                {
                    var relativePath = Path.GetRelativePath(projectPath, file).Replace('\\', '/');
                    if (!ShouldIgnore(relativePath))
                    {
                        yield return file;
                    }
                }

                string[] subDirectories;
                try
                {
                    subDirectories = Directory.GetDirectories(directory);
                }
                catch
                {
                    continue;
                }

                foreach (var subDirectory in subDirectories)
                {
                    var directoryName = Path.GetFileName(subDirectory);
                    if (directoryName.StartsWith(".", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    var relativePath = Path.GetRelativePath(projectPath, subDirectory).Replace('\\', '/') + "/";
                    if (!ShouldIgnore(relativePath))
                    {
                        stack.Push(subDirectory);
                    }
                }
            }
        }

        private List<UnusedCodeResult> RunTypeScriptCompilerAnalysis(string projectPath, IReadOnlyList<SourceFile> sourceFiles)
        {
            var compiler = ResolveTypeScriptCompiler(projectPath);
            if (compiler is null)
            {
                return new List<UnusedCodeResult>();
            }

            var arguments = new List<string>(compiler.Arguments)
            {
                "--pretty",
                "false",
                "--noEmit",
                "--noUnusedLocals",
                "--noUnusedParameters"
            };

            string? temporaryConfigPath = null;

            try
            {
                var existingConfig = ExistingTypeScriptConfigName(projectPath);
                if (!string.IsNullOrEmpty(existingConfig))
                {
                    arguments.Add("--project");
                    arguments.Add(existingConfig);
                }
                else
                {
                    temporaryConfigPath = CreateTemporaryTypeScriptConfig(sourceFiles);
                    arguments.Add("--project");
                    arguments.Add(temporaryConfigPath);
                }

                var startInfo = new ProcessStartInfo
                {
                    FileName = compiler.Path,
                    WorkingDirectory = projectPath,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                foreach (var argument in arguments)
                {
                    startInfo.ArgumentList.Add(argument);
                }

                using var process = Process.Start(startInfo);
                if (process is null)
                {
                    return new List<UnusedCodeResult>();
                }

                var stdout = process.StandardOutput.ReadToEnd();
                var stderr = process.StandardError.ReadToEnd();
                process.WaitForExit();

                var output = string.Join("\n", new[] { stdout, stderr }.Where(value => !string.IsNullOrWhiteSpace(value)));
                if (string.IsNullOrWhiteSpace(output))
                {
                    return new List<UnusedCodeResult>();
                }

                return ParseTypeScriptDiagnostics(output, projectPath, sourceFiles);
            }
            catch
            {
                return new List<UnusedCodeResult>();
            }
            finally
            {
                if (!string.IsNullOrEmpty(temporaryConfigPath))
                {
                    try
                    {
                        File.Delete(temporaryConfigPath);
                    }
                    catch
                    {
                    }
                }
            }
        }

        private Executable? ResolveTypeScriptCompiler(string projectPath)
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                var localCommandPath = Path.Combine(projectPath, "node_modules", ".bin", "tsc.cmd");
                if (File.Exists(localCommandPath))
                {
                    return new Executable("cmd.exe", new[] { "/c", localCommandPath });
                }
            }
            else
            {
                var localCommandPath = Path.Combine(projectPath, "node_modules", ".bin", "tsc");
                if (File.Exists(localCommandPath))
                {
                    return new Executable(localCommandPath, Array.Empty<string>());
                }
            }

            var localScriptPath = Path.Combine(projectPath, "node_modules", "typescript", "bin", "tsc");
            if (File.Exists(localScriptPath))
            {
                var nodePath = ResolveExecutableOnPath("node");
                if (!string.IsNullOrEmpty(nodePath))
                {
                    return new Executable(nodePath, new[] { localScriptPath });
                }
            }

            foreach (var candidate in GetTypeScriptCompilerCandidates())
            {
                if (File.Exists(candidate))
                {
                    return WrapExecutable(candidate);
                }
            }

            var shellPath = ResolveExecutableOnPath("tsc");
            return string.IsNullOrEmpty(shellPath) ? null : WrapExecutable(shellPath);
        }

        private IEnumerable<string> GetTypeScriptCompilerCandidates()
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                yield return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "nodejs", "tsc.cmd");
                yield return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "nodejs", "tsc.cmd");
            }
            else
            {
                yield return "/opt/homebrew/bin/tsc";
                yield return "/usr/local/bin/tsc";
            }
        }

        private Executable WrapExecutable(string path)
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                && string.Equals(Path.GetExtension(path), ".cmd", StringComparison.OrdinalIgnoreCase))
            {
                return new Executable("cmd.exe", new[] { "/c", path });
            }

            return new Executable(path, Array.Empty<string>());
        }

        private string? ResolveExecutableOnPath(string command)
        {
            var locator = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "where.exe" : "which";

            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = locator,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                startInfo.ArgumentList.Add(command);

                using var process = Process.Start(startInfo);
                if (process is null)
                {
                    return null;
                }

                var output = process.StandardOutput.ReadToEnd();
                process.WaitForExit();

                if (process.ExitCode != 0)
                {
                    return null;
                }

                return output
                    .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                    .FirstOrDefault()?.Trim();
            }
            catch
            {
                return null;
            }
        }

        private string? ExistingTypeScriptConfigName(string projectPath)
        {
            foreach (var candidate in new[] { "tsconfig.json", "jsconfig.json" })
            {
                if (File.Exists(Path.Combine(projectPath, candidate)))
                {
                    return candidate;
                }
            }

            return null;
        }

        private string CreateTemporaryTypeScriptConfig(IReadOnlyList<SourceFile> sourceFiles)
        {
            var config = new
            {
                compilerOptions = new
                {
                    allowJs = true,
                    checkJs = true,
                    jsx = "react-jsx",
                    module = "esnext",
                    moduleResolution = "node",
                    target = "esnext",
                    skipLibCheck = true
                },
                files = sourceFiles.Select(sourceFile => sourceFile.Path).ToArray()
            };

            var configJson = JsonSerializer.Serialize(config, new JsonSerializerOptions
            {
                WriteIndented = true
            });

            var configPath = Path.Combine(Path.GetTempPath(), $"devatlas-unused-{Guid.NewGuid():N}.json");
            File.WriteAllText(configPath, configJson);

            return configPath;
        }

        private List<UnusedCodeResult> ParseTypeScriptDiagnostics(
            string output,
            string projectPath,
            IReadOnlyList<SourceFile> sourceFiles)
        {
            var sourceFileLookup = sourceFiles.ToDictionary(sourceFile => sourceFile.Path, StringComparer.OrdinalIgnoreCase);
            var results = new List<UnusedCodeResult>();

            foreach (var rawLine in output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var line = rawLine.Trim();
                if (line.Length == 0)
                {
                    continue;
                }

                TypeScriptDiagnostic? diagnostic = null;
                foreach (var regex in DiagnosticFormats)
                {
                    diagnostic = ParseDiagnosticLine(line, regex, projectPath);
                    if (diagnostic is not null)
                    {
                        break;
                    }
                }

                if (diagnostic is null || !SupportedDiagnosticCodes.Contains(diagnostic.Code))
                {
                    continue;
                }

                if (!sourceFileLookup.TryGetValue(diagnostic.FilePath, out var sourceFile) || sourceFile.IsDeclarationFile)
                {
                    continue;
                }

                var sourceLine = diagnostic.Line > 0 && diagnostic.Line <= sourceFile.Lines.Count
                    ? sourceFile.Lines[diagnostic.Line - 1]
                    : string.Empty;

                var name = ExtractQuotedName(diagnostic.Message) ?? GuessDeclarationName(sourceLine);
                if (string.IsNullOrEmpty(name) || !ShouldTrackDeclaration(name))
                {
                    continue;
                }

                var kind = InferKind(name, sourceLine);
                results.Add(new UnusedCodeResult
                {
                    Kind = kind,
                    Name = name,
                    Location = $"{sourceFile.RelativePath}:{diagnostic.Line}",
                    Hints = new List<string> { CompilerHintFor(kind) }
                });
            }

            return Deduplicate(results);
        }

        private TypeScriptDiagnostic? ParseDiagnosticLine(string line, Regex regex, string projectPath)
        {
            var match = regex.Match(line);
            if (!match.Success || match.Groups.Count < 6)
            {
                return null;
            }

            var filePath = match.Groups[1].Value;
            if (!int.TryParse(match.Groups[2].Value, out var lineNumber)
                || !int.TryParse(match.Groups[3].Value, out var columnNumber)
                || lineNumber <= 0
                || columnNumber <= 0)
            {
                return null;
            }

            var code = match.Groups[4].Value;
            var message = match.Groups[5].Value;
            var resolvedPath = Path.IsPathRooted(filePath)
                ? StandardizePath(filePath)
                : StandardizePath(Path.Combine(projectPath, filePath));

            return new TypeScriptDiagnostic(resolvedPath, lineNumber, code, message);
        }

        private HashSet<string> CollectExportedNames(string content)
        {
            var exportedNames = new HashSet<string>(StringComparer.Ordinal);

            foreach (var regex in ExportListRegexes)
            {
                foreach (Match match in regex.Matches(content))
                {
                    if (!match.Success || match.Groups.Count < 2)
                    {
                        continue;
                    }

                    foreach (var name in ParseExportList(match.Groups[1].Value))
                    {
                        exportedNames.Add(name);
                    }
                }
            }

            foreach (var regex in DirectExportRegexes)
            {
                foreach (Match match in regex.Matches(content))
                {
                    if (match.Success && match.Groups.Count > 1)
                    {
                        exportedNames.Add(match.Groups[1].Value);
                    }
                }
            }

            return exportedNames;
        }

        private IEnumerable<string> ParseExportList(string rawList)
        {
            foreach (var rawItem in rawList.Split(','))
            {
                var item = rawItem.Trim();
                if (string.IsNullOrEmpty(item))
                {
                    continue;
                }

                if (item.StartsWith("type ", StringComparison.Ordinal))
                {
                    item = item[5..].Trim();
                }

                var aliasIndex = item.IndexOf(" as ", StringComparison.Ordinal);
                if (aliasIndex >= 0)
                {
                    item = item[..aliasIndex];
                }

                var colonIndex = item.LastIndexOf(':');
                if (colonIndex >= 0 && colonIndex + 1 < item.Length)
                {
                    item = item[(colonIndex + 1)..].Trim();
                }

                if (ShouldTrackDeclaration(item))
                {
                    yield return item;
                }
            }
        }

        private bool ShouldIgnore(string relativePath)
        {
            var normalized = relativePath.Replace('\\', '/');
            if (normalized.EndsWith(".min.js", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return IgnoredDirectories.Any(ignoredDirectory => normalized.Contains(ignoredDirectory, StringComparison.OrdinalIgnoreCase));
        }

        private bool ShouldAnalyze(string line)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                return false;
            }

            if (line.StartsWith("//", StringComparison.Ordinal)
                || line.StartsWith("/*", StringComparison.Ordinal)
                || line.StartsWith("*", StringComparison.Ordinal))
            {
                return false;
            }

            if (line.StartsWith("import ", StringComparison.Ordinal))
            {
                return false;
            }

            if (line.StartsWith("declare ", StringComparison.Ordinal)
                || line.StartsWith("declare global", StringComparison.Ordinal)
                || line.StartsWith("declare module", StringComparison.Ordinal))
            {
                return false;
            }

            return true;
        }

        private bool ShouldTrackDeclaration(string name)
        {
            return !name.StartsWith("_", StringComparison.Ordinal) && !IgnoredSymbolNames.Contains(name);
        }

        private bool LooksLikeFunctionOrClassAssignment(string line)
        {
            return line.Contains("=>", StringComparison.Ordinal)
                || line.Contains("= function", StringComparison.Ordinal)
                || line.Contains("=class", StringComparison.Ordinal)
                || line.Contains("= class", StringComparison.Ordinal);
        }

        private bool IsExported(string name, string line, HashSet<string> exportedNames)
        {
            return line.Contains("export ", StringComparison.Ordinal) || exportedNames.Contains(name);
        }

        private string HintFor(string kind, bool exported)
        {
            if (exported)
            {
                return $"Exported {kind} appears unreferenced across project";
            }

            return $"{Capitalize(kind)} appears unused";
        }

        private string CompilerHintFor(string kind)
        {
            return kind switch
            {
                "parameter" => "Compiler reported an unused parameter",
                "import" => "Compiler reported an unused import",
                _ => $"Compiler reported an unused {kind}"
            };
        }

        private string InferKind(string name, string sourceLine)
        {
            var trimmed = sourceLine.Trim();

            if (trimmed.Contains("import ", StringComparison.Ordinal))
            {
                return "import";
            }

            if (IsParameter(name, trimmed))
            {
                return "parameter";
            }

            if (trimmed.Contains($"interface {name}", StringComparison.Ordinal))
            {
                return "interface";
            }

            if (trimmed.Contains($"type {name}", StringComparison.Ordinal))
            {
                return "typealias";
            }

            if (trimmed.Contains($"enum {name}", StringComparison.Ordinal))
            {
                return "enum";
            }

            if (trimmed.Contains($"class {name}", StringComparison.Ordinal))
            {
                return "class";
            }

            if (trimmed.Contains($"function {name}", StringComparison.Ordinal) || trimmed.Contains("=>", StringComparison.Ordinal))
            {
                return "function";
            }

            return "variable";
        }

        private bool IsParameter(string name, string sourceLine)
        {
            if (!sourceLine.Contains('(') || !sourceLine.Contains(')'))
            {
                return false;
            }

            var patterns = new[]
            {
                $@"\(\s*{Regex.Escape(name)}\s*[:,=\)]",
                $@",\s*{Regex.Escape(name)}\s*[:,=\)]"
            };

            foreach (var pattern in patterns)
            {
                try
                {
                    if (Regex.IsMatch(sourceLine, pattern))
                    {
                        return true;
                    }
                }
                catch
                {
                }
            }

            return false;
        }

        private string? FirstCapture(string line, Regex regex)
        {
            var match = regex.Match(line);
            return match.Success && match.Groups.Count > 1 ? match.Groups[1].Value : null;
        }

        private string? ExtractQuotedName(string message)
        {
            var match = Regex.Match(message, "'([^']+)'");
            return match.Success && match.Groups.Count > 1 ? match.Groups[1].Value : null;
        }

        private string? GuessDeclarationName(string sourceLine)
        {
            var match = Regex.Match(sourceLine, @"(?:const|let|var|function|class|interface|type|enum)\s+([a-zA-Z_$][a-zA-Z0-9_$]*)");
            return match.Success && match.Groups.Count > 1 ? match.Groups[1].Value : null;
        }

        private string StandardizePath(string path)
        {
            return Path.GetFullPath(path);
        }

        private string StripComments(string content)
        {
            var result = new StringBuilder(content.Length);
            var mode = CommentStripMode.Normal;
            char? previousCharacter = null;

            for (int index = 0; index < content.Length; index++)
            {
                var character = content[index];
                var nextCharacter = index + 1 < content.Length ? content[index + 1] : (char?)null;

                switch (mode)
                {
                    case CommentStripMode.Normal:
                        if (character == '/' && nextCharacter == '/')
                        {
                            mode = CommentStripMode.LineComment;
                            result.Append(' ');
                            index++;
                        }
                        else if (character == '/' && nextCharacter == '*')
                        {
                            mode = CommentStripMode.BlockComment;
                            result.Append(' ');
                            index++;
                        }
                        else
                        {
                            result.Append(character);
                            if (character == '\'')
                            {
                                mode = CommentStripMode.SingleQuote;
                            }
                            else if (character == '"')
                            {
                                mode = CommentStripMode.DoubleQuote;
                            }
                            else if (character == '`')
                            {
                                mode = CommentStripMode.TemplateString;
                            }
                        }
                        break;

                    case CommentStripMode.LineComment:
                        if (character == '\n')
                        {
                            mode = CommentStripMode.Normal;
                            result.Append('\n');
                        }
                        else
                        {
                            result.Append(' ');
                        }
                        break;

                    case CommentStripMode.BlockComment:
                        if (character == '*' && nextCharacter == '/')
                        {
                            mode = CommentStripMode.Normal;
                            result.Append(' ');
                            index++;
                        }
                        else if (character == '\n')
                        {
                            result.Append('\n');
                        }
                        else
                        {
                            result.Append(' ');
                        }
                        break;

                    case CommentStripMode.SingleQuote:
                        result.Append(character);
                        if (character == '\'' && previousCharacter != '\\')
                        {
                            mode = CommentStripMode.Normal;
                        }
                        break;

                    case CommentStripMode.DoubleQuote:
                        result.Append(character);
                        if (character == '"' && previousCharacter != '\\')
                        {
                            mode = CommentStripMode.Normal;
                        }
                        break;

                    case CommentStripMode.TemplateString:
                        result.Append(character);
                        if (character == '`' && previousCharacter != '\\')
                        {
                            mode = CommentStripMode.Normal;
                        }
                        break;
                }

                previousCharacter = character;
            }

            return result.ToString();
        }

        private List<UnusedCodeResult> Deduplicate(IEnumerable<UnusedCodeResult> results)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var deduplicated = new List<UnusedCodeResult>();

            foreach (var result in results)
            {
                var key = $"{result.Location}|{result.Name}";
                if (seen.Add(key))
                {
                    deduplicated.Add(result);
                }
            }

            return deduplicated
                .OrderBy(result => result.Location, StringComparer.Ordinal)
                .ThenBy(result => result.Name, StringComparer.Ordinal)
                .ToList();
        }

        private string Capitalize(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return value;
            }

            return value.Length == 1
                ? value.ToUpperInvariant()
                : char.ToUpperInvariant(value[0]) + value[1..];
        }

        private sealed record SourceFile(
            string Path,
            string RelativePath,
            string Content,
            string SearchableContent,
            List<string> Lines,
            bool IsDeclarationFile);

        private sealed record Declaration(
            string Kind,
            string Name,
            string Location,
            List<string> Hints,
            SearchScope SearchScope);

        private sealed record Executable(string Path, IReadOnlyList<string> Arguments);

        private sealed record TypeScriptDiagnostic(string FilePath, int Line, string Code, string Message);

        private enum SearchScope
        {
            File,
            Project
        }

        private enum CommentStripMode
        {
            Normal,
            LineComment,
            BlockComment,
            SingleQuote,
            DoubleQuote,
            TemplateString
        }
    }
}
