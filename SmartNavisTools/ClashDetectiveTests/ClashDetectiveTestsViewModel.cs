using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using Autodesk.Navisworks.Api.Clash;
using Microsoft.Win32;
using NavisworksApplication = Autodesk.Navisworks.Api.Application;

namespace SmartNavisTools
{
    /// <summary>
    /// Элемент списка проверок Clash Detective.
    /// </summary>
    internal sealed class TestItemViewModel : ViewModelBase
    {
        private bool _isSelected;

        public TestItemViewModel(ClashDetectiveTestsLogic.ClashTestInfo info)
        {
            DisplayName = info.DisplayName;
            Test = info.Test;
        }

        /// <summary>Отображаемое имя проверки.</summary>
        public string DisplayName { get; }

        /// <summary>Ссылка на ClashTest.</summary>
        public ClashTest Test { get; }

        /// <summary>Выбрана ли проверка.</summary>
        public bool IsSelected
        {
            get => _isSelected;
            set => SetProperty(ref _isSelected, value);
        }
    }

    /// <summary>
    /// ViewModel панели проверок Clash Detective.
    /// </summary>
    internal sealed class ClashDetectiveTestsViewModel : ViewModelBase, IDisposable
    {
        private string _filterText = string.Empty;
        private bool _useRegex;
        private string _filterSummary = "Показано: 0";
        private string _selectionSummary = "Выбрано проверок: 0";
        private string _projectHint = string.Empty;
        private string _busyStatusText = "Отправка отчётов в БД...";
        private int _totalTestCount;
        private bool _isSubscribed;
        private bool _updatingProjects;
        private bool _isSending;
        private bool _sendNewStatus = true;
        private bool _sendActiveStatus = true;
        private bool _sendReviewedStatus = true;
        private bool _sendApprovedStatus = true;
        private bool _sendResolvedStatus;
        private bool _sendImagesForNewOnly = true;
        private bool _sendWithoutImages;
        private bool _useNativeReportImages;
        private SpClashServiceClient.ProjectItem _selectedProject;
        private readonly Dispatcher _dispatcher;
        private List<ClashDetectiveTestsLogic.ClashTestInfo> _allTests =
            new List<ClashDetectiveTestsLogic.ClashTestInfo>();

        public ClashDetectiveTestsViewModel()
        {
            _dispatcher = Dispatcher.CurrentDispatcher;
            Tests = new ObservableCollection<TestItemViewModel>();
            Projects = new ObservableCollection<SpClashServiceClient.ProjectItem>();

            ClearFilterCommand = new RelayCommand(ExecuteClearFilter);
            RefreshCommand = new RelayCommand(ExecuteRefresh);
            UpdateTestsCommand = new RelayCommand(ExecuteUpdateTests, CanExecuteUpdateTests);
            ExportXmlCommand = new RelayCommand(ExecuteExportXml, CanExecuteExport);
            ExportHtmlCommand = new RelayCommand(ExecuteExportHtml, CanExecuteExport);
            SendReportCommand = new RelayCommand(ExecuteSendReport, CanExecuteExport);
            RefreshProjectsCommand = new RelayCommand(ExecuteRefreshProjects, CanExecuteWhenIdle);

            SubscribeToDocumentChanges();
            ApplyFilter();
            _dispatcher.BeginInvoke(new Action(LoadProjects));
        }

        /// <summary>Текст фильтра.</summary>
        public string FilterText
        {
            get => _filterText;
            set
            {
                if (SetProperty(ref _filterText, value))
                {
                    ApplyFilter();
                }
            }
        }

        /// <summary>Использовать ли регулярное выражение для фильтра.</summary>
        public bool UseRegex
        {
            get => _useRegex;
            set
            {
                if (SetProperty(ref _useRegex, value))
                {
                    ApplyFilter();
                }
            }
        }

        /// <summary>Текст с информацией о фильтрации.</summary>
        public string FilterSummary
        {
            get => _filterSummary;
            private set => SetProperty(ref _filterSummary, value);
        }

        /// <summary>Текст с информацией о выборе.</summary>
        public string SelectionSummary
        {
            get => _selectionSummary;
            private set => SetProperty(ref _selectionSummary, value);
        }

