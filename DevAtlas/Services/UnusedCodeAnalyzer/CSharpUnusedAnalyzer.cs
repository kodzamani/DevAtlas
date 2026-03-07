using DevAtlas.Models;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Text;

namespace DevAtlas.Services.UnusedCodeAnalyzer
{
    /// <summary>
    /// Roslyn-based unused code analyzer for C# projects.
    /// Keeps the existing framework-aware heuristics for WPF, WinForms and ASP.NET-style projects.
    /// </summary>
    public class CSharpUnusedAnalyzer : ILanguageAnalyzer
    {
        private static readonly HashSet<string> ReferenceExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".cs", ".xaml", ".axaml", ".razor", ".cshtml", ".aspx", ".ascx", ".master",
            ".resx", ".config", ".json", ".xml"
        };

        private static readonly string[] LogicalStemSuffixes =
        {
            ".xaml.cs", ".axaml.cs", ".razor.cs", ".cshtml.cs", ".aspx.cs", ".ascx.cs", ".master.cs",
            ".Designer.cs", ".designer.cs", ".g.i.cs", ".g.cs", ".generated.cs",
            ".xaml", ".axaml", ".razor", ".cshtml", ".aspx", ".ascx", ".master", ".resx",
            ".cs"
        };

        private static readonly string[] GeneratedCSharpSuffixes =
        {
            ".Designer.cs", ".designer.cs", ".g.cs", ".g.i.cs", ".generated.cs", ".AssemblyAttributes.cs"
        };

        private static readonly string[] FrameworkTypeSuffixHints =
        {
            "Controller", "PageModel", "Hub", "HostedService", "BackgroundService"
        };

        private static readonly HashSet<string> FrameworkTypeNames = new(StringComparer.Ordinal)
        {
            "ControllerBase", "Controller", "PageModel", "Hub", "Window", "UserControl",
            "Page", "Application", "Form", "DbContext", "BackgroundService", "IHostedService"
        };

        private static readonly string[] FrameworkAttributeSkipHints =
        {
            "ApiController", "ApiControllerAttribute", "Route", "RouteAttribute"
        };

        private static readonly string[] MethodAttributeSkipHints =
        {
            "GeneratedRegex", "GeneratedRegexAttribute",
            "LibraryImport", "LibraryImportAttribute",
            "DllImport", "DllImportAttribute",
            "JSImport", "JSImportAttribute",
            "JSExport", "JSExportAttribute",
            "JSInvokable", "JSInvokableAttribute",
            "UnmanagedCallersOnly", "UnmanagedCallersOnlyAttribute",
            "OnDeserialized", "OnDeserializedAttribute",
            "OnDeserializing", "OnDeserializingAttribute",
            "OnSerialized", "OnSerializedAttribute",
            "OnSerializing", "OnSerializingAttribute",
            "ModuleInitializer", "ModuleInitializerAttribute"
        };

        private static readonly HashSet<string> SkipTypes = new(StringComparer.Ordinal)
        {
            "String", "Int32", "Int64", "Boolean", "Object", "List", "Dictionary",
            "Task", "ValueTask", "Action", "Func", "EventArgs", "Exception", "DateTime",
            "IEnumerable", "IList", "ICollection", "IDictionary", "Program", "App",
            "MainWindow", "AssemblyInfo"
        };

        private sealed record SymbolCandidate(
            ISymbol Symbol,
            string FilePath,
            int Line,
            string ShortFile,
            string Kind,
            string Hint,
            bool RequiresMarkupSearch = false);

        public string[] SupportedExtensions => new[] { ".cs" };
        public string LanguageName => "C#";

