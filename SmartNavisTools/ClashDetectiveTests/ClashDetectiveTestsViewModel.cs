using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Threading;
using Autodesk.Navisworks.Api.Clash;
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
        private int _totalTestCount;
        private bool _isSubscribed;
        private readonly Dispatcher _dispatcher;
        private List<ClashDetectiveTestsLogic.ClashTestInfo> _allTests =
            new List<ClashDetectiveTestsLogic.ClashTestInfo>();

        public ClashDetectiveTestsViewModel()
        {
            _dispatcher = Dispatcher.CurrentDispatcher;
            Tests = new ObservableCollection<TestItemViewModel>();

            ClearFilterCommand = new RelayCommand(ExecuteClearFilter);
            RefreshCommand = new RelayCommand(ExecuteRefresh);
            UpdateTestsCommand = new RelayCommand(ExecuteUpdateTests, CanExecuteUpdateTests);
            ExportXmlCommand = new RelayCommand(ExecuteExportXml, CanExecuteExport);
            ExportHtmlCommand = new RelayCommand(ExecuteExportHtml, CanExecuteExport);

            SubscribeToDocumentChanges();
            ApplyFilter();
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
        }

        /// <summary>Проверяет наличие выбранных проверок.</summary>
        private bool CanExecuteUpdateTests()
        {
            return Tests.Any(t => t.IsSelected);
        }

        /// <summary>Проверяет наличие выбранных проверок для экспорта.</summary>
        private bool CanExecuteExport()
        {
            return Tests.Any(t => t.IsSelected);
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
                    ClashDetectiveTestsLogic.ExportTests(selectedTests, format);

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
                MessageBox.Show(
                    "Запущен стандартный экспорт Clash Detective (Write Report).\n"
                    + "Проверок: " + result.ExportedCount
                    + "\nФормат: " + formatName
                    + "\n\nУкажите файл в диалоге Navisworks. Содержимое отчёта — по настройкам вкладки Report.",
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