        /// <summary>Общее количество проверок.</summary>
        public int TotalTestCount
        {
            get => _totalTestCount;
            private set => SetProperty(ref _totalTestCount, value);
        }

        /// <summary>Отфильтрованная коллекция проверок.</summary>
        public ObservableCollection<TestItemViewModel> Tests { get; }

        /// <summary>Проекты SP-Service, доступные текущему пользователю.</summary>
        public ObservableCollection<SpClashServiceClient.ProjectItem> Projects { get; }

        /// <summary>Выбранный проект для отправки отчётов в БД.</summary>
        public SpClashServiceClient.ProjectItem SelectedProject
        {
            get { return _selectedProject; }
            set
            {
                if (SetProperty(ref _selectedProject, value) && !_updatingProjects)
                    PersistSelectedProject();
            }
        }

        /// <summary>Подсказка по подключению и списку проектов.</summary>
        public string ProjectHint
        {
            get { return _projectHint; }
            private set { SetProperty(ref _projectHint, value); }
        }

        /// <summary>Идёт ли отправка снимка на сервер.</summary>
        public bool IsSending
        {
            get { return _isSending; }
            private set
            {
                if (!SetProperty(ref _isSending, value))
                    return;

                OnPropertyChanged(nameof(IsIdle));
                UpdateTestsCommand.RaiseCanExecuteChanged();
                ExportXmlCommand.RaiseCanExecuteChanged();
                ExportHtmlCommand.RaiseCanExecuteChanged();
                SendReportCommand.RaiseCanExecuteChanged();
                RefreshProjectsCommand.RaiseCanExecuteChanged();
            }
        }

        /// <summary>Панель доступна, отправка не идёт.</summary>
        public bool IsIdle
        {
            get { return !_isSending; }
        }

        /// <summary>Текст на затемнении во время отправки.</summary>
        public string BusyStatusText
        {
            get { return _busyStatusText; }
            private set { SetProperty(ref _busyStatusText, value); }
        }

        /// <summary>Отправлять пересечения со статусом «Новые».</summary>
        public bool SendNewStatus
        {
            get { return _sendNewStatus; }
            set { SetProperty(ref _sendNewStatus, value); }
        }

        /// <summary>Отправлять пересечения со статусом «Активные».</summary>
        public bool SendActiveStatus
        {
            get { return _sendActiveStatus; }
            set { SetProperty(ref _sendActiveStatus, value); }
        }

        /// <summary>Отправлять пересечения со статусом «Проверенные».</summary>
        public bool SendReviewedStatus
        {
            get { return _sendReviewedStatus; }
            set { SetProperty(ref _sendReviewedStatus, value); }
        }

        /// <summary>Отправлять пересечения со статусом «Утверждённые».</summary>
        public bool SendApprovedStatus
        {
            get { return _sendApprovedStatus; }
            set { SetProperty(ref _sendApprovedStatus, value); }
        }

        /// <summary>Отправлять пересечения со статусом «Исправленные».</summary>
        public bool SendResolvedStatus
        {
            get { return _sendResolvedStatus; }
            set { SetProperty(ref _sendResolvedStatus, value); }
        }

        /// <summary>Снимать превью только для пересечений, у которых на сервере ещё нет картинки.</summary>
        public bool SendImagesForNewOnly
        {
            get { return _sendImagesForNewOnly; }
            set { SetProperty(ref _sendImagesForNewOnly, value); }
        }

        /// <summary>Не снимать и не слать картинки ни в БД, ни в XML/HTML.</summary>
        public bool SendWithoutImages
        {
            get { return _sendWithoutImages; }
            set
            {
                if (SetProperty(ref _sendWithoutImages, value))
                {
                    OnPropertyChanged(nameof(CanSendImagesForNewOnly));
                    OnPropertyChanged(nameof(CanUseNativeReportImages));
                    if (value && UseNativeReportImages)
                        UseNativeReportImages = false;
                }
            }
        }

        /// <summary>
        /// Тестовый режим: картинки брать из Native Write Report (XML), а не через TestsImageForResult.
        /// </summary>
        public bool UseNativeReportImages
        {
            get { return _useNativeReportImages; }
            set
            {
                if (SetProperty(ref _useNativeReportImages, value))
                    OnPropertyChanged(nameof(CanSendImagesForNewOnly));
            }
        }

        /// <summary>Галочка «только для новых» доступна, пока не включён режим без картинок.</summary>
        public bool CanSendImagesForNewOnly
        {
            get { return !_sendWithoutImages; }
        }

        /// <summary>Галочка Write Report доступна, пока не включён режим без картинок.</summary>
        public bool CanUseNativeReportImages
        {
            get { return !_sendWithoutImages; }
        }

        /// <summary>Команда очистки фильтра.</summary>
        public RelayCommand ClearFilterCommand { get; }

        /// <summary>Команда обновления списка.</summary>
        public RelayCommand RefreshCommand { get; }

        /// <summary>Команда запуска выбранных проверок.</summary>
        public RelayCommand UpdateTestsCommand { get; }

        /// <summary>Команда экспорта в XML.</summary>
        public RelayCommand ExportXmlCommand { get; }

        /// <summary>Команда экспорта в HTML.</summary>
        public RelayCommand ExportHtmlCommand { get; }

        /// <summary>Команда отправки снимка проверок в SP-Service.</summary>
        public RelayCommand SendReportCommand { get; }

        /// <summary>Команда загрузки списка проектов с сервера.</summary>
        public RelayCommand RefreshProjectsCommand { get; }

        /// <summary>Очищает текст фильтра и флаг regex.</summary>
        private void ExecuteClearFilter()
        {
            FilterText = string.Empty;
            UseRegex = false;
        }

        /// <summary>Обновляет список проверок из Navisworks.</summary>
        private void ExecuteRefresh()
        {
            ApplyFilter();
        }

        /// <summary>Применяет фильтр к списку проверок.</summary>
        private void ApplyFilter()
        {
            try
            {
                var selectedNames = new HashSet<string>(
                    Tests.Where(t => t.IsSelected).Select(t => t.DisplayName),
                    StringComparer.Ordinal);

                _allTests = new List<ClashDetectiveTestsLogic.ClashTestInfo>(
                    ClashDetectiveTestsLogic.GetTests());
                TotalTestCount = _allTests.Count;

                string pattern = _filterText;
                bool useRegex = _useRegex;
                bool hasFilter = !string.IsNullOrWhiteSpace(pattern);

                if (hasFilter && useRegex)
                {
                    ClashDetectiveTestsLogic.MatchesFilter(string.Empty, pattern, true, out string regexError);
                    if (regexError != null)
                    {
                        Tests.Clear();
                        FilterSummary = "Ошибка regex: " + regexError;
                        UpdateSelectionSummary();
                        return;
                    }
                }

                Tests.Clear();
                int visibleCount = 0;

                foreach (ClashDetectiveTestsLogic.ClashTestInfo test in _allTests)
                {
                    if (!ClashDetectiveTestsLogic.MatchesFilter(test.DisplayName, pattern, useRegex, out _))
                    {
                        continue;
                    }

                    var item = new TestItemViewModel(test);
                    if (selectedNames.Contains(test.DisplayName))
                    {
                        item.IsSelected = true;
                    }

                    item.PropertyChanged += (s, e) =>
                    {
                        if (e.PropertyName == nameof(TestItemViewModel.IsSelected))
                        {
                            UpdateSelectionSummary();
                        }
                    };
                    Tests.Add(item);
                    visibleCount++;
                }

                if (hasFilter)
                {
                    FilterSummary = "Показано: " + visibleCount + " из " + _totalTestCount;
                }
                else
                {
                    FilterSummary = "Показано: " + visibleCount;
                }

                UpdateSelectionSummary();
            }
            catch (Exception ex)
            {
                FilterSummary = "Ошибка: " + ex.Message;
            }
        }

        /// <summary>Обновляет сводку выбранных проверок.</summary>
        private void UpdateSelectionSummary()
        {
            int selectedCount = Tests.Count(t => t.IsSelected);
            int visibleCount = Tests.Count;

            if (visibleCount < _totalTestCount && _totalTestCount > 0)
            {
                SelectionSummary = "Выбрано: " + selectedCount + " (видно " + visibleCount + " из " + _totalTestCount + ")";
            }
            else
            {
                SelectionSummary = "Выбрано проверок: " + selectedCount;
            }

            UpdateTestsCommand.RaiseCanExecuteChanged();
            ExportXmlCommand.RaiseCanExecuteChanged();
            ExportHtmlCommand.RaiseCanExecuteChanged();
            SendReportCommand.RaiseCanExecuteChanged();
        }

        /// <summary>Проверяет наличие выбранных проверок.</summary>
        private bool CanExecuteUpdateTests()
        {
            return !_isSending && Tests.Any(t => t.IsSelected);
        }

        /// <summary>Проверяет наличие выбранных проверок для экспорта.</summary>
        private bool CanExecuteExport()
        {
            return !_isSending && Tests.Any(t => t.IsSelected);
        }

        /// <summary>Команды панели, которые нельзя жать во время отправки.</summary>
        private bool CanExecuteWhenIdle()
        {
            return !_isSending;
        }

