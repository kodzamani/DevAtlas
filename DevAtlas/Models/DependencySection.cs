namespace DevAtlas.Models
{
    /// <summary>
    /// Top-level grouping of dependencies for a project (solution-level or project root).
    /// </summary>
    public class DependencySection
    {
        public string Name { get; set; } = "";
        public string Icon { get; set; } = "📦";
        public List<DependencyGroup> Groups { get; set; } = new();
        public int TotalPackageCount => Groups.Sum(g => g.PackageCount);
        public bool IsExpanded { get; set; } = true;
    }
}
