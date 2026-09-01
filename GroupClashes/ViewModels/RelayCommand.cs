using System;
using System.Windows.Input;

namespace SmartGroupClashes.ViewModels
{
    /// <summary>
    /// Реализация <see cref="ICommand"/> с делегатами Execute и CanExecute.
    /// </summary>
    public class RelayCommand : ICommand
    {
        private readonly Action _execute;
        private readonly Func<bool> _canExecute;

        /// <summary>Создаёт команду с указанными делегатами.</summary>
        public RelayCommand(Action execute, Func<bool> canExecute = null)
        {
            _execute = execute ?? throw new ArgumentNullException(nameof(execute));
            _canExecute = canExecute;
        }

        public event EventHandler CanExecuteChanged;

        /// <summary>Определяет, доступна ли команда для выполнения.</summary>
        public bool CanExecute(object parameter)
        {
            return _canExecute == null || _canExecute();
        }

        /// <summary>Выполняет команду.</summary>
        public void Execute(object parameter)
        {
            _execute();
        }

        /// <summary>Принудительно обновляет состояние доступности команды.</summary>
        public void RaiseCanExecuteChanged()
        {
            CanExecuteChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}