        /// <summary>Запускает обновление выбранных проверок.</summary>
        private void ExecuteUpdateTests()
        {
            try
            {
                ClashTest[] selectedTests = Tests
                    .Where(t => t.IsSelected)
                    .Select(t => t.Test)
                    .ToArray();

                ClashDetectiveTestsLogic.RunTestsResult result =
                    ClashDetectiveTestsLogic.RunTests(selectedTests);

                if (!result.Success)
                {
                    MessageBox.Show(
                        result.ErrorMessage ?? "Не удалось обновить проверки.",
                        "Ошибка",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                    return;
                }

                string message = result.WasCanceled
                    ? "Обновление прервано. Обновлено проверок: " + result.RunCount + "."
                    : "Обновлено проверок: " + result.RunCount + ".";

                MessageBox.Show(message, "Результат", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "Ошибка: " + ex.Message,
                    "Ошибка",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        /// <summary>Экспортирует выбранные проверки в XML.</summary>
        private void ExecuteExportXml()
        {
            ExecuteExport(ClashDetectiveTestsLogic.ExportFormat.Xml);
        }

        /// <summary>Экспортирует выбранные проверки в HTML.</summary>
        private void ExecuteExportHtml()
        {
            ExecuteExport(ClashDetectiveTestsLogic.ExportFormat.Html);
        }

        /// <summary>Загружает список проектов по кнопке.</summary>
        private void ExecuteRefreshProjects()
        {
            LoadProjects();
        }

        /// <summary>Загружает проекты текущего пользователя и восстанавливает сохранённый выбор.</summary>
        private void LoadProjects()
        {
            try
            {
                SpClashServiceClient client;
                string error;
                if (!SpClashServiceSession.TryOpen(out client, out error))
                {
                    _updatingProjects = true;
                    try
                    {
                        Projects.Clear();
                        SelectedProject = null;
                    }
                    finally
                    {
                        _updatingProjects = false;
                    }

                    ProjectHint = error ?? "Не удалось подключиться к серверу.";
                    return;
                }

                Guid savedId = Guid.Empty;
                try
                {
                    SpServiceSettings settings = SpServiceSettingsStore.Load();
                    Guid parsed;
                    if (Guid.TryParse(settings.ProjectId, out parsed))
                        savedId = parsed;
                }
                catch
                {
                    savedId = Guid.Empty;
                }

                using (client)
                {
                    List<SpClashServiceClient.ProjectItem> list = client.GetProjects();
                    _updatingProjects = true;
                    try
                    {
                        Projects.Clear();
                        SpClashServiceClient.ProjectItem match = null;
                        for (int i = 0; i < list.Count; i++)
                        {
                            SpClashServiceClient.ProjectItem project = list[i];
                            if (project == null)
                                continue;

                            Projects.Add(project);
                            if (savedId != Guid.Empty && project.Id == savedId)
                                match = project;
                        }

                        if (match != null)
                            SelectedProject = match;
                        else if (Projects.Count > 0)
                            SelectedProject = Projects[0];
                        else
                            SelectedProject = null;
                    }
                    finally
                    {
                        _updatingProjects = false;
                    }

                    PersistSelectedProject();

                    if (Projects.Count == 0)
                        ProjectHint = "Нет доступных проектов. Добавьте пользователя в проект в админке.";
                    else
                        ProjectHint = string.Empty;
                }
            }
            catch (Exception ex)
            {
                ProjectHint = "Не удалось загрузить проекты: " + ex.Message;
            }
        }

        /// <summary>Пишет выбранный проект в настройки, чтобы им пользовались другие вкладки.</summary>
        private void PersistSelectedProject()
        {
            try
            {
                if (_selectedProject == null || _selectedProject.Id == Guid.Empty)
                    return;

                SpServiceSettingsStore.SaveProjectId(_selectedProject.Id.ToString("D"));
            }
            catch
            {
            }
        }

        /// <summary>Отправляет снимок выбранных проверок в SP-Service.</summary>
        private void ExecuteSendReport()
        {
            try
            {
                if (_isSending)
                    return;

                ClashTest[] selectedTests = Tests
                    .Where(t => t.IsSelected)
                    .Select(t => t.Test)
                    .ToArray();

                if (selectedTests.Length == 0)
                {
                    MessageBox.Show(
                        "Выберите хотя бы одну проверку.",
                        "Нет выбора",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                    return;
                }

                if (SelectedProject == null || SelectedProject.Id == Guid.Empty)
                {
                    MessageBox.Show(
                        "Выберите проект.",
                        "Отправка отчёта",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                    return;
                }

                SpServiceSettings settings = SpServiceSettingsStore.Load();
                if (settings == null || !settings.HasConnection)
                {
                    MessageBox.Show(
                        "Сначала укажите адрес сервера, логин и пароль в Настройках.",
                        "Отправка отчёта",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                    return;
                }

                Guid projectId = SelectedProject.Id;
                HashSet<ClashResultStatus> enabledStatuses = GetEnabledSendStatuses();
                if (enabledStatuses.Count == 0)
                {
                    MessageBox.Show(
                        "Выберите хотя бы один статус для отправки в БД.",
                        "Отправка отчёта",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                    return;
                }

                BusyStatusText = "Сбор пересечений...";
                IsSending = true;
                _dispatcher.BeginInvoke(
                    new Action(() => ContinueSendReport(selectedTests, projectId, enabledStatuses)),
                    DispatcherPriority.Background);
            }
            catch (Exception ex)
            {
                FinishSending();
                MessageBox.Show(
                    "Ошибка отправки отчёта: " + ex.Message,
                    "Ошибка",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// Логин, GUID существующих превью, сбор снимка на UI-потоке, затем POST в фоне.
        /// </summary>
        private void ContinueSendReport(
            ClashTest[] selectedTests,
            Guid projectId,
            HashSet<ClashResultStatus> enabledStatuses)
        {
            SpClashServiceClient client = null;
            try
            {
                BusyStatusText = "Подключение к серверу...";
                string error;
                if (!SpClashServiceSession.TryOpen(out client, out error))
                {
                    FinishSending();
                    MessageBox.Show(
                        error ?? "Не удалось подключиться к серверу.",
                        "Отправка отчёта",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                    return;
                }

                ClashImageSendMode imageMode = ClashImageSendMode.All;
                HashSet<string> skipPreviewGuids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                if (SendWithoutImages)
                {
                    imageMode = ClashImageSendMode.None;
                }
                else if (SendImagesForNewOnly)
                {
                    imageMode = ClashImageSendMode.NewOnly;
                    try
                    {
                        BusyStatusText = "Поиск пересечений с уже загруженными картинками...";
                        string reportName = SpClashSnapshotBuilder.BuildReportName(selectedTests);
                        List<SpClashServiceClient.ReportItem> reports = client.GetReports(projectId);
                        SpClashServiceClient.ReportItem match = null;
                        for (int i = 0; i < reports.Count; i++)
                        {
                            if (reports[i] != null
                                && string.Equals(reports[i].Name, reportName, StringComparison.OrdinalIgnoreCase))
                            {
                                match = reports[i];
                                break;
                            }
                        }

                        if (match != null && match.Id != Guid.Empty)
                        {
                            List<string> guids = client.GetExistingClashGuids(match.Id);
                            for (int i = 0; i < guids.Count; i++)
                            {
                                if (!string.IsNullOrWhiteSpace(guids[i]))
                                    skipPreviewGuids.Add(guids[i]);
                            }
                        }
                    }
                    catch
                    {
                    }
                }

                BusyStatusText = imageMode == ClashImageSendMode.None
                    ? "Сбор пересечений..."
                    : (UseNativeReportImages
                        ? "Сбор пересечений (картинки через Write Report)..."
                        : "Сбор пересечений и изображений...");

                Stopwatch totalWatch = Stopwatch.StartNew();
                string timingNote;
                Dictionary<string, object> snapshot;

                if (imageMode != ClashImageSendMode.None && UseNativeReportImages)
                {
                    snapshot = BuildSnapshotViaNativeReport(
                        selectedTests,
                        enabledStatuses,
                        imageMode,
                        skipPreviewGuids,
                        out timingNote);
                    if (snapshot == null)
                    {
                        if (client != null)
                            client.Dispose();
                        FinishSending();
                        return;
                    }
                }
                else
                {
                    Stopwatch captureWatch = Stopwatch.StartNew();
                    snapshot = SpClashSnapshotBuilder.Build(
                        selectedTests,
                        enabledStatuses,
                        imageMode,
                        skipPreviewGuids,
                        (done, total) =>
                        {
                            _dispatcher.BeginInvoke(new Action(() =>
                            {
                                BusyStatusText = "Снимки: " + done + " / " + total;
                            }));
                        });
                    captureWatch.Stop();
                    timingNote = imageMode == ClashImageSendMode.None
                        ? "Режим: без изображений"
                        : ("Режим: TestsImageForResult\nСъёмка превью: "
                            + FormatSeconds(captureWatch.Elapsed));
                }

                totalWatch.Stop();
                timingNote = timingNote + "\nВсего сбор: " + FormatSeconds(totalWatch.Elapsed);

                int count = 0;
                object resultsObj;
                if (snapshot.TryGetValue("results", out resultsObj))
                {
                    System.Collections.ICollection collection = resultsObj as System.Collections.ICollection;
                    if (collection != null)
                        count = collection.Count;
                }

                if (count == 0)
                {
                    if (client != null)
                        client.Dispose();
                    FinishSending();
                    MessageBox.Show(
                        "Нет пересечений с выбранными статусами.",
                        "Отправка отчёта",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                    return;
                }

                PersistSelectedProject();
                BusyStatusText = "Отправка на сервер...";
                SpClashServiceClient clientToSend = client;
                client = null;
                string timingToShow = timingNote;
                ThreadPool.QueueUserWorkItem(_ => SendSnapshotToServer(clientToSend, snapshot, projectId, count, timingToShow));
            }
            catch (Exception ex)
            {
                if (client != null)
                    client.Dispose();
                FinishSending();
                MessageBox.Show(
                    "Ошибка отправки отчёта: " + ex.Message,
                    "Ошибка",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// Снимок без картинок + Native Write Report (XML) + разбор jpg из отчёта.
        /// </summary>
        private Dictionary<string, object> BuildSnapshotViaNativeReport(
            ClashTest[] selectedTests,
            HashSet<ClashResultStatus> enabledStatuses,
            ClashImageSendMode imageMode,
            HashSet<string> skipPreviewGuids,
            out string timingNote)
        {
            timingNote = "Режим: Write Report (XML)";

            MessageBoxResult confirm = MessageBox.Show(
                "Тестовый режим: картинки из Native Write Report.\n\n"
                + "1) Откройте Clash Detective → вкладка Report.\n"
                + "2) Сейчас запустится Write Report (XML) — сохраните отчёт с картинками.\n"
                + "3) Затем укажите сохранённый XML в следующем окне.\n\n"
                + "Будет замерено время Write Report и разбора картинок.",
                "Write Report → БД",
                MessageBoxButton.OKCancel,
                MessageBoxImage.Information);
            if (confirm != MessageBoxResult.OK)
            {
                timingNote = null;
                return null;
            }

            BusyStatusText = "Сбор пересечений...";
            Dictionary<string, object> snapshot = SpClashSnapshotBuilder.Build(
                selectedTests,
                enabledStatuses,
                ClashImageSendMode.None,
                null,
                null);

            BusyStatusText = "Write Report (ожидание сохранения)...";
            Stopwatch writeWatch = Stopwatch.StartNew();
            ClashDetectiveTestsLogic.ExportTestsResult exportResult =
                ClashDetectiveTestsLogic.ExportTests(
                    selectedTests,
                    ClashDetectiveTestsLogic.ExportFormat.Xml,
                    includeImages: true);
            writeWatch.Stop();

            if (!exportResult.Success)
            {
                MessageBox.Show(
                    exportResult.ErrorMessage ?? "Write Report не выполнен.",
                    "Write Report",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return null;
            }

            OpenFileDialog dialog = new OpenFileDialog
            {
                Title = "Укажите XML, сохранённый Write Report",
                Filter = "Clash Report XML (*.xml)|*.xml|Все файлы (*.*)|*.*",
                Multiselect = true,
                CheckFileExists = true
            };

            if (dialog.ShowDialog() != true || dialog.FileNames == null || dialog.FileNames.Length == 0)
            {
                MessageBox.Show(
                    "XML не выбран — отправка отменена.",
                    "Write Report",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return null;
            }

            BusyStatusText = "Разбор картинок из Write Report...";
            Stopwatch harvestWatch = Stopwatch.StartNew();
            SpClashNativeReportImageHarvester.HarvestResult harvest =
                SpClashNativeReportImageHarvester.AttachImages(
                    snapshot,
                    dialog.FileNames,
                    imageMode,
                    skipPreviewGuids);
            harvestWatch.Stop();

            if (!string.IsNullOrWhiteSpace(harvest.ErrorMessage))
            {
                MessageBox.Show(
                    harvest.ErrorMessage,
                    "Write Report",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return null;
            }

            timingNote = "Режим: Write Report (XML)\n"
                + "Write Report (вкл. диалог сохранения): " + FormatSeconds(writeWatch.Elapsed) + "\n"
                + "Разбор картинок: " + FormatSeconds(harvestWatch.Elapsed) + "\n"
                + "Картинок прикреплено: " + harvest.AttachedCount
                + (harvest.MissingCount > 0 ? (", не найдено: " + harvest.MissingCount) : string.Empty);

            if (!string.IsNullOrWhiteSpace(exportResult.WarningMessage))
                timingNote = timingNote + "\n" + exportResult.WarningMessage;

            return snapshot;
        }

        /// <summary>Формат длительности для сравнения режимов.</summary>
        private static string FormatSeconds(TimeSpan elapsed)
        {
            return elapsed.TotalSeconds.ToString("0.0", CultureInfo.InvariantCulture) + " с";
        }

        /// <summary>POST снимка вне UI-потока, чтобы крутился ProgressBar.</summary>
        private void SendSnapshotToServer(
            SpClashServiceClient client,
            Dictionary<string, object> snapshot,
            Guid projectId,
            int count,
            string timingNote)
        {
            try
            {
                using (client)
                {
                    client.UpsertReport(projectId, snapshot);
                }

                _dispatcher.BeginInvoke(new Action(() =>
                {
                    FinishSending();
                    string message = "Отчёт отправлен на сервер.\nПересечений: " + count + ".";
                    if (!string.IsNullOrWhiteSpace(timingNote))
                        message = message + "\n\n" + timingNote;
                    MessageBox.Show(
                        message,
                        "Отправка отчёта",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                }));
            }
            catch (Exception ex)
            {
                _dispatcher.BeginInvoke(new Action(() =>
                {
                    FinishSending();
                    MessageBox.Show(
                        "Ошибка отправки отчёта: " + ex.Message,
                        "Ошибка",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                }));
            }
        }

        /// <summary>Собирает статусы, отмеченные для отправки в БД.</summary>
        private HashSet<ClashResultStatus> GetEnabledSendStatuses()
        {
            var statuses = new HashSet<ClashResultStatus>();
            try
            {
                if (SendNewStatus)
                    statuses.Add(ClashResultStatus.New);
                if (SendActiveStatus)
                    statuses.Add(ClashResultStatus.Active);
                if (SendReviewedStatus)
                    statuses.Add(ClashResultStatus.Reviewed);
                if (SendApprovedStatus)
                    statuses.Add(ClashResultStatus.Approved);
                if (SendResolvedStatus)
                    statuses.Add(ClashResultStatus.Resolved);
            }
            catch
            {
            }

            return statuses;
        }

        /// <summary>Снимает затемнение после завершения отправки.</summary>
        private void FinishSending()
        {
            IsSending = false;
            BusyStatusText = "Отправка отчётов в БД...";
        }

        /// <summary>Выполняет экспорт в указанном формате.</summary>
        private void ExecuteExport(ClashDetectiveTestsLogic.ExportFormat format)
        {
            try
            {
                ClashTest[] selectedTests = Tests
                    .Where(t => t.IsSelected)
                    .Select(t => t.Test)
                    .ToArray();

                if (selectedTests.Length == 0)
                {
                    MessageBox.Show(
                        "Выберите хотя бы одну проверку.",
                        "Нет выбора",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                    return;
                }

                ClashDetectiveTestsLogic.ExportTestsResult result =
                    ClashDetectiveTestsLogic.ExportTests(selectedTests, format, !SendWithoutImages);

                if (!result.Success)
                {
                    MessageBox.Show(
                        result.ErrorMessage ?? "Не удалось экспортировать отчёты.",
                        "Ошибка экспорта",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                    return;
                }

                string formatName = format == ClashDetectiveTestsLogic.ExportFormat.Xml ? "XML" : "HTML";
                string imagesNote = SendWithoutImages
                    ? "\nКартинки в отчёте отключены."
                    : "\n\nУкажите файл в диалоге Navisworks. Содержимое отчёта — по настройкам вкладки Report.";
                if (!string.IsNullOrWhiteSpace(result.WarningMessage))
                    imagesNote = "\n" + result.WarningMessage + imagesNote;

                MessageBox.Show(
                    "Запущен стандартный экспорт Clash Detective (Write Report).\n"
                    + "Проверок: " + result.ExportedCount
                    + "\nФормат: " + formatName
                    + imagesNote,
                    "Экспорт",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "Ошибка экспорта: " + ex.Message,
                    "Ошибка",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        /// <summary>Подписывается на изменения документа Navisworks.</summary>
        private void SubscribeToDocumentChanges()
        {
            if (_isSubscribed || NavisworksApplication.MainDocument == null)
            {
                return;
            }

            NavisworksApplication.MainDocument.Database.Changed += OnDocumentChanged;

            DocumentClashTests testsData = NavisworksApplication.MainDocument.GetClash()?.TestsData;
            if (testsData != null)
            {
                testsData.Changed += OnDocumentChanged;
            }

            _isSubscribed = true;
        }

        /// <summary>Отписывается от изменений документа Navisworks.</summary>
        private void UnsubscribeFromDocumentChanges()
        {
            if (!_isSubscribed)
            {
                return;
            }

            Autodesk.Navisworks.Api.Document document = NavisworksApplication.MainDocument;
            if (document != null)
            {
                document.Database.Changed -= OnDocumentChanged;

                DocumentClashTests testsData = document.GetClash()?.TestsData;
                if (testsData != null)
                {
                    testsData.Changed -= OnDocumentChanged;
                }
            }

            _isSubscribed = false;
        }

        /// <summary>Обработчик изменения документа с маршалингом в UI-поток.</summary>
        private void OnDocumentChanged(object sender, EventArgs e)
        {
            _dispatcher.BeginInvoke(new Action(ApplyFilter));
        }

        /// <summary>Освобождает подписки.</summary>
        public void Dispose()
        {
            UnsubscribeFromDocumentChanges();
        }
    }
}
