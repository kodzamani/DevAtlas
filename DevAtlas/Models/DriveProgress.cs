using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace DevAtlas.Models;

public class DriveProgress : INotifyPropertyChanged
{
    private double _progress;
    private string _status = "";

    public string DriveName { get; set; } = "";

    public double Progress
    {
        get => _progress;
        set
        {
            _progress = value;
            OnPropertyChanged();
        }
    }

    public string Status
    {
        get => _status;
        set
        {
            _status = value;
            OnPropertyChanged();
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}