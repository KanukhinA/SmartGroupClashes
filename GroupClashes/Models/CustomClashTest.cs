using System.Linq;
using Autodesk.Navisworks.Api;
using Autodesk.Navisworks.Api.Clash;

namespace SmartGroupClashes.Models
{
    /// <summary>
    /// Обёртка над <see cref="ClashTest"/> для обратной совместимости.
    /// </summary>
    public class CustomClashTest
    {
        private readonly ClashTest _clashTest;

        /// <summary>Создаёт обёртку для указанного теста коллизий.</summary>
        public CustomClashTest(ClashTest test)
        {
            _clashTest = test;
        }

        /// <summary>Отображаемое имя теста.</summary>
        public string DisplayName { get { return _clashTest.DisplayName; } }

        /// <summary>Исходный тест коллизий.</summary>
        public ClashTest ClashTest { get { return _clashTest; } }

        /// <summary>Краткое описание набора выбора A.</summary>
        public string SelectionAName
        {
            get { return GetSelectedItem(_clashTest.SelectionA); }
        }

        /// <summary>Краткое описание набора выбора B.</summary>
        public string SelectionBName
        {
            get { return GetSelectedItem(_clashTest.SelectionB); }
        }

        /// <summary>Формирует строку описания источника выбора.</summary>
        private static string GetSelectedItem(ClashSelection selection)
        {
            string result = "";
            if (selection.Selection.HasSelectionSources)
            {
                result = selection.Selection.SelectionSources.FirstOrDefault().ToString();
                if (result.Contains("lcop_selection_set_tree\\"))
                {
                    result = result.Replace("lcop_selection_set_tree\\", "");
                }

                if (selection.Selection.SelectionSources.Count > 1)
                {
                    result = result + " (и другие наборы выбора)";
                }
            }
            else if (selection.Selection.GetSelectedItems().Count == 0)
            {
                result = "Элементы не выбраны.";
            }
            else if (selection.Selection.GetSelectedItems().Count == 1)
            {
                result = selection.Selection.GetSelectedItems().FirstOrDefault().DisplayName;
            }
            else
            {
                result = selection.Selection.GetSelectedItems().FirstOrDefault().DisplayName;
                foreach (ModelItem item in selection.Selection.GetSelectedItems().Skip(1))
                {
                    result = result + "; " + item.DisplayName;
                }
            }

            return result;
        }
    }
}
