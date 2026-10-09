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
            LoadFromServerCommand = new RelayCommand(ExecuteLoadFromServer);
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

        /// <summary>Команда загрузки Clash Report XML с SP-Service.</summary>
        public RelayCommand LoadFromServerCommand { get; }

        /// <summary>Команда обновления статусов в Clash Detective.</summary>
        public RelayCommand UpdateCommand { get; }

        /// <summary>Загружает Clash Report XML с сервера и подставляет его в импорт.</summary>
        private void ExecuteLoadFromServer()
        {
            string tempPath = null;
            try
            {
                SpClashServiceClient client;
                Guid projectId;
                string error;
                if (!SpClashServiceSession.TryOpen(out client, out projectId, out error))
                {
                    MessageBox.Show(
                        error ?? "Не удалось подключиться к серверу.",
                        "Загрузка с сервера",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                    return;
                }

                using (client)
                {
                    System.Collections.Generic.List<SpClashServiceClient.ReportItem> reports =
                        client.GetReports(projectId);
                    if (reports.Count == 0)
                    {
                        MessageBox.Show(
                            "На сервере нет отчётов пересечений для выбранного проекта.",
                            "Загрузка с сервера",
                            MessageBoxButton.OK,
                            MessageBoxImage.Information);
                        return;
                    }

                    SpClashServiceClient.ReportItem selected;
                    using (SpClashReportPickForm picker = new SpClashReportPickForm(reports))
                    {
                        if (picker.ShowDialog() != System.Windows.Forms.DialogResult.OK
                            || picker.SelectedReport == null)
                        {
                            return;
                        }

                        selected = picker.SelectedReport;
                    }

                    byte[] xmlBytes = client.ExportXml(selected.Id);
                    tempPath = System.IO.Path.Combine(
                        System.IO.Path.GetTempPath(),
                        "sp-clash-" + selected.Id.ToString("N") + ".xml");
                    System.IO.File.WriteAllBytes(tempPath, xmlBytes);
                }

                ApplyImportedFiles(new[] { tempPath });
                if (!_hasLoadedFiles)
                {
                    try
                    {
                        if (System.IO.File.Exists(tempPath))
                            System.IO.File.Delete(tempPath);
                    }
                    catch
                    {
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "Ошибка загрузки с сервера: " + ex.Message,
                    "Ошибка",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                try
                {
                    if (!string.IsNullOrEmpty(tempPath) && System.IO.File.Exists(tempPath))
                        System.IO.File.Delete(tempPath);
                }
                catch
                {
                }
            }
        }

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

                ApplyImportedFiles(dialog.FileNames);
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

        /// <summary>Разбирает XML и заполняет предпросмотр.</summary>
        private void ApplyImportedFiles(string[] fileNames)
        {
            ClashStatusUpdaterLogic.ImportResult result =
                ClashStatusUpdaterLogic.ImportFiles(fileNames);

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

            if (!ConfirmPartialExport(fileNames[0]))
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

                if (ClashStatusUpdaterLogic.IsSpServiceClashReport(document))
                {
                    return true;
                }

                bool hasReviewedInSummary = ClashStatusUpdaterLogic.DocumentHasPositiveSummary(document, "reviewed");
                bool hasApprovedInSummary = ClashStatusUpdaterLogic.DocumentHasPositiveSummary(document, "approved");
                bool hasReviewedInResults = ClashStatusUpdaterLogic.DocumentHasResultStatus(document, "reviewed");
                bool hasApprovedInResults = ClashStatusUpdaterLogic.DocumentHasResultStatus(document, "approved");

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

                if (hasReviewedInSummary && hasApprovedInSummary &&
                    !hasReviewedInResults && !hasApprovedInResults)
                {
                    return MessageBox.Show(
                        "В summary есть Reviewed и Approved, но в XML нет таких пересечений (часто из‑за фильтра Included Clashes или статуса в resultstatus). Продолжить загрузку?",
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
    }
}
