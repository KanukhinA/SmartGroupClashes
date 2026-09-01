using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using Autodesk.Navisworks.Api;
using DocumentSelectionSets = Autodesk.Navisworks.Api.DocumentParts.DocumentSelectionSets;
using NavisworksApplication = Autodesk.Navisworks.Api.Application;

namespace SmartNavisTools
{
    /// <summary>
    /// UI панели «Поисковые наборы PRO».
    /// </summary>
    internal sealed class SearchSetsProControl : UserControl
    {
        private readonly PropertyCatalog _catalog = new PropertyCatalog();
        private readonly TreeView _treeView;
        private readonly TextBox _filterTextBox;
        private readonly Label _selectionLabel;
        private readonly RadioButton _deleteFromListRadio;
        private readonly RadioButton _deleteByPropertyRadio;
        private readonly Panel _deleteModePanel;
        private readonly CheckedListBox _conditionsCheckedListBox;
        private readonly Label _conditionsSummaryLabel;
        private readonly Button _refreshConditionsButton;
        private readonly FlowLayoutPanel _conditionsButtonsPanel;
        private readonly Panel _sourcePanel;
        private readonly Panel _targetPanel;
        private readonly ComboBox _sourceCategoryCombo;
        private readonly ComboBox _sourcePropertyCombo;
        private readonly ComboBox _targetCategoryCombo;
        private readonly ComboBox _targetPropertyCombo;
        private readonly ComboBox _comparisonCombo;
        private readonly TextBox _valueTextBox;
        private readonly Label _valueSectionLabel;
        private readonly FlowLayoutPanel _valueSectionPanel;
        private readonly CheckBox _ignoreCaseCheckBox;
        private readonly CheckBox _ignoreAccentsCheckBox;
        private readonly RadioButton _updateExistingRadio;
        private readonly RadioButton _createCopiesRadio;
        private readonly TextBox _copySuffixTextBox;
        private readonly Button _applyButton;
        private readonly Button _refreshTreeButton;
        private readonly Button _refreshCatalogButton;
        private readonly Label _limitationsLabel;
        private readonly SplitContainer _mainSplitContainer;
        private readonly TabControl _operationTabs;
        private readonly TabPage _addTabPage;
        private readonly TabPage _replaceTabPage;
        private readonly TabPage _deleteTabPage;
        private bool _isSubscribed;
        private bool _isUpdatingTree;

        private sealed class TreeViewState
        {
            public HashSet<Guid> CheckedGuids { get; } = new HashSet<Guid>();

