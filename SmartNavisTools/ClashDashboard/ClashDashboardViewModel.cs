using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Web.Script.Serialization;
using System.Windows.Input;
using System.Windows.Threading;
using Autodesk.Navisworks.Api;
using Autodesk.Navisworks.Api.Clash;
using NavisworksApplication = Autodesk.Navisworks.Api.Application;

namespace SmartNavisTools
{
    /// <summary>
    /// ViewModel для панели дашборда коллизий.
    /// </summary>
    internal sealed class ClashDashboardViewModel : ViewModelBase, IDisposable
    {
        private readonly PropertyCatalog _propertyCatalog;
        private readonly Dispatcher _dispatcher;
        private readonly DispatcherTimer _autosaveTimer;
        private bool _isSubscribed;
        private bool _isApplyingSettings;
        private string _lastDocumentPath;
        private string _testsFilter;
        private bool _floorSourceIsGrid = true;
        private bool _floorSourceIsProperty;
        private string _floorPropertyName = string.Empty;
        private bool _disciplineSourceIsModel = true;
        private bool _disciplineSourceIsProperty;
        private string _disciplinePropertyName = string.Empty;
        private string _summaryText = "Готово.";
        private string _newSectionName = string.Empty;

        /// <summary>
        /// Создаёт ViewModel и инициализирует таймер автосохранения.
        /// </summary>
        public ClashDashboardViewModel()
        {
            _propertyCatalog = new PropertyCatalog();
            _dispatcher = Dispatcher.CurrentDispatcher;

            _autosaveTimer = new DispatcherTimer(DispatcherPriority.Background, _dispatcher)
            {
                Interval = TimeSpan.FromMilliseconds(300)
            };
            _autosaveTimer.Tick += OnAutosaveTick;

            Tests = new ObservableCollection<ClashTestItemViewModel>();
            Statuses = new ObservableCollection<StatusItemViewModel>
            {
                new StatusItemViewModel("New", true),
                new StatusItemViewModel("Active", true),
                new StatusItemViewModel("Reviewed", true),
                new StatusItemViewModel("Approved", true),
                new StatusItemViewModel("Resolved", false)
            };
            FloorPropertySuggestions = new ObservableCollection<string>();
            DisciplinePropertySuggestions = new ObservableCollection<string>();
            ModelFileSuggestions = new ObservableCollection<string>();
            Mappings = new ObservableCollection<MappingItemViewModel>();

            RefreshCommand = new RelayCommand(ExecuteRefresh);
            SaveSettingsCommand = new RelayCommand(ExecuteSaveSettings);
            BuildReportCommand = new RelayCommand(ExecuteBuildReport);
            RemoveMappingCommand = new RelayCommand(ExecuteRemoveMapping);
            SelectAllTestsCommand = new RelayCommand(ExecuteSelectAllTests);
            ClearTestsSelectionCommand = new RelayCommand(ExecuteClearTestsSelection);

            foreach (var status in Statuses)
            {
                status.PropertyChanged += OnSettingChanged;
            }
        }

        public string TestsFilter
        {
            get => _testsFilter;
            set
            {
                if (SetProperty(ref _testsFilter, value))
                {
                    ApplyTestsFilter();
                    ScheduleAutosave();
                }
            }
        }

        public ObservableCollection<ClashTestItemViewModel> Tests { get; }
        public ObservableCollection<StatusItemViewModel> Statuses { get; }

        public bool FloorSourceIsGrid
        {
            get => _floorSourceIsGrid;
            set
            {
                if (SetProperty(ref _floorSourceIsGrid, value))
                {
                    if (value) FloorSourceIsProperty = false;
                    ScheduleAutosave();
                }
            }
        }

        public bool FloorSourceIsProperty
        {
            get => _floorSourceIsProperty;
            set
            {
                if (SetProperty(ref _floorSourceIsProperty, value))
                {
                    if (value) FloorSourceIsGrid = false;
                    ScheduleAutosave();
                }
            }
        }

        public string FloorPropertyName
        {
            get => _floorPropertyName;
            set { if (SetProperty(ref _floorPropertyName, value)) ScheduleAutosave(); }
        }

