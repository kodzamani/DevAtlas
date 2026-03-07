using Avalonia;
using Avalonia.Controls;

namespace DevAtlas.Controls
{
    /// <summary>
    /// Interaction logic for StatusDisplay
    /// </summary>
    public partial class StatusDisplay : UserControl
    {
        public static readonly StyledProperty<int> ProjectCountProperty =
            AvaloniaProperty.Register<StatusDisplay, int>(nameof(ProjectCount), defaultValue: 0);

        public static readonly StyledProperty<long> FileCountProperty =
            AvaloniaProperty.Register<StatusDisplay, long>(nameof(FileCount), defaultValue: 0L);

        public static readonly StyledProperty<long> LinesCountProperty =
            AvaloniaProperty.Register<StatusDisplay, long>(nameof(LinesCount), defaultValue: 0L);

        public static readonly StyledProperty<bool> IsLoadingProperty =
            AvaloniaProperty.Register<StatusDisplay, bool>(nameof(IsLoading), defaultValue: false);

        public int ProjectCount
        {
            get => GetValue(ProjectCountProperty);
            set => SetValue(ProjectCountProperty, value);
        }

        public long FileCount
        {
            get => GetValue(FileCountProperty);
            set => SetValue(FileCountProperty, value);
        }

        public long LinesCount
        {
            get => GetValue(LinesCountProperty);
            set => SetValue(LinesCountProperty, value);
        }

        public bool IsLoading
        {
            get => GetValue(IsLoadingProperty);
            set => SetValue(IsLoadingProperty, value);
        }

        static StatusDisplay()
        {
            ProjectCountProperty.Changed.AddClassHandler<StatusDisplay>((control, e) => control.OnProjectCountChanged());
            FileCountProperty.Changed.AddClassHandler<StatusDisplay>((control, e) => control.OnFileCountChanged());
            LinesCountProperty.Changed.AddClassHandler<StatusDisplay>((control, e) => control.OnLinesCountChanged());
            IsLoadingProperty.Changed.AddClassHandler<StatusDisplay>((control, e) => control.OnIsLoadingChanged());
        }

        public StatusDisplay()
        {
            InitializeComponent();
        }

        private void OnProjectCountChanged()
        {
            if (IsLoading)
            {
                ProjectCountText.Text = "";
                ProjectLoadingText.IsVisible = true;
            }
            else
            {
                ProjectCountText.Text = FormatNumber(ProjectCount);
                ProjectLoadingText.IsVisible = false;
            }
        }

        private void OnFileCountChanged()
        {
            if (IsLoading)
            {
                FileCountText.Text = "";
                FileLoadingText.IsVisible = true;
            }
            else
            {
                FileCountText.Text = FormatNumber(FileCount);
                FileLoadingText.IsVisible = false;
            }
        }

        private void OnLinesCountChanged()
        {
            if (IsLoading)
            {
                LinesCountText.Text = "";
                LinesLoadingText.IsVisible = true;
            }
            else
            {
                LinesCountText.Text = FormatNumber(LinesCount);
                LinesLoadingText.IsVisible = false;
            }
        }

        private void OnIsLoadingChanged()
        {
            bool isLoading = IsLoading;

            if (isLoading)
            {
                ProjectCountText.Text = "";
                FileCountText.Text = "";
                LinesCountText.Text = "";
                ProjectLoadingText.IsVisible = true;
                FileLoadingText.IsVisible = true;
                LinesLoadingText.IsVisible = true;
            }
            else
            {
                ProjectLoadingText.IsVisible = false;
                FileLoadingText.IsVisible = false;
                LinesLoadingText.IsVisible = false;
            }
        }

        private static string FormatNumber(long number)
        {
            if (number < 1000)
                return number.ToString();

            if (number < 1000000)
                return $"{number / 1000.0:F1}K";

            if (number < 1000000000)
                return $"{number / 1000000.0:F1}M";

            return $"{number / 1000000000.0:F1}B";
        }
    }
}