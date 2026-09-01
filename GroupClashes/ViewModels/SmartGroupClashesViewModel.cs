using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Data;
using IOPath = System.IO.Path;

using Autodesk.Navisworks.Api;
using Autodesk.Navisworks.Api.Clash;
using NavisworksApplication = Autodesk.Navisworks.Api.Application;

namespace SmartGroupClashes.ViewModels
{
    /// <summary>
    /// Главная ViewModel для панели группировки коллизий.
    /// </summary>
    public class SmartGroupClashesViewModel : ViewModelBase
    {
        private string _filterText = string.Empty;
        private GroupingModeOption _selectedGroupBy;
        private GroupingModeOption _selectedThenBy;
        private GroupingModeOption _selectedThirdBy;
        private bool _keepExistingGroups;
        private bool _skipFixedGroups = true;
        private bool _analyzeNewStatus = true;
        private bool _analyzeActiveStatus = true;
        private bool _analyzeReviewedStatus = true;
        private bool _analyzeApprovedStatus = true;
        private bool _analyzeResolvedStatus;
        private string _customPropertyStage1A = string.Empty;
        private string _customPropertyStage1B = string.Empty;
        private string _customPropertyStage2A = string.Empty;
        private string _customPropertyStage2B = string.Empty;
        private string _customPropertyStage3A = string.Empty;
        private string _customPropertyStage3B = string.Empty;
        private string _selectedTestsCountText = "Выбрано тестов: 0";
        private string _selectedTestDisplayName = string.Empty;
        private string _selectionAName = string.Empty;
        private string _selectionBName = string.Empty;
        private bool _canGroup;
        private bool _canUngroup;
        private bool _areComboBoxesEnabled;
        private bool _isApplyingSettings;
        private string _loadedSettingsDocumentKey;

        private ICollectionView _clashTestsView;

        /// <summary>Создаёт ViewModel, заполняет коллекции и подписывается на события документа.</summary>
        public SmartGroupClashesViewModel()
        {
            ClashTests = new ObservableCollection<ClashTestItemViewModel>();
            GroupByList = new ObservableCollection<GroupingModeOption>();
            GroupThenList = new ObservableCollection<GroupingModeOption>();
            GroupThirdList = new ObservableCollection<GroupingModeOption>();
            CustomPropertySuggestions = new ObservableCollection<string>();

            foreach (string suggestion in GroupingFunctions.DefaultCustomPropertyDisplayNameSuggestions)
            {
                CustomPropertySuggestions.Add(suggestion);
            }

            GroupCommand = new RelayCommand(ExecuteGroup, () => CanGroup);
            UngroupCommand = new RelayCommand(ExecuteUngroup, () => CanUngroup);
            SaveSettingsCommand = new RelayCommand(ExecuteSaveSettings);
            SelectAllTestsCommand = new RelayCommand(ExecuteSelectAll);
            ClearSelectionCommand = new RelayCommand(ExecuteClearSelection);

            RegisterChanges();

            _clashTestsView = CollectionViewSource.GetDefaultView(ClashTests);
            _clashTestsView.Filter = FilterClashTests;
        }

        #region Коллекции

        /// <summary>Тесты коллизий текущего документа.</summary>
        public ObservableCollection<ClashTestItemViewModel> ClashTests { get; }

        /// <summary>Варианты режима «Группировать по».</summary>
        public ObservableCollection<GroupingModeOption> GroupByList { get; }

        /// <summary>Варианты режима «Затем по».</summary>
        public ObservableCollection<GroupingModeOption> GroupThenList { get; }

        /// <summary>Варианты режима «И затем по».</summary>
        public ObservableCollection<GroupingModeOption> GroupThirdList { get; }

        /// <summary>Подсказки для полей «Своё свойство».</summary>
        public ObservableCollection<string> CustomPropertySuggestions { get; }

        #endregion

        #region Свойства

        /// <summary>Текст фильтра для поиска тестов.</summary>
        public string FilterText
        {
            get { return _filterText; }
            set
            {
                if (SetProperty(ref _filterText, value))
                {
                    _clashTestsView?.Refresh();
                }
            }
        }

        /// <summary>Выбранный режим первого уровня группировки.</summary>
        public GroupingModeOption SelectedGroupBy
        {
            get { return _selectedGroupBy; }
            set
            {
                if (SetProperty(ref _selectedGroupBy, value))
                {
                    OnGroupingModeChanged();
                }
            }
        }

        /// <summary>Выбранный режим второго уровня группировки.</summary>
        public GroupingModeOption SelectedThenBy
        {
            get { return _selectedThenBy; }
            set
            {
                if (SetProperty(ref _selectedThenBy, value))
                {
                    OnGroupingModeChanged();
                }
            }
        }