            public HashSet<string> CheckedSetKeys { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            public HashSet<string> ExpandedFolderPaths { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            public string TopNodeKey { get; set; }
        }

        public SearchSetsProControl()
        {
            var headerLabel = new Label
            {
                Text = "Поисковые наборы PRO",
                Dock = DockStyle.Top,
                Height = 24,
                Padding = new Padding(8, 6, 8, 0)
            };

            _filterTextBox = new TextBox { Dock = DockStyle.Fill };
            _filterTextBox.TextChanged += (_, __) => RefreshTree();

            var filterPanel = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                Height = 28,
                ColumnCount = 2,
                Padding = new Padding(8, 4, 8, 0)
            };
            filterPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            filterPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            filterPanel.Controls.Add(new Label
            {
                Text = "Фильтр:",
                AutoSize = true,
                Anchor = AnchorStyles.Left,
                Padding = new Padding(0, 4, 6, 0)
            }, 0, 0);
            filterPanel.Controls.Add(_filterTextBox, 1, 0);

            _treeView = new TreeView
            {
                Dock = DockStyle.Fill,
                CheckBoxes = true,
                HideSelection = false,
                ShowNodeToolTips = true
            };
            _treeView.BeforeCheck += OnTreeBeforeCheck;
            _treeView.AfterCheck += OnTreeAfterCheck;

            _selectionLabel = new Label
            {
                Text = "Выбрано наборов: 0",
                AutoSize = true
            };

            _refreshTreeButton = new Button
            {
                Text = "Обновить список",
                AutoSize = true,
                MinimumSize = new Size(120, 23)
            };
            _refreshTreeButton.Click += (_, __) => RefreshTree();

            var selectAllButton = new Button
            {
                Text = "Выбрать все",
                AutoSize = true,
                MinimumSize = new Size(100, 23)
            };
            selectAllButton.Click += (_, __) => SetAllChecks(true);

            var clearSelectionButton = new Button
            {
                Text = "Снять выбор",
                AutoSize = true,
                MinimumSize = new Size(100, 23)
            };
            clearSelectionButton.Click += (_, __) => SetAllChecks(false);

            var treeButtonsPanel = new FlowLayoutPanel
            {
                Dock = DockStyle.Bottom,
                AutoSize = true,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = true,
                Padding = new Padding(8, 4, 8, 4)
            };
            treeButtonsPanel.Controls.Add(_selectionLabel);
            treeButtonsPanel.Controls.Add(_refreshTreeButton);
            treeButtonsPanel.Controls.Add(selectAllButton);
            treeButtonsPanel.Controls.Add(clearSelectionButton);

            var treePanel = new Panel { Dock = DockStyle.Fill };
            treePanel.Controls.Add(_treeView);
            treePanel.Controls.Add(treeButtonsPanel);

            _deleteFromListRadio = new RadioButton
            {
                Text = "Выбрать из условий выбранных наборов",
                AutoSize = true,
                Checked = true
            };
            _deleteByPropertyRadio = new RadioButton
            {
                Text = "По категории и свойству (все совпадения)",
                AutoSize = true
            };
            _deleteFromListRadio.CheckedChanged += (_, __) => OnDeleteModeRadioChanged();
            _deleteByPropertyRadio.CheckedChanged += (_, __) => OnDeleteModeRadioChanged();

            _conditionsCheckedListBox = new CheckedListBox
            {
                CheckOnClick = true,
                Height = 140,
                Width = 480,
                IntegralHeight = false,
                HorizontalScrollbar = true
            };

            _conditionsSummaryLabel = new Label
            {
                Text = "Условия: выберите наборы в дереве",
                AutoSize = true,
                ForeColor = SystemColors.GrayText
            };

            _refreshConditionsButton = new Button
            {
                Text = "Обновить список условий",
                AutoSize = true,
                MinimumSize = new Size(160, 23)
            };
            _refreshConditionsButton.Click += (_, __) => RefreshDeleteConditionsList();

            var selectAllConditionsButton = new Button
            {
                Text = "Отметить все",
                AutoSize = true,
                MinimumSize = new Size(100, 23)
            };
            selectAllConditionsButton.Click += (_, __) => SetAllConditionChecks(true);

            var clearConditionsButton = new Button
            {
                Text = "Снять все",
                AutoSize = true,
                MinimumSize = new Size(100, 23)
            };
            clearConditionsButton.Click += (_, __) => SetAllConditionChecks(false);

            var conditionsButtonsPanel = new FlowLayoutPanel
            {
                AutoSize = true,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = true,
                Padding = new Padding(0, 4, 0, 0)
            };
            _conditionsButtonsPanel = conditionsButtonsPanel;
            conditionsButtonsPanel.Controls.Add(_refreshConditionsButton);
            conditionsButtonsPanel.Controls.Add(selectAllConditionsButton);
            conditionsButtonsPanel.Controls.Add(clearConditionsButton);

            _deleteModePanel = new Panel
            {
                AutoSize = true,
                Padding = new Padding(8, 0, 8, 0)
            };
            var deleteModeLayout = new FlowLayoutPanel
            {
                AutoSize = true,
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                Width = 500
            };
            deleteModeLayout.Controls.Add(new Label
            {
                Text = "Режим удаления:",
                AutoSize = true,
                Padding = new Padding(0, 4, 0, 2)
            });
            deleteModeLayout.Controls.Add(_deleteFromListRadio);
            deleteModeLayout.Controls.Add(_deleteByPropertyRadio);
            deleteModeLayout.Controls.Add(_conditionsSummaryLabel);
            deleteModeLayout.Controls.Add(_conditionsCheckedListBox);
            deleteModeLayout.Controls.Add(conditionsButtonsPanel);
            _deleteModePanel.Controls.Add(deleteModeLayout);

            _sourceCategoryCombo = CreatePropertyCombo();
            _sourcePropertyCombo = CreatePropertyCombo();
            _sourceCategoryCombo.DropDown += (_, __) => OnCategoryComboDropDown(_sourceCategoryCombo, _sourcePropertyCombo);
            _sourcePropertyCombo.DropDown += (_, __) => OnPropertyComboDropDown(_sourceCategoryCombo, _sourcePropertyCombo);
            _sourceCategoryCombo.SelectedIndexChanged += (_, __) => PopulatePropertiesCombo(_sourceCategoryCombo, _sourcePropertyCombo);

            _targetCategoryCombo = CreatePropertyCombo();
            _targetPropertyCombo = CreatePropertyCombo();
            _targetCategoryCombo.DropDown += (_, __) => OnCategoryComboDropDown(_targetCategoryCombo, _targetPropertyCombo);
            _targetPropertyCombo.DropDown += (_, __) => OnPropertyComboDropDown(_targetCategoryCombo, _targetPropertyCombo);
            _targetCategoryCombo.SelectedIndexChanged += (_, __) => PopulatePropertiesCombo(_targetCategoryCombo, _targetPropertyCombo);

            _sourcePanel = BuildLabeledRowPanel("Исходное условие", _sourceCategoryCombo, _sourcePropertyCombo);

            _comparisonCombo = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Width = 160
            };
            _comparisonCombo.Items.AddRange(new object[]
            {
                new ComparisonOption(SearchConditionComparison.Equal, "Равно"),
                new ComparisonOption(SearchConditionComparison.NotEqual, "Не равно"),
                new ComparisonOption(SearchConditionComparison.DisplayStringContains, "Содержит"),
                new ComparisonOption(SearchConditionComparison.DisplayStringWildcard, "Маска"),
                new ComparisonOption(SearchConditionComparison.NumericLessThan, "Меньше"),
                new ComparisonOption(SearchConditionComparison.NumericLessThanOrEqual, "Меньше или равно"),
                new ComparisonOption(SearchConditionComparison.NumericGreaterThanOrEqual, "Больше или равно"),
                new ComparisonOption(SearchConditionComparison.NumericGreaterThan, "Больше")
            });
            _comparisonCombo.SelectedIndex = 0;