        public ObservableCollection<string> FloorPropertySuggestions { get; }

        public bool DisciplineSourceIsModel
        {
            get => _disciplineSourceIsModel;
            set
            {
                if (SetProperty(ref _disciplineSourceIsModel, value))
                {
                    if (value) DisciplineSourceIsProperty = false;
                    ScheduleAutosave();
                }
            }
        }

        public bool DisciplineSourceIsProperty
        {
            get => _disciplineSourceIsProperty;
            set
            {
                if (SetProperty(ref _disciplineSourceIsProperty, value))
                {
                    if (value) DisciplineSourceIsModel = false;
                    ScheduleAutosave();
                }
            }
        }

        public string DisciplinePropertyName
        {
            get => _disciplinePropertyName;
            set { if (SetProperty(ref _disciplinePropertyName, value)) ScheduleAutosave(); }
        }

        public ObservableCollection<string> DisciplinePropertySuggestions { get; }
        public ObservableCollection<string> ModelFileSuggestions { get; }
        public ObservableCollection<MappingItemViewModel> Mappings { get; }

        /// <summary>
        /// Имя раздела для кнопки «Сгруппировать в раздел».
        /// </summary>
        public string NewSectionName
        {
            get => _newSectionName;
            set => SetProperty(ref _newSectionName, value);
        }

        public string SummaryText
        {
            get => _summaryText;
            set => SetProperty(ref _summaryText, value);
        }

        public ICommand RefreshCommand { get; }
        public ICommand SaveSettingsCommand { get; }
        public ICommand BuildReportCommand { get; }
        public ICommand RemoveMappingCommand { get; }
        public ICommand SelectAllTestsCommand { get; }
        public ICommand ClearTestsSelectionCommand { get; }

        /// <summary>
        /// Инициализирует подписки и загружает начальные данные. Вызывать после монтирования View.
        /// </summary>
        public void Initialize()
        {
            SubscribeToDocumentChanges();
            RefreshDataAndSettings();
        }

        /// <summary>
        /// Освобождает подписки и таймер.
        /// </summary>
        public void Dispose()
        {
            UnsubscribeFromDocumentChanges();
            _autosaveTimer.Stop();

            foreach (var test in Tests)
            {
                test.PropertyChanged -= OnTestSettingChanged;
            }

            foreach (var status in Statuses)
            {
                status.PropertyChanged -= OnSettingChanged;
            }

            foreach (var mapping in Mappings)
            {
                mapping.PropertyChanged -= OnSettingChanged;
            }
        }

        /// <summary>
        /// Подписывает на изменения документа Navisworks.
        /// </summary>
        private void SubscribeToDocumentChanges()
        {
            if (_isSubscribed || NavisworksApplication.MainDocument == null)
            {
                return;
            }

            try
            {
                Document document = NavisworksApplication.MainDocument;
                document.Database.Changed += OnDocumentChanged;
                if (document.GetClash()?.TestsData != null)
                {
                    document.GetClash().TestsData.Changed += OnDocumentChanged;
                }

                _isSubscribed = true;
            }
            catch (Exception ex)
            {
                SummaryText = "Ошибка подписки: " + ex.Message;
            }
        }

        /// <summary>
        /// Отписывает от событий документа Navisworks.
        /// </summary>
        private void UnsubscribeFromDocumentChanges()
        {
            if (!_isSubscribed || NavisworksApplication.MainDocument == null)
            {
                return;
            }

            try
            {
                Document document = NavisworksApplication.MainDocument;
                document.Database.Changed -= OnDocumentChanged;
                if (document.GetClash()?.TestsData != null)
                {
                    document.GetClash().TestsData.Changed -= OnDocumentChanged;
                }
            }
            catch
            {
            }

            _isSubscribed = false;
        }

        /// <summary>
        /// Обрабатывает изменение документа через WPF Dispatcher.
        /// </summary>
        private void OnDocumentChanged(object sender, EventArgs e)
        {
            _dispatcher.BeginInvoke(new Action(RefreshDataAndSettings));
        }

        /// <summary>
        /// Обновляет списки тестов, свойств и применяет настройки.
        /// </summary>
        private void RefreshDataAndSettings()
        {
            try
            {
                RefreshTestsList();
                RefreshPropertyCatalog();
                RefreshModelFileSuggestions();
                EnsureSettingsLoadedForDocument();
            }
            catch (Exception ex)
            {
                SummaryText = "Ошибка обновления: " + ex.Message;
            }
        }

        /// <summary>
        /// Обновляет список моделей для группировки по разделам.
        /// Для NWD показывает вложенные NWC, а не сам контейнер.
        /// </summary>
        private void RefreshModelFileSuggestions()
        {
            try
            {
                var names = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
                Document document = NavisworksApplication.MainDocument;
                if (document?.Models != null)
                {
                    foreach (Model model in document.Models)
                    {
                        CollectModelNamesForGrouping(model, names);
                    }
                }

                ModelFileSuggestions.Clear();
                foreach (string name in names)
                {
                    ModelFileSuggestions.Add(name);
                }
            }
            catch
            {
                // Оставляем предыдущий список, если чтение моделей временно недоступно.
            }
        }

        /// <summary>
        /// Добавляет в список либо сам файл модели, либо вложенные NWC из NWD/NWF.
        /// </summary>
        private static void CollectModelNamesForGrouping(Model model, ISet<string> names)
        {
            if (model == null || names == null)
            {
                return;
            }

            try
            {
                string modelPath = FirstNonEmpty(model.FileName, model.SourceFileName);
                string rootName = FirstNonEmpty(
                    model.RootItem?.DisplayName,
                    System.IO.Path.GetFileName(modelPath));

                if (IsNavisworksContainer(modelPath) || IsNavisworksContainer(rootName))
                {
                    bool addedNestedNwc = false;
                    ModelItem root = model.RootItem;
                    if (root != null)
                    {
                        foreach (ModelItem item in root.Descendants)
                        {
                            if (item == null || !item.HasModel)
                            {
                                continue;
                            }

                            string nestedPath = string.Empty;
                            try
                            {
                                nestedPath = FirstNonEmpty(item.Model?.FileName, item.Model?.SourceFileName);
                            }
                            catch
                            {
                                nestedPath = string.Empty;
                            }

                            string nestedName = FirstNonEmpty(item.DisplayName, System.IO.Path.GetFileName(nestedPath));
                            if (!IsNwcModel(nestedPath) && !IsNwcModel(nestedName))
                            {
                                continue;
                            }

                            if (!string.IsNullOrWhiteSpace(nestedName))
                            {
                                names.Add(nestedName.Trim());
                                addedNestedNwc = true;
                            }
                        }
                    }

                    // Если внутри NWD нет NWC, контейнер в список не добавляем.
                    if (!addedNestedNwc)
                    {
                        return;
                    }

                    return;
                }

                if (!string.IsNullOrWhiteSpace(rootName))
                {
                    names.Add(rootName.Trim());
                }
            }
            catch
            {
                // Пропускаем проблемную модель, остальные продолжаем собирать.
            }
        }