        /// <summary>Выбранный режим третьего уровня группировки.</summary>
        public GroupingModeOption SelectedThirdBy
        {
            get { return _selectedThirdBy; }
            set
            {
                if (SetProperty(ref _selectedThirdBy, value))
                {
                    OnGroupingModeChanged();
                }
            }
        }

        /// <summary>Сохранять существующие группы.</summary>
        public bool KeepExistingGroups
        {
            get { return _keepExistingGroups; }
            set
            {
                if (SetProperty(ref _keepExistingGroups, value))
                {
                    OnSettingChanged();
                }
            }
        }

        /// <summary>Пропускать группы, где все пересечения исправлены.</summary>
        public bool SkipFixedGroups
        {
            get { return _skipFixedGroups; }
            set
            {
                if (SetProperty(ref _skipFixedGroups, value))
                {
                    OnSettingChanged();
                }
            }
        }

        /// <summary>Группировать пересечения со статусом «Новые».</summary>
        public bool AnalyzeNewStatus
        {
            get { return _analyzeNewStatus; }
            set
            {
                if (SetProperty(ref _analyzeNewStatus, value))
                {
                    OnSettingChanged();
                }
            }
        }

        /// <summary>Группировать пересечения со статусом «Активные».</summary>
        public bool AnalyzeActiveStatus
        {
            get { return _analyzeActiveStatus; }
            set
            {
                if (SetProperty(ref _analyzeActiveStatus, value))
                {
                    OnSettingChanged();
                }
            }
        }

        /// <summary>Группировать пересечения со статусом «Проверенные».</summary>
        public bool AnalyzeReviewedStatus
        {
            get { return _analyzeReviewedStatus; }
            set
            {
                if (SetProperty(ref _analyzeReviewedStatus, value))
                {
                    OnSettingChanged();
                }
            }
        }

        /// <summary>Группировать пересечения со статусом «Утверждённые».</summary>
        public bool AnalyzeApprovedStatus
        {
            get { return _analyzeApprovedStatus; }
            set
            {
                if (SetProperty(ref _analyzeApprovedStatus, value))
                {
                    OnSettingChanged();
                }
            }
        }

        /// <summary>Группировать пересечения со статусом «Устранённые».</summary>
        public bool AnalyzeResolvedStatus
        {
            get { return _analyzeResolvedStatus; }
            set
            {
                if (SetProperty(ref _analyzeResolvedStatus, value))
                {
                    OnSettingChanged();
                }
            }
        }

        /// <summary>Параметр (A) первого уровня «Своё свойство».</summary>
        public string CustomPropertyStage1A
        {
            get { return _customPropertyStage1A; }
            set { SetProperty(ref _customPropertyStage1A, value ?? string.Empty); }
        }

        /// <summary>Параметр (B) первого уровня «Своё свойство».</summary>
        public string CustomPropertyStage1B
        {
            get { return _customPropertyStage1B; }
            set { SetProperty(ref _customPropertyStage1B, value ?? string.Empty); }
        }

        /// <summary>Параметр (A) второго уровня «Своё свойство».</summary>
        public string CustomPropertyStage2A
        {
            get { return _customPropertyStage2A; }
            set { SetProperty(ref _customPropertyStage2A, value ?? string.Empty); }
        }

        /// <summary>Параметр (B) второго уровня «Своё свойство».</summary>
        public string CustomPropertyStage2B
        {
            get { return _customPropertyStage2B; }
            set { SetProperty(ref _customPropertyStage2B, value ?? string.Empty); }
        }

        /// <summary>Параметр (A) третьего уровня «Своё свойство».</summary>
        public string CustomPropertyStage3A
        {
            get { return _customPropertyStage3A; }
            set { SetProperty(ref _customPropertyStage3A, value ?? string.Empty); }
        }

        /// <summary>Параметр (B) третьего уровня «Своё свойство».</summary>
        public string CustomPropertyStage3B
        {
            get { return _customPropertyStage3B; }
            set { SetProperty(ref _customPropertyStage3B, value ?? string.Empty); }
        }

        /// <summary>Текст счётчика выбранных тестов.</summary>
        public string SelectedTestsCountText
        {
            get { return _selectedTestsCountText; }
            private set { SetProperty(ref _selectedTestsCountText, value); }
        }

        /// <summary>Имя последнего выбранного теста.</summary>
        public string SelectedTestDisplayName
        {
            get { return _selectedTestDisplayName; }
            private set { SetProperty(ref _selectedTestDisplayName, value); }
        }

        /// <summary>Описание выбора A последнего выбранного теста.</summary>
        public string SelectionAName
        {
            get { return _selectionAName; }
            private set { SetProperty(ref _selectionAName, value); }
        }

        /// <summary>Описание выбора B последнего выбранного теста.</summary>
        public string SelectionBName
        {
            get { return _selectionBName; }
            private set { SetProperty(ref _selectionBName, value); }
        }

