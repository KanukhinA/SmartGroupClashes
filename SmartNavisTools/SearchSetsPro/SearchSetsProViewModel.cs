using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Threading;
using Autodesk.Navisworks.Api;
using DocumentSelectionSets = Autodesk.Navisworks.Api.DocumentParts.DocumentSelectionSets;
using NavisworksApplication = Autodesk.Navisworks.Api.Application;

namespace SmartNavisTools
{
    /// <summary>
    /// Узел дерева поисковых наборов.
    /// </summary>
    internal sealed class TreeNodeViewModel : ViewModelBase
    {
        private bool _isChecked;
        private bool _isExpanded;
        private bool _isUpdating;

        public TreeNodeViewModel(string displayName, bool isFolder, bool hasSearch, object tag)
        {
            DisplayName = displayName;
            IsFolder = isFolder;
            HasSearch = hasSearch;
            Tag = tag;
            Children = new ObservableCollection<TreeNodeViewModel>();
        }

        /// <summary>Отображаемое имя.</summary>
        public string DisplayName { get; }

        /// <summary>Является ли узел папкой.</summary>
        public bool IsFolder { get; }

        /// <summary>Содержит ли набор условия поиска.</summary>
        public bool HasSearch { get; }

        /// <summary>Ссылка на объект (SearchSetReference или FolderNode).</summary>
        public object Tag { get; set; }

        /// <summary>Дочерние узлы.</summary>
        public ObservableCollection<TreeNodeViewModel> Children { get; }

        /// <summary>Отмечен ли узел.</summary>
        public bool IsChecked
        {
            get => _isChecked;
            set
            {
                if (_isUpdating || !SetProperty(ref _isChecked, value))
                {
                    return;
                }

                if (IsFolder)
                {
                    _isUpdating = true;
                    try
                    {
                        SetDescendantChecks(value);
                    }
                    finally
                    {
                        _isUpdating = false;
                    }
                }

                OnCheckedChanged?.Invoke();
            }
        }

        /// <summary>Развёрнут ли узел.</summary>
        public bool IsExpanded
        {
            get => _isExpanded;
            set => SetProperty(ref _isExpanded, value);
        }

        /// <summary>Обратный вызов при изменении выбора.</summary>
        public Action OnCheckedChanged { get; set; }

        /// <summary>Устанавливает отметку без рекурсии и уведомлений.</summary>
        public void SetCheckedSilent(bool value)
        {
            _isUpdating = true;
            try
            {
                SetProperty(ref _isChecked, value, nameof(IsChecked));
            }
            finally
            {
                _isUpdating = false;
            }
        }

        /// <summary>Рекурсивно устанавливает отметку дочерних наборов.</summary>
        private void SetDescendantChecks(bool value)
        {
            foreach (TreeNodeViewModel child in Children)
            {
                if (child.IsFolder)
                {
                    child.SetCheckedSilent(value);
                    child.SetDescendantChecks(value);
                }
                else if (child.HasSearch)
                {
                    child.SetCheckedSilent(value);
                }
            }
        }
    }

    /// <summary>
    /// Элемент условия для удаления.
    /// </summary>
    internal sealed class ConditionItemViewModel : ViewModelBase
    {
        private bool _isChecked;

        public ConditionItemViewModel(SearchSetsProLogic.CollectedConditionInfo info)
        {
            DisplayText = info.DisplayText;
            Descriptor = info.Descriptor;
        }

        /// <summary>Текст отображения.</summary>
        public string DisplayText { get; }

        /// <summary>Дескриптор условия.</summary>
        public SearchConditionDescriptor Descriptor { get; }

        /// <summary>Отмечено ли условие.</summary>
        public bool IsChecked
        {
            get => _isChecked;
            set => SetProperty(ref _isChecked, value);
        }
    }

    /// <summary>
    /// Элемент списка операторов сравнения.
    /// </summary>
    internal sealed class ComparisonOptionViewModel
    {
        public ComparisonOptionViewModel(SearchConditionComparison comparison, string displayName)
        {
            Comparison = comparison;
            DisplayName = displayName;
        }

        /// <summary>Значение перечисления.</summary>
        public SearchConditionComparison Comparison { get; }