        /// <summary>
        /// Проверяет, является ли файл контейнером Navisworks (NWD/NWF).
        /// </summary>
        private static bool IsNavisworksContainer(string pathOrName)
        {
            if (string.IsNullOrWhiteSpace(pathOrName))
            {
                return false;
            }

            string extension = System.IO.Path.GetExtension(pathOrName);
            return string.Equals(extension, ".nwd", StringComparison.OrdinalIgnoreCase)
                || string.Equals(extension, ".nwf", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Проверяет, является ли файл моделью NWC.
        /// </summary>
        private static bool IsNwcModel(string pathOrName)
        {
            if (string.IsNullOrWhiteSpace(pathOrName))
            {
                return false;
            }

            return string.Equals(
                System.IO.Path.GetExtension(pathOrName),
                ".nwc",
                StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Возвращает первую непустую строку из кандидатов.
        /// </summary>
        private static string FirstNonEmpty(params string[] values)
        {
            if (values == null)
            {
                return string.Empty;
            }

            foreach (string value in values)
            {
                if (!string.IsNullOrWhiteSpace(value))
                {
                    return value.Trim();
                }
            }

            return string.Empty;
        }

        /// <summary>
        /// Загружает список тестов из Clash Detective.
        /// </summary>
        private void RefreshTestsList()
        {
            var tests = ClashDashboardLogic.GetTests();
            var selectedBefore = new HashSet<string>(
                Tests.Where(t => t.IsChecked).Select(t => t.DisplayName),
                StringComparer.OrdinalIgnoreCase);

            foreach (var t in Tests)
            {
                t.PropertyChanged -= OnTestSettingChanged;
            }

            Tests.Clear();

            foreach (var test in tests)
            {
                var vm = new ClashTestItemViewModel
                {
                    DisplayName = test.DisplayName ?? string.Empty,
                    IsChecked = selectedBefore.Contains(test.DisplayName)
                };
                vm.PropertyChanged += OnTestSettingChanged;
                Tests.Add(vm);
            }

            ApplyTestsFilter();
        }

        /// <summary>
        /// Обновляет каталог свойств и заполняет списки подсказок.
        /// </summary>
        private void RefreshPropertyCatalog()
        {
            Document document = NavisworksApplication.MainDocument;
            _propertyCatalog.Refresh(document);

            string floorCurrent = _floorPropertyName;
            string disciplineCurrent = _disciplinePropertyName;

            List<string> props = _propertyCatalog.Categories
                .SelectMany(cat => _propertyCatalog.GetProperties(cat))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(v => v, StringComparer.OrdinalIgnoreCase)
                .ToList();

            FloorPropertySuggestions.Clear();
            DisciplinePropertySuggestions.Clear();
            foreach (string p in props)
            {
                FloorPropertySuggestions.Add(p);
                DisciplinePropertySuggestions.Add(p);
            }

            FloorPropertyName = floorCurrent;
            DisciplinePropertyName = disciplineCurrent;
        }

        /// <summary>
        /// Фильтрует тесты по строке поиска.
        /// </summary>
        private void ApplyTestsFilter()
        {
            string filter = (_testsFilter ?? string.Empty).Trim();
            foreach (var test in Tests)
            {
                test.IsVisible = string.IsNullOrWhiteSpace(filter)
                    || (test.DisplayName ?? string.Empty).IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0;
            }
        }

        /// <summary>
        /// Загружает настройки при смене документа.
        /// </summary>
        private void EnsureSettingsLoadedForDocument()
        {
            Document document = NavisworksApplication.MainDocument;
            string currentPath = SafeGetDocumentPath(document);
            if (string.Equals(_lastDocumentPath, currentPath, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            _isApplyingSettings = true;
            try
            {
                Dictionary<string, string> map = ClashDashboardSettingsStore.Load(document);
                ApplyDefaults();
                if (map != null)
                {
                    ApplyMap(map);
                }
            }
            finally
            {
                _isApplyingSettings = false;
            }

            _lastDocumentPath = currentPath;
        }

        /// <summary>
        /// Устанавливает значения по умолчанию.
        /// </summary>
        private void ApplyDefaults()
        {
            foreach (var s in Statuses)
            {
                s.IsChecked = !string.Equals(s.Name, "Resolved", StringComparison.OrdinalIgnoreCase);
            }

            FloorSourceIsGrid = true;
            DisciplineSourceIsModel = true;
            ClearMappings();
        }

        /// <summary>
        /// Применяет словарь настроек к свойствам ViewModel.
        /// </summary>
        private void ApplyMap(IReadOnlyDictionary<string, string> map)
        {
            SetCheckedTests(SplitCsv(GetValue(map, "SelectedTests")));
            SetCheckedStatuses(SplitCsv(GetValue(map, "Statuses")));

            bool isGrid = string.Equals(GetValue(map, "FloorSource"), "Grid", StringComparison.OrdinalIgnoreCase);
            FloorSourceIsGrid = isGrid;
            FloorSourceIsProperty = !isGrid;
            FloorPropertyName = GetValue(map, "FloorProperty");

            bool isModelFile = string.Equals(GetValue(map, "DisciplineSource"), "ModelFile", StringComparison.OrdinalIgnoreCase);
            DisciplineSourceIsModel = isModelFile;
            DisciplineSourceIsProperty = !isModelFile;
            DisciplinePropertyName = GetValue(map, "DisciplineProperty");

            LoadMappingsFromJson(GetValue(map, "ModelFileDisciplineMappingsJson"));
        }

        /// <summary>
        /// Загружает маппинги из JSON-строки.
        /// </summary>
        private void LoadMappingsFromJson(string json)
        {
            ClearMappings();
            if (string.IsNullOrWhiteSpace(json))
            {
                return;
            }

            try
            {
                var serializer = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
                var list = serializer.Deserialize<List<ClashDimensionResolver.ModelFileDisciplineMapping>>(json);
                if (list == null)
                {
                    return;
                }

                foreach (var m in list)
                {
                    if (m == null) continue;
                    var vm = new MappingItemViewModel
                    {
                        Key = m.Key ?? string.Empty,
                        Values = string.Join(";", m.Values ?? Array.Empty<string>())
                    };
                    vm.PropertyChanged += OnSettingChanged;
                    Mappings.Add(vm);
                }
            }
            catch
            {
            }
        }

        /// <summary>
        /// Очищает коллекцию маппингов и отписывает обработчики.
        /// </summary>
        private void ClearMappings()
        {
            foreach (var m in Mappings)
            {
                m.PropertyChanged -= OnSettingChanged;
            }

            Mappings.Clear();
        }

        /// <summary>
        /// Устанавливает выбранные тесты по именам.
        /// </summary>
        private void SetCheckedTests(string[] names)
        {
            var set = new HashSet<string>(names ?? Array.Empty<string>(), StringComparer.OrdinalIgnoreCase);
            foreach (var t in Tests)
            {
                t.IsChecked = set.Contains(t.DisplayName);
            }
        }

        /// <summary>
        /// Устанавливает выбранные статусы по именам.
        /// </summary>
        private void SetCheckedStatuses(string[] names)
        {
            var set = new HashSet<string>(names ?? Array.Empty<string>(), StringComparer.OrdinalIgnoreCase);
            foreach (var s in Statuses)
            {
                s.IsChecked = set.Contains(s.Name);
            }
        }

        /// <summary>
        /// Обработчик изменения свойства в дочерних элементах (статусы, маппинги).
        /// </summary>
        private void OnSettingChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e != null && e.PropertyName == nameof(MappingItemViewModel.IsSelected))
            {
                return;
            }

            if (!_isApplyingSettings)
            {
                ScheduleAutosave();
            }
        }

        /// <summary>
        /// Обработчик изменения свойства теста.
        /// </summary>
        private void OnTestSettingChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (!_isApplyingSettings)
            {
                ScheduleAutosave();
            }
        }

        /// <summary>
        /// Перезапускает таймер автосохранения (debounce 300мс).
        /// </summary>
        private void ScheduleAutosave()
        {
            if (_isApplyingSettings)
            {
                return;
            }

            _autosaveTimer.Stop();
            _autosaveTimer.Start();
        }

        /// <summary>
        /// Сохраняет настройки по таймеру.
        /// </summary>
        private void OnAutosaveTick(object sender, EventArgs e)
        {
            _autosaveTimer.Stop();
            SaveCurrentSettings();
        }

        /// <summary>
        /// Сохраняет текущее состояние ViewModel в файл настроек.
        /// </summary>
        private void SaveCurrentSettings()
        {
            if (_isApplyingSettings)
            {
                return;
            }

            try
            {
                var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["SelectedTests"] = string.Join(";", Tests.Where(t => t.IsChecked).Select(t => t.DisplayName)),
                    ["Statuses"] = string.Join(";", GetSelectedStatuses().Select(s => s.ToString())),
                    ["FloorSource"] = _floorSourceIsGrid ? "Grid" : "Property",
                    ["FloorProperty"] = _floorPropertyName ?? string.Empty,
                    ["DisciplineSource"] = _disciplineSourceIsModel ? "ModelFile" : "Property",
                    ["DisciplineProperty"] = _disciplinePropertyName ?? string.Empty,
                    ["ModelFileDisciplineMappingsJson"] = SerializeModelFileMappings(GetMappingsFromVm())
                };

                ClashDashboardSettingsStore.Save(NavisworksApplication.MainDocument, map);
            }
            catch
            {
            }
        }

        /// <summary>
        /// Выполняет команду обновления данных.
        /// </summary>
        private void ExecuteRefresh()
        {
            RefreshDataAndSettings();
            SummaryText = "Данные обновлены.";
        }

        /// <summary>
        /// Выполняет команду сохранения настроек.
        /// </summary>
        private void ExecuteSaveSettings()
        {
            SaveCurrentSettings();
            SummaryText = "Настройки сохранены.";
        }

        /// <summary>
        /// Выполняет команду формирования отчёта.
        /// </summary>
        private void ExecuteBuildReport()
        {
            try
            {
                var dialog = new Microsoft.Win32.SaveFileDialog
                {
                    Filter = "HTML Files (*.html)|*.html",
                    FileName = "ClashDashboard_" + DateTime.Now.ToString("yyyyMMdd_HHmm") + ".html",
                    OverwritePrompt = true
                };

                if (dialog.ShowDialog() != true)
                {
                    return;
                }

                var result = ClashDashboardLogic.BuildReport(dialog.FileName, BuildOptionsFromVm());
                if (!result.Success)
                {
                    System.Windows.MessageBox.Show(
                        result.ErrorMessage, "Статистика пересечений",
                        System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
                    return;
                }

                SummaryText = "Готово. Записей: " + result.RecordsCount;
                SaveCurrentSettings();

                try
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = dialog.FileName,
                        UseShellExecute = true
                    });
                }
                catch (Exception ex)
                {
                    System.Windows.MessageBox.Show(
                        "Отчет создан, но не удалось открыть браузер: " + ex.Message, "Статистика пересечений");
                }
            }
            catch (Exception ex)
            {
                SummaryText = "Ошибка: " + ex.Message;
            }
        }

