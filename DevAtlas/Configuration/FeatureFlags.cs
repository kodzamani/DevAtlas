namespace DevAtlas.Configuration
{
    public class FeatureFlags
    {
        /// <summary>
        /// Enable new SQLite database backend
        /// </summary>
        public bool EnableSqliteDatabase { get; set; } = true;

        /// <summary>
        /// Maintain backward compatibility with JSON format
        /// </summary>
        public bool MaintainJsonCompatibility { get; set; } = true;

        /// <summary>
        /// Enable experimental parallel scanning features
        /// </summary>
        public bool EnableExperimentalParallelScanning { get; set; } = false;

        /// <summary>
        /// Enable real-time project monitoring
        /// </summary>
        public bool EnableRealTimeMonitoring { get; set; } = false;

        /// <summary>
        /// Enable advanced caching strategies
        /// </summary>
        public bool EnableAdvancedCaching { get; set; } = true;
    }
}
