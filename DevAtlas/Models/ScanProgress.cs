namespace DevAtlas.Models
{
    public class ScanProgress
    {
        public string CurrentDrive { get; set; } = "";
        public string CurrentPath { get; set; } = "";
        public int TotalDrives { get; set; }
        public int ProcessedDrives { get; set; }
        public int ProjectsFound { get; set; }
        public int DirectoriesScanned { get; set; }
        public double ProgressPercentage { get; set; }
        public bool IsScanning { get; set; }
        public string Status { get; set; } = "";
    }
}