        /// <summary>
        /// Группирует выбранные модели в раздел с указанным именем.
        /// </summary>
        public void GroupSelectedModels(IEnumerable<string> selectedModels)
        {
            try
            {
                string sectionName = (_newSectionName ?? string.Empty).Trim();
                if (string.IsNullOrWhiteSpace(sectionName))
                {
                    SummaryText = "Укажите имя раздела.";
                    return;
                }

                List<string> models = (selectedModels ?? Enumerable.Empty<string>())
                    .Where(m => !string.IsNullOrWhiteSpace(m))
                    .Select(m => m.Trim())
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                if (models.Count == 0)
                {
                    SummaryText = "Выберите модели в списке (можно через Shift).";
                    return;
                }

                var selectedSet = new HashSet<string>(models, StringComparer.OrdinalIgnoreCase);

                // Модель может быть только в одном разделе: убираем её из остальных групп.
                foreach (MappingItemViewModel mapping in Mappings.ToList())
                {
                    List<string> remaining = SplitCsv(mapping.Values)
                        .Where(v => !selectedSet.Contains(v))
                        .ToList();

                    if (remaining.Count == 0)
                    {
                        mapping.PropertyChanged -= OnSettingChanged;
                        Mappings.Remove(mapping);
                    }
                    else
                    {
                        mapping.Values = string.Join(";", remaining);
                    }
                }

                MappingItemViewModel target = Mappings.FirstOrDefault(m =>
                    string.Equals((m.Key ?? string.Empty).Trim(), sectionName, StringComparison.OrdinalIgnoreCase));

                if (target == null)
                {
                    target = new MappingItemViewModel
                    {
                        Key = sectionName,
                        Values = string.Join(";", models),
                        IsSelected = true
                    };
                    target.PropertyChanged += OnSettingChanged;
                    foreach (MappingItemViewModel existing in Mappings)
                    {
                        existing.IsSelected = false;
                    }

                    Mappings.Add(target);
                }
                else
                {
                    var merged = new SortedSet<string>(SplitCsv(target.Values), StringComparer.OrdinalIgnoreCase);
                    foreach (string model in models)
                    {
                        merged.Add(model);
                    }

                    target.Values = string.Join(";", merged);
                    foreach (MappingItemViewModel existing in Mappings)
                    {
                        existing.IsSelected = ReferenceEquals(existing, target);
                    }
                }

                SummaryText = "Сгруппировано в раздел «" + sectionName + "»: " + models.Count;
                ScheduleAutosave();
            }
            catch (Exception ex)
            {
                SummaryText = "Ошибка группировки: " + ex.Message;
            }
        }

