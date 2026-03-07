namespace DevAtlas.Models
{
    /// <summary>
    /// Result of a project code analysis
    /// </summary>
    public class ProjectAnalysisResult
    {
        public int TotalFiles { get; set; }
        public int TotalLines { get; set; }
        public int AvgLinesPerFile => TotalFiles > 0 ? TotalLines / TotalFiles : 0;
        public int LargestFileLines { get; set; }
        public string LargestFileName { get; set; } = "";
        public List<FileAnalysisInfo> Files { get; set; } = new();
        public List<LanguageBreakdown> Languages { get; set; } = new();
    }
}
