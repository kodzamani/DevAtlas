using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using DevAtlas.Models;
using DevAtlas.Services;

namespace DevAtlas.Controls
{
    /// <summary>
    /// Popup for selecting a code editor to open a project
    /// </summary>
    public partial class EditorSelectorPopup : UserControl
    {
        private readonly CodeEditorDetector _detector;

        /// <summary>
        /// Event fired when a user selects an editor
        /// </summary>
        public event EventHandler<CodeEditor>? EditorSelected;

        public EditorSelectorPopup()
        {
            InitializeComponent();
            _detector = new CodeEditorDetector();
        }

        /// <summary>
        /// Shows the popup with available editors
        /// </summary>
        public void Show()
        {
            LoadEditors();
            IsVisible = true;
        }

        /// <summary>
        /// Hides the popup
        /// </summary>
        public void Hide()
        {
            IsVisible = false;
        }

        /// <summary>
        /// Loads the list of installed editors
        /// </summary>
        private void LoadEditors()
        {
            var editors = _detector.DetectInstalledEditors();
            var installedEditors = editors.Where(e => e.IsInstalled).ToList();

            EditorsList.Items.Clear();

            if (installedEditors.Any())
            {
                foreach (var editor in installedEditors)
                {
                    EditorsList.Items.Add(editor);
                }
                EditorsList.IsVisible = true;
                NoEditorsMessage.IsVisible = false;
            }
            else
            {
                EditorsList.IsVisible = false;
                NoEditorsMessage.IsVisible = true;
            }
        }

        /// <summary>
        /// Handles clicking on an editor item
        /// </summary>
        private void EditorItem_Click(object sender, PointerPressedEventArgs e)
        {
            if (sender is Border border && border.Tag is CodeEditor editor)
            {
                EditorSelected?.Invoke(this, editor);
                Hide();
            }
        }

        /// <summary>
        /// Handles clicking the close button
        /// </summary>
        private void CloseButton_Click(object sender, PointerPressedEventArgs e)
        {
            Hide();
        }

        private void EditorIcon_Loaded(object? sender, RoutedEventArgs e)
        {
            if (sender is not Image image || image.Source != null || image.DataContext is not CodeEditor editor)
            {
                return;
            }

            // Fallback path: enforce icon load for environments where Source binding
            // does not convert asset URIs automatically.
            try
            {
                using var stream = AssetLoader.Open(new Uri(editor.IconPath, UriKind.Absolute));
                image.Source = new Bitmap(stream);
            }
            catch
            {
                // keep empty on failure
            }
        }
    }
}
