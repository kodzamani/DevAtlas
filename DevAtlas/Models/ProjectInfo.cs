using Avalonia.Media;
using System.Text.Json.Serialization;

namespace DevAtlas.Models
{
    public class ProjectInfo
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string Name { get; set; } = "";
        public string Path { get; set; } = "";
        public string ProjectType { get; set; } = "";
        public string Category { get; set; } = "Other"; // Web, Desktop, Mobile, Cloud, Other
        public List<string> Tags { get; set; } = new();
        public DateTime LastModified { get; set; } = DateTime.Now;
        public DateTime LastIndexed { get; set; } = DateTime.Now;
        public bool IsActive { get; set; }
        public string? GitBranch { get; set; }
        public string? IconText { get; set; }
        public string? IconColor { get; set; }
        public int? TotalLines { get; set; }
        public int? TotalFiles { get; set; }

        [JsonIgnore]
        public IBrush IconBrush
        {
            get
            {
                try
                {
                    if (!string.IsNullOrEmpty(IconColor))
                    {
                        return new SolidColorBrush(Color.Parse(IconColor));
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Error converting IconColor '{IconColor}' to brush: {ex.Message}");
                }

                return new SolidColorBrush(Color.FromRgb(107, 114, 128));
            }
        }
    }
}
