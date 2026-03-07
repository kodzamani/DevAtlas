using DevAtlas.Services;
using System.Windows.Input;

namespace DevAtlas.ViewModels;

/// <summary>
/// Generic relay command implementation for MVVM pattern.
/// </summary>
public class RelayCommand : ICommand
{
    private readonly Action<object?> _execute;
    private readonly Func<object?, bool>? _canExecute;

    public RelayCommand(Action<object?> execute, Func<object?, bool>? canExecute = null)
    {
        if (execute == null)
        {
            ErrorDialogService.ShowError("Command could not be created because execute action is null.");
            _execute = _ => { };
        }
        else
        {
            _execute = execute;
        }

        _canExecute = canExecute;
    }

    public RelayCommand(Action execute, Func<bool>? canExecute = null)
        : this(_ => execute(), canExecute != null ? _ => canExecute() : null)
    { }

    public event EventHandler? CanExecuteChanged;

    public bool CanExecute(object? parameter) => _canExecute?.Invoke(parameter) ?? true;
    public void Execute(object? parameter) => _execute(parameter);

    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}
