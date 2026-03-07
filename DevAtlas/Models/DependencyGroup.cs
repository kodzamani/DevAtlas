namespace DevAtlas.Models
{
    /// <summary>
    /// A sub-project/module that contains dependencies (e.g. ProjectBase.Application).
    /// </summary>
    public class DependencyGroup
    {
        public string Name { get; set; } = "";
        public string FilePath { get; set; } = ""; // The dependency file path (e.g. .csproj, package.json)
        public List<PackageDependency> Packages { get; set; } = new();
        public int PackageCount => Packages.Count;
        public bool IsExpanded { get; set; } = false;
    }
}
