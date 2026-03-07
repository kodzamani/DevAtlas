namespace DevAtlas.Models
{
    /// <summary>
    /// Analysis info for a single file
    /// </summary>
    public class FileAnalysisInfo
    {
        public string RelativePath { get; set; } = "";
        public string Extension { get; set; } = "";
        public int Lines { get; set; }
    }
}
