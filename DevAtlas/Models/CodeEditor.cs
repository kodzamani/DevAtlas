using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace DevAtlas.Models
{
    /// <summary>
    /// Represents a code editor with its properties
    /// </summary>
    public class CodeEditor
    {
        private IImage? _iconImage;

        /// <summary>
        /// Internal name of the editor (e.g., "vscode", "cursor", "windsurf", "antigravity")
        /// </summary>
        public string Name { get; set; } = "";

        /// <summary>
        /// Display name shown to the user (e.g., "VS Code", "Cursor", "Windsurf", "Antigravity")
        /// </summary>
        public string DisplayName { get; set; } = "";

        /// <summary>
        /// Command line command to launch the editor (e.g., "code", "cursor", "windsurf")
        /// </summary>
        public string Command { get; set; } = "";

        /// <summary>
        /// Full path to the editor executable (e.g., "C:\Users\...\Cursor\Cursor.exe")
        /// </summary>
        public string FullPath { get; set; } = "";

        /// <summary>
        /// Path to the icon file for the editor
        /// </summary>
        public string IconPath { get; set; } = "";

        /// <summary>
        /// Loaded icon image for direct binding in Avalonia Image.Source.
        /// </summary>
        public IImage? IconImage
        {
            get
            {
                if (_iconImage != null || string.IsNullOrWhiteSpace(IconPath))
                {
                    return _iconImage;
                }

                try
                {
                    using var stream = AssetLoader.Open(new Uri(IconPath, UriKind.Absolute));
                    _iconImage = new Bitmap(stream);
                }
                catch
                {
                    _iconImage = null;
                }

                return _iconImage;
            }
            set => _iconImage = value;
        }

        /// <summary>
        /// Indicates whether the editor is installed on the system
        /// </summary>
        public bool IsInstalled { get; set; }
    }
}
