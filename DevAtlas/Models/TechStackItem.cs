namespace DevAtlas.Models
{
    /// <summary>
    /// Tech stack item with line count for display in the detail view
    /// </summary>
    public class TechStackItem
    {
        public string Name { get; set; } = "";
        public int Lines { get; set; }
        public string LinesFormatted => Lines > 0 ? $"{Lines:N0} lines" : "";
    }
}
