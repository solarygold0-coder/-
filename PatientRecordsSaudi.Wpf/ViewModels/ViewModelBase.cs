using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;

namespace PatientRecordsSaudi.Wpf.ViewModels
{
    public abstract class ViewModelBase : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;
        protected void Raise([CallerMemberName] string name = null) { var handler = PropertyChanged; if (handler != null) handler(this, new PropertyChangedEventArgs(name)); }
        protected bool Set<T>(ref T field, T value, [CallerMemberName] string name = null) { if (Equals(field, value)) return false; field = value; Raise(name); return true; }
    }

    public sealed class RelayCommand : ICommand
    {
        private readonly Action execute; private readonly Func<bool> canExecute;
        public RelayCommand(Action execute, Func<bool> canExecute = null) { this.execute = execute ?? throw new ArgumentNullException("execute"); this.canExecute = canExecute; }
        public bool CanExecute(object parameter) { return canExecute == null || canExecute(); }
        public void Execute(object parameter) { execute(); }
        public event EventHandler CanExecuteChanged { add { CommandManager.RequerySuggested += value; } remove { CommandManager.RequerySuggested -= value; } }
    }
}