        /// <summary>
        /// Удаляет все выбранные разделы из коллекции.
        /// </summary>
        private void ExecuteRemoveMapping()
        {
            if (Mappings.Count <= 0)
            {
                return;
            }

            var selected = Mappings.Where(m => m.IsSelected).ToList();
            if (selected.Count == 0)
            {
                selected.Add(Mappings[Mappings.Count - 1]);
            }

            foreach (MappingItemViewModel mapping in selected)
            {
                mapping.PropertyChanged -= OnSettingChanged;
                Mappings.Remove(mapping);
            }

            ScheduleAutosave();
        }

        /// <summary>
        /// Выделяет все проверки в списке.
        /// </summary>
        private void ExecuteSelectAllTests()
        {
            foreach (var test in Tests)
            {
                test.IsChecked = true;
            }
        }

        /// <summary>
        /// Полностью снимает выбор со всех проверок.
        /// </summary>
        private void ExecuteClearTestsSelection()
        {
            foreach (var test in Tests)
            {
                test.IsChecked = false;
            }
        }

        /// <summary>
        /// Формирует объект параметров построения отчёта из текущих свойств ViewModel.
        /// </summary>
        private ClashDashboardLogic.BuildOptions BuildOptionsFromVm()
        {
            return new ClashDashboardLogic.BuildOptions
            {
                SelectedTests = Tests.Where(t => t.IsChecked).Select(t => t.DisplayName).ToList(),
                EnabledStatuses = GetSelectedStatuses(),
                FloorSource = _floorSourceIsGrid ? ClashDimensionResolver.FloorSource.Grid : ClashDimensionResolver.FloorSource.Property,
                FloorPropertyName = (_floorPropertyName ?? string.Empty).Trim(),
                DisciplineSource = _disciplineSourceIsModel ? ClashDimensionResolver.DisciplineSource.ModelFile : ClashDimensionResolver.DisciplineSource.Property,
                DisciplinePropertyName = (_disciplinePropertyName ?? string.Empty).Trim(),
                ModelFileDisciplineMappings = GetMappingsFromVm()
            };
        }

