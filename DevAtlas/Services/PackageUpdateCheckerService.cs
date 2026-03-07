using DevAtlas.Models;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace DevAtlas.Services
{
    /// <summary>
    /// Checks for available package updates from various registries.
    /// Only returns stable (non-pre-release) versions.
    /// Supports: NuGet, npm, PyPI, crates.io, pub.dev, Maven Central, Packagist, RubyGems.
    /// </summary>
    public class PackageUpdateCheckerService : IDisposable
    {
        private static readonly HttpClient _httpClient = new()
        {
            Timeout = TimeSpan.FromSeconds(10)
        };

        private bool _disposed;

        static PackageUpdateCheckerService()
        {
            _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("DevAtlas/1.0");
        }

        /// <summary>
        /// Checks for updates for all packages in the given dependency sections.
        /// Updates the LatestVersion property of each PackageDependency in-place.
        /// Calls onPackageUpdated callback when a package update check completes, for UI refresh.
        /// </summary>
        public async Task CheckForUpdatesAsync(
            List<DependencySection> sections,
            Action? onPackageUpdated = null,
            CancellationToken cancellationToken = default)
        {
            var allPackages = sections
                .SelectMany(s => s.Groups)
                .SelectMany(g => g.Packages)
                .ToList();

            // Process in batches of 8 to avoid overwhelming the APIs
            const int batchSize = 8;
            for (int i = 0; i < allPackages.Count; i += batchSize)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var batch = allPackages.Skip(i).Take(batchSize).ToList();
                var tasks = batch.Select(async pkg =>
                {
                    try
                    {
                        pkg.IsCheckingUpdate = true;
                        var latest = await GetLatestVersionAsync(pkg.Name, pkg.Source, cancellationToken);
                        if (!string.IsNullOrEmpty(latest))
                        {
                            pkg.LatestVersion = latest;
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Error checking update for {pkg.Name}: {ex.Message}");
                    }
                    finally
                    {
                        pkg.IsCheckingUpdate = false;
                    }
                });

                await Task.WhenAll(tasks);
                onPackageUpdated?.Invoke();
            }
        }

        private async Task<string?> GetLatestVersionAsync(string packageName, string source, CancellationToken ct)
        {
            return source switch
            {
                "NuGet" => await GetNuGetLatestAsync(packageName, ct),
                "npm" => await GetNpmLatestAsync(packageName, ct),
                "PyPI" => await GetPyPILatestAsync(packageName, ct),
                "crates.io" => await GetCratesLatestAsync(packageName, ct),
                "pub.dev" => await GetPubDevLatestAsync(packageName, ct),
                "Maven" => await GetMavenLatestAsync(packageName, ct),
                "Packagist" => await GetPackagistLatestAsync(packageName, ct),
                "RubyGems" => await GetRubyGemsLatestAsync(packageName, ct),
                _ => null
            };
        }

        #region NuGet

        private async Task<string?> GetNuGetLatestAsync(string packageName, CancellationToken ct)
        {
            try
            {
                var url = $"https://api.nuget.org/v3-flatcontainer/{packageName.ToLowerInvariant()}/index.json";
                var response = await _httpClient.GetStringAsync(url, ct);
                using var doc = JsonDocument.Parse(response);

                if (doc.RootElement.TryGetProperty("versions", out var versions))
                {
                    // Get last non-prerelease version
                    var stableVersions = versions.EnumerateArray()
                        .Select(v => v.GetString() ?? "")
                        .Where(v => !string.IsNullOrEmpty(v) && !IsPreRelease(v))
                        .ToList();

                    return stableVersions.LastOrDefault();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"NuGet check failed for {packageName}: {ex.Message}");
            }
            return null;
        }

        #endregion

        #region npm

        private async Task<string?> GetNpmLatestAsync(string packageName, CancellationToken ct)
        {
            try
            {
                var url = $"https://registry.npmjs.org/{Uri.EscapeDataString(packageName)}/latest";
                var response = await _httpClient.GetStringAsync(url, ct);
                using var doc = JsonDocument.Parse(response);

                if (doc.RootElement.TryGetProperty("version", out var version))
                {
                    var v = version.GetString();
                    if (v != null && !IsPreRelease(v))
                        return v;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"npm check failed for {packageName}: {ex.Message}");
            }
            return null;
        }

        #endregion

        #region PyPI

        private async Task<string?> GetPyPILatestAsync(string packageName, CancellationToken ct)
        {
            try
            {
                var url = $"https://pypi.org/pypi/{Uri.EscapeDataString(packageName)}/json";
                var response = await _httpClient.GetStringAsync(url, ct);
                using var doc = JsonDocument.Parse(response);

                if (doc.RootElement.TryGetProperty("info", out var info) &&
                    info.TryGetProperty("version", out var version))
                {
                    var v = version.GetString();
                    if (v != null && !IsPreRelease(v))
                        return v;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"PyPI check failed for {packageName}: {ex.Message}");
            }
            return null;
        }

        #endregion

        #region crates.io

        private async Task<string?> GetCratesLatestAsync(string packageName, CancellationToken ct)
        {
            try
            {
                var url = $"https://crates.io/api/v1/crates/{Uri.EscapeDataString(packageName)}";
                var response = await _httpClient.GetStringAsync(url, ct);
                using var doc = JsonDocument.Parse(response);

                if (doc.RootElement.TryGetProperty("crate", out var crate) &&
                    crate.TryGetProperty("max_stable_version", out var version))
                {
                    return version.GetString();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"crates.io check failed for {packageName}: {ex.Message}");
            }
            return null;
        }

        #endregion

        #region pub.dev

        private async Task<string?> GetPubDevLatestAsync(string packageName, CancellationToken ct)
        {
            try
            {
                var url = $"https://pub.dev/api/packages/{Uri.EscapeDataString(packageName)}";
                var response = await _httpClient.GetStringAsync(url, ct);
                using var doc = JsonDocument.Parse(response);

                if (doc.RootElement.TryGetProperty("latest", out var latest) &&
                    latest.TryGetProperty("version", out var version))
                {
                    var v = version.GetString();
                    if (v != null && !IsPreRelease(v))
                        return v;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"pub.dev check failed for {packageName}: {ex.Message}");
            }
            return null;
        }

        #endregion

        #region Maven Central

        private async Task<string?> GetMavenLatestAsync(string packageName, CancellationToken ct)
        {
            try
            {
                // packageName format: groupId:artifactId
                var parts = packageName.Split(':');
                if (parts.Length != 2)
                    return null;

                var url = $"https://search.maven.org/solrsearch/select?q=g:{Uri.EscapeDataString(parts[0])}+AND+a:{Uri.EscapeDataString(parts[1])}&rows=1&wt=json";
                var response = await _httpClient.GetStringAsync(url, ct);
                using var doc = JsonDocument.Parse(response);

                if (doc.RootElement.TryGetProperty("response", out var resp) &&
                    resp.TryGetProperty("docs", out var docs))
                {
                    foreach (var d in docs.EnumerateArray())
                    {
                        if (d.TryGetProperty("latestVersion", out var version))
                        {
                            var v = version.GetString();
                            if (v != null && !IsPreRelease(v))
                                return v;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Maven check failed for {packageName}: {ex.Message}");
            }
            return null;
        }

        #endregion

        #region Packagist

        private async Task<string?> GetPackagistLatestAsync(string packageName, CancellationToken ct)
        {
            try
            {
                var url = $"https://repo.packagist.org/p2/{Uri.EscapeDataString(packageName)}.json";
                var response = await _httpClient.GetStringAsync(url, ct);
                using var doc = JsonDocument.Parse(response);

                if (doc.RootElement.TryGetProperty("packages", out var packages) &&
                    packages.TryGetProperty(packageName, out var versions))
                {
                    foreach (var ver in versions.EnumerateArray())
                    {
                        if (ver.TryGetProperty("version", out var version))
                        {
                            var v = version.GetString();
                            if (v != null && !IsPreRelease(v) && Regex.IsMatch(v, @"^\d"))
                                return v;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Packagist check failed for {packageName}: {ex.Message}");
            }
            return null;
        }

        #endregion

        #region RubyGems

        private async Task<string?> GetRubyGemsLatestAsync(string packageName, CancellationToken ct)
        {
            try
            {
                var url = $"https://rubygems.org/api/v1/versions/{Uri.EscapeDataString(packageName)}/latest.json";
                var response = await _httpClient.GetStringAsync(url, ct);
                using var doc = JsonDocument.Parse(response);

                if (doc.RootElement.TryGetProperty("version", out var version))
                {
                    var v = version.GetString();
                    if (v != null && !IsPreRelease(v))
                        return v;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"RubyGems check failed for {packageName}: {ex.Message}");
            }
            return null;
        }

        #endregion

        #region Helpers

        /// <summary>
        /// Determines if a version string is a pre-release.
        /// Pre-release versions contain: -alpha, -beta, -rc, -preview, -dev, -pre, -canary, -nightly, etc.
        /// </summary>
        private static bool IsPreRelease(string version)
        {
            if (string.IsNullOrEmpty(version))
                return false;

            return Regex.IsMatch(version,
                @"[-\.](alpha|beta|rc|preview|dev|pre|canary|nightly|snapshot|insiders|experimental|unstable|next|edge)",
                RegexOptions.IgnoreCase);
        }

        #endregion

        public void Dispose()
        {
            if (!_disposed)
            {
                _disposed = true;
            }
        }
    }
}