        public List<UnusedCodeResult> Analyze(string projectPath)
        {
            var analyzableFiles = GetAnalyzableCSharpFiles(projectPath);
            if (analyzableFiles.Count == 0)
            {
                return new List<UnusedCodeResult>();
            }

            var referenceFiles = GetReferenceFiles(projectPath);
            var companionLookup = BuildCompanionLookup(referenceFiles);
            var markupContents = LoadNonCSharpReferenceContents(referenceFiles);
            var syntaxTrees = ParseSyntaxTrees(analyzableFiles);
            if (syntaxTrees.Count == 0)
            {
                return new List<UnusedCodeResult>();
            }

            var compilation = CreateCompilation(syntaxTrees);
            var semanticModels = syntaxTrees
                .Select(tree => compilation.GetSemanticModel(tree, ignoreAccessibility: true))
                .ToList();

            var referenceCounts = CountSymbolReferences(semanticModels);
            var candidates = CollectCandidates(semanticModels, companionLookup);
            var results = new List<UnusedCodeResult>();

            foreach (var candidate in candidates)
            {
                if (referenceCounts.TryGetValue(candidate.Symbol, out var count) && count > 0)
                {
                    continue;
                }

                if (candidate.RequiresMarkupSearch)
                {
                    var relatedMarkupContent = GetRelatedContent(candidate.FilePath, markupContents, companionLookup);
                    if (ContainsSymbolReference(relatedMarkupContent, candidate.Symbol.Name))
                    {
                        continue;
                    }
                }

                results.Add(new UnusedCodeResult
                {
                    Kind = candidate.Kind,
                    Name = candidate.Symbol.Name,
                    Location = $"{candidate.ShortFile}:{candidate.Line}",
                    Hints = new List<string> { candidate.Hint }
                });
            }

            return results;
        }

        private static List<SyntaxTree> ParseSyntaxTrees(IEnumerable<string> analyzableFiles)
        {
            var parseOptions = new CSharpParseOptions(LanguageVersion.Preview);
            var trees = new List<SyntaxTree>();

            foreach (var file in analyzableFiles)
            {
                try
                {
                    var source = File.ReadAllText(file);
                    trees.Add(CSharpSyntaxTree.ParseText(source, parseOptions, file));
                }
                catch
                {
                }
            }

            return trees;
        }

        private static CSharpCompilation CreateCompilation(IEnumerable<SyntaxTree> syntaxTrees)
        {
            return CSharpCompilation.Create(
                assemblyName: "DevAtlas.UnusedCodeAnalysis",
                syntaxTrees: syntaxTrees,
                references: GetMetadataReferences(),
                options: new CSharpCompilationOptions(
                    OutputKind.DynamicallyLinkedLibrary,
                    nullableContextOptions: NullableContextOptions.Enable));
        }

        private static IReadOnlyList<MetadataReference> GetMetadataReferences()
        {
            var trustedPlatformAssemblies = AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string;
            if (string.IsNullOrWhiteSpace(trustedPlatformAssemblies))
            {
                return new[]
                {
                    MetadataReference.CreateFromFile(typeof(object).Assembly.Location)
                };
            }

            return trustedPlatformAssemblies
                .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Select(path => MetadataReference.CreateFromFile(path))
                .ToList();
        }

        private static Dictionary<ISymbol, int> CountSymbolReferences(IEnumerable<SemanticModel> semanticModels)
        {
            var counts = new Dictionary<ISymbol, int>(SymbolEqualityComparer.Default);

            foreach (var semanticModel in semanticModels)
            {
                var root = semanticModel.SyntaxTree.GetRoot();
                foreach (var simpleName in root.DescendantNodes().OfType<SimpleNameSyntax>())
                {
                    foreach (var symbol in ResolveUsageSymbols(semanticModel, simpleName))
                    {
                        counts[symbol] = counts.GetValueOrDefault(symbol) + 1;
                    }
                }
            }

            return counts;
        }

        private static IEnumerable<ISymbol> ResolveUsageSymbols(SemanticModel semanticModel, SimpleNameSyntax simpleName)
        {
            var symbols = new HashSet<ISymbol>(SymbolEqualityComparer.Default);

            foreach (var symbol in ResolveReferencedSymbols(semanticModel, simpleName))
            {
                symbols.Add(symbol);
                AddContainingStaticTypeUsage(symbols, semanticModel, simpleName, symbol);
            }

            return symbols;
        }

