using System.Linq;
using Autodesk.Navisworks.Api;
using Autodesk.Navisworks.Api.Clash;

namespace SmartGroupClashes.ViewModels
{
    /// <summary>
    /// ViewModel-обёртка для одного теста коллизий в списке.
    /// </summary>
    public class ClashTestItemViewModel : ViewModelBase
    {
        private readonly ClashTest _clashTest;
        private bool _isSelected;

        /// <summary>Создаёт элемент для указанного теста коллизий.</summary>
        public ClashTestItemViewModel(ClashTest test)
        {
            _clashTest = test;
        }

        /// <summary>Отображаемое имя теста.</summary>
        public string DisplayName
        {
            get { return _clashTest.DisplayName; }
        }

        /// <summary>Исходный тест коллизий Navisworks.</summary>
        public ClashTest ClashTest
        {
            get { return _clashTest; }
        }

        /// <summary>Признак выбранности элемента в списке.</summary>
        public bool IsSelected
        {
            get { return _isSelected; }
            set { SetProperty(ref _isSelected, value); }
        }

        /// <summary>Краткое описание набора выбора A.</summary>
        public string SelectionAName
        {
            get { return GetSelectionDescription(_clashTest.SelectionA); }
        }

        /// <summary>Краткое описание набора выбора B.</summary>
        public string SelectionBName
        {
            get { return GetSelectionDescription(_clashTest.SelectionB); }
        }

        /// <summary>Формирует строку описания выбора для отображения в UI.</summary>
        private static string GetSelectionDescription(ClashSelection selection)
        {
            if (selection.Selection.HasSelectionSources)
            {
                string result = selection.Selection.SelectionSources.FirstOrDefault().ToString();
                if (result.Contains("lcop_selection_set_tree\\"))
                {
                    result = result.Replace("lcop_selection_set_tree\\", "");
                }

                if (selection.Selection.SelectionSources.Count > 1)
                {
                    result = result + " (и другие наборы выбора)";
                }

                return result;
            }

            if (selection.Selection.GetSelectedItems().Count == 0)
            {
                return "Элементы не выбраны.";
            }

            if (selection.Selection.GetSelectedItems().Count == 1)
            {
                return selection.Selection.GetSelectedItems().FirstOrDefault().DisplayName;
            }

            string names = selection.Selection.GetSelectedItems().FirstOrDefault().DisplayName;
            foreach (ModelItem item in selection.Selection.GetSelectedItems().Skip(1))
            {
                names = names + "; " + item.DisplayName;
            }

            return names;
        }
    }
}
