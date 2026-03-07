namespace DevAtlas.Models
{
    /// <summary>
    /// Language/extension breakdown with percentage and color
    /// </summary>
    public class LanguageBreakdown
    {
        public string Name { get; set; } = "";
        public string Extension { get; set; } = "";
        public int FileCount { get; set; }
        public int TotalLines { get; set; }
        public double Percentage { get; set; }
        public string Color { get; set; } = "#6B7280";
    }
}