        /// <summary>Отображаемое имя.</summary>
        public string DisplayName { get; }

        public override string ToString() => DisplayName;
    }

    /// <summary>
    /// ViewModel панели «Поисковые наборы PRO».
    /// </summary>
    internal sealed class SearchSetsProViewModel : ViewModelBase, IDisposable
    {
        private readonly PropertyCatalog _catalog = new PropertyCatalog();
        private readonly Dispatcher _dispatcher;
        private bool _isSubscribed;
        private bool _isUpdatingTree;

        private string _filterText = string.Empty;
        private int _selectedCount;
        private int _activeTabIndex;
        private string _sourceCategory = string.Empty;
        private string _sourceProperty = string.Empty;
        private string _targetCategory = string.Empty;
        private string _targetProperty = string.Empty;
        private ComparisonOptionViewModel _selectedComparison;
        private string _valueText = string.Empty;
        private bool _ignoreCase;
        private bool _ignoreAccents;
        private bool _updateExisting = true;
        private bool _createCopies;
        private string _copySuffix = " (копия)";
        private string _applyButtonText = "Создать";
        private bool _deleteFromList = true;
        private bool _deleteByProperty;
        private string _conditionsSummary = "Условия: выберите наборы в дереве";
        private string _limitationsText =
            "Поддерживаются только поисковые наборы (с условиями фильтрации). "
            + "Категории и свойства берутся из поисковых наборов документа; "
            + "редкие значения можно ввести вручную.";

        public SearchSetsProViewModel()
        {
            _dispatcher = Dispatcher.CurrentDispatcher;

            TreeRootNodes = new ObservableCollection<TreeNodeViewModel>();
            CategorySuggestions = new ObservableCollection<string>();
            PropertySuggestions = new ObservableCollection<string>();
            Conditions = new ObservableCollection<ConditionItemViewModel>();

            ComparisonOptions = new ObservableCollection<ComparisonOptionViewModel>
            {
                new ComparisonOptionViewModel(SearchConditionComparison.Equal, "Равно"),
                new ComparisonOptionViewModel(SearchConditionComparison.NotEqual, "Не равно"),
                new ComparisonOptionViewModel(SearchConditionComparison.DisplayStringContains, "Содержит"),
                new ComparisonOptionViewModel(SearchConditionComparison.DisplayStringWildcard, "Маска"),
                new ComparisonOptionViewModel(SearchConditionComparison.NumericLessThan, "Меньше"),
                new ComparisonOptionViewModel(SearchConditionComparison.NumericLessThanOrEqual, "Меньше или равно"),
                new ComparisonOptionViewModel(SearchConditionComparison.NumericGreaterThanOrEqual, "Больше или равно"),
                new ComparisonOptionViewModel(SearchConditionComparison.NumericGreaterThan, "Больше")
            };
            _selectedComparison = ComparisonOptions[0];

            RefreshTreeCommand = new RelayCommand(ExecuteRefreshTree);
            SelectAllCommand = new RelayCommand(() => SetAllChecks(true));
            ClearSelectionCommand = new RelayCommand(() => SetAllChecks(false));
            RefreshCatalogCommand = new RelayCommand(ExecuteRefreshCatalog);
            RefreshConditionsCommand = new RelayCommand(ExecuteRefreshConditions);
            SelectAllConditionsCommand = new RelayCommand(() => SetAllConditionChecks(true));
            ClearConditionsCommand = new RelayCommand(() => SetAllConditionChecks(false));
            ApplyCommand = new RelayCommand(ExecuteApply, CanExecuteApply);

            SubscribeToDocumentChanges();
            _catalog.BindDocument(NavisworksApplication.MainDocument);
            RefreshTree();
        }

        // --- Свойства ---

        /// <summary>Текст фильтра дерева.</summary>
        public string FilterText
        {
            get => _filterText;
            set
            {
                if (SetProperty(ref _filterText, value))
                {
                    RefreshTree();
                }
            }
        }

        /// <summary>Корневые узлы дерева.</summary>
        public ObservableCollection<TreeNodeViewModel> TreeRootNodes { get; }

        /// <summary>Количество выбранных наборов.</summary>
        public int SelectedCount
        {
            get => _selectedCount;
            private set => SetProperty(ref _selectedCount, value);
        }

