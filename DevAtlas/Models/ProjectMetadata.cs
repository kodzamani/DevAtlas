namespace DevAtlas.Models
{
    public class ProjectMetadata
    {
        public int Id { get; set; }
        public string ProjectPath { get; set; } = "";
        public string MetadataType { get; set; } = ""; // FileSize, LastScan, Dependencies, etc.
        public string MetadataValue { get; set; } = "";
        public DateTime LastUpdated { get; set; } = DateTime.Now;
    }
}
