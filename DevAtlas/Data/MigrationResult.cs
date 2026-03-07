namespace DevAtlas.Data
{
    public class MigrationResult
    {
        public MigrationStatus Status { get; set; }
        public string Message { get; set; } = "";
        public int TotalProjects { get; set; }
        public int MigratedProjects { get; set; }
        public Exception? Exception { get; set; }
    }
}