        /// <summary>Индекс активной вкладки (0=Добавить, 1=Заменить, 2=Удалить).</summary>
        public int ActiveTabIndex
        {
            get => _activeTabIndex;
            set
            {
                if (SetProperty(ref _activeTabIndex, value))
                {
                    UpdateApplyButtonText();
                    if (value == 2 && _deleteFromList)
                    {
                        RefreshDeleteConditionsList();
                    }
                }
            }
        }

        /// <summary>Категория исходного условия.</summary>
        public string SourceCategory
        {
            get => _sourceCategory;
            set
            {
                if (SetProperty(ref _sourceCategory, value))
                {
                    RefreshPropertySuggestions(value);
                }
            }
        }

        /// <summary>Свойство исходного условия.</summary>
        public string SourceProperty
        {
            get => _sourceProperty;
            set => SetProperty(ref _sourceProperty, value);
        }

        /// <summary>Категория нового условия.</summary>
        public string TargetCategory
        {
            get => _targetCategory;
            set
            {
                if (SetProperty(ref _targetCategory, value))
                {
                    RefreshPropertySuggestions(value);
                }
            }
        }

        /// <summary>Свойство нового условия.</summary>
        public string TargetProperty
        {
            get => _targetProperty;
            set => SetProperty(ref _targetProperty, value);
        }

        /// <summary>Подсказки категорий из каталога.</summary>
        public ObservableCollection<string> CategorySuggestions { get; }

        /// <summary>Подсказки свойств из каталога.</summary>
        public ObservableCollection<string> PropertySuggestions { get; }

        /// <summary>Список операторов сравнения.</summary>
        public ObservableCollection<ComparisonOptionViewModel> ComparisonOptions { get; }

        /// <summary>Выбранный оператор сравнения.</summary>
        public ComparisonOptionViewModel SelectedComparison
        {
            get => _selectedComparison;
            set => SetProperty(ref _selectedComparison, value);
        }

        /// <summary>Текст значения.</summary>
        public string ValueText
        {
            get => _valueText;
            set => SetProperty(ref _valueText, value);
        }

        /// <summary>Игнорировать регистр.</summary>
        public bool IgnoreCase
        {
            get => _ignoreCase;
            set => SetProperty(ref _ignoreCase, value);
        }

        /// <summary>Игнорировать акценты.</summary>
        public bool IgnoreAccents
        {
            get => _ignoreAccents;
            set => SetProperty(ref _ignoreAccents, value);
        }

        /// <summary>Обновить существующие наборы.</summary>
        public bool UpdateExisting
        {
            get => _updateExisting;
            set
            {
                if (SetProperty(ref _updateExisting, value) && value)
                {
                    if (_createCopies)
                    {
                        SetProperty(ref _createCopies, false, nameof(CreateCopies));
                    }
                }
            }
        }

        /// <summary>Создать копии.</summary>
        public bool CreateCopies
        {
            get => _createCopies;
            set
            {
                if (SetProperty(ref _createCopies, value) && value)
                {
                    if (_updateExisting)
                    {
                        SetProperty(ref _updateExisting, false, nameof(UpdateExisting));
                    }
                }
            }
        }

        /// <summary>Суффикс копии.</summary>
        public string CopySuffix
        {
            get => _copySuffix;
            set => SetProperty(ref _copySuffix, value);
        }

        /// <summary>Текст кнопки применения.</summary>
        public string ApplyButtonText
        {
            get => _applyButtonText;
            private set => SetProperty(ref _applyButtonText, value);
        }

        /// <summary>Удалять из списка условий.</summary>
        public bool DeleteFromList
        {
            get => _deleteFromList;
            set
            {
                if (SetProperty(ref _deleteFromList, value))
                {
                    if (value && _deleteByProperty)
                    {
                        SetProperty(ref _deleteByProperty, false, nameof(DeleteByProperty));
                    }

                    if (value && _activeTabIndex == 2)
                    {
                        RefreshDeleteConditionsList();
                    }
                }
            }
        }

