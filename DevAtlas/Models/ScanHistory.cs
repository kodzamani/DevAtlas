namespace DevAtlas.Models
{
    public class ScanHistory
    {
        public int Id { get; set; }
        public DateTime ScanStartTime { get; set; } = DateTime.Now;
        public DateTime? ScanEndTime { get; set; }
        public string ScanType { get; set; } = "Full"; // Full, Incremental, Priority
        public string Status { get; set; } = "Running"; // Running, Completed, Failed, Cancelled
        public int ProjectsFound { get; set; }
        public int DirectoriesScanned { get; set; }
        public string? ErrorMessage { get; set; }
        public TimeSpan Duration => ScanEndTime.HasValue ? ScanEndTime.Value - ScanStartTime : TimeSpan.Zero;
    }
}