            _valueTextBox = new TextBox { Width = 180 };
            _ignoreCaseCheckBox = new CheckBox { Text = "Без учёта регистра", AutoSize = true };
            _ignoreAccentsCheckBox = new CheckBox { Text = "Без учёта акцентов", AutoSize = true };

            _targetPanel = BuildLabeledRowPanel("Новое условие", _targetCategoryCombo, _targetPropertyCombo);

            _valueSectionLabel = new Label
            {
                Text = "Значение нового условия:",
                AutoSize = true,
                Padding = new Padding(8, 6, 4, 0)
            };

            var valuePanel = new FlowLayoutPanel
            {
                AutoSize = true,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = true,
                Padding = new Padding(0, 0, 8, 0)
            };
            valuePanel.Controls.Add(new Label { Text = "Оператор:", AutoSize = true, Padding = new Padding(0, 6, 4, 0) });
            valuePanel.Controls.Add(_comparisonCombo);
            valuePanel.Controls.Add(new Label { Text = "Значение:", AutoSize = true, Padding = new Padding(8, 6, 4, 0) });
            valuePanel.Controls.Add(_valueTextBox);
            valuePanel.Controls.Add(_ignoreCaseCheckBox);
            valuePanel.Controls.Add(_ignoreAccentsCheckBox);

            var valueSectionPanel = new FlowLayoutPanel
            {
                AutoSize = true,
                FlowDirection = FlowDirection.TopDown,
                WrapContents = true,
                Padding = new Padding(8, 0, 8, 0)
            };
            _valueSectionPanel = valueSectionPanel;
            valueSectionPanel.Controls.Add(_valueSectionLabel);
            valueSectionPanel.Controls.Add(valuePanel);

            _updateExistingRadio = new RadioButton
            {
                Text = "Обновить существующие",
                AutoSize = true,
                Checked = true
            };
            _createCopiesRadio = new RadioButton
            {
                Text = "Создать копии",
                AutoSize = true
            };
            _copySuffixTextBox = new TextBox
            {
                Width = 120,
                Text = " (копия)"
            };
            _createCopiesRadio.CheckedChanged += (_, __) => _copySuffixTextBox.Enabled = _createCopiesRadio.Checked;
            _copySuffixTextBox.Enabled = false;

            var applyModePanel = new FlowLayoutPanel
            {
                AutoSize = true,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = true,
                Padding = new Padding(8, 4, 8, 0)
            };
            applyModePanel.Controls.Add(_updateExistingRadio);
            applyModePanel.Controls.Add(_createCopiesRadio);
            applyModePanel.Controls.Add(new Label { Text = "Суффикс:", AutoSize = true, Padding = new Padding(8, 6, 4, 0) });
            applyModePanel.Controls.Add(_copySuffixTextBox);

            _refreshCatalogButton = new Button
            {
                Text = "Обновить каталог из наборов",
                AutoSize = true,
                MinimumSize = new Size(190, 23)
            };
            _refreshCatalogButton.Click += (_, __) => RefreshCatalog();

            _applyButton = new Button
            {
                Text = "Создать",
                AutoSize = true,
                MinimumSize = new Size(120, 28)
            };
            _applyButton.Click += OnApplyClick;

            _limitationsLabel = new Label
            {
                Text = "Поддерживаются только поисковые наборы (с условиями фильтрации). "
                    + "Категории и свойства берутся из поисковых наборов документа; "
                    + "редкие значения можно ввести вручную.",
                Dock = DockStyle.Bottom,
                AutoSize = false,
                Height = 36,
                Padding = new Padding(8, 4, 8, 4),
                ForeColor = SystemColors.GrayText
            };

            _addTabPage = new TabPage("Добавить") { AutoScroll = true, Padding = new Padding(4) };
            _replaceTabPage = new TabPage("Заменить") { AutoScroll = true, Padding = new Padding(4) };
            _deleteTabPage = new TabPage("Удалить") { AutoScroll = true, Padding = new Padding(4) };

            _operationTabs = new TabControl
            {
                Dock = DockStyle.Fill,
                Padding = new Point(8, 4)
            };
            _operationTabs.TabPages.Add(_addTabPage);
            _operationTabs.TabPages.Add(_replaceTabPage);
            _operationTabs.TabPages.Add(_deleteTabPage);
            _operationTabs.SelectedIndexChanged += (_, __) =>
            {
                if (!IsHandleCreated)
                {
                    return;
                }

                BeginInvoke(new Action(OnOperationTabChanged));
            };

            var bottomButtons = new FlowLayoutPanel
            {
                AutoSize = true,
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = true,
                Padding = new Padding(8, 4, 8, 6)
            };
            bottomButtons.Controls.Add(_refreshCatalogButton);
            bottomButtons.Controls.Add(_applyButton);

            var operationFooter = new TableLayoutPanel
            {
                Dock = DockStyle.Bottom,
                AutoSize = true,
                ColumnCount = 1,
                Padding = new Padding(0)
            };
            operationFooter.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            operationFooter.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            operationFooter.Controls.Add(applyModePanel, 0, 0);
            operationFooter.Controls.Add(bottomButtons, 0, 1);