        /// <summary>Удалять по категории и свойству.</summary>
        public bool DeleteByProperty
        {
            get => _deleteByProperty;
            set
            {
                if (SetProperty(ref _deleteByProperty, value))
                {
                    if (value && _deleteFromList)
                    {
                        SetProperty(ref _deleteFromList, false, nameof(DeleteFromList));
                    }

                    if (value)
                    {
                        Conditions.Clear();
                        ConditionsSummary = "Условия: используется удаление по категории и свойству.";
                    }
                }
            }
        }

        /// <summary>Коллекция условий для удаления.</summary>
        public ObservableCollection<ConditionItemViewModel> Conditions { get; }

        /// <summary>Сводка по условиям.</summary>
        public string ConditionsSummary
        {
            get => _conditionsSummary;
            private set => SetProperty(ref _conditionsSummary, value);
        }

        /// <summary>Текст ограничений.</summary>
        public string LimitationsText
        {
            get => _limitationsText;
            private set => SetProperty(ref _limitationsText, value);
        }

        // --- Команды ---

        public RelayCommand RefreshTreeCommand { get; }
        public RelayCommand SelectAllCommand { get; }
        public RelayCommand ClearSelectionCommand { get; }
        public RelayCommand RefreshCatalogCommand { get; }
        public RelayCommand RefreshConditionsCommand { get; }
        public RelayCommand SelectAllConditionsCommand { get; }
        public RelayCommand ClearConditionsCommand { get; }
        public RelayCommand ApplyCommand { get; }

        // --- Дерево ---

        /// <summary>Обновляет дерево из Navisworks.</summary>
        private void ExecuteRefreshTree()
        {
            RefreshTree();
        }

        /// <summary>Перестраивает дерево с учётом фильтра и сохранением состояния.</summary>
        private void RefreshTree()
        {
            if (_isUpdatingTree)
            {
                return;
            }

            _isUpdatingTree = true;
            try
            {
                var checkedGuids = new HashSet<Guid>();
                CollectCheckedGuids(TreeRootNodes, checkedGuids);

                string filter = (_filterText ?? string.Empty).Trim();
                TreeRootNodes.Clear();

                SearchSetsProLogic.FolderNode root = SearchSetsProLogic.GetSearchSetTree();

                foreach (SearchSetsProLogic.FolderNode folder in root.Folders)
                {
                    TreeNodeViewModel folderVm = BuildFolderNode(folder, filter, checkedGuids);
                    if (folderVm != null)
                    {
                        TreeRootNodes.Add(folderVm);
                    }
                }

                foreach (SearchSetsProLogic.SearchSetReference reference in root.SearchSets)
                {
                    if (!MatchesFilter(reference.DisplayName, filter))
                    {
                        continue;
                    }

                    TreeRootNodes.Add(CreateSearchSetNode(reference, checkedGuids));
                }

                SyncFolderCheckStates(TreeRootNodes);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("RefreshTree error: " + ex.Message);
            }
            finally
            {
                _isUpdatingTree = false;
            }

            UpdateSelectionSummary();
        }

        /// <summary>Собирает GUID отмеченных наборов.</summary>
        private static void CollectCheckedGuids(
            ObservableCollection<TreeNodeViewModel> nodes,
            HashSet<Guid> guids)
        {
            foreach (TreeNodeViewModel node in nodes)
            {
                if (!node.IsFolder && node.IsChecked && node.Tag is SearchSetsProLogic.SearchSetReference reference)
                {
                    guids.Add(reference.Guid);
                }

                CollectCheckedGuids(node.Children, guids);
            }
        }

        /// <summary>Строит узел папки рекурсивно.</summary>
        private TreeNodeViewModel BuildFolderNode(
            SearchSetsProLogic.FolderNode folder,
            string filter,
            HashSet<Guid> checkedGuids)
        {
            var node = new TreeNodeViewModel(folder.DisplayName, true, false, folder);
            node.OnCheckedChanged = UpdateSelectionSummary;
            node.IsExpanded = true;

            bool hasVisibleChildren = false;

            foreach (SearchSetsProLogic.FolderNode childFolder in folder.Folders)
            {
                TreeNodeViewModel childVm = BuildFolderNode(childFolder, filter, checkedGuids);
                if (childVm != null)
                {
                    node.Children.Add(childVm);
                    hasVisibleChildren = true;
                }
            }

            foreach (SearchSetsProLogic.SearchSetReference reference in folder.SearchSets)
            {
                if (!MatchesFilter(reference.DisplayName, filter))
                {
                    continue;
                }

                node.Children.Add(CreateSearchSetNode(reference, checkedGuids));
                hasVisibleChildren = true;
            }

            return hasVisibleChildren ? node : null;
        }

