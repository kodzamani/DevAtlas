namespace DevAtlas.Models
{
    /// <summary>
    /// Represents a single unused code finding from the analyzer
    /// </summary>
    public class UnusedCodeResult
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public string Kind { get; set; } = "";
        public string Name { get; set; } = "";
        public string Location { get; set; } = "";
        public List<string> Hints { get; set; } = new();
    }
}
