using DevAtlas.Models;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace DevAtlas.Services
{
    /// <summary>
    /// Detects project dependencies from various package manager files across all major languages.
    /// Supports: NuGet (.csproj), npm (package.json), Python (requirements.txt, pyproject.toml, Pipfile),
    /// Rust (Cargo.toml), Go (go.mod), Flutter (pubspec.yaml), Java (pom.xml, build.gradle),
    /// PHP (composer.json), Ruby (Gemfile), Swift (Package.swift).
    /// </summary>
    public class DependencyDetectorService
    {
        /// <summary>
        /// Detects all dependencies for a project at the given path.
        /// Returns a list of DependencySection (top-level groups).
        /// </summary>
        public async Task<List<DependencySection>> DetectDependenciesAsync(string projectPath, CancellationToken cancellationToken = default)
        {
            var sections = new List<DependencySection>();

            if (!Directory.Exists(projectPath))
                return sections;

            await Task.Run(() =>
            {
                // Check for .NET solution files first (.sln/.slnx)
                var slnFiles = Directory.GetFiles(projectPath, "*.sln", SearchOption.TopDirectoryOnly)
                    .Concat(Directory.GetFiles(projectPath, "*.slnx", SearchOption.TopDirectoryOnly))
                    .ToList();

                if (slnFiles.Count > 0)
                {
                    // .NET Solution - find all .csproj/.fsproj/.vbproj in subdirectories
                    var solutionName = Path.GetFileNameWithoutExtension(slnFiles[0]);
                    var section = new DependencySection { Name = solutionName, Icon = "⚙️" };

                    var csprojFiles = Directory.GetFiles(projectPath, "*.csproj", SearchOption.AllDirectories)
                        .Concat(Directory.GetFiles(projectPath, "*.fsproj", SearchOption.AllDirectories))
                        .Concat(Directory.GetFiles(projectPath, "*.vbproj", SearchOption.AllDirectories))
                        .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}") &&
                                   !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
                        .OrderBy(f => f)
                        .ToList();

                    foreach (var csprojFile in csprojFiles)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        var group = ParseCsprojDependencies(csprojFile);
                        if (group != null && group.Packages.Count > 0)
                        {
                            section.Groups.Add(group);
                        }
                    }

                    if (section.Groups.Count > 0)
                        sections.Add(section);
                }
                else
                {
                    // Single .csproj in directory
                    var csprojFiles = Directory.GetFiles(projectPath, "*.csproj", SearchOption.TopDirectoryOnly)
                        .Concat(Directory.GetFiles(projectPath, "*.fsproj", SearchOption.TopDirectoryOnly))
                        .Concat(Directory.GetFiles(projectPath, "*.vbproj", SearchOption.TopDirectoryOnly))
                        .ToList();

                    foreach (var csprojFile in csprojFiles)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        var group = ParseCsprojDependencies(csprojFile);
                        if (group != null && group.Packages.Count > 0)
                        {
                            var section = new DependencySection
                            {
                                Name = Path.GetFileNameWithoutExtension(csprojFile),
                                Icon = "⚙️"
                            };
                            section.Groups.Add(group);
                            sections.Add(section);
                        }
                    }
                }

                // Node.js (package.json)
                var packageJsonPath = Path.Combine(projectPath, "package.json");
                if (File.Exists(packageJsonPath))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var npmSection = ParsePackageJson(packageJsonPath);
                    if (npmSection != null && npmSection.Groups.Count > 0)
                        sections.Add(npmSection);
                }

                // Python (requirements.txt, pyproject.toml, Pipfile)
                var requirementsPath = Path.Combine(projectPath, "requirements.txt");
                if (File.Exists(requirementsPath))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var pySection = ParseRequirementsTxt(requirementsPath, projectPath);
                    if (pySection != null && pySection.Groups.Count > 0)
                        sections.Add(pySection);
                }

                var pyprojectPath = Path.Combine(projectPath, "pyproject.toml");
                if (File.Exists(pyprojectPath) && !File.Exists(requirementsPath))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var pySection = ParsePyprojectToml(pyprojectPath, projectPath);
                    if (pySection != null && pySection.Groups.Count > 0)
                        sections.Add(pySection);
                }

                var pipfilePath = Path.Combine(projectPath, "Pipfile");
                if (File.Exists(pipfilePath) && !File.Exists(requirementsPath) && !File.Exists(pyprojectPath))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var pySection = ParsePipfile(pipfilePath, projectPath);
                    if (pySection != null && pySection.Groups.Count > 0)
                        sections.Add(pySection);
                }

                // Rust (Cargo.toml)
                var cargoPath = Path.Combine(projectPath, "Cargo.toml");
                if (File.Exists(cargoPath))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var rustSection = ParseCargoToml(cargoPath, projectPath);
                    if (rustSection != null && rustSection.Groups.Count > 0)
                        sections.Add(rustSection);
                }

                // Go (go.mod)
                var goModPath = Path.Combine(projectPath, "go.mod");
                if (File.Exists(goModPath))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var goSection = ParseGoMod(goModPath, projectPath);
                    if (goSection != null && goSection.Groups.Count > 0)
                        sections.Add(goSection);
                }

                // Flutter/Dart (pubspec.yaml)
                var pubspecPath = Path.Combine(projectPath, "pubspec.yaml");
                if (File.Exists(pubspecPath))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var flutterSection = ParsePubspecYaml(pubspecPath, projectPath);
                    if (flutterSection != null && flutterSection.Groups.Count > 0)
                        sections.Add(flutterSection);
                }

                // Java/Maven (pom.xml)
                var pomPath = Path.Combine(projectPath, "pom.xml");
                if (File.Exists(pomPath))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var mavenSection = ParsePomXml(pomPath, projectPath);
                    if (mavenSection != null && mavenSection.Groups.Count > 0)
                        sections.Add(mavenSection);
                }

                // Java/Gradle (build.gradle or build.gradle.kts)
                var gradlePath = Path.Combine(projectPath, "build.gradle");
                var gradleKtsPath = Path.Combine(projectPath, "build.gradle.kts");
                if (File.Exists(gradlePath))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var gradleSection = ParseBuildGradle(gradlePath, projectPath);
                    if (gradleSection != null && gradleSection.Groups.Count > 0)
                        sections.Add(gradleSection);
                }
                else if (File.Exists(gradleKtsPath))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var gradleSection = ParseBuildGradle(gradleKtsPath, projectPath);
                    if (gradleSection != null && gradleSection.Groups.Count > 0)
                        sections.Add(gradleSection);
                }

                // PHP (composer.json)
                var composerPath = Path.Combine(projectPath, "composer.json");
                if (File.Exists(composerPath))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var phpSection = ParseComposerJson(composerPath, projectPath);
                    if (phpSection != null && phpSection.Groups.Count > 0)
                        sections.Add(phpSection);
                }

                // Ruby (Gemfile)
                var gemfilePath = Path.Combine(projectPath, "Gemfile");
                if (File.Exists(gemfilePath))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var rubySection = ParseGemfile(gemfilePath, projectPath);
                    if (rubySection != null && rubySection.Groups.Count > 0)
                        sections.Add(rubySection);
                }

                // Swift (Package.swift)
                var swiftPkgPath = Path.Combine(projectPath, "Package.swift");
                if (File.Exists(swiftPkgPath))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var swiftSection = ParsePackageSwift(swiftPkgPath, projectPath);
                    if (swiftSection != null && swiftSection.Groups.Count > 0)
                        sections.Add(swiftSection);
                }

            }, cancellationToken);

            return sections;
        }

        #region .NET (csproj/fsproj/vbproj)

        private DependencyGroup? ParseCsprojDependencies(string csprojPath)
        {
            try
            {
                var doc = XDocument.Load(csprojPath);
                var packages = doc.Descendants("PackageReference")
                    .Select(pr => new PackageDependency
                    {
                        Name = pr.Attribute("Include")?.Value ?? pr.Attribute("Update")?.Value ?? "",
                        Version = pr.Attribute("Version")?.Value ?? pr.Element("Version")?.Value ?? "",
                        Source = "NuGet"
                    })
                    .Where(p => !string.IsNullOrEmpty(p.Name))
                    .OrderBy(p => p.Name)
                    .ToList();

                if (packages.Count == 0)
                    return null;

                return new DependencyGroup
                {
                    Name = Path.GetFileNameWithoutExtension(csprojPath),
                    FilePath = csprojPath,
                    Packages = packages
                };
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error parsing {csprojPath}: {ex.Message}");
                return null;
            }
        }

        #endregion

        #region Node.js (package.json)

        private DependencySection? ParsePackageJson(string packageJsonPath)
        {
            try
            {
                var json = File.ReadAllText(packageJsonPath);
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                var projectName = root.TryGetProperty("name", out var nameEl)
                    ? nameEl.GetString() ?? Path.GetDirectoryName(packageJsonPath) ?? "Node.js"
                    : Path.GetFileName(Path.GetDirectoryName(packageJsonPath)) ?? "Node.js";

                var section = new DependencySection { Name = projectName, Icon = "📦" };

                // dependencies
                if (root.TryGetProperty("dependencies", out var deps))
                {
                    var packages = new List<PackageDependency>();
                    foreach (var prop in deps.EnumerateObject())
                    {
                        packages.Add(new PackageDependency
                        {
                            Name = prop.Name,
                            Version = CleanVersionPrefix(prop.Value.GetString() ?? ""),
                            Source = "npm"
                        });
                    }
                    if (packages.Count > 0)
                    {
                        section.Groups.Add(new DependencyGroup
                        {
                            Name = "dependencies",
                            FilePath = packageJsonPath,
                            Packages = packages.OrderBy(p => p.Name).ToList()
                        });
                    }
                }

                // devDependencies
                if (root.TryGetProperty("devDependencies", out var devDeps))
                {
                    var packages = new List<PackageDependency>();
                    foreach (var prop in devDeps.EnumerateObject())
                    {
                        packages.Add(new PackageDependency
                        {
                            Name = prop.Name,
                            Version = CleanVersionPrefix(prop.Value.GetString() ?? ""),
                            Source = "npm"
                        });
                    }
                    if (packages.Count > 0)
                    {
                        section.Groups.Add(new DependencyGroup
                        {
                            Name = "devDependencies",
                            FilePath = packageJsonPath,
                            Packages = packages.OrderBy(p => p.Name).ToList()
                        });
                    }
                }

                return section;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error parsing package.json: {ex.Message}");
                return null;
            }
        }

        #endregion

        #region Python (requirements.txt)

        private DependencySection? ParseRequirementsTxt(string filePath, string projectPath)
        {
            try
            {
                var lines = File.ReadAllLines(filePath);
                var packages = new List<PackageDependency>();

                foreach (var raw in lines)
                {
                    var line = raw.Trim();
                    if (string.IsNullOrEmpty(line) || line.StartsWith('#') || line.StartsWith('-'))
                        continue;

                    // Pattern: package==1.0.0 or package>=1.0.0 or package~=1.0.0 or just package
                    var match = Regex.Match(line, @"^([a-zA-Z0-9_\-\.]+)\s*([=~!<>]+)\s*(.+)$");
                    if (match.Success)
                    {
                        packages.Add(new PackageDependency
                        {
                            Name = match.Groups[1].Value,
                            Version = match.Groups[3].Value.Trim(),
                            Source = "PyPI"
                        });
                    }
                    else if (Regex.IsMatch(line, @"^[a-zA-Z0-9_\-\.]+$"))
                    {
                        packages.Add(new PackageDependency
                        {
                            Name = line,
                            Version = "*",
                            Source = "PyPI"
                        });
                    }
                }

                if (packages.Count == 0)
                    return null;

                var projectName = Path.GetFileName(projectPath) ?? "Python";
                return new DependencySection
                {
                    Name = projectName,
                    Icon = "🐍",
                    Groups = new List<DependencyGroup>
                    {
                        new DependencyGroup
                        {
                            Name = "requirements.txt",
                            FilePath = filePath,
                            Packages = packages.OrderBy(p => p.Name).ToList()
                        }
                    }
                };
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error parsing requirements.txt: {ex.Message}");
                return null;
            }
        }

        #endregion

        #region Python (pyproject.toml)

        private DependencySection? ParsePyprojectToml(string filePath, string projectPath)
        {
            try
            {
                var content = File.ReadAllText(filePath);
                var packages = new List<PackageDependency>();

                // Simple TOML parser for dependencies array
                var depsMatch = Regex.Match(content, @"dependencies\s*=\s*\[(.*?)\]", RegexOptions.Singleline);
                if (depsMatch.Success)
                {
                    var depsBlock = depsMatch.Groups[1].Value;
                    var pkgMatches = Regex.Matches(depsBlock, @"""([a-zA-Z0-9_\-\.]+)\s*([><=~!]*)\s*([^""]*?)""");
                    foreach (Match m in pkgMatches)
                    {
                        packages.Add(new PackageDependency
                        {
                            Name = m.Groups[1].Value,
                            Version = string.IsNullOrWhiteSpace(m.Groups[3].Value) ? "*" : m.Groups[3].Value.Trim(),
                            Source = "PyPI"
                        });
                    }
                }

                if (packages.Count == 0)
                    return null;

                var projectName = Path.GetFileName(projectPath) ?? "Python";
                return new DependencySection
                {
                    Name = projectName,
                    Icon = "🐍",
                    Groups = new List<DependencyGroup>
                    {
                        new DependencyGroup
                        {
                            Name = "pyproject.toml",
                            FilePath = filePath,
                            Packages = packages.OrderBy(p => p.Name).ToList()
                        }
                    }
                };
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error parsing pyproject.toml: {ex.Message}");
                return null;
            }
        }

        #endregion

        #region Python (Pipfile)

        private DependencySection? ParsePipfile(string filePath, string projectPath)
        {
            try
            {
                var content = File.ReadAllText(filePath);
                var packages = new List<PackageDependency>();

                // Parse [packages] section
                var packagesMatch = Regex.Match(content, @"\[packages\](.*?)(\[|$)", RegexOptions.Singleline);
                if (packagesMatch.Success)
                {
                    ParsePipfileSection(packagesMatch.Groups[1].Value, packages);
                }

                // Parse [dev-packages] section
                var devPackagesMatch = Regex.Match(content, @"\[dev-packages\](.*?)(\[|$)", RegexOptions.Singleline);
                if (devPackagesMatch.Success)
                {
                    ParsePipfileSection(devPackagesMatch.Groups[1].Value, packages);
                }

                if (packages.Count == 0)
                    return null;

                var projectName = Path.GetFileName(projectPath) ?? "Python";
                return new DependencySection
                {
                    Name = projectName,
                    Icon = "🐍",
                    Groups = new List<DependencyGroup>
                    {
                        new DependencyGroup
                        {
                            Name = "Pipfile",
                            FilePath = filePath,
                            Packages = packages.OrderBy(p => p.Name).ToList()
                        }
                    }
                };
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error parsing Pipfile: {ex.Message}");
                return null;
            }
        }

        private void ParsePipfileSection(string section, List<PackageDependency> packages)
        {
            var lines = section.Split('\n');
            foreach (var raw in lines)
            {
                var line = raw.Trim();
                if (string.IsNullOrEmpty(line) || line.StartsWith('#'))
                    continue;

                var match = Regex.Match(line, @"^([a-zA-Z0-9_\-\.]+)\s*=\s*""(.+?)""");
                if (match.Success)
                {
                    packages.Add(new PackageDependency
                    {
                        Name = match.Groups[1].Value,
                        Version = match.Groups[2].Value == "*" ? "*" : match.Groups[2].Value,
                        Source = "PyPI"
                    });
                }
            }
        }

        #endregion

        #region Rust (Cargo.toml)

        private DependencySection? ParseCargoToml(string filePath, string projectPath)
        {
            try
            {
                var content = File.ReadAllText(filePath);
                var packages = new List<PackageDependency>();

                // Parse [dependencies] section
                var depsMatch = Regex.Match(content, @"\[dependencies\](.*?)(\[|$)", RegexOptions.Singleline);
                if (depsMatch.Success)
                {
                    ParseCargoSection(depsMatch.Groups[1].Value, packages);
                }

                // Parse [dev-dependencies] section
                var devDepsMatch = Regex.Match(content, @"\[dev-dependencies\](.*?)(\[|$)", RegexOptions.Singleline);
                if (devDepsMatch.Success)
                {
                    ParseCargoSection(devDepsMatch.Groups[1].Value, packages);
                }

                if (packages.Count == 0)
                    return null;

                // Get project name from Cargo.toml
                var nameMatch = Regex.Match(content, @"name\s*=\s*""(.+?)""");
                var projectName = nameMatch.Success ? nameMatch.Groups[1].Value : Path.GetFileName(projectPath) ?? "Rust";

                return new DependencySection
                {
                    Name = projectName,
                    Icon = "🦀",
                    Groups = new List<DependencyGroup>
                    {
                        new DependencyGroup
                        {
                            Name = "Cargo.toml",
                            FilePath = filePath,
                            Packages = packages.OrderBy(p => p.Name).ToList()
                        }
                    }
                };
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error parsing Cargo.toml: {ex.Message}");
                return null;
            }
        }

        private void ParseCargoSection(string section, List<PackageDependency> packages)
        {
            var lines = section.Split('\n');
            foreach (var raw in lines)
            {
                var line = raw.Trim();
                if (string.IsNullOrEmpty(line) || line.StartsWith('#'))
                    continue;

                // Simple: package = "1.0"
                var simpleMatch = Regex.Match(line, @"^([a-zA-Z0-9_\-]+)\s*=\s*""(.+?)""");
                if (simpleMatch.Success)
                {
                    packages.Add(new PackageDependency
                    {
                        Name = simpleMatch.Groups[1].Value,
                        Version = simpleMatch.Groups[2].Value,
                        Source = "crates.io"
                    });
                    continue;
                }

                // Table: package = { version = "1.0", ... }
                var tableMatch = Regex.Match(line, @"^([a-zA-Z0-9_\-]+)\s*=\s*\{.*?version\s*=\s*""(.+?)""");
                if (tableMatch.Success)
                {
                    packages.Add(new PackageDependency
                    {
                        Name = tableMatch.Groups[1].Value,
                        Version = tableMatch.Groups[2].Value,
                        Source = "crates.io"
                    });
                }
            }
        }

        #endregion

        #region Go (go.mod)

        private DependencySection? ParseGoMod(string filePath, string projectPath)
        {
            try
            {
                var content = File.ReadAllText(filePath);
                var packages = new List<PackageDependency>();

                // Parse require block
                var requireMatch = Regex.Match(content, @"require\s*\((.*?)\)", RegexOptions.Singleline);
                if (requireMatch.Success)
                {
                    var lines = requireMatch.Groups[1].Value.Split('\n');
                    foreach (var raw in lines)
                    {
                        var line = raw.Trim();
                        if (string.IsNullOrEmpty(line) || line.StartsWith("//"))
                            continue;

                        // Skip indirect deps
                        if (line.Contains("// indirect"))
                            continue;

                        var match = Regex.Match(line, @"^(\S+)\s+(v[\d\.]+.*)$");
                        if (match.Success)
                        {
                            packages.Add(new PackageDependency
                            {
                                Name = match.Groups[1].Value,
                                Version = match.Groups[2].Value.Trim(),
                                Source = "Go Modules"
                            });
                        }
                    }
                }

                // Single-line require
                var singleRequires = Regex.Matches(content, @"require\s+(\S+)\s+(v[\d\.]+\S*)");
                foreach (Match m in singleRequires)
                {
                    if (!packages.Any(p => p.Name == m.Groups[1].Value))
                    {
                        packages.Add(new PackageDependency
                        {
                            Name = m.Groups[1].Value,
                            Version = m.Groups[2].Value,
                            Source = "Go Modules"
                        });
                    }
                }

                if (packages.Count == 0)
                    return null;

                // Get module name
                var moduleMatch = Regex.Match(content, @"module\s+(\S+)");
                var moduleName = moduleMatch.Success ? moduleMatch.Groups[1].Value : Path.GetFileName(projectPath) ?? "Go";

                return new DependencySection
                {
                    Name = moduleName,
                    Icon = "🔷",
                    Groups = new List<DependencyGroup>
                    {
                        new DependencyGroup
                        {
                            Name = "go.mod",
                            FilePath = filePath,
                            Packages = packages.OrderBy(p => p.Name).ToList()
                        }
                    }
                };
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error parsing go.mod: {ex.Message}");
                return null;
            }
        }

        #endregion

        #region Flutter/Dart (pubspec.yaml)

        private DependencySection? ParsePubspecYaml(string filePath, string projectPath)
        {
            try
            {
                var content = File.ReadAllText(filePath);
                var lines = content.Split('\n');

                var regularPackages = new List<PackageDependency>();
                var devPackages = new List<PackageDependency>();
                var currentSection = "";
                int firstDepIndent = -1; // indentation level of direct dep entries

                // Special sub-keys that are not package names
                var subKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                {
                    "sdk", "flutter", "git", "path", "url", "ref",
                    "hosted", "version", "name", "transitive"
                };

                foreach (var rawLine in lines)
                {
                    var line = rawLine.TrimEnd('\r');
                    var trimmed = line.TrimStart();
                    var indent = line.Length - trimmed.Length;

                    // Detect top-level section headers (zero indentation)
                    if (indent == 0 && !string.IsNullOrWhiteSpace(trimmed) && !trimmed.StartsWith("#"))
                    {
                        if (trimmed.StartsWith("dependencies:"))
                        {
                            currentSection = "dependencies";
                            firstDepIndent = -1;
                        }
                        else if (trimmed.StartsWith("dev_dependencies:"))
                        {
                            currentSection = "dev_dependencies";
                            firstDepIndent = -1;
                        }
                        else
                        {
                            // Another top-level key — leave dep section
                            currentSection = "";
                        }
                        continue;
                    }

                    // Skip blank lines and comments (don't reset section)
                    if (string.IsNullOrWhiteSpace(trimmed) || trimmed.StartsWith("#"))
                        continue;

                    if (currentSection != "dependencies" && currentSection != "dev_dependencies")
                        continue;

                    // Establish the direct-dependency indentation level from the first entry
                    if (firstDepIndent == -1 && indent > 0)
                        firstDepIndent = indent;

                    // Only process lines at the direct-dependency indent level
                    if (indent != firstDepIndent)
                        continue;

                    var colonIdx = trimmed.IndexOf(':');
                    if (colonIdx <= 0) continue;

                    var pkgName = trimmed.Substring(0, colonIdx).Trim();
                    var rest = trimmed.Substring(colonIdx + 1).Trim();

                    // Skip YAML structural / SDK keys
                    if (subKeys.Contains(pkgName))
                        continue;

                    // Validate package name: must start with letter and contain only word chars
                    if (!Regex.IsMatch(pkgName, @"^[a-zA-Z][a-zA-Z0-9_]*$"))
                        continue;

                    // Determine version
                    string version;
                    if (string.IsNullOrEmpty(rest) || rest == "{}")
                    {
                        // Nested git/path dep — no inline version
                        version = "";
                    }
                    else if (rest.StartsWith("{"))
                    {
                        // Inline object e.g. {sdk: flutter} or {path: ../lib}
                        if (rest.Contains("sdk:"))
                            continue;  // SDK dep, not a real package
                        version = "";
                    }
                    else
                    {
                        // Inline version: ^1.0.0, ">=1.0.0 <2.0.0", any, etc.
                        version = rest.Trim('"', '\'').Trim();
                        // Strip leading range operators (^ ~) for cleaner comparisons
                        if (version.StartsWith("^") || version.StartsWith("~"))
                            version = version.Substring(1);
                        if (version == "any" || version == "*")
                            version = "";
                    }

                    var pkg = new PackageDependency
                    {
                        Name = pkgName,
                        Version = version,
                        Source = "pub.dev"
                    };

                    if (currentSection == "dev_dependencies")
                        devPackages.Add(pkg);
                    else
                        regularPackages.Add(pkg);
                }

                if (regularPackages.Count == 0 && devPackages.Count == 0)
                    return null;

                var nameMatch = Regex.Match(content, @"^name:\s*(\S+)", RegexOptions.Multiline);
                var projectName = nameMatch.Success ? nameMatch.Groups[1].Value : Path.GetFileName(projectPath) ?? "Flutter";

                var section = new DependencySection
                {
                    Name = projectName,
                    Icon = "💙",
                    Groups = new List<DependencyGroup>()
                };

                if (regularPackages.Count > 0)
                {
                    section.Groups.Add(new DependencyGroup
                    {
                        Name = "dependencies",
                        FilePath = filePath,
                        Packages = regularPackages.OrderBy(p => p.Name).ToList()
                    });
                }

                if (devPackages.Count > 0)
                {
                    section.Groups.Add(new DependencyGroup
                    {
                        Name = "dev_dependencies",
                        FilePath = filePath,
                        Packages = devPackages.OrderBy(p => p.Name).ToList()
                    });
                }

                return section;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error parsing pubspec.yaml: {ex.Message}");
                return null;
            }
        }

        #endregion

        #region Java/Maven (pom.xml)

        private DependencySection? ParsePomXml(string filePath, string projectPath)
        {
            try
            {
                var doc = XDocument.Load(filePath);
                XNamespace ns = doc.Root?.GetDefaultNamespace() ?? XNamespace.None;

                var packages = doc.Descendants(ns + "dependency")
                    .Select(d => new PackageDependency
                    {
                        Name = $"{d.Element(ns + "groupId")?.Value}:{d.Element(ns + "artifactId")?.Value}",
                        Version = d.Element(ns + "version")?.Value ?? "",
                        Source = "Maven"
                    })
                    .Where(p => !string.IsNullOrEmpty(p.Name) && p.Name != ":")
                    .OrderBy(p => p.Name)
                    .ToList();

                if (packages.Count == 0)
                    return null;

                var artifactId = doc.Root?.Element(ns + "artifactId")?.Value ?? Path.GetFileName(projectPath) ?? "Maven";

                return new DependencySection
                {
                    Name = artifactId,
                    Icon = "☕",
                    Groups = new List<DependencyGroup>
                    {
                        new DependencyGroup
                        {
                            Name = "pom.xml",
                            FilePath = filePath,
                            Packages = packages
                        }
                    }
                };
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error parsing pom.xml: {ex.Message}");
                return null;
            }
        }

        #endregion

        #region Java/Gradle (build.gradle / build.gradle.kts)

        private DependencySection? ParseBuildGradle(string filePath, string projectPath)
        {
            try
            {
                var content = File.ReadAllText(filePath);
                var packages = new List<PackageDependency>();

                // Match: implementation 'group:artifact:version', api "group:artifact:version", etc.
                var matches = Regex.Matches(content,
                    @"(?:implementation|api|compile|testImplementation|runtimeOnly|compileOnly)\s*[\(]?\s*['""]([^:]+):([^:]+):([^'""]+)['""]",
                    RegexOptions.IgnoreCase);

                foreach (Match m in matches)
                {
                    packages.Add(new PackageDependency
                    {
                        Name = $"{m.Groups[1].Value}:{m.Groups[2].Value}",
                        Version = m.Groups[3].Value,
                        Source = "Maven"
                    });
                }

                if (packages.Count == 0)
                    return null;

                var projectName = Path.GetFileName(projectPath) ?? "Gradle";

                return new DependencySection
                {
                    Name = projectName,
                    Icon = "☕",
                    Groups = new List<DependencyGroup>
                    {
                        new DependencyGroup
                        {
                            Name = Path.GetFileName(filePath),
                            FilePath = filePath,
                            Packages = packages.OrderBy(p => p.Name).ToList()
                        }
                    }
                };
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error parsing build.gradle: {ex.Message}");
                return null;
            }
        }

        #endregion

        #region PHP (composer.json)

        private DependencySection? ParseComposerJson(string filePath, string projectPath)
        {
            try
            {
                var json = File.ReadAllText(filePath);
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                var projectName = root.TryGetProperty("name", out var nameEl)
                    ? nameEl.GetString() ?? Path.GetFileName(projectPath) ?? "PHP"
                    : Path.GetFileName(projectPath) ?? "PHP";

                var section = new DependencySection { Name = projectName, Icon = "🐘" };

                // require
                if (root.TryGetProperty("require", out var require))
                {
                    var packages = new List<PackageDependency>();
                    foreach (var prop in require.EnumerateObject())
                    {
                        // Skip php and extensions
                        if (prop.Name == "php" || prop.Name.StartsWith("ext-"))
                            continue;

                        packages.Add(new PackageDependency
                        {
                            Name = prop.Name,
                            Version = CleanVersionPrefix(prop.Value.GetString() ?? ""),
                            Source = "Packagist"
                        });
                    }
                    if (packages.Count > 0)
                    {
                        section.Groups.Add(new DependencyGroup
                        {
                            Name = "require",
                            FilePath = filePath,
                            Packages = packages.OrderBy(p => p.Name).ToList()
                        });
                    }
                }

                // require-dev
                if (root.TryGetProperty("require-dev", out var requireDev))
                {
                    var packages = new List<PackageDependency>();
                    foreach (var prop in requireDev.EnumerateObject())
                    {
                        packages.Add(new PackageDependency
                        {
                            Name = prop.Name,
                            Version = CleanVersionPrefix(prop.Value.GetString() ?? ""),
                            Source = "Packagist"
                        });
                    }
                    if (packages.Count > 0)
                    {
                        section.Groups.Add(new DependencyGroup
                        {
                            Name = "require-dev",
                            FilePath = filePath,
                            Packages = packages.OrderBy(p => p.Name).ToList()
                        });
                    }
                }

                return section;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error parsing composer.json: {ex.Message}");
                return null;
            }
        }

        #endregion

        #region Ruby (Gemfile)

        private DependencySection? ParseGemfile(string filePath, string projectPath)
        {
            try
            {
                var content = File.ReadAllText(filePath);
                var packages = new List<PackageDependency>();

                // gem 'name', '~> 1.0' or gem "name", "1.0"
                var matches = Regex.Matches(content, @"gem\s+['""]([^'""]+)['""](?:\s*,\s*['""]([^'""]*)['""])?");
                foreach (Match m in matches)
                {
                    packages.Add(new PackageDependency
                    {
                        Name = m.Groups[1].Value,
                        Version = m.Groups[2].Success ? m.Groups[2].Value : "*",
                        Source = "RubyGems"
                    });
                }

                if (packages.Count == 0)
                    return null;

                var projectName = Path.GetFileName(projectPath) ?? "Ruby";

                return new DependencySection
                {
                    Name = projectName,
                    Icon = "💎",
                    Groups = new List<DependencyGroup>
                    {
                        new DependencyGroup
                        {
                            Name = "Gemfile",
                            FilePath = filePath,
                            Packages = packages.OrderBy(p => p.Name).ToList()
                        }
                    }
                };
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error parsing Gemfile: {ex.Message}");
                return null;
            }
        }

        #endregion

        #region Swift (Package.swift)

        private DependencySection? ParsePackageSwift(string filePath, string projectPath)
        {
            try
            {
                var content = File.ReadAllText(filePath);
                var packages = new List<PackageDependency>();

                // .package(url: "https://github.com/...", from: "1.0.0")
                var matches = Regex.Matches(content, @"\.package\s*\(\s*url:\s*""([^""]+)""\s*,\s*(?:from:\s*""([^""]+)""|\.upToNextMajor\(from:\s*""([^""]+)""\))");
                foreach (Match m in matches)
                {
                    var url = m.Groups[1].Value;
                    var version = m.Groups[2].Success ? m.Groups[2].Value : (m.Groups[3].Success ? m.Groups[3].Value : "");
                    var name = url.Split('/').LastOrDefault()?.Replace(".git", "") ?? url;

                    packages.Add(new PackageDependency
                    {
                        Name = name,
                        Version = version,
                        Source = "Swift PM"
                    });
                }

                if (packages.Count == 0)
                    return null;

                // Get package name
                var nameMatch = Regex.Match(content, @"name:\s*""([^""]+)""");
                var projectName = nameMatch.Success ? nameMatch.Groups[1].Value : Path.GetFileName(projectPath) ?? "Swift";

                return new DependencySection
                {
                    Name = projectName,
                    Icon = "🐦",
                    Groups = new List<DependencyGroup>
                    {
                        new DependencyGroup
                        {
                            Name = "Package.swift",
                            FilePath = filePath,
                            Packages = packages.OrderBy(p => p.Name).ToList()
                        }
                    }
                };
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error parsing Package.swift: {ex.Message}");
                return null;
            }
        }

        #endregion

        #region Helpers

        private static string CleanVersionPrefix(string version)
        {
            if (string.IsNullOrEmpty(version)) return version;
            // Remove ^, ~, >= etc. prefixes for display
            return version.TrimStart('^', '~', '>', '<', '=', ' ');
        }

        #endregion
    }
}