        /// <summary>Создаёт узел поискового набора.</summary>
        private TreeNodeViewModel CreateSearchSetNode(
            SearchSetsProLogic.SearchSetReference reference,
            HashSet<Guid> checkedGuids)
        {
            string text = reference.DisplayName;
            if (!reference.HasSearch)
            {
                text += " (без поиска)";
            }

            var node = new TreeNodeViewModel(text, false, reference.HasSearch, reference);
            node.OnCheckedChanged = UpdateSelectionSummary;

            if (reference.HasSearch && checkedGuids.Contains(reference.Guid))
            {
                node.SetCheckedSilent(true);
            }

            return node;
        }

        /// <summary>Синхронизирует состояние отметок папок с дочерними узлами.</summary>
        private static void SyncFolderCheckStates(ObservableCollection<TreeNodeViewModel> nodes)
        {
            foreach (TreeNodeViewModel node in nodes)
            {
                if (node.IsFolder)
                {
                    SyncFolderCheckStates(node.Children);
                    bool allChecked = AreAllSelectableDescendantsChecked(node);
                    node.SetCheckedSilent(allChecked);
                }
            }
        }

        /// <summary>Проверяет, все ли выбираемые потомки отмечены.</summary>
        private static bool AreAllSelectableDescendantsChecked(TreeNodeViewModel folderNode)
        {
            bool hasSelectable = false;
            foreach (TreeNodeViewModel node in EnumerateAll(folderNode.Children))
            {
                if (!node.IsFolder && node.HasSearch)
                {
                    hasSelectable = true;
                    if (!node.IsChecked)
                    {
                        return false;
                    }
                }
            }

            return hasSelectable;
        }

        /// <summary>Рекурсивно перечисляет все узлы.</summary>
        private static IEnumerable<TreeNodeViewModel> EnumerateAll(
            ObservableCollection<TreeNodeViewModel> nodes)
        {
            foreach (TreeNodeViewModel node in nodes)
            {
                yield return node;
                foreach (TreeNodeViewModel child in EnumerateAll(node.Children))
                {
                    yield return child;
                }
            }
        }