        private static IEnumerable<ISymbol> ResolveReferencedSymbols(SemanticModel semanticModel, SimpleNameSyntax simpleName)
        {
            var symbols = new HashSet<ISymbol>(SymbolEqualityComparer.Default);
            var symbolInfo = semanticModel.GetSymbolInfo(simpleName);

            AddNormalizedSymbol(symbols, symbolInfo.Symbol);

            foreach (var candidate in symbolInfo.CandidateSymbols)
            {
                AddNormalizedSymbol(symbols, candidate);
            }

            if (symbols.Count == 0)
            {
                AddNormalizedSymbol(symbols, semanticModel.GetTypeInfo(simpleName).Type);
            }

            return symbols;
        }

        private static void AddContainingStaticTypeUsage(
            HashSet<ISymbol> symbols,
            SemanticModel semanticModel,
            SimpleNameSyntax simpleName,
            ISymbol symbol)
        {
            if (symbol is INamedTypeSymbol ||
                symbol.ContainingType is not INamedTypeSymbol containingType ||
                !containingType.IsStatic ||
                IsReferenceWithinType(semanticModel, simpleName, containingType))
            {
                return;
            }

            AddNormalizedSymbol(symbols, containingType);
        }

        private static bool IsReferenceWithinType(
            SemanticModel semanticModel,
            SyntaxNode referenceNode,
            INamedTypeSymbol typeSymbol)
        {
            for (var current = semanticModel.GetEnclosingSymbol(referenceNode.SpanStart)?.ContainingType;
                 current is not null;
                 current = current.ContainingType)
            {
                if (SymbolEqualityComparer.Default.Equals(current.OriginalDefinition, typeSymbol.OriginalDefinition))
                {
                    return true;
                }
            }

            return false;
        }

        private static void AddNormalizedSymbol(HashSet<ISymbol> symbols, ISymbol? symbol)
        {
            var normalized = NormalizeSymbol(symbol);
            if (normalized is not null)
            {
                symbols.Add(normalized);
            }
        }

        private static ISymbol? NormalizeSymbol(ISymbol? symbol)
        {
            if (symbol is null)
            {
                return null;
            }

            if (symbol is IAliasSymbol aliasSymbol)
            {
                symbol = aliasSymbol.Target;
            }

            if (symbol is IMethodSymbol methodSymbol)
            {
                if (methodSymbol.ReducedFrom is not null)
                {
                    methodSymbol = methodSymbol.ReducedFrom;
                }

                if (methodSymbol.MethodKind is MethodKind.Constructor or MethodKind.StaticConstructor)
                {
                    return methodSymbol.ContainingType.OriginalDefinition;
                }

                return methodSymbol.OriginalDefinition;
            }

            return symbol.OriginalDefinition;
        }

        private static List<SymbolCandidate> CollectCandidates(
            IEnumerable<SemanticModel> semanticModels,
            IReadOnlyDictionary<string, List<string>> companionLookup)
        {
            var candidates = new List<SymbolCandidate>();
            var seenSymbols = new HashSet<ISymbol>(SymbolEqualityComparer.Default);

            foreach (var semanticModel in semanticModels)
            {
                var root = semanticModel.SyntaxTree.GetRoot();
                var filePath = semanticModel.SyntaxTree.FilePath;
                var shortFile = Path.GetFileName(filePath);

                foreach (var fieldDeclaration in root.DescendantNodes().OfType<FieldDeclarationSyntax>())
                {
                    if (!fieldDeclaration.Modifiers.Any(SyntaxKind.PrivateKeyword))
                    {
                        continue;
                    }

                    foreach (var variable in fieldDeclaration.Declaration.Variables)
                    {
                        if (semanticModel.GetDeclaredSymbol(variable) is not IFieldSymbol fieldSymbol ||
                            !ShouldTrackField(fieldSymbol))
                        {
                            continue;
                        }

                        AddCandidate(
                            candidates,
                            seenSymbols,
                            fieldSymbol,
                            filePath,
                            shortFile,
                            GetLineNumber(variable.Identifier),
                            "field",
                            "Private field appears unused");
                    }
                }

                foreach (var propertyDeclaration in root.DescendantNodes().OfType<PropertyDeclarationSyntax>())
                {
                    if (!propertyDeclaration.Modifiers.Any(SyntaxKind.PrivateKeyword) ||
                        semanticModel.GetDeclaredSymbol(propertyDeclaration) is not IPropertySymbol propertySymbol)
                    {
                        continue;
                    }

                    AddCandidate(
                        candidates,
                        seenSymbols,
                        propertySymbol,
                        filePath,
                        shortFile,
                        GetLineNumber(propertyDeclaration.Identifier),
                        "property",
                        "Private property appears unused");
                }

                foreach (var methodDeclaration in root.DescendantNodes().OfType<MethodDeclarationSyntax>())
                {
                    if (!methodDeclaration.Modifiers.Any(SyntaxKind.PrivateKeyword) ||
                        ShouldSkipMethodAnalysis(methodDeclaration) ||
                        semanticModel.GetDeclaredSymbol(methodDeclaration) is not IMethodSymbol methodSymbol)
                    {
                        continue;
                    }

                    AddCandidate(
                        candidates,
                        seenSymbols,
                        methodSymbol,
                        filePath,
                        shortFile,
                        GetLineNumber(methodDeclaration.Identifier),
                        "method",
                        "Private method appears unused",
                        requiresMarkupSearch: true);
                }

                foreach (var localDeclaration in root.DescendantNodes().OfType<LocalDeclarationStatementSyntax>())
                {
                    foreach (var variable in localDeclaration.Declaration.Variables)
                    {
                        if (semanticModel.GetDeclaredSymbol(variable) is not ILocalSymbol localSymbol ||
                            !ShouldTrackLocal(localSymbol))
                        {
                            continue;
                        }

                        AddCandidate(
                            candidates,
                            seenSymbols,
                            localSymbol,
                            filePath,
                            shortFile,
                            GetLineNumber(variable.Identifier),
                            "variable",
                            "Local variable appears unused");
                    }
                }

                foreach (var classDeclaration in root.DescendantNodes().OfType<ClassDeclarationSyntax>())
                {
                    AddTypeCandidate(
                        semanticModel,
                        classDeclaration,
                        "class",
                        filePath,
                        shortFile,
                        companionLookup,
                        candidates,
                        seenSymbols);
                }

                foreach (var recordDeclaration in root.DescendantNodes().OfType<RecordDeclarationSyntax>())
                {
                    var kind = recordDeclaration.ClassOrStructKeyword.IsKind(SyntaxKind.StructKeyword)
                        ? "struct"
                        : "class";

                    AddTypeCandidate(
                        semanticModel,
                        recordDeclaration,
                        kind,
                        filePath,
                        shortFile,
                        companionLookup,
                        candidates,
                        seenSymbols);
                }

                foreach (var structDeclaration in root.DescendantNodes().OfType<StructDeclarationSyntax>())
                {
                    AddTypeCandidate(
                        semanticModel,
                        structDeclaration,
                        "struct",
                        filePath,
                        shortFile,
                        companionLookup,
                        candidates,
                        seenSymbols);
                }

                foreach (var enumDeclaration in root.DescendantNodes().OfType<EnumDeclarationSyntax>())
                {
                    if (semanticModel.GetDeclaredSymbol(enumDeclaration) is not INamedTypeSymbol enumSymbol ||
                        !ShouldTrackType(enumSymbol.Name))
                    {
                        continue;
                    }

                    AddCandidate(
                        candidates,
                        seenSymbols,
                        enumSymbol,
                        filePath,
                        shortFile,
                        GetLineNumber(enumDeclaration.Identifier),
                        "enum",
                        "Enum appears unused across project");
                }

                foreach (var interfaceDeclaration in root.DescendantNodes().OfType<InterfaceDeclarationSyntax>())
                {
                    if (semanticModel.GetDeclaredSymbol(interfaceDeclaration) is not INamedTypeSymbol interfaceSymbol ||
                        !ShouldTrackType(interfaceSymbol.Name))
                    {
                        continue;
                    }

                    AddCandidate(
                        candidates,
                        seenSymbols,
                        interfaceSymbol,
                        filePath,
                        shortFile,
                        GetLineNumber(interfaceDeclaration.Identifier),
                        "interface",
                        "Interface appears unused across project");
                }
            }

            return candidates;
        }

