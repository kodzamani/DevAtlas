namespace DevAtlas.Models
{
    public class ProjectTag
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
        public string? Color { get; set; }
        public string? Description { get; set; }

        // Navigation properties
        public ICollection<Project> Projects { get; set; } = new List<Project>();
    }
}
