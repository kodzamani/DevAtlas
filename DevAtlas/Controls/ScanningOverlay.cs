using Avalonia.Controls;
using Avalonia.Threading;
using DevAtlas.Models;
using DevAtlas.Services;
using System.Collections.ObjectModel;

namespace DevAtlas.Controls
{
    public partial class ScanningOverlay : UserControl, IDisposable
    {
        private readonly DispatcherTimer _animationTimer;
        private double _targetProgress;
        private double _displayProgress;
        private bool _disposed = false;

        public ObservableCollection<DriveProgress> Drives { get; } = new();


        public ScanningOverlay()
        {
            InitializeComponent();

            _animationTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(16)
            };
            _animationTimer.Tick += AnimationTimer_Tick;
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    // Stop timer and unsubscribe from event
                    _animationTimer.Stop();
                    _animationTimer.Tick -= AnimationTimer_Tick;

                    // Clear drives collection
                    Drives.Clear();
                }

                _disposed = true;
            }
        }

        public void Show()
        {
            IsVisible = true;
            _displayProgress = 0;
            _targetProgress = 0;
            UpdateProgressBar(0);
        }

        public void Hide()
        {
            _animationTimer.Stop();
            Drives.Clear();
            IsVisible = false;
        }


        public void UpdateProgress(string drive, string path, double percentage, string status)
        {
            Avalonia.Threading.Dispatcher.UIThread.Invoke(() =>
            {
                // Update status
                StatusText.Text = status;
                CurrentPathText.Text = path.Length > 60 ? "..." + path[^57..] : path;

                // Update progress bar
                _targetProgress = percentage;
                if (!_animationTimer.IsEnabled)
                {
                    _animationTimer.Start();
                }

                // Update drive list
                UpdateDriveProgress(drive, percentage);
            });
        }

        private void UpdateDriveProgress(string drive, double progress)
        {
            var existing = Drives.FirstOrDefault(d => d.DriveName == drive);
            if (existing != null)
            {
                existing.Progress = progress;
                existing.Status = $"{(int)progress}%";
            }
            else if (!string.IsNullOrEmpty(drive))
            {
                Drives.Add(new DriveProgress
                {
                    DriveName = drive,
                    Progress = progress,
                    Status = $"{(int)progress}%"
                });
            }
        }

        private void AnimationTimer_Tick(object? sender, EventArgs e)
        {
            // Smooth animation towards target
            var diff = _targetProgress - _displayProgress;
            if (Math.Abs(diff) < 0.5)
            {
                _displayProgress = _targetProgress;
                _animationTimer.Stop();
            }
            else
            {
                _displayProgress += diff * 0.1;
            }

            UpdateProgressBar(_displayProgress);
        }

        private void UpdateProgressBar(double percentage)
        {
            // Width is based on parent container (360px - padding), using 280px as max width
            var maxWidth = 280.0;
            var width = Math.Max(0, Math.Min(maxWidth, (percentage / 100) * maxWidth));

            // Find the ProgressBar element by name
            if (this.FindControl<Avalonia.Controls.Control>("ProgressBar") is Border progressBar)
            {
                progressBar.Width = width;
            }
        }

        public void Reset()
        {
            Drives.Clear();
            _displayProgress = 0;
            _targetProgress = 0;
            UpdateProgressBar(0);
            StatusText.Text = LanguageManager.Instance["ScanStarting"];
            CurrentPathText.Text = LanguageManager.Instance["ScanInitializing"];
        }
    }
}
