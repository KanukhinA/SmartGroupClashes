using System.Windows.Controls;
using SmartGroupClashes.ViewModels;

namespace SmartGroupClashes
{
    /// <summary>
    /// Код-бихайнд панели группировки коллизий. Вся логика находится в <see cref="SmartGroupClashesViewModel"/>.
    /// </summary>
    public partial class SmartGroupClashesInterface : UserControl
    {
        /// <summary>Создаёт панель и назначает ViewModel в качестве DataContext.</summary>
        public SmartGroupClashesInterface()
        {
            InitializeComponent();
            DataContext = new SmartGroupClashesViewModel();
        }
    }
}
