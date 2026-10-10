using System;
using System.Windows.Input;

namespace NewToys.Shared.Input;

public class DelegateCommand : ICommand
{
    private readonly Action _execute;
    private readonly Func<bool>? _canExecute;
    public DelegateCommand(Action execute, Func<bool>? canExecute = null) { _execute = execute; _canExecute = canExecute; }
    public bool CanExecute(object? p) => _canExecute?.Invoke() ?? true;
    public void Execute(object? p) => _execute();
    public event EventHandler? CanExecuteChanged;
    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}

public class DelegateCommand<T> : ICommand
{
    private readonly Action<T?> _execute;
    private readonly Func<T?, bool>? _canExecute;
    public DelegateCommand(Action<T?> execute, Func<T?, bool>? canExecute = null) { _execute = execute; _canExecute = canExecute; }
    public bool CanExecute(object? p) => _canExecute?.Invoke((T?)p!) ?? true;
    public void Execute(object? p) => _execute((T?)p!);
    public event EventHandler? CanExecuteChanged;
    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}