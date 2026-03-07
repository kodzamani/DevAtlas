namespace DevAtlas.Models
{
    // Database entity model
    public class Project
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
        public string Path { get; set; } = "";
        public string ProjectType { get; set; } = "";
        public string Category { get; set; } = "Other";
        public DateTime LastModified { get; set; } = DateTime.Now;
        public DateTime LastIndexed { get; set; } = DateTime.Now;
        public bool IsActive { get; set; }
        public string? GitBranch { get; set; }
        public string? IconText { get; set; }
        public string? IconColor { get; set; }

        // Navigation properties
        public ICollection<ProjectTag> Tags { get; set; } = new List<ProjectTag>();
    }
}