        private static void AddTypeCandidate(
            SemanticModel semanticModel,
            TypeDeclarationSyntax typeDeclaration,
            string kind,
            string filePath,
            string shortFile,
            IReadOnlyDictionary<string, List<string>> companionLookup,
            List<SymbolCandidate> candidates,
            HashSet<ISymbol> seenSymbols)
        {
            if (semanticModel.GetDeclaredSymbol(typeDeclaration) is not INamedTypeSymbol typeSymbol ||
                !ShouldTrackType(typeSymbol.Name))
            {
                return;
            }

            if (kind == "class" && ShouldSkipTypeAnalysis(filePath, typeDeclaration, typeSymbol, companionLookup))
            {
                return;
            }

            var hint = typeSymbol.DeclaredAccessibility == Microsoft.CodeAnalysis.Accessibility.Private
                ? "Private class appears unused"
                : kind == "struct"
                    ? "Struct appears unused across project"
                    : "Class appears unused across project";

            AddCandidate(
                candidates,
                seenSymbols,
                typeSymbol,
                filePath,
                shortFile,
                GetLineNumber(typeDeclaration.Identifier),
                kind,
                hint);
        }

        private static void AddCandidate(
            List<SymbolCandidate> candidates,
            HashSet<ISymbol> seenSymbols,
            ISymbol symbol,
            string filePath,
            string shortFile,
            int line,
            string kind,
            string hint,
            bool requiresMarkupSearch = false)
        {
            var normalizedSymbol = NormalizeSymbol(symbol);
            if (normalizedSymbol is null || !seenSymbols.Add(normalizedSymbol))
            {
                return;
            }

            candidates.Add(new SymbolCandidate(
                normalizedSymbol,
                filePath,
                line,
                shortFile,
                kind,
                hint,
                requiresMarkupSearch));
        }

        private static bool ShouldTrackField(IFieldSymbol fieldSymbol)
        {
            return !fieldSymbol.IsConst &&
                   !fieldSymbol.Name.StartsWith("_", StringComparison.Ordinal) &&
                   fieldSymbol.Name.Length > 1;
        }

        private static bool ShouldTrackLocal(ILocalSymbol localSymbol)
        {
            return !string.IsNullOrWhiteSpace(localSymbol.Name) &&
                   localSymbol.Name != "_";
        }

        private static bool ShouldTrackType(string name)
        {
            return !SkipTypes.Contains(name) &&
                   !name.StartsWith("_", StringComparison.Ordinal);
        }

        private static bool ShouldSkipMethodAnalysis(MethodDeclarationSyntax methodDeclaration)
        {
            return methodDeclaration.Modifiers.Any(SyntaxKind.ExternKeyword) ||
                   methodDeclaration.Modifiers.Any(SyntaxKind.PartialKeyword) ||
                   HasAnyAttribute(methodDeclaration, MethodAttributeSkipHints);
        }