            var operationRoot = new Panel { Dock = DockStyle.Fill };
            operationRoot.Controls.Add(_operationTabs);
            operationRoot.Controls.Add(operationFooter);

            _mainSplitContainer = new SplitContainer
            {
                Dock = DockStyle.Fill,
                Orientation = Orientation.Horizontal,
                SplitterWidth = 6,
                FixedPanel = FixedPanel.None
            };
            _mainSplitContainer.Panel1.Controls.Add(treePanel);
            treePanel.Dock = DockStyle.Fill;
            _mainSplitContainer.Panel2.Controls.Add(operationRoot);

            Controls.Add(_mainSplitContainer);
            Controls.Add(_limitationsLabel);
            Controls.Add(filterPanel);
            Controls.Add(headerLabel);

            MinimumSize = new Size(520, 420);
            Load += OnControlLoad;
            LayoutOperationTabPages();
            UpdateDeleteTabPanels();
            UpdateApplyButtonText();
        }

        private void OnOperationTabChanged()
        {
            LayoutOperationTabPages();
            UpdateApplyButtonText();
            if (_operationTabs.SelectedIndex == 2)
            {
                RefreshDeleteConditionsList();
            }
        }

        private void UpdateApplyButtonText()
        {
            switch (_operationTabs.SelectedIndex)
            {
                case 1:
                    _applyButton.Text = "Заменить";
                    break;
                case 2:
                    _applyButton.Text = "Удалить";
                    break;
                default:
                    _applyButton.Text = "Создать";
                    break;
            }
        }

        private void OnDeleteModeRadioChanged()
        {
            UpdateDeleteTabPanels();
            if (_operationTabs.SelectedIndex != 2)
            {
                return;
            }

            if (_deleteFromListRadio.Checked)
            {
                BeginInvoke(new Action(RefreshDeleteConditionsList));
            }
            else
            {
                ClearDeleteConditionsList();
            }
        }

        private void ClearDeleteConditionsList()
        {
            if (_conditionsCheckedListBox == null || _conditionsCheckedListBox.IsDisposed)
            {
                return;
            }

            _conditionsCheckedListBox.BeginUpdate();
            try
            {
                _conditionsCheckedListBox.Items.Clear();
            }
            finally
            {
                _conditionsCheckedListBox.EndUpdate();
            }

            _conditionsSummaryLabel.Text = "Условия: используется удаление по категории и свойству.";
        }

        private void LayoutOperationTabPages()
        {
            DetachFromParent(_sourcePanel);
            DetachFromParent(_targetPanel);
            DetachFromParent(_valueSectionPanel);
            DetachFromParent(_deleteModePanel);

            switch (_operationTabs.SelectedIndex)
            {
                case 1:
                    AddToTab(_replaceTabPage, _sourcePanel, _targetPanel, _valueSectionPanel);
                    break;
                case 2:
                    AddToTab(_deleteTabPage, _deleteModePanel, _sourcePanel);
                    UpdateDeleteTabPanels();
                    break;
                default:
                    AddToTab(_addTabPage, _targetPanel, _valueSectionPanel);
                    break;
            }
        }

        private static void DetachFromParent(Control control)
        {
            if (control?.Parent != null)
            {
                control.Parent.Controls.Remove(control);
            }
        }

        private static void AddToTab(TabPage tab, params Control[] controls)
        {
            for (int index = controls.Length - 1; index >= 0; index--)
            {
                Control control = controls[index];
                if (control == null)
                {
                    continue;
                }

                control.Dock = DockStyle.Top;
                control.Visible = true;
                tab.Controls.Add(control);
            }
        }

        private void OnControlLoad(object sender, EventArgs e)
        {
            if (_mainSplitContainer.Height < 200)
            {
                return;
            }

            _mainSplitContainer.Panel1MinSize = 80;
            _mainSplitContainer.Panel2MinSize = 160;

            int maxDistance = _mainSplitContainer.Height - _mainSplitContainer.Panel2MinSize - _mainSplitContainer.SplitterWidth;
            int preferred = Math.Max(_mainSplitContainer.Panel1MinSize, (int)(_mainSplitContainer.Height * 0.38));
            _mainSplitContainer.SplitterDistance = Math.Min(preferred, maxDistance);
        }

        protected override void OnParentChanged(EventArgs e)
        {
            base.OnParentChanged(e);
            Dock = DockStyle.Fill;

            if (Parent != null)
            {
                SubscribeToDocumentChanges();
                _catalog.BindDocument(NavisworksApplication.MainDocument);
                BeginInvoke(new Action(() => RefreshTree()));
            }
            else
            {
                UnsubscribeFromDocumentChanges();
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                UnsubscribeFromDocumentChanges();
            }

            base.Dispose(disposing);
        }

        private static ComboBox CreatePropertyCombo()
        {
            return new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDown,
                Width = 180
            };
        }