        /// <summary>Доступна ли кнопка «Группировать».</summary>
        public bool CanGroup
        {
            get { return _canGroup; }
            private set
            {
                if (SetProperty(ref _canGroup, value))
                {
                    GroupCommand?.RaiseCanExecuteChanged();
                }
            }
        }

        /// <summary>Доступна ли кнопка «Разгруппировать».</summary>
        public bool CanUngroup
        {
            get { return _canUngroup; }
            private set
            {
                if (SetProperty(ref _canUngroup, value))
                {
                    UngroupCommand?.RaiseCanExecuteChanged();
                }
            }
        }

        /// <summary>Доступны ли комбобоксы режимов группировки.</summary>
        public bool AreComboBoxesEnabled
        {
            get { return _areComboBoxesEnabled; }
            private set { SetProperty(ref _areComboBoxesEnabled, value); }
        }

        /// <summary>Видимость панели параметров «Своё свойство» (первый уровень).</summary>
        public bool IsCustomPropertyStage1Visible
        {
            get { return GetSelectedMode(_selectedGroupBy) == GroupingMode.CustomProperty; }
        }

        /// <summary>Видимость панели параметров «Своё свойство» (второй уровень).</summary>
        public bool IsCustomPropertyStage2Visible
        {
            get { return GetSelectedMode(_selectedThenBy) == GroupingMode.CustomProperty; }
        }

        /// <summary>Видимость панели параметров «Своё свойство» (третий уровень).</summary>
        public bool IsCustomPropertyStage3Visible
        {
            get { return GetSelectedMode(_selectedThirdBy) == GroupingMode.CustomProperty; }
        }

        /// <summary>Видимость общей панели параметров «Своё свойство».</summary>
        public bool IsCustomPropertyParamsVisible
        {
            get { return IsCustomPropertyStage1Visible || IsCustomPropertyStage2Visible || IsCustomPropertyStage3Visible; }
        }

        #endregion

        #region Команды

        /// <summary>Команда группировки пересечений.</summary>
        public RelayCommand GroupCommand { get; }

        /// <summary>Команда снятия группировки.</summary>
        public RelayCommand UngroupCommand { get; }

        /// <summary>Команда сохранения настроек.</summary>
        public RelayCommand SaveSettingsCommand { get; }

        /// <summary>Команда «Выбрать всё».</summary>
        public RelayCommand SelectAllTestsCommand { get; }

        /// <summary>Команда «Снять выбор».</summary>
        public RelayCommand ClearSelectionCommand { get; }

        #endregion

        #region Подписка на события документа

        /// <summary>Подписывается на изменения документа и обновляет данные в UI.</summary>
        private void RegisterChanges()
        {
            try
            {
                NavisworksApplication.MainDocument.Database.Changed += DocumentClashTests_Changed;
                DocumentClashTests dct = NavisworksApplication.MainDocument.GetClash().TestsData;
                dct.Changed += DocumentClashTests_Changed;
            }
            catch (Exception)
            {
                // Документ ещё не загружен
            }

            GetClashTests();
            CheckPlugin();
            LoadComboBox();
            TryLoadSettingsForCurrentDocument();
        }

        /// <summary>Отписывается от событий документа.</summary>
        private void UnRegisterChanges()
        {
            try
            {
                NavisworksApplication.MainDocument.Database.Changed -= DocumentClashTests_Changed;
                DocumentClashTests dct = NavisworksApplication.MainDocument.GetClash().TestsData;
                dct.Changed -= DocumentClashTests_Changed;
            }
            catch (Exception)
            {
                // Игнорируем ошибку при отписке
            }
        }

        /// <summary>Обработчик изменений в документе: обновляет все данные.</summary>
        private void DocumentClashTests_Changed(object sender, EventArgs e)
        {
            GetClashTests();
            CheckPlugin();
            LoadComboBox();
            TryLoadSettingsForCurrentDocument();
            _clashTestsView?.Refresh();
            UpdateSelectionSummary();
        }

        #endregion

        #region Загрузка данных

        /// <summary>Загружает тесты коллизий из текущего документа.</summary>
        private void GetClashTests()
        {
            foreach (var item in ClashTests)
            {
                item.PropertyChanged -= ClashTestItem_PropertyChanged;
            }

            ClashTests.Clear();

            try
            {
                DocumentClashTests dct = NavisworksApplication.MainDocument.GetClash().TestsData;
                foreach (SavedItem savedItem in dct.Tests)
                {
                    if (savedItem.GetType() == typeof(ClashTest))
                    {
                        var vm = new ClashTestItemViewModel(savedItem as ClashTest);
                        vm.PropertyChanged += ClashTestItem_PropertyChanged;
                        ClashTests.Add(vm);
                    }
                }
            }
            catch (Exception)
            {
                // Документ не открыт или нет тестов
            }

            _clashTestsView?.Refresh();
        }

