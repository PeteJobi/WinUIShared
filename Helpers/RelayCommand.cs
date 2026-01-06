using System;
using System.Collections.Generic;
using System.Text;
using System.Windows.Input;

namespace WinUIShared.Helpers
{
    public class RelayCommand<T>(Action<T?> execute, Func<T?, bool>? canExecute = null) : ICommand
    {
        public bool CanExecute(object? parameter)
            => canExecute?.Invoke((T?)parameter) ?? true;

        public void Execute(object? parameter)
            => execute((T?)parameter);

        public event EventHandler? CanExecuteChanged;
    }
}