        private static Panel BuildLabeledRowPanel(string title, ComboBox categoryCombo, ComboBox propertyCombo)
        {
            var panel = new FlowLayoutPanel
            {
                AutoSize = true,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = true,
                Padding = new Padding(8, 2, 8, 0)
            };
            panel.Controls.Add(new Label
            {
                Text = title + ":",
                AutoSize = true,
                Padding = new Padding(0, 6, 4, 0)
            });
            panel.Controls.Add(new Label { Text = "Категория", AutoSize = true, Padding = new Padding(0, 6, 4, 0) });
            panel.Controls.Add(categoryCombo);
            panel.Controls.Add(new Label { Text = "Свойство", AutoSize = true, Padding = new Padding(8, 6, 4, 0) });
            panel.Controls.Add(propertyCombo);
            return panel;
        }

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

        private void OnDocumentChanged(object sender, EventArgs e)
        {
            if (IsDisposed)
            {
                return;
            }

            if (InvokeRequired)
            {
                BeginInvoke(new Action(OnDocumentChangedOnUiThread));
                return;
            }

            OnDocumentChangedOnUiThread();
        }

        private void OnDocumentChangedOnUiThread()
        {
            _catalog.InvalidateCurrentDocument();
            _catalog.BindDocument(NavisworksApplication.MainDocument);
            RefreshTree();
        }

        private void OnCategoryComboDropDown(ComboBox categoryCombo, ComboBox propertyCombo)
        {
            Document document = NavisworksApplication.MainDocument;
            _catalog.EnsureLoaded(document);
            string currentCategory = categoryCombo.Text;
            FillCombo(categoryCombo, _catalog.Categories, currentCategory);
            PopulatePropertiesCombo(categoryCombo, propertyCombo);
        }

        private void OnPropertyComboDropDown(ComboBox categoryCombo, ComboBox propertyCombo)
        {
            Document document = NavisworksApplication.MainDocument;
            _catalog.EnsureLoaded(document);
            PopulatePropertiesCombo(categoryCombo, propertyCombo);
        }