        /// <summary>
        /// Возвращает выбранные статусы как HashSet enum.
        /// </summary>
        private HashSet<ClashResultStatus> GetSelectedStatuses()
        {
            var result = new HashSet<ClashResultStatus>();
            foreach (var s in Statuses)
            {
                if (s.IsChecked && Enum.TryParse(s.Name, out ClashResultStatus status))
                {
                    result.Add(status);
                }
            }

            return result;
        }

        /// <summary>
        /// Преобразует коллекцию маппингов ViewModel в список для логики.
        /// </summary>
        private List<ClashDimensionResolver.ModelFileDisciplineMapping> GetMappingsFromVm()
        {
            var result = new List<ClashDimensionResolver.ModelFileDisciplineMapping>();
            foreach (var m in Mappings)
            {
                string key = (m.Key ?? string.Empty).Trim();
                string valuesText = m.Values ?? string.Empty;
                if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(valuesText))
                {
                    continue;
                }

                string[] patterns = valuesText
                    .Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(p => p.Trim())
                    .Where(p => p.Length > 0)
                    .ToArray();

                if (patterns.Length == 0)
                {
                    continue;
                }

                result.Add(new ClashDimensionResolver.ModelFileDisciplineMapping
                {
                    Key = key,
                    Values = patterns
                });
            }