        /// <summary>Проверяет соответствие имени фильтру.</summary>
        private static bool MatchesFilter(string displayName, string filter)
        {
            return string.IsNullOrWhiteSpace(filter)
                || displayName.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        /// <summary>Устанавливает отметки на всех узлах.</summary>
        private void SetAllChecks(bool isChecked)
        {
            _isUpdatingTree = true;
            try
            {
                foreach (TreeNodeViewModel node in EnumerateAll(TreeRootNodes))
                {
                    if (node.IsFolder || node.HasSearch)
                    {
                        node.SetCheckedSilent(isChecked);
                    }
                }
            }
            finally
            {
                _isUpdatingTree = false;
            }

            UpdateSelectionSummary();
        }

        /// <summary>Возвращает отмеченные ссылки на наборы.</summary>
        private IEnumerable<SearchSetsProLogic.SearchSetReference> GetCheckedReferences()
        {
            return EnumerateAll(TreeRootNodes)
                .Where(n => !n.IsFolder && n.IsChecked && n.Tag is SearchSetsProLogic.SearchSetReference)
                .Select(n => (SearchSetsProLogic.SearchSetReference)n.Tag);
        }

        /// <summary>Обновляет сводку выбранных наборов.</summary>
        private void UpdateSelectionSummary()
        {
            SelectedCount = GetCheckedReferences().Count();
            ApplyCommand.RaiseCanExecuteChanged();

            if (_activeTabIndex == 2 && _deleteFromList)
            {
                RefreshDeleteConditionsList();
            }
        }

        // --- Каталог ---

        /// <summary>Обновляет каталог категорий и свойств из документа.</summary>
        private void ExecuteRefreshCatalog()
        {
            try
            {
                Document document = NavisworksApplication.MainDocument;
                if (document == null || document.IsClear)
                {
                    return;
                }

                _catalog.Refresh(document);
                _catalog.EnsureLoaded(document);
                RefreshCategorySuggestions();

                int categoryCount = _catalog.Categories.Count;
                string message = categoryCount > 0
                    ? "Каталог обновлён из поисковых наборов документа. Категорий: " + categoryCount + "."
                    : "В документе не найдено условий в поисковых наборах. Введите категорию и свойство вручную.";

                MessageBox.Show(
                    message,
                    "Каталог свойств",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
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

        /// <summary>Обновляет подсказки категорий.</summary>
        private void RefreshCategorySuggestions()
        {
            _catalog.EnsureLoaded(NavisworksApplication.MainDocument);
            CategorySuggestions.Clear();
            foreach (string cat in _catalog.Categories)
            {
                CategorySuggestions.Add(cat);
            }
        }

        /// <summary>Обновляет подсказки свойств для указанной категории.</summary>
        private void RefreshPropertySuggestions(string category)
        {
            _catalog.EnsureLoaded(NavisworksApplication.MainDocument);
            PropertySuggestions.Clear();
            foreach (string prop in _catalog.GetProperties(category))
            {
                PropertySuggestions.Add(prop);
            }
        }

        // --- Условия (удаление) ---

        /// <summary>Обновляет список условий для удаления.</summary>
        private void ExecuteRefreshConditions()
        {
            RefreshDeleteConditionsList();
        }

        /// <summary>Загружает условия из выбранных наборов.</summary>
        private void RefreshDeleteConditionsList()
        {
            if (_activeTabIndex != 2 || !_deleteFromList)
            {
                return;
            }

            try
            {
                SearchSetsProLogic.SearchSetReference[] references = GetCheckedReferences().ToArray();
                IReadOnlyList<SearchSetsProLogic.CollectedConditionInfo> conditions =
                    SearchSetsProLogic.CollectConditionsFromReferences(references);

                Conditions.Clear();
                foreach (SearchSetsProLogic.CollectedConditionInfo condition in conditions)
                {
                    Conditions.Add(new ConditionItemViewModel(condition));
                }

                if (references.Length == 0)
                {
                    ConditionsSummary = "Условия: сначала выберите поисковые наборы в дереве.";
                }
                else if (conditions.Count == 0)
                {
                    ConditionsSummary =
                        "Условия: в выбранных наборах (" + references.Length + ") не найдено условий поиска.";
                }
                else
                {
                    ConditionsSummary =
                        "Уникальных условий в " + references.Length + " наборах: " + conditions.Count + ".";
                }
            }
            catch (Exception ex)
            {
                ConditionsSummary = "Не удалось загрузить условия: " + ex.Message;
            }
        }

        /// <summary>Устанавливает отметки на всех условиях.</summary>
        private void SetAllConditionChecks(bool isChecked)
        {
            foreach (ConditionItemViewModel item in Conditions)
            {
                item.IsChecked = isChecked;
            }
        }

        // --- Вкладки ---

        /// <summary>Обновляет текст кнопки применения.</summary>
        private void UpdateApplyButtonText()
        {
            switch (_activeTabIndex)
            {
                case 1:
                    ApplyButtonText = "Заменить";
                    break;
                case 2:
                    ApplyButtonText = "Удалить";
                    break;
                default:
                    ApplyButtonText = "Создать";
                    break;
            }
        }

        // --- Применение ---

        /// <summary>Проверяет возможность применения операции.</summary>
        private bool CanExecuteApply()
        {
            return _selectedCount > 0;
        }

        /// <summary>Выполняет операцию над выбранными наборами.</summary>
        private void ExecuteApply()
        {
            try
            {
                SearchSetsProOperation operation;
                switch (_activeTabIndex)
                {
                    case 1:
                        operation = SearchSetsProOperation.Replace;
                        break;
                    case 2:
                        operation = SearchSetsProOperation.Delete;
                        break;
                    default:
                        operation = SearchSetsProOperation.Add;
                        break;
                }

                var request = new SearchSetsProLogic.ApplyRequest
                {
                    Operation = operation,
                    ApplyMode = _createCopies
                        ? SearchSetsProApplyMode.CreateCopies
                        : SearchSetsProApplyMode.UpdateExisting,
                    DeleteMode = _deleteByProperty
                        ? SearchSetsProDeleteMode.ByProperty
                        : SearchSetsProDeleteMode.FromSelectedSets,
                    CopySuffix = _copySuffix,
                    SourceCategory = (_sourceCategory ?? string.Empty).Trim(),
                    SourceProperty = (_sourceProperty ?? string.Empty).Trim(),
                    TargetCategory = (_targetCategory ?? string.Empty).Trim(),
                    TargetProperty = (_targetProperty ?? string.Empty).Trim(),
                    ValueText = _valueText,
                    IgnoreCase = _ignoreCase,
                    IgnoreAccents = _ignoreAccents,
                    ConditionsToDelete = GetSelectedConditionsToDelete()
                };

                if (_selectedComparison != null)
                {
                    request.Comparison = _selectedComparison.Comparison;
                }

                _catalog.EnsureLoaded(NavisworksApplication.MainDocument);
                _catalog.EnsureProperty(request.TargetCategory, request.TargetProperty);
                if (request.Operation == SearchSetsProOperation.Replace
                    || (request.Operation == SearchSetsProOperation.Delete
                        && request.DeleteMode == SearchSetsProDeleteMode.ByProperty))
                {
                    _catalog.EnsureProperty(request.SourceCategory, request.SourceProperty);
                }

                SearchSetsProLogic.ApplyResult result =
                    SearchSetsProLogic.Apply(GetCheckedReferences(), request);

                MessageBox.Show(
                    result.Success
                        ? SearchSetsProLogic.FormatResultMessage(result)
                        : result.ErrorMessage ?? "Не удалось применить изменения.",
                    result.Success ? "Результат" : "Ошибка",
                    MessageBoxButton.OK,
                    result.Success ? MessageBoxImage.Information : MessageBoxImage.Warning);

                if (result.ProcessedCount > 0)
                {
                    RefreshTree();
                }
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

        /// <summary>Возвращает дескрипторы отмеченных условий для удаления.</summary>
        private IReadOnlyList<SearchConditionDescriptor> GetSelectedConditionsToDelete()
        {
            return Conditions
                .Where(c => c.IsChecked)
                .Select(c => c.Descriptor)
                .ToArray();
        }

        // --- Подписки ---

        /// <summary>Подписывается на изменения документа.</summary>
        private void SubscribeToDocumentChanges()
        {
            if (_isSubscribed || NavisworksApplication.MainDocument == null)
            {
                return;
            }

            NavisworksApplication.MainDocument.Database.Changed += OnDocumentChanged;
            DocumentSelectionSets selectionSets = NavisworksApplication.MainDocument.SelectionSets;
            if (selectionSets != null)
            {
                selectionSets.Changed += OnDocumentChanged;
            }

            _isSubscribed = true;
        }

        /// <summary>Отписывается от изменений документа.</summary>
        private void UnsubscribeFromDocumentChanges()
        {
            if (!_isSubscribed)
            {
                return;
            }

            Document document = NavisworksApplication.MainDocument;
            if (document != null)
            {
                document.Database.Changed -= OnDocumentChanged;
                DocumentSelectionSets selectionSets = document.SelectionSets;
                if (selectionSets != null)
                {
                    selectionSets.Changed -= OnDocumentChanged;
                }
            }

            _isSubscribed = false;
        }

        /// <summary>Обработчик изменения документа с маршалингом в UI-поток.</summary>
        private void OnDocumentChanged(object sender, EventArgs e)
        {
            _dispatcher.BeginInvoke(new Action(OnDocumentChangedOnUiThread));
        }

        /// <summary>Обработчик изменения документа в UI-потоке.</summary>
        private void OnDocumentChangedOnUiThread()
        {
            _catalog.InvalidateCurrentDocument();
            _catalog.BindDocument(NavisworksApplication.MainDocument);
            RefreshTree();
        }

        /// <summary>Освобождает подписки.</summary>
        public void Dispose()
        {
            UnsubscribeFromDocumentChanges();
        }
    }
}
