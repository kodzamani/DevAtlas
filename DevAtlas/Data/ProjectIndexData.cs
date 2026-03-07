using DevAtlas.Models;

namespace DevAtlas.Data
{
    // Legacy JSON data structure for migration
    internal class ProjectIndexData
    {
        public List<ProjectInfo>? Projects { get; set; }
        public DateTime LastIndexed { get; set; }
        public int Version { get; set; }
    }
}