            return result;
        }

        /// <summary>
        /// Сериализует маппинги в JSON для хранения.
        /// </summary>
        private static string SerializeModelFileMappings(IEnumerable<ClashDimensionResolver.ModelFileDisciplineMapping> mappings)
        {
            try
            {
                var serializer = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
                return serializer.Serialize(mappings ?? Array.Empty<ClashDimensionResolver.ModelFileDisciplineMapping>());
            }
            catch
            {
                return string.Empty;
            }
        }

        /// <summary>
        /// Безопасно получает значение из словаря настроек.
        /// </summary>
        private static string GetValue(IReadOnlyDictionary<string, string> map, string key)
        {
            if (map == null || string.IsNullOrWhiteSpace(key))
            {
                return string.Empty;
            }

            return map.TryGetValue(key, out string value) ? value ?? string.Empty : string.Empty;
        }

        /// <summary>
        /// Разбивает строку по разделителю ';'.
        /// </summary>
        private static string[] SplitCsv(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return Array.Empty<string>();
            }

            return value
                .Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(part => part.Trim())
                .Where(part => part.Length > 0)
                .ToArray();
        }

        /// <summary>
        /// Безопасно получает путь текущего документа.
        /// </summary>
        private static string SafeGetDocumentPath(Document document)
        {
            if (document == null || document.IsClear)
            {
                return string.Empty;
            }

            try
            {
                return document.FileName ?? string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }
    }

    /// <summary>
    /// ViewModel для элемента теста Clash Detective.
    /// </summary>
    internal sealed class ClashTestItemViewModel : ViewModelBase
    {
        private string _displayName;
        private bool _isChecked;
        private bool _isVisible = true;

        public string DisplayName
        {
            get => _displayName;
            set => SetProperty(ref _displayName, value);
        }

        public bool IsChecked
        {
            get => _isChecked;
            set => SetProperty(ref _isChecked, value);
        }

        public bool IsVisible
        {
            get => _isVisible;
            set => SetProperty(ref _isVisible, value);
        }
    }

    /// <summary>
    /// ViewModel для элемента статуса.
    /// </summary>
    internal sealed class StatusItemViewModel : ViewModelBase
    {
        private string _name;
        private bool _isChecked;

        /// <summary>
        /// Создаёт элемент статуса с именем и начальным состоянием.
        /// </summary>
        public StatusItemViewModel(string name, bool isChecked)
        {
            _name = name;
            _isChecked = isChecked;
        }

        public string Name
        {
            get => _name;
            set => SetProperty(ref _name, value);
        }

        public bool IsChecked
        {
            get => _isChecked;
            set => SetProperty(ref _isChecked, value);
        }
    }

    /// <summary>
    /// ViewModel для строки маппинга (ключ -> значения).
    /// </summary>
    internal sealed class MappingItemViewModel : ViewModelBase
    {
        private string _key;
        private string _values;
        private bool _isSelected;

        /// <summary>
        /// Признак выделения строки маппинга в UI.
        /// </summary>
        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (SetProperty(ref _isSelected, value))
                {
                    // Состояние выделения важно только для UI, логика автосохранения будет игнорировать его.
                }
            }
        }

        /// <summary>
        /// Отображаемое представление маппинга для UI: «Группа: ..., модели: ...».
        /// </summary>
        public string DisplayText
        {
            get
            {
                string key = string.IsNullOrWhiteSpace(_key) ? "без названия" : _key.Trim();
                string values = string.IsNullOrWhiteSpace(_values) ? "нет моделей" : _values.Trim();
                return "Раздел: " + key + " — " + values;
            }
        }

        public string Key
        {
            get => _key;
            set
            {
                if (SetProperty(ref _key, value))
                {
                    OnPropertyChanged(nameof(DisplayText));
                }
            }
        }

        public string Values
        {
            get => _values;
            set
            {
                if (SetProperty(ref _values, value))
                {
                    OnPropertyChanged(nameof(DisplayText));
                }
            }
        }
    }
}