        private void RefreshCatalog()
        {
            Document document = NavisworksApplication.MainDocument;
            if (document == null || document.IsClear)
            {
                return;
            }

            _catalog.Refresh(document);
            _catalog.EnsureLoaded(document);
            PopulateCategoryCombos();

            int categoryCount = _catalog.Categories.Count;
            string message = categoryCount > 0
                ? "Каталог обновлён из поисковых наборов документа. Категорий: " + categoryCount + "."
                : "В документе не найдено условий в поисковых наборах. Введите категорию и свойство вручную.";

            MessageBox.Show(
                message,
                "Каталог свойств",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }

        private void PopulateCategoryCombos()
        {
            string sourceCategory = _sourceCategoryCombo.Text;
            string targetCategory = _targetCategoryCombo.Text;

            FillCombo(_sourceCategoryCombo, _catalog.Categories, sourceCategory);
            FillCombo(_targetCategoryCombo, _catalog.Categories, targetCategory);

            PopulatePropertiesCombo(_sourceCategoryCombo, _sourcePropertyCombo);
            PopulatePropertiesCombo(_targetCategoryCombo, _targetPropertyCombo);
        }

        private static void FillCombo(ComboBox combo, IEnumerable<string> items, string selectedText)
        {
            string current = selectedText ?? combo.Text;
            combo.BeginUpdate();
            try
            {
                combo.Items.Clear();
                foreach (string item in items)
                {
                    combo.Items.Add(item);
                }

                if (!string.IsNullOrWhiteSpace(current))
                {
                    int index = combo.FindStringExact(current);
                    if (index >= 0)
                    {
                        combo.SelectedIndex = index;
                    }
                    else
                    {
                        combo.Text = current;
                    }
                }
            }
            finally
            {
                combo.EndUpdate();
            }
        }

        private void PopulatePropertiesCombo(ComboBox categoryCombo, ComboBox propertyCombo)
        {
            string current = propertyCombo.Text;
            IReadOnlyCollection<string> properties = _catalog.GetProperties(categoryCombo.Text);
            FillCombo(propertyCombo, properties, current);
        }

        private void RefreshSearchSetReferencesInTree()
        {
            _isUpdatingTree = true;
            try
            {
                foreach (TreeNode node in EnumerateNodes(_treeView.Nodes))
                {
                    if (node.Tag is SearchSetsProLogic.SearchSetReference reference)
                    {
                        node.Tag = SearchSetsProLogic.RefreshReference(reference);
                    }
                }
            }
            finally
            {
                _isUpdatingTree = false;
            }
        }

        private void RefreshTree(TreeViewState preservedState = null)
        {
            if (_isUpdatingTree)
            {
                return;
            }

            TreeViewState state = preservedState ?? CaptureTreeState();
            string filter = _filterTextBox.Text?.Trim() ?? string.Empty;

            _isUpdatingTree = true;
            _treeView.BeginUpdate();
            try
            {
                _treeView.Nodes.Clear();
                SearchSetsProLogic.FolderNode root = SearchSetsProLogic.GetSearchSetTree();
                foreach (SearchSetsProLogic.FolderNode folder in root.Folders)
                {
                    TreeNode folderNode = BuildFolderNode(folder, filter, state);
                    if (folderNode != null)
                    {
                        _treeView.Nodes.Add(folderNode);
                    }
                }

                foreach (SearchSetsProLogic.SearchSetReference reference in root.SearchSets)
                {
                    if (!MatchesFilter(reference.DisplayName, filter))
                    {
                        continue;
                    }

                    _treeView.Nodes.Add(CreateSearchSetNode(reference, state));
                }

                SyncFolderCheckStates(_treeView.Nodes);
                RestoreTreeExpansion(state);
            }
            finally
            {
                _treeView.EndUpdate();
                _isUpdatingTree = false;
            }

            UpdateSelectionSummary();
        }

        private TreeViewState CaptureTreeState()
        {
            var state = new TreeViewState();
            state.TopNodeKey = GetTreeNodeKey(_treeView.TopNode);

            foreach (TreeNode node in EnumerateNodes(_treeView.Nodes))
            {
                if (node.Tag is SearchSetsProLogic.FolderNode folder)
                {
                    if (node.IsExpanded)
                    {
                        state.ExpandedFolderPaths.Add(folder.Path ?? string.Empty);
                    }
                }
                else if (node.Tag is SearchSetsProLogic.SearchSetReference reference
                    && reference.HasSearch
                    && node.Checked)
                {
                    state.CheckedGuids.Add(reference.Guid);
                    state.CheckedSetKeys.Add(GetSearchSetKey(reference));
                }
            }

            return state;
        }

        private static string GetSearchSetKey(SearchSetsProLogic.SearchSetReference reference)
        {
            return (reference.Path ?? string.Empty) + "\x1e" + (reference.DisplayName ?? string.Empty);
        }

        private static string GetTreeNodeKey(TreeNode node)
        {
            if (node?.Tag is SearchSetsProLogic.FolderNode folder)
            {
                return "F:" + (folder.Path ?? string.Empty);
            }

            if (node?.Tag is SearchSetsProLogic.SearchSetReference reference)
            {
                return "S:" + GetSearchSetKey(reference);
            }

            return null;
        }

        private static TreeNode FindNodeByKey(TreeNodeCollection nodes, string key)
        {
            if (string.IsNullOrEmpty(key))
            {
                return null;
            }

            foreach (TreeNode node in EnumerateNodesStatic(nodes))
            {
                if (GetTreeNodeKey(node) == key)
                {
                    return node;
                }
            }

            return null;
        }

        private static bool IsSearchSetChecked(
            SearchSetsProLogic.SearchSetReference reference,
            TreeViewState state)
        {
            if (state == null)
            {
                return false;
            }

            return state.CheckedGuids.Contains(reference.Guid)
                || state.CheckedSetKeys.Contains(GetSearchSetKey(reference));
        }

        private static void RestoreExpandedFolders(TreeNodeCollection nodes, HashSet<string> expandedFolderPaths)
        {
            foreach (TreeNode node in nodes)
            {
                if (node.Tag is SearchSetsProLogic.FolderNode folder)
                {
                    if (expandedFolderPaths.Contains(folder.Path ?? string.Empty))
                    {
                        node.Expand();
                    }

                    RestoreExpandedFolders(node.Nodes, expandedFolderPaths);
                }
            }
        }

        private void RestoreTreeExpansion(TreeViewState state)
        {
            if (state.ExpandedFolderPaths.Count > 0)
            {
                RestoreExpandedFolders(_treeView.Nodes, state.ExpandedFolderPaths);
            }
            else if (state.CheckedGuids.Count == 0 && state.CheckedSetKeys.Count == 0)
            {
                foreach (TreeNode node in _treeView.Nodes)
                {
                    node.Expand();
                }
            }

            RestoreTopNode(state);
        }

        private void RestoreTopNode(TreeViewState state)
        {
            TreeNode topNode = FindNodeByKey(_treeView.Nodes, state.TopNodeKey);
            if (topNode != null)
            {
                _treeView.TopNode = topNode;
            }
        }

        private static IEnumerable<TreeNode> EnumerateNodesStatic(TreeNodeCollection nodes)
        {
            foreach (TreeNode node in nodes)
            {
                yield return node;
                foreach (TreeNode child in EnumerateNodesStatic(node.Nodes))
                {
                    yield return child;
                }
            }
        }

        private void RefreshDeleteConditionsList()
        {
            if (_operationTabs.SelectedIndex != 2
                || _conditionsCheckedListBox == null
                || _conditionsCheckedListBox.IsDisposed)
            {
                return;
            }

            if (!_deleteFromListRadio.Checked)
            {
                return;
            }

            IReadOnlyList<SearchSetsProLogic.CollectedConditionInfo> conditions;
            SearchSetsProLogic.SearchSetReference[] references;
            try
            {
                references = GetCheckedReferences().ToArray();
                conditions = SearchSetsProLogic.CollectConditionsFromReferences(references);
            }
            catch (Exception ex)
            {
                _conditionsSummaryLabel.Text = "Не удалось загрузить условия: " + ex.Message;
                return;
            }

            _conditionsCheckedListBox.BeginUpdate();
            try
            {
                _conditionsCheckedListBox.Items.Clear();
                foreach (SearchSetsProLogic.CollectedConditionInfo condition in conditions)
                {
                    _conditionsCheckedListBox.Items.Add(condition, false);
                }

                _conditionsCheckedListBox.SelectedIndex = -1;
            }
            finally
            {
                _conditionsCheckedListBox.EndUpdate();
            }

            if (references.Length == 0)
            {
                _conditionsSummaryLabel.Text = "Условия: сначала выберите поисковые наборы в дереве.";
            }
            else if (conditions.Count == 0)
            {
                _conditionsSummaryLabel.Text =
                    "Условия: в выбранных наборах (" + references.Length + ") не найдено условий поиска.";
            }
            else
            {
                _conditionsSummaryLabel.Text =
                    "Уникальных условий в " + references.Length + " наборах: " + conditions.Count + ".";
            }
        }

        private void SetAllConditionChecks(bool isChecked)
        {
            for (int index = 0; index < _conditionsCheckedListBox.Items.Count; index++)
            {
                _conditionsCheckedListBox.SetItemChecked(index, isChecked);
            }
        }

        private TreeNode BuildFolderNode(
            SearchSetsProLogic.FolderNode folder,
            string filter,
            TreeViewState state)
        {
            var node = new TreeNode(folder.DisplayName)
            {
                Tag = folder,
                ToolTipText = folder.Path
            };

            bool hasVisibleChildren = false;
            foreach (SearchSetsProLogic.FolderNode childFolder in folder.Folders)
            {
                TreeNode childNode = BuildFolderNode(childFolder, filter, state);
                if (childNode != null)
                {
                    node.Nodes.Add(childNode);
                    hasVisibleChildren = true;
                }
            }

            foreach (SearchSetsProLogic.SearchSetReference reference in folder.SearchSets)
            {
                if (!MatchesFilter(reference.DisplayName, filter))
                {
                    continue;
                }

                node.Nodes.Add(CreateSearchSetNode(reference, state));
                hasVisibleChildren = true;
            }

            return hasVisibleChildren ? node : null;
        }

        private TreeNode CreateSearchSetNode(
            SearchSetsProLogic.SearchSetReference reference,
            TreeViewState state)
        {
            string text = reference.DisplayName;
            if (!reference.HasSearch)
            {
                text += " (без поиска)";
            }

            var node = new TreeNode(text)
            {
                Tag = reference,
                ToolTipText = reference.Path,
                ForeColor = reference.HasSearch ? SystemColors.ControlText : SystemColors.GrayText,
                Checked = IsSearchSetChecked(reference, state)
            };
            return node;
        }

        private static bool MatchesFilter(string displayName, string filter)
        {
            return string.IsNullOrWhiteSpace(filter)
                || displayName.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private void OnTreeBeforeCheck(object sender, TreeViewCancelEventArgs e)
        {
            if (_isUpdatingTree)
            {
                return;
            }

            if (e.Node?.Tag is SearchSetsProLogic.FolderNode)
            {
                return;
            }

            if (e.Node?.Tag is SearchSetsProLogic.SearchSetReference reference)
            {
                if (!reference.HasSearch)
                {
                    e.Cancel = true;
                }

                return;
            }

            e.Cancel = true;
        }

        private void OnTreeAfterCheck(object sender, TreeViewEventArgs e)
        {
            if (_isUpdatingTree || e.Node == null)
            {
                return;
            }

            if (e.Node.Tag is SearchSetsProLogic.FolderNode)
            {
                _isUpdatingTree = true;
                try
                {
                    SetDescendantSearchSetChecks(e.Node, e.Node.Checked);
                }
                finally
                {
                    _isUpdatingTree = false;
                }
            }

            UpdateSelectionSummary();
        }

        private static void SetDescendantSearchSetChecks(TreeNode node, bool isChecked)
        {
            foreach (TreeNode child in node.Nodes)
            {
                if (child.Tag is SearchSetsProLogic.SearchSetReference reference)
                {
                    if (reference.HasSearch)
                    {
                        child.Checked = isChecked;
                    }
                }
                else if (child.Tag is SearchSetsProLogic.FolderNode)
                {
                    child.Checked = isChecked;
                    SetDescendantSearchSetChecks(child, isChecked);
                }
            }
        }

        private static void SyncFolderCheckStates(TreeNodeCollection nodes)
        {
            foreach (TreeNode node in nodes)
            {
                if (node.Tag is SearchSetsProLogic.FolderNode)
                {
                    SyncFolderCheckStates(node.Nodes);
                    node.Checked = AreAllSelectableDescendantsChecked(node);
                }
            }
        }

        private static bool AreAllSelectableDescendantsChecked(TreeNode folderNode)
        {
            bool hasSelectable = false;
            foreach (TreeNode node in EnumerateNodes(folderNode.Nodes))
            {
                if (node.Tag is SearchSetsProLogic.SearchSetReference reference && reference.HasSearch)
                {
                    hasSelectable = true;
                    if (!node.Checked)
                    {
                        return false;
                    }
                }
            }

            return hasSelectable;
        }

        private IEnumerable<SearchSetsProLogic.SearchSetReference> GetCheckedReferences()
        {
            return EnumerateNodes(_treeView.Nodes)
                .Where(node => node.Checked && node.Tag is SearchSetsProLogic.SearchSetReference)
                .Select(node => (SearchSetsProLogic.SearchSetReference)node.Tag);
        }

        private static IEnumerable<TreeNode> EnumerateNodes(TreeNodeCollection nodes)
        {
            foreach (TreeNode node in nodes)
            {
                yield return node;
                foreach (TreeNode child in EnumerateNodes(node.Nodes))
                {
                    yield return child;
                }
            }
        }

        private void SetAllChecks(bool isChecked)
        {
            _isUpdatingTree = true;
            try
            {
                foreach (TreeNode node in EnumerateNodes(_treeView.Nodes))
                {
                    if (node.Tag is SearchSetsProLogic.FolderNode)
                    {
                        node.Checked = isChecked;
                    }

                    if (node.Tag is SearchSetsProLogic.SearchSetReference reference && reference.HasSearch)
                    {
                        node.Checked = isChecked;
                    }
                }
            }
            finally
            {
                _isUpdatingTree = false;
            }

            UpdateSelectionSummary();
        }

        private void UpdateSelectionSummary()
        {
            int count = GetCheckedReferences().Count();
            _selectionLabel.Text = "Выбрано наборов: " + count;
            _applyButton.Enabled = count > 0;
            RefreshDeleteConditionsList();
        }

        private void UpdateDeleteTabPanels()
        {
            bool deleteFromList = _deleteFromListRadio.Checked;
            _conditionsCheckedListBox.Visible = deleteFromList;
            _conditionsSummaryLabel.Visible = deleteFromList;
            _conditionsButtonsPanel.Visible = deleteFromList;
            _sourcePanel.Visible = !deleteFromList;
        }

        private SearchSetsProOperation GetSelectedOperation()
        {
            switch (_operationTabs.SelectedIndex)
            {
                case 1:
                    return SearchSetsProOperation.Replace;
                case 2:
                    return SearchSetsProOperation.Delete;
                default:
                    return SearchSetsProOperation.Add;
            }
        }

        private void OnApplyClick(object sender, EventArgs e)
        {
            SearchSetsProOperation operation = GetSelectedOperation();

            var request = new SearchSetsProLogic.ApplyRequest
            {
                Operation = operation,
                ApplyMode = _createCopiesRadio.Checked
                    ? SearchSetsProApplyMode.CreateCopies
                    : SearchSetsProApplyMode.UpdateExisting,
                DeleteMode = _deleteByPropertyRadio.Checked
                    ? SearchSetsProDeleteMode.ByProperty
                    : SearchSetsProDeleteMode.FromSelectedSets,
                CopySuffix = _copySuffixTextBox.Text,
                SourceCategory = _sourceCategoryCombo.Text.Trim(),
                SourceProperty = _sourcePropertyCombo.Text.Trim(),
                TargetCategory = _targetCategoryCombo.Text.Trim(),
                TargetProperty = _targetPropertyCombo.Text.Trim(),
                ValueText = _valueTextBox.Text,
                IgnoreCase = _ignoreCaseCheckBox.Checked,
                IgnoreAccents = _ignoreAccentsCheckBox.Checked,
                ConditionsToDelete = GetSelectedConditionsToDelete()
            };

            if (_comparisonCombo.SelectedItem is ComparisonOption comparisonOption)
            {
                request.Comparison = comparisonOption.Comparison;
            }

            _catalog.EnsureLoaded(NavisworksApplication.MainDocument);
            _catalog.EnsureProperty(request.TargetCategory, request.TargetProperty);
            if (request.Operation == SearchSetsProOperation.Replace
                || (request.Operation == SearchSetsProOperation.Delete
                    && request.DeleteMode == SearchSetsProDeleteMode.ByProperty))
            {
                _catalog.EnsureProperty(request.SourceCategory, request.SourceProperty);
            }

            TreeViewState preservedTreeState = request.ApplyMode == SearchSetsProApplyMode.CreateCopies
                ? CaptureTreeState()
                : null;

            SearchSetsProLogic.ApplyResult result =
                SearchSetsProLogic.Apply(GetCheckedReferences(), request);

            MessageBox.Show(
                result.Success
                    ? SearchSetsProLogic.FormatResultMessage(result)
                    : result.ErrorMessage ?? "Не удалось применить изменения.",
                result.Success ? "Результат" : "Ошибка",
                MessageBoxButtons.OK,
                result.Success ? MessageBoxIcon.Information : MessageBoxIcon.Warning);

            if (result.ProcessedCount > 0)
            {
                if (request.ApplyMode == SearchSetsProApplyMode.CreateCopies)
                {
                    RefreshTree(preservedTreeState);
                }
                else
                {
                    RefreshSearchSetReferencesInTree();
                    if (_operationTabs.SelectedIndex == 2)
                    {
                        RefreshDeleteConditionsList();
                    }
                }
            }
        }

        private IReadOnlyList<SearchConditionDescriptor> GetSelectedConditionsToDelete()
        {
            var selected = new List<SearchConditionDescriptor>();
            foreach (object item in _conditionsCheckedListBox.CheckedItems)
            {
                if (item is SearchSetsProLogic.CollectedConditionInfo info)
                {
                    selected.Add(info.Descriptor);
                }
            }

            return selected;
        }

        private sealed class ComparisonOption
        {
            public ComparisonOption(SearchConditionComparison comparison, string displayName)
            {
                Comparison = comparison;
                DisplayName = displayName;
            }

            public SearchConditionComparison Comparison { get; }

            public string DisplayName { get; }

            public override string ToString()
            {
                return DisplayName;
            }
        }
    }
}