        /// <summary>Заполняет комбобоксы режимов группировки.</summary>
        private void LoadComboBox()
        {
            GroupByList.Clear();
            GroupThenList.Clear();
            GroupThirdList.Clear();

            foreach (GroupingMode mode in Enum.GetValues(typeof(GroupingMode)).Cast<GroupingMode>())
            {
                string label = GroupingModeOption.GetDisplayName(mode);
                GroupByList.Add(new GroupingModeOption { Mode = mode, DisplayName = label });
                GroupThenList.Add(new GroupingModeOption { Mode = mode, DisplayName = label });
                GroupThirdList.Add(new GroupingModeOption { Mode = mode, DisplayName = label });
            }

            try
            {
                if (NavisworksApplication.MainDocument.Grids.ActiveSystem == null)
                {
                    RemoveModeOption(GroupByList, GroupingMode.GridIntersection);
                    RemoveModeOption(GroupByList, GroupingMode.Level);
                    RemoveModeOption(GroupThenList, GroupingMode.GridIntersection);
                    RemoveModeOption(GroupThenList, GroupingMode.Level);
                    RemoveModeOption(GroupThirdList, GroupingMode.GridIntersection);
                    RemoveModeOption(GroupThirdList, GroupingMode.Level);
                }
            }
            catch (Exception)
            {
                // Документ не готов
            }

            if (GroupByList.Count > 0)
            {
                SelectedGroupBy = GroupByList[0];
            }

            if (GroupThenList.Count > 0)
            {
                SelectedThenBy = GroupThenList[0];
            }

            if (GroupThirdList.Count > 0)
            {
                SelectedThirdBy = GroupThirdList[0];
            }

            RaiseCustomPropertyVisibilityChanged();
        }

        /// <summary>Удаляет указанный режим из списка опций.</summary>
        private static void RemoveModeOption(ObservableCollection<GroupingModeOption> list, GroupingMode mode)
        {
            for (int i = list.Count - 1; i >= 0; i--)
            {
                if (list[i].Mode == mode)
                {
                    list.RemoveAt(i);
                }
            }
        }

        #endregion

        #region Фильтрация

        /// <summary>Фильтр списка тестов: совпадение по имени с учётом текста поиска.</summary>
        private bool FilterClashTests(object item)
        {
            if (string.IsNullOrWhiteSpace(_filterText))
            {
                return true;
            }

            var clashTest = item as ClashTestItemViewModel;
            if (clashTest == null)
            {
                return false;
            }

            return clashTest.DisplayName.IndexOf(_filterText.Trim(), StringComparison.OrdinalIgnoreCase) >= 0;
        }

        #endregion

        #region Проверка состояния

        /// <summary>Обновляет доступность кнопок и комбобоксов.</summary>
        private void CheckPlugin()
        {
            bool hasGroupingCondition =
                GetSelectedMode(_selectedGroupBy) != GroupingMode.None
                || GetSelectedMode(_selectedThenBy) != GroupingMode.None
                || GetSelectedMode(_selectedThirdBy) != GroupingMode.None;

            bool hasStatusSelection =
                _analyzeNewStatus || _analyzeActiveStatus || _analyzeReviewedStatus
                || _analyzeApprovedStatus || _analyzeResolvedStatus;

            bool documentReady = false;
            try
            {
                documentReady = NavisworksApplication.MainDocument != null
                    && !NavisworksApplication.MainDocument.IsClear
                    && NavisworksApplication.MainDocument.GetClash() != null
                    && NavisworksApplication.MainDocument.GetClash().TestsData.Tests.Count > 0;
            }
            catch (Exception)
            {
                // Документ не доступен
            }

            int selectedCount = ClashTests.Count(t => t.IsSelected);

            if (!documentReady)
            {
                CanGroup = false;
                AreComboBoxesEnabled = false;
                CanUngroup = false;
            }
            else
            {
                CanGroup = selectedCount > 0 && hasGroupingCondition && hasStatusSelection;
                AreComboBoxesEnabled = true;
                CanUngroup = selectedCount > 0;
            }
        }

        #endregion

        #region Выделение элементов

