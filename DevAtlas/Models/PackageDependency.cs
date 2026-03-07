namespace DevAtlas.Models
{
    /// <summary>
    /// Represents a single package dependency with version and update info.
    /// </summary>
    public class PackageDependency
    {
        public string Name { get; set; } = "";
        public string Version { get; set; } = "";
        public string Source { get; set; } = ""; // NuGet, npm, PyPI, crates.io, etc.
        public string? LatestVersion { get; set; }
        public bool HasUpdate => !string.IsNullOrEmpty(LatestVersion) && LatestVersion != Version;
        public bool IsCheckingUpdate { get; set; }
    }
}
