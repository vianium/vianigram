// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

// AsyncCommand.cs / RelayCommand.cs
//
// Lightweight ICommand implementations used by the auth-page ViewModels.
// The repo does not yet ship a shared Vianigram.ViewModels.BaseViewModel
// or AsyncCommand, so the auth pages carry their own minimal copy that
// matches WinRT XAML expectations. Once the shared assembly arrives these
// types can be deleted in favour of the canonical versions.

using System;
using System.Threading.Tasks;
using System.Windows.Input;

namespace Vianigram.App.ViewModels.Pages
{
    /// <summary>
    /// Synchronous relay command — single static handler, optional CanExecute.
    /// </summary>
    public sealed class RelayCommand : ICommand
    {
        private readonly Action<object> _execute;
        private readonly Func<object, bool> _canExecute;

        public RelayCommand(Action execute)
            : this(_ => { if (execute != null) execute(); }, null)
        {
        }

        public RelayCommand(Action<object> execute)
            : this(execute, null)
        {
        }

        public RelayCommand(Action<object> execute, Func<object, bool> canExecute)
        {
            _execute = execute;
            _canExecute = canExecute;
        }

        public event EventHandler CanExecuteChanged;

        public bool CanExecute(object parameter)
        {
            return _canExecute == null || _canExecute(parameter);
        }

        public void Execute(object parameter)
        {
            if (_execute != null) _execute(parameter);
        }

        public void RaiseCanExecuteChanged()
        {
            var h = CanExecuteChanged;
            if (h != null) h(this, EventArgs.Empty);
        }
    }

    /// <summary>
    /// Async relay command — Execute kicks off a Task and forgets it (any
    /// exception is swallowed at the boundary; the VM is responsible for
    /// surfacing failure state via observable properties).
    /// </summary>
    public sealed class AsyncCommand : ICommand
    {
        private readonly Func<object, Task> _execute;
        private readonly Func<object, bool> _canExecute;
        private bool _isRunning;

        public AsyncCommand(Func<Task> execute)
            : this(_ => execute != null ? execute() : Task.FromResult(0) as Task, null)
        {
        }

        public AsyncCommand(Func<object, Task> execute, Func<object, bool> canExecute)
        {
            _execute = execute;
            _canExecute = canExecute;
        }

        public event EventHandler CanExecuteChanged;

        public bool IsRunning
        {
            get { return _isRunning; }
            private set
            {
                if (_isRunning == value) return;
                _isRunning = value;
                RaiseCanExecuteChanged();
            }
        }

        public bool CanExecute(object parameter)
        {
            if (_isRunning) return false;
            return _canExecute == null || _canExecute(parameter);
        }

        public async void Execute(object parameter)
        {
            if (!CanExecute(parameter)) return;

            IsRunning = true;
            try
            {
                if (_execute != null)
                {
                    var task = _execute(parameter);
                    if (task != null) await task.ConfigureAwait(true);
                }
            }
            catch
            {
                // Swallow — VM surfaces failure via its own observable state.
            }
            finally
            {
                IsRunning = false;
            }
        }

        public void RaiseCanExecuteChanged()
        {
            var h = CanExecuteChanged;
            if (h != null) h(this, EventArgs.Empty);
        }
    }
}