        /// <summary>Обработчик изменения свойства IsSelected у элемента списка.</summary>
        private void ClashTestItem_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(ClashTestItemViewModel.IsSelected))
            {
                UpdateSelectionSummary();
                CheckPlugin();
            }
        }

        /// <summary>Обновляет счётчик выбранных тестов и информацию о последнем выбранном.</summary>
        private void UpdateSelectionSummary()
        {
            int count = ClashTests.Count(t => t.IsSelected);
            SelectedTestsCountText = $"Выбрано тестов: {count}";

            var lastSelected = ClashTests.LastOrDefault(t => t.IsSelected);
            if (lastSelected != null)
            {
                SelectedTestDisplayName = lastSelected.DisplayName;
                SelectionAName = lastSelected.SelectionAName;
                SelectionBName = lastSelected.SelectionBName;
            }
            else
            {
                SelectedTestDisplayName = string.Empty;
                SelectionAName = string.Empty;
                SelectionBName = string.Empty;
            }
        }

        #endregion

        #region Команды: реализация

        /// <summary>Выполняет группировку пересечений в выбранных тестах.</summary>
        private void ExecuteGroup()
        {
            var selectedItems = ClashTests.Where(t => t.IsSelected).ToList();
            if (selectedItems.Count == 0)
            {
                return;
            }

            bool groupingPerformed = false;
            UnRegisterChanges();

            try
            {
                GroupingMode groupByMode = GetSelectedMode(_selectedGroupBy);
                GroupingMode thenByMode = GetSelectedMode(_selectedThenBy);
                GroupingMode thirdByMode = GetSelectedMode(_selectedThirdBy);

                string customStage1A = null;
                string customStage1B = null;
                string customStage2A = null;
                string customStage2B = null;
                string customStage3A = null;
                string customStage3B = null;

                if (groupByMode == GroupingMode.CustomProperty)
                {
                    customStage1A = _customPropertyStage1A?.Trim();
                    customStage1B = _customPropertyStage1B?.Trim();
                    if (string.IsNullOrEmpty(customStage1A) || string.IsNullOrEmpty(customStage1B))
                    {
                        MessageBox.Show(
                            "Для первого уровня («Группировать по» -> «Своё свойство») укажите имя параметра для выбора A и для выбора B.",
                            "SmartGroupClashes",
                            MessageBoxButton.OK,
                            MessageBoxImage.Warning);
                        return;
                    }
                }

                if (thenByMode == GroupingMode.CustomProperty)
                {
                    customStage2A = _customPropertyStage2A?.Trim();
                    customStage2B = _customPropertyStage2B?.Trim();
                    if (string.IsNullOrEmpty(customStage2A) || string.IsNullOrEmpty(customStage2B))
                    {
                        MessageBox.Show(
                            "Для второго уровня («Затем по» -> «Своё свойство») укажите имя параметра для выбора A и для выбора B.",
                            "SmartGroupClashes",
                            MessageBoxButton.OK,
                            MessageBoxImage.Warning);
                        return;
                    }
                }

                if (thirdByMode == GroupingMode.CustomProperty)
                {
                    customStage3A = _customPropertyStage3A?.Trim();
                    customStage3B = _customPropertyStage3B?.Trim();
                    if (string.IsNullOrEmpty(customStage3A) || string.IsNullOrEmpty(customStage3B))
                    {
                        MessageBox.Show(
                            "Для третьего уровня («И затем по» -> «Своё свойство») укажите имя параметра для выбора A и для выбора B.",
                            "SmartGroupClashes",
                            MessageBoxButton.OK,
                            MessageBoxImage.Warning);
                        return;
                    }
                }

                foreach (var selectedItem in selectedItems)
                {
                    ClashTest clashTest = selectedItem.ClashTest;
                    if (clashTest.Children.Count != 0)
                    {
                        if (groupByMode != GroupingMode.None
                            || thenByMode != GroupingMode.None
                            || thirdByMode != GroupingMode.None)
                        {
                            bool grouped = GroupingFunctions.GroupClashes(
                                clashTest,
                                groupByMode,
                                thenByMode,
                                thirdByMode,
                                _keepExistingGroups,
                                _skipFixedGroups,
                                _analyzeNewStatus,
                                _analyzeActiveStatus,
                                _analyzeReviewedStatus,
                                _analyzeApprovedStatus,
                                _analyzeResolvedStatus,
                                customStage1A,
                                customStage1B,
                                customStage2A,
                                customStage2B,
                                customStage3A,
                                customStage3B);
                            groupingPerformed = groupingPerformed || grouped;
                        }
                    }
                }

                if (groupingPerformed)
                {
                    try
                    {
                        MessageBox.Show(
                            "Группировка выполнена успешно.",
                            "SmartGroupClashes",
                            MessageBoxButton.OK,
                            MessageBoxImage.Information);
                    }
                    catch (Exception notifyEx)
                    {
                        MessageBox.Show(
                            "Группировка выполнена, но завершить интерфейс без ошибок не удалось: " + notifyEx.Message,
                            "SmartGroupClashes",
                            MessageBoxButton.OK,
                            MessageBoxImage.Warning);
                    }
                }
                else
                {
                    MessageBox.Show(
                        "Нет пересечений для группировки.",
                        "SmartGroupClashes",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "Во время группировки произошла ошибка: " + ex.Message,
                    "SmartGroupClashes",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
            finally
            {
                RegisterChanges();
            }
        }

        /// <summary>Снимает группировку в выбранных тестах.</summary>
        private void ExecuteUngroup()
        {
            var selectedItems = ClashTests.Where(t => t.IsSelected).ToList();
            if (selectedItems.Count == 0)
            {
                return;
            }

            UnRegisterChanges();
            try
            {
                foreach (var selectedItem in selectedItems)
                {
                    ClashTest clashTest = selectedItem.ClashTest;
                    if (clashTest.Children.Count != 0)
                    {
                        GroupingFunctions.UnGroupClashes(clashTest);
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "Ошибка при снятии группировки: " + ex.Message,
                    "SmartGroupClashes",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
            finally
            {
                RegisterChanges();
            }
        }

        /// <summary>Сохраняет настройки принудительно.</summary>
        private void ExecuteSaveSettings()
        {
            SaveCurrentSettingsForDocument(force: true);
        }

        /// <summary>Выделяет все видимые тесты.</summary>
        private void ExecuteSelectAll()
        {
            foreach (var item in ClashTests)
            {
                if (_clashTestsView == null || _clashTestsView.Filter == null || _clashTestsView.Filter(item))
                {
                    item.IsSelected = true;
                }
            }
        }

        /// <summary>Снимает выбор со всех тестов.</summary>
        private void ExecuteClearSelection()
        {
            foreach (var item in ClashTests)
            {
                item.IsSelected = false;
            }
        }

        #endregion

        #region Видимость пользовательских свойств

        /// <summary>Возвращает режим группировки из выбранной опции или None.</summary>
        private static GroupingMode GetSelectedMode(GroupingModeOption option)
        {
            return option?.Mode ?? GroupingMode.None;
        }

        /// <summary>Реагирует на смену режима группировки: обновляет видимость и автосохранение.</summary>
        private void OnGroupingModeChanged()
        {
            RaiseCustomPropertyVisibilityChanged();
            CheckPlugin();
            AutoSaveSettingsIfMissingForDocument();
        }

        /// <summary>Реагирует на изменение настроек: обновляет состояние и автосохранение.</summary>
        private void OnSettingChanged()
        {
            CheckPlugin();
            AutoSaveSettingsIfMissingForDocument();
        }

        /// <summary>Уведомляет UI об изменении вычисляемых свойств видимости.</summary>
        private void RaiseCustomPropertyVisibilityChanged()
        {
            OnPropertyChanged(nameof(IsCustomPropertyStage1Visible));
            OnPropertyChanged(nameof(IsCustomPropertyStage2Visible));
            OnPropertyChanged(nameof(IsCustomPropertyStage3Visible));
            OnPropertyChanged(nameof(IsCustomPropertyParamsVisible));
        }

        #endregion

        #region Настройки (загрузка/сохранение)

        /// <summary>Загружает настройки из cfg-файла для текущего документа.</summary>
        private void TryLoadSettingsForCurrentDocument()
        {
            string documentKey = GetCurrentDocumentSettingsKey();
            if (string.IsNullOrWhiteSpace(documentKey))
            {
                return;
            }

            if (string.Equals(_loadedSettingsDocumentKey, documentKey, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            string cfgPath = GetSettingsFilePath(documentKey);
            if (!File.Exists(cfgPath))
            {
                _loadedSettingsDocumentKey = documentKey;
                return;
            }

            Dictionary<string, string> map = ReadSettingsMap(cfgPath);
            _isApplyingSettings = true;
            try
            {
                KeepExistingGroups = ParseBool(map, "KeepExistingGroups", false);
                SkipFixedGroups = ParseBool(map, "SkipFixedGroups", true);
                AnalyzeNewStatus = ParseBool(map, "AnalyzeStatusNew", true);
                AnalyzeActiveStatus = ParseBool(map, "AnalyzeStatusActive", true);
                AnalyzeReviewedStatus = ParseBool(map, "AnalyzeStatusReviewed", true);
                AnalyzeApprovedStatus = ParseBool(map, "AnalyzeStatusApproved", true);
                AnalyzeResolvedStatus = ParseBool(map, "AnalyzeStatusResolved", false);

                SetModeSelectionFromName(GroupByList, ParseText(map, "GroupByMode"), v => SelectedGroupBy = v);
                SetModeSelectionFromName(GroupThenList, ParseText(map, "ThenByMode"), v => SelectedThenBy = v);
                SetModeSelectionFromName(GroupThirdList, ParseText(map, "ThirdByMode"), v => SelectedThirdBy = v);

                ApplySelectedTests(ParseText(map, "SelectedTests"));

                CustomPropertyStage1A = ParseText(map, "CustomPropertyStage1A");
                CustomPropertyStage1B = ParseText(map, "CustomPropertyStage1B");
                CustomPropertyStage2A = ParseText(map, "CustomPropertyStage2A");
                CustomPropertyStage2B = ParseText(map, "CustomPropertyStage2B");
                CustomPropertyStage3A = ParseText(map, "CustomPropertyStage3A");
                CustomPropertyStage3B = ParseText(map, "CustomPropertyStage3B");
            }
            finally
            {
                _isApplyingSettings = false;
            }

            RaiseCustomPropertyVisibilityChanged();
            CheckPlugin();
            _loadedSettingsDocumentKey = documentKey;
        }

        /// <summary>Автосохранение настроек, если файл ещё не существует.</summary>
        private void AutoSaveSettingsIfMissingForDocument()
        {
            SaveCurrentSettingsForDocument(force: false);
        }

        /// <summary>Сохраняет текущие настройки в cfg-файл.</summary>
        private void SaveCurrentSettingsForDocument(bool force)
        {
            if (_isApplyingSettings)
            {
                return;
            }

            string documentKey = GetCurrentDocumentSettingsKey();
            if (string.IsNullOrWhiteSpace(documentKey))
            {
                return;
            }

            string cfgPath = GetSettingsFilePath(documentKey);
            if (!force && File.Exists(cfgPath))
            {
                return;
            }

            try
            {
                string cfgDir = IOPath.GetDirectoryName(cfgPath);
                if (!string.IsNullOrWhiteSpace(cfgDir))
                {
                    Directory.CreateDirectory(cfgDir);
                }

                List<string> lines = new List<string>
                {
                    "KeepExistingGroups=" + (_keepExistingGroups ? "true" : "false"),
                    "SkipFixedGroups=" + (_skipFixedGroups ? "true" : "false"),
                    "AnalyzeStatusNew=" + (_analyzeNewStatus ? "true" : "false"),
                    "AnalyzeStatusActive=" + (_analyzeActiveStatus ? "true" : "false"),
                    "AnalyzeStatusReviewed=" + (_analyzeReviewedStatus ? "true" : "false"),
                    "AnalyzeStatusApproved=" + (_analyzeApprovedStatus ? "true" : "false"),
                    "AnalyzeStatusResolved=" + (_analyzeResolvedStatus ? "true" : "false"),
                    "GroupByMode=" + GetSelectedMode(_selectedGroupBy),
                    "ThenByMode=" + GetSelectedMode(_selectedThenBy),
                    "ThirdByMode=" + GetSelectedMode(_selectedThirdBy),
                    "SelectedTests=" + EscapeCfgValue(GetSelectedTestsValue()),
                    "CustomPropertyStage1A=" + EscapeCfgValue(_customPropertyStage1A),
                    "CustomPropertyStage1B=" + EscapeCfgValue(_customPropertyStage1B),
                    "CustomPropertyStage2A=" + EscapeCfgValue(_customPropertyStage2A),
                    "CustomPropertyStage2B=" + EscapeCfgValue(_customPropertyStage2B),
                    "CustomPropertyStage3A=" + EscapeCfgValue(_customPropertyStage3A),
                    "CustomPropertyStage3B=" + EscapeCfgValue(_customPropertyStage3B),
                };

                File.WriteAllLines(cfgPath, lines, Encoding.UTF8);
                _loadedSettingsDocumentKey = documentKey;
            }
            catch (Exception)
            {
                // Ошибка записи настроек не должна прерывать работу
            }
        }

        /// <summary>Возвращает список выбранных тестов как строку с разделителем ';'.</summary>
        private string GetSelectedTestsValue()
        {
            List<string> selectedNames = new List<string>();
            foreach (var item in ClashTests.Where(t => t.IsSelected))
            {
                string name = item.DisplayName?.Trim();
                if (!string.IsNullOrWhiteSpace(name))
                {
                    selectedNames.Add(name.Replace(";", "\\;"));
                }
            }

            return string.Join(";", selectedNames);
        }

        /// <summary>Применяет выбор тестов по сохранённому списку имён.</summary>
        private void ApplySelectedTests(string serializedSelectedTests)
        {
            foreach (var item in ClashTests)
            {
                item.IsSelected = false;
            }

            if (string.IsNullOrWhiteSpace(serializedSelectedTests))
            {
                return;
            }

            HashSet<string> savedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string token in SplitEscapedBySemicolon(serializedSelectedTests))
            {
                string cleaned = token?.Trim();
                if (!string.IsNullOrWhiteSpace(cleaned))
                {
                    savedNames.Add(cleaned);
                }
            }

            if (savedNames.Count == 0)
            {
                return;
            }

            foreach (var item in ClashTests)
            {
                string name = item.DisplayName?.Trim();
                if (!string.IsNullOrWhiteSpace(name) && savedNames.Contains(name))
                {
                    item.IsSelected = true;
                }
            }
        }

        /// <summary>Выбирает режим группировки в списке по имени enum.</summary>
        private static void SetModeSelectionFromName(
            ObservableCollection<GroupingModeOption> list,
            string modeName,
            Action<GroupingModeOption> setter)
        {
            if (string.IsNullOrWhiteSpace(modeName))
            {
                return;
            }

            foreach (var option in list)
            {
                if (string.Equals(option.Mode.ToString(), modeName, StringComparison.OrdinalIgnoreCase))
                {
                    setter(option);
                    return;
                }
            }
        }

        #endregion

        #region Утилиты cfg

        /// <summary>Экранирует спецсимволы для записи в cfg.</summary>
        private static string EscapeCfgValue(string value)
        {
            return (value ?? string.Empty)
                .Replace("\\", "\\\\")
                .Replace("\r", string.Empty)
                .Replace("\n", "\\n");
        }

        /// <summary>Восстанавливает экранированные символы при чтении cfg.</summary>
        private static string UnescapeCfgValue(string value)
        {
            return (value ?? string.Empty)
                .Replace("\\n", "\n")
                .Replace("\\\\", "\\");
        }

        /// <summary>Читает cfg-файл в словарь ключ-значение.</summary>
        private static Dictionary<string, string> ReadSettingsMap(string cfgPath)
        {
            Dictionary<string, string> map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                foreach (string line in File.ReadAllLines(cfgPath, Encoding.UTF8))
                {
                    if (string.IsNullOrWhiteSpace(line))
                    {
                        continue;
                    }

                    int separatorIndex = line.IndexOf('=');
                    if (separatorIndex <= 0)
                    {
                        continue;
                    }

                    string key = line.Substring(0, separatorIndex).Trim();
                    string val = line.Substring(separatorIndex + 1);
                    map[key] = UnescapeCfgValue(val);
                }
            }
            catch (Exception)
            {
                // Ошибка чтения не должна прерывать работу
            }

            return map;
        }

        /// <summary>Возвращает строковое значение из словаря или пустую строку.</summary>
        private static string ParseText(Dictionary<string, string> map, string key)
        {
            string value;
            if (map.TryGetValue(key, out value))
            {
                return value ?? string.Empty;
            }

            return string.Empty;
        }

        /// <summary>Возвращает булево значение из словаря или значение по умолчанию.</summary>
        private static bool ParseBool(Dictionary<string, string> map, string key, bool defaultValue)
        {
            string value;
            if (!map.TryGetValue(key, out value))
            {
                return defaultValue;
            }

            bool parsed;
            if (bool.TryParse(value, out parsed))
            {
                return parsed;
            }

            return defaultValue;
        }

        /// <summary>Возвращает ключ настроек текущего документа.</summary>
        private static string GetCurrentDocumentSettingsKey()
        {
            try
            {
                if (NavisworksApplication.MainDocument == null || NavisworksApplication.MainDocument.IsClear)
                {
                    return null;
                }

                string fullPath = NavisworksApplication.MainDocument.FileName;
                if (string.IsNullOrWhiteSpace(fullPath))
                {
                    return null;
                }

                return IOPath.GetFileNameWithoutExtension(fullPath);
            }
            catch
            {
                return null;
            }
        }

        /// <summary>Возвращает каталог хранения пользовательских настроек.</summary>
        private static string GetSettingsRootDirectory()
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            return IOPath.Combine(appData, "SmartGroupClashes", "settings");
        }

        /// <summary>Формирует полный путь к cfg-файлу настроек.</summary>
        private static string GetSettingsFilePath(string documentKey)
        {
            string safeDocumentKey = SanitizeFileName(documentKey);
            return IOPath.Combine(GetSettingsRootDirectory(), safeDocumentKey + ".cfg");
        }

        /// <summary>Приводит строку к безопасному имени файла.</summary>
        private static string SanitizeFileName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return "unnamed";
            }

            char[] invalidChars = IOPath.GetInvalidFileNameChars();
            StringBuilder builder = new StringBuilder(name.Length);
            foreach (char c in name)
            {
                bool isInvalid = invalidChars.Contains(c);
                builder.Append(isInvalid ? '_' : c);
            }

            string cleaned = builder.ToString().Trim();
            return string.IsNullOrWhiteSpace(cleaned) ? "unnamed" : cleaned;
        }

        /// <summary>Делит строку по ';' с поддержкой экранирования '\;'.</summary>
        private static List<string> SplitEscapedBySemicolon(string value)
        {
            List<string> parts = new List<string>();
            if (string.IsNullOrEmpty(value))
            {
                return parts;
            }

            StringBuilder current = new StringBuilder();
            bool escape = false;
            foreach (char c in value)
            {
                if (escape)
                {
                    current.Append(c);
                    escape = false;
                    continue;
                }

                if (c == '\\')
                {
                    escape = true;
                    continue;
                }

                if (c == ';')
                {
                    parts.Add(current.ToString());
                    current.Clear();
                    continue;
                }

                current.Append(c);
            }

            if (escape)
            {
                current.Append('\\');
            }

            parts.Add(current.ToString());
            return parts;
        }

        #endregion
    }
}
