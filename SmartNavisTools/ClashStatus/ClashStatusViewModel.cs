using System;
using System.Windows;

namespace SmartNavisTools
{
    /// <summary>
    /// ViewModel панели импорта Clash Report XML и обновления статусов.
    /// </summary>
    internal sealed class ClashStatusViewModel : ViewModelBase
    {
        private string _previewText = string.Empty;
        private bool _hasLoadedFiles;
        private string _summaryText = string.Empty;
        private string[] _loadedFiles = Array.Empty<string>();

        public ClashStatusViewModel()
        {
            ImportCommand = new RelayCommand(ExecuteImport);
            UpdateCommand = new RelayCommand(ExecuteUpdate, CanExecuteUpdate);
        }

        /// <summary>Текст предпросмотра импортированных данных.</summary>
        public string PreviewText
        {
            get => _previewText;
            set => SetProperty(ref _previewText, value);
        }

        /// <summary>Признак наличия загруженных файлов.</summary>
        public bool HasLoadedFiles
        {
            get => _hasLoadedFiles;
            private set
            {
                if (SetProperty(ref _hasLoadedFiles, value))
                {
                    UpdateCommand.RaiseCanExecuteChanged();
                }
            }
        }

        /// <summary>Сводка по импорту.</summary>
        public string SummaryText
        {
            get => _summaryText;
            set => SetProperty(ref _summaryText, value);
        }

        /// <summary>Команда импорта XML-файлов.</summary>
        public RelayCommand ImportCommand { get; }

        /// <summary>Команда обновления статусов в Clash Detective.</summary>
        public RelayCommand UpdateCommand { get; }

        /// <summary>Выполняет импорт выбранных XML-файлов.</summary>
        private void ExecuteImport()
        {
            try
            {
                var dialog = new Microsoft.Win32.OpenFileDialog
                {
                    Multiselect = true,
                    Filter = "XML (*.xml)|*.xml"
                };

                if (dialog.ShowDialog() != true)
                {
                    return;
                }

                ClashStatusUpdaterLogic.ImportResult result =
                    ClashStatusUpdaterLogic.ImportFiles(dialog.FileNames);

                if (!result.Success)
                {
                    PreviewText = string.Empty;
                    _loadedFiles = Array.Empty<string>();
                    HasLoadedFiles = false;
                    SummaryText = string.Empty;
                    MessageBox.Show(
                        result.ErrorMessage,
                        "Ошибка импорта",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                    return;
                }

                if (!ConfirmPartialExport(dialog.FileNames[0]))
                {
                    PreviewText = string.Empty;
                    _loadedFiles = Array.Empty<string>();
                    HasLoadedFiles = false;
                    SummaryText = string.Empty;
                    return;
                }

                _loadedFiles = result.LoadedFiles;
                HasLoadedFiles = _loadedFiles.Length > 0;
                PreviewText = result.PreviewText;

                string message = result.LoadedFiles.Length == 1
                    ? "Загружен 1 XML-файл."
                    : "Загружено файлов: " + result.LoadedFiles.Length + ".";
                SummaryText = message;
                MessageBox.Show(message, "Импорт", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "Ошибка при импорте: " + ex.Message,
                    "Ошибка",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        /// <summary>Проверяет возможность обновления статусов.</summary>
        private bool CanExecuteUpdate()
        {
            return _hasLoadedFiles && _loadedFiles.Length > 0;
        }

        /// <summary>Применяет загруженные данные к Clash Detective.</summary>
        private void ExecuteUpdate()
        {
            try
            {
                if (_loadedFiles.Length == 0 || string.IsNullOrWhiteSpace(_previewText))
                {
                    MessageBox.Show(
                        "Сначала загрузите файл Clash Report XML.",
                        "Нет данных",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                    return;
                }

                ClashStatusUpdaterLogic.UpdateResult result =
                    ClashStatusUpdaterLogic.ApplyLoadedFiles(_loadedFiles);

                if (!result.Success)
                {
                    MessageBox.Show(
                        result.ErrorMessage,
                        "Ошибка обновления",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                    return;
                }

                MessageBox.Show("Готово.", "Результат", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "Ошибка при обновлении: " + ex.Message,
                    "Ошибка",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        /// <summary>Проверяет частичный экспорт и запрашивает подтверждение у пользователя.</summary>
        private static bool ConfirmPartialExport(string firstFilePath)
        {
            try
            {
                var document = new System.Xml.XmlDocument();
                document.Load(firstFilePath);

                bool hasReviewedInSummary = HasPositiveSummary(document, "reviewed");
                bool hasApprovedInSummary = HasPositiveSummary(document, "approved");
                bool hasReviewedInResults = HasStatus(document, "reviewed");
                bool hasApprovedInResults = HasStatus(document, "approved");

                if (hasReviewedInSummary && hasApprovedInSummary &&
                    hasReviewedInResults && !hasApprovedInResults)
                {
                    return MessageBox.Show(
                        "В Included Clashes Report не отмечен «Approved». Продолжить?",
                        "Подтверждение",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Question) == MessageBoxResult.Yes;
                }

                if (hasReviewedInSummary && hasApprovedInSummary &&
                    !hasReviewedInResults && hasApprovedInResults)
                {
                    return MessageBox.Show(
                        "В Included Clashes Report не отмечен «Reviewed». Продолжить?",
                        "Подтверждение",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Question) == MessageBoxResult.Yes;
                }
            }
            catch
            {
                return false;
            }

            return true;
        }

        /// <summary>Проверяет наличие указанного статуса в результатах.</summary>
        private static bool HasStatus(System.Xml.XmlDocument document, string status)
        {
            foreach (System.Xml.XmlElement element in document.GetElementsByTagName("clashresult"))
            {
                if (string.Equals(element.GetAttribute("status"), status, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>Проверяет наличие положительного значения атрибута в summary.</summary>
        private static bool HasPositiveSummary(System.Xml.XmlDocument document, string attributeName)
        {
            foreach (System.Xml.XmlNode node in document.GetElementsByTagName("summary"))
            {
                System.Xml.XmlAttribute attribute = node.Attributes?[attributeName];
                if (attribute != null && int.TryParse(attribute.Value, out int value) && value > 0)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