        private static bool ShouldSkipTypeAnalysis(
            string filePath,
            TypeDeclarationSyntax typeDeclaration,
            INamedTypeSymbol typeSymbol,
            IReadOnlyDictionary<string, List<string>> companionLookup)
        {
            foreach (var suffix in FrameworkTypeSuffixHints)
            {
                if (typeSymbol.Name.EndsWith(suffix, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            if (HasAnyAttribute(typeDeclaration, FrameworkAttributeSkipHints) ||
                InheritsFrameworkType(typeSymbol) ||
                ContainsInitializeComponent(typeDeclaration))
            {
                return true;
            }

            return typeDeclaration.Modifiers.Any(SyntaxKind.PartialKeyword) &&
                   HasMarkupCompanion(filePath, companionLookup);
        }

        private static bool HasAnyAttribute(MemberDeclarationSyntax declaration, IEnumerable<string> skipHints)
        {
            var hints = skipHints.ToArray();

            foreach (var attribute in declaration.AttributeLists.SelectMany(list => list.Attributes))
            {
                var attributeName = attribute.Name.ToString();
                foreach (var hint in hints)
                {
                    if (attributeName.Contains(hint, StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private static bool InheritsFrameworkType(INamedTypeSymbol typeSymbol)
        {
            for (var current = typeSymbol.BaseType; current is not null; current = current.BaseType)
            {
                if (FrameworkTypeNames.Contains(current.Name))
                {
                    return true;
                }
            }

            return typeSymbol.AllInterfaces.Any(interfaceSymbol => FrameworkTypeNames.Contains(interfaceSymbol.Name));
        }

        private static bool ContainsInitializeComponent(TypeDeclarationSyntax typeDeclaration)
        {
            foreach (var invocation in typeDeclaration.DescendantNodes().OfType<InvocationExpressionSyntax>())
            {
                var invokedName = invocation.Expression switch
                {
                    IdentifierNameSyntax identifierName => identifierName.Identifier.ValueText,
                    MemberAccessExpressionSyntax memberAccess => memberAccess.Name.Identifier.ValueText,
                    _ => string.Empty
                };

                if (string.Equals(invokedName, "InitializeComponent", StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        private static int GetLineNumber(SyntaxToken token)
        {
            return token.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
        }

        private static Dictionary<string, string> LoadNonCSharpReferenceContents(IEnumerable<string> referenceFiles)
        {
            var contents = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (var file in referenceFiles)
            {
                if (IsCSharpFile(file))
                {
                    continue;
                }

                try
                {
                    contents[file] = File.ReadAllText(file);
                }
                catch
                {
                }
            }

            return contents;
        }

        private static bool ContainsSymbolReference(string content, string symbolName)
        {
            if (string.IsNullOrWhiteSpace(content) || string.IsNullOrWhiteSpace(symbolName))
            {
                return false;
            }

            var startIndex = 0;
            while (startIndex < content.Length)
            {
                var matchIndex = content.IndexOf(symbolName, startIndex, StringComparison.Ordinal);
                if (matchIndex < 0)
                {
                    return false;
                }

                var beforeIndex = matchIndex - 1;
                var afterIndex = matchIndex + symbolName.Length;
                var isStartBoundary = beforeIndex < 0 || !IsIdentifierCharacter(content[beforeIndex]);
                var isEndBoundary = afterIndex >= content.Length || !IsIdentifierCharacter(content[afterIndex]);

                if (isStartBoundary && isEndBoundary)
                {
                    return true;
                }

                startIndex = matchIndex + symbolName.Length;
            }

            return false;
        }

        private static bool IsIdentifierCharacter(char value)
        {
            return char.IsLetterOrDigit(value) || value == '_';
        }

        private static Dictionary<string, List<string>> BuildCompanionLookup(IEnumerable<string> files)
        {
            var lookup = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

            foreach (var file in files)
            {
                var key = GetCompanionKey(file);
                if (!lookup.TryGetValue(key, out var entries))
                {
                    entries = new List<string>();
                    lookup[key] = entries;
                }

                entries.Add(file);
            }

            return lookup;
        }

        private static string GetRelatedContent(
            string file,
            IReadOnlyDictionary<string, string> contents,
            IReadOnlyDictionary<string, List<string>> companionLookup)
        {
            var key = GetCompanionKey(file);
            if (!companionLookup.TryGetValue(key, out var relatedFiles))
            {
                return contents.TryGetValue(file, out var currentContent) ? currentContent : string.Empty;
            }

            var builder = new StringBuilder();
            foreach (var relatedFile in relatedFiles)
            {
                if (contents.TryGetValue(relatedFile, out var content))
                {
                    builder.AppendLine(content);
                }
            }

            return builder.ToString();
        }

        private static bool HasMarkupCompanion(
            string file,
            IReadOnlyDictionary<string, List<string>> companionLookup)
        {
            var key = GetCompanionKey(file);
            return companionLookup.TryGetValue(key, out var relatedFiles) &&
                   relatedFiles.Any(IsMarkupFile);
        }

        private static string GetCompanionKey(string file)
        {
            var directory = Path.GetDirectoryName(file) ?? string.Empty;
            var stem = GetLogicalStem(file);
            return $"{directory}|{stem}";
        }

        private static string GetLogicalStem(string file)
        {
            var fileName = Path.GetFileName(file);
            foreach (var suffix in LogicalStemSuffixes)
            {
                if (fileName.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                {
                    return fileName[..^suffix.Length];
                }
            }

            return Path.GetFileNameWithoutExtension(fileName);
        }

        private static bool IsCSharpFile(string file)
        {
            return file.EndsWith(".cs", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsMarkupFile(string file)
        {
            var extension = Path.GetExtension(file);
            return extension.Equals(".xaml", StringComparison.OrdinalIgnoreCase) ||
                   extension.Equals(".axaml", StringComparison.OrdinalIgnoreCase) ||
                   extension.Equals(".razor", StringComparison.OrdinalIgnoreCase) ||
                   extension.Equals(".cshtml", StringComparison.OrdinalIgnoreCase) ||
                   extension.Equals(".aspx", StringComparison.OrdinalIgnoreCase) ||
                   extension.Equals(".ascx", StringComparison.OrdinalIgnoreCase) ||
                   extension.Equals(".master", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsGeneratedCSharpFile(string file)
        {
            foreach (var suffix in GeneratedCSharpSuffixes)
            {
                if (file.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private static List<string> GetAnalyzableCSharpFiles(string path)
        {
            var files = new List<string>();
            var stack = new Stack<string>();
            stack.Push(path);

            while (stack.Count > 0)
            {
                var dir = stack.Pop();
                try
                {
                    foreach (var file in Directory.GetFiles(dir, "*.cs"))
                    {
                        var relativePath = file.Replace(path, string.Empty, StringComparison.OrdinalIgnoreCase);
                        if (ShouldSkipPath(relativePath, file) || IsGeneratedCSharpFile(file))
                        {
                            continue;
                        }

                        files.Add(file);
                    }

                    foreach (var subDir in Directory.GetDirectories(dir))
                    {
                        if (!ShouldSkipDirectory(subDir))
                        {
                            stack.Push(subDir);
                        }
                    }
                }
                catch
                {
                }
            }

            return files;
        }

        private static List<string> GetReferenceFiles(string path)
        {
            var files = new List<string>();
            var stack = new Stack<string>();
            stack.Push(path);

            while (stack.Count > 0)
            {
                var dir = stack.Pop();
                try
                {
                    foreach (var file in Directory.GetFiles(dir))
                    {
                        var relativePath = file.Replace(path, string.Empty, StringComparison.OrdinalIgnoreCase);
                        if (ShouldSkipPath(relativePath, file))
                        {
                            continue;
                        }

                        if (ReferenceExtensions.Contains(Path.GetExtension(file)))
                        {
                            files.Add(file);
                        }
                    }

                    foreach (var subDir in Directory.GetDirectories(dir))
                    {
                        if (!ShouldSkipDirectory(subDir))
                        {
                            stack.Push(subDir);
                        }
                    }
                }
                catch
                {
                }
            }

            return files;
        }

        private static bool ShouldSkipPath(string relativePath, string fullPath)
        {
            return relativePath.Contains($"obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase) ||
                   relativePath.Contains($"bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase) ||
                   relativePath.Contains($"Migrations{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase) ||
                   fullPath.EndsWith("ModelSnapshot.cs", StringComparison.OrdinalIgnoreCase);
        }

        private static bool ShouldSkipDirectory(string subDir)
        {
            var dirName = Path.GetFileName(subDir);
            return dirName.StartsWith(".", StringComparison.Ordinal) ||
                   dirName.Equals("obj", StringComparison.OrdinalIgnoreCase) ||
                   dirName.Equals("bin", StringComparison.OrdinalIgnoreCase) ||
                   dirName.Equals("Migrations", StringComparison.OrdinalIgnoreCase);
        }
    }
}
