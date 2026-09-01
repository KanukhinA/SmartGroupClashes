using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using System.Web.Script.Serialization;
using Autodesk.Navisworks.Api;
using Autodesk.Navisworks.Api.Clash;
using NavisworksApplication = Autodesk.Navisworks.Api.Application;

namespace SmartNavisTools
{
    /// <summary>
    /// UI-панель настройки и запуска формирования HTML-дашборда.
    /// </summary>
    internal sealed class ClashDashboardControl : UserControl
    {
        private readonly CheckedListBox _testsCheckedList;
        private readonly TextBox _testsFilterTextBox;
        private readonly CheckedListBox _statusCheckedList;
        private readonly RadioButton _floorGridRadio;
        private readonly RadioButton _floorPropertyRadio;
        private readonly ComboBox _floorPropertyCombo;
        private readonly RadioButton _disciplineModelRadio;
        private readonly RadioButton _disciplinePropertyRadio;
        private readonly ComboBox _disciplinePropertyCombo;
        private readonly DataGridView _modelFileDisciplineMappingsGrid;
        private readonly Button _addMappingButton;
        private readonly Button _removeMappingButton;
        private readonly Button _refreshButton;
        private readonly Button _saveSettingsButton;
        private readonly Button _buildButton;
        private readonly Label _summaryLabel;
        private readonly Timer _autosaveTimer;
        private readonly PropertyCatalog _propertyCatalog;
        private bool _isSubscribed;
        private bool _isApplyingSettings;
        private string _lastDocumentPath;

        /// <summary>
        /// Инициализирует элементы управления панели и обработчики событий.
        /// </summary>
        public ClashDashboardControl()
        {
            _propertyCatalog = new PropertyCatalog();
            _autosaveTimer = new Timer { Interval = 300 };
            _autosaveTimer.Tick += AutosaveTimer_Tick;

            var titleLabel = new Label
            {
                Text = "Статистика пересечений",
                Dock = DockStyle.Top,
                Height = 24,
                Padding = new Padding(8, 6, 8, 0),
                AutoSize = false
            };

            _testsFilterTextBox = new TextBox { Dock = DockStyle.Top, Margin = new Padding(8) };
            _testsFilterTextBox.TextChanged += TestsFilterTextBox_TextChanged;

            _testsCheckedList = new CheckedListBox
            {
                Dock = DockStyle.Top,
                Height = 140,
                CheckOnClick = true
            };
            _testsCheckedList.ItemCheck += SettingsControl_ChangedDelayed;

            _statusCheckedList = new CheckedListBox
            {
                Dock = DockStyle.Top,
                Height = 95,
                CheckOnClick = true
            };
            _statusCheckedList.Items.Add("New", true);
            _statusCheckedList.Items.Add("Active", true);
            _statusCheckedList.Items.Add("Reviewed", true);
            _statusCheckedList.Items.Add("Approved", true);
            _statusCheckedList.Items.Add("Resolved", false);
            _statusCheckedList.ItemCheck += SettingsControl_ChangedDelayed;

            _floorGridRadio = new RadioButton { Text = "Этаж по активной сетке", Checked = true, AutoSize = true };
            _floorPropertyRadio = new RadioButton { Text = "Этаж по свойству", AutoSize = true };
            _floorGridRadio.CheckedChanged += SettingsControl_Changed;
            _floorPropertyRadio.CheckedChanged += SettingsControl_Changed;

            _floorPropertyCombo = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDown,
                Width = 240,
                Enabled = false
            };
            _floorPropertyCombo.TextChanged += SettingsControl_Changed;
            _floorPropertyCombo.Leave += SettingsControl_Changed;

            _disciplineModelRadio = new RadioButton { Text = "Раздел по файлу модели", Checked = true, AutoSize = true };
            _disciplinePropertyRadio = new RadioButton { Text = "Раздел по свойству", AutoSize = true };
            _disciplineModelRadio.CheckedChanged += SettingsControl_Changed;
            _disciplinePropertyRadio.CheckedChanged += SettingsControl_Changed;

            _disciplinePropertyCombo = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDown,
                Width = 240,
                Enabled = false
            };
            _disciplinePropertyCombo.TextChanged += SettingsControl_Changed;
            _disciplinePropertyCombo.Leave += SettingsControl_Changed;

            _modelFileDisciplineMappingsGrid = new DataGridView
            {
                Dock = DockStyle.Top,
                Height = 130,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                MultiSelect = true,
                RowHeadersVisible = false,
                Enabled = true,
                ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize
            };
            _modelFileDisciplineMappingsGrid.Columns.Add("Key", "Key");
            _modelFileDisciplineMappingsGrid.Columns.Add("Values", "Value");
            _modelFileDisciplineMappingsGrid.CellValueChanged += ModelMappingsGrid_Changed;

            _addMappingButton = new Button { Text = "Добавить", AutoSize = true };
            _addMappingButton.Click += AddMappingButton_Click;

            _removeMappingButton = new Button { Text = "Удалить выбранные", AutoSize = true };
            _removeMappingButton.Click += RemoveMappingButton_Click;

            _refreshButton = new Button { Text = "Обновить данные", AutoSize = true };
            _refreshButton.Click += RefreshButton_Click;

            _saveSettingsButton = new Button { Text = "Сохранить настройки", AutoSize = true };
            _saveSettingsButton.Click += SaveSettingsButton_Click;

            _buildButton = new Button { Text = "Сформировать отчёт", AutoSize = true };
            _buildButton.Click += BuildButton_Click;

            _summaryLabel = new Label
            {
                Text = "Готово.",
                AutoSize = true,
                ForeColor = SystemColors.GrayText
            };

            var layout = BuildLayout();
            Controls.Add(layout);
            Controls.Add(titleLabel);
            MinimumSize = new Size(320, 360);
        }

        /// <summary>
        /// Подключает подписки и загружает состояние панели после присоединения к родителю.
        /// </summary>
        protected override void OnParentChanged(EventArgs e)
        {
            base.OnParentChanged(e);
            Dock = DockStyle.Fill;
            if (Parent != null)
            {
                SubscribeToDocumentChanges();
                RefreshDataAndSettings();
            }
            else
            {
                UnsubscribeFromDocumentChanges();
            }
        }

        /// <summary>
        /// Освобождает подписки и таймер при удалении контрола.
        /// </summary>
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                UnsubscribeFromDocumentChanges();
                _autosaveTimer.Dispose();
            }

            base.Dispose(disposing);
        }

        /// <summary>
        /// Строит древовидный layout панели из WinForms-контейнеров.
        /// </summary>
        private Control BuildLayout()
        {
            var root = new Panel
            {
                Dock = DockStyle.Fill,
                AutoScroll = true,
                Padding = new Padding(8)
            };

            var stack = new FlowLayoutPanel
            {
                Dock = DockStyle.Top,
                FlowDirection = FlowDirection.TopDown,
                AutoSize = true,
                WrapContents = false
            };

            stack.Controls.Add(new Label { Text = "Фильтр проверок:", AutoSize = true });
            stack.Controls.Add(_testsFilterTextBox);
            stack.Controls.Add(new Label { Text = "Проверки Clash Detective:", AutoSize = true });
            stack.Controls.Add(_testsCheckedList);
            stack.Controls.Add(new Label { Text = "Статусы:", AutoSize = true });
            stack.Controls.Add(_statusCheckedList);
            stack.Controls.Add(new Label { Text = "Этаж:", AutoSize = true });
            stack.Controls.Add(_floorGridRadio);
            stack.Controls.Add(_floorPropertyRadio);
            stack.Controls.Add(_floorPropertyCombo);
            stack.Controls.Add(new Label { Text = "Сегменты (разделы):", AutoSize = true });
            stack.Controls.Add(_disciplineModelRadio);
            stack.Controls.Add(_disciplinePropertyRadio);
            stack.Controls.Add(_disciplinePropertyCombo);
            stack.Controls.Add(new Label
            {
                Text = "Маппинг (Key -> одна категория) для «Раздел по файлу модели». Value: подстроки через ';'",
                AutoSize = true
            });
            stack.Controls.Add(_modelFileDisciplineMappingsGrid);
            var mappingButtons = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.LeftToRight,
                AutoSize = true,
                WrapContents = true
            };
            mappingButtons.Controls.Add(_addMappingButton);
            mappingButtons.Controls.Add(_removeMappingButton);
            stack.Controls.Add(mappingButtons);

            var buttons = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.LeftToRight,
                AutoSize = true,
                WrapContents = true
            };
            buttons.Controls.Add(_refreshButton);
            buttons.Controls.Add(_saveSettingsButton);
            buttons.Controls.Add(_buildButton);

            stack.Controls.Add(buttons);
            stack.Controls.Add(_summaryLabel);

            root.Controls.Add(stack);
            return root;
        }

        /// <summary>
        /// Подписывает панель на изменения документа и раздела Clash Detective.
        /// </summary>
        private void SubscribeToDocumentChanges()
        {
            if (_isSubscribed || NavisworksApplication.MainDocument == null)
            {
                return;
            }

            Document document = NavisworksApplication.MainDocument;
            document.Database.Changed += OnDocumentChanged;
            if (document.GetClash()?.TestsData != null)
            {
                document.GetClash().TestsData.Changed += OnDocumentChanged;
            }

            _isSubscribed = true;
        }

        /// <summary>
        /// Отписывает панель от событий документа.
        /// </summary>
        private void UnsubscribeFromDocumentChanges()
        {
            if (!_isSubscribed || NavisworksApplication.MainDocument == null)
            {
                return;
            }

            Document document = NavisworksApplication.MainDocument;
            document.Database.Changed -= OnDocumentChanged;
            if (document.GetClash()?.TestsData != null)
            {
                document.GetClash().TestsData.Changed -= OnDocumentChanged;
            }

            _isSubscribed = false;
        }

        /// <summary>
        /// Обрабатывает изменение документа и обновляет UI через поток формы.
        /// </summary>
        private void OnDocumentChanged(object sender, EventArgs e)
        {
            if (IsDisposed || !IsHandleCreated)
            {
                return;
            }

            BeginInvoke(new Action(RefreshDataAndSettings));
        }

        /// <summary>
        /// Обновляет списки тестов/свойств и применяет сохраненные настройки.
        /// </summary>
        private void RefreshDataAndSettings()
        {
            try
            {
                RefreshTestsList();
                RefreshPropertyCatalog();
                EnsureSettingsLoadedForDocument();
            }
            catch (Exception exception)
            {
                _summaryLabel.Text = "Ошибка обновления: " + exception.Message;
            }
        }

        /// <summary>
        /// Загружает список тестов Clash Detective в список с чекбоксами.
        /// </summary>
        private void RefreshTestsList()
        {
            var tests = ClashDashboardLogic.GetTests();
            var selectedBefore = new HashSet<string>(GetSelectedTestNames(), StringComparer.OrdinalIgnoreCase);
            _testsCheckedList.Items.Clear();

            foreach (ClashDetectiveTestsLogic.ClashTestInfo test in tests)
            {
                int index = _testsCheckedList.Items.Add(test.DisplayName ?? string.Empty);
                if (selectedBefore.Contains(test.DisplayName))
                {
                    _testsCheckedList.SetItemChecked(index, true);
                }
            }

            ApplyTestsFilter();
        }

        /// <summary>
        /// Обновляет каталог свойств и заполняет выпадающие списки для выбора измерений.
        /// </summary>
        private void RefreshPropertyCatalog()
        {
            Document document = NavisworksApplication.MainDocument;
            _propertyCatalog.Refresh(document);

            string floorCurrent = _floorPropertyCombo.Text;
            string disciplineCurrent = _disciplinePropertyCombo.Text;
            List<string> props = _propertyCatalog.Categories
                .SelectMany(category => _propertyCatalog.GetProperties(category))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
                .ToList();

            _floorPropertyCombo.Items.Clear();
            _disciplinePropertyCombo.Items.Clear();
            foreach (string propertyName in props)
            {
                _floorPropertyCombo.Items.Add(propertyName);
                _disciplinePropertyCombo.Items.Add(propertyName);
            }

            _floorPropertyCombo.Text = floorCurrent;
            _disciplinePropertyCombo.Text = disciplineCurrent;
        }

        /// <summary>
        /// Фильтрует отображаемые тесты по введенной строке поиска.
        /// </summary>
        private void ApplyTestsFilter()
        {
            string filter = (_testsFilterTextBox.Text ?? string.Empty).Trim();
            for (int i = 0; i < _testsCheckedList.Items.Count; i++)
            {
                string testName = _testsCheckedList.Items[i]?.ToString() ?? string.Empty;
                bool visible = string.IsNullOrWhiteSpace(filter)
                    || testName.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0;
                _testsCheckedList.SetItemCheckState(i, _testsCheckedList.GetItemChecked(i)
                    ? CheckState.Checked
                    : CheckState.Unchecked);
                _testsCheckedList.SetSelected(i, false);
                if (!visible && _testsCheckedList.GetItemChecked(i))
                {
                    // Оставляем выбор, но показываем количество только по фильтру.
                }
            }
        }

        /// <summary>
        /// Загружает сохраненные настройки для текущего документа один раз на смену файла.
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
            UpdatePropertyControlsState();
        }

        /// <summary>
        /// Устанавливает базовые значения контролов, если сохраненных настроек нет.
        /// </summary>
        private void ApplyDefaults()
        {
            for (int i = 0; i < _statusCheckedList.Items.Count; i++)
            {
                string status = _statusCheckedList.Items[i].ToString();
                _statusCheckedList.SetItemChecked(i, !string.Equals(status, "Resolved", StringComparison.OrdinalIgnoreCase));
            }

            _floorGridRadio.Checked = true;
            _disciplineModelRadio.Checked = true;
            _modelFileDisciplineMappingsGrid.Rows.Clear();
        }

        /// <summary>
        /// Применяет словарь key=value к контролам панели.
        /// </summary>
        private void ApplyMap(IReadOnlyDictionary<string, string> map)
        {
            SetCheckedTests(SplitCsv(GetValue(map, "SelectedTests")));
            SetCheckedStatuses(SplitCsv(GetValue(map, "Statuses")));
            _floorGridRadio.Checked = string.Equals(GetValue(map, "FloorSource"), "Grid", StringComparison.OrdinalIgnoreCase);
            _floorPropertyRadio.Checked = !_floorGridRadio.Checked;
            _floorPropertyCombo.Text = GetValue(map, "FloorProperty");
            _disciplineModelRadio.Checked = string.Equals(GetValue(map, "DisciplineSource"), "ModelFile", StringComparison.OrdinalIgnoreCase);
            _disciplinePropertyRadio.Checked = !_disciplineModelRadio.Checked;
            _disciplinePropertyCombo.Text = GetValue(map, "DisciplineProperty");
            SetModelFileDisciplineMappingsFromMap(map);
        }

        /// <summary>
        /// Сохраняет настройки панели сразу по нажатию кнопки.
        /// </summary>
        private void SaveSettingsButton_Click(object sender, EventArgs e)
        {
            SaveCurrentSettings();
            _summaryLabel.Text = "Настройки сохранены.";
        }

        /// <summary>
        /// Реагирует на кнопку обновления и перечитывает тесты/свойства из Navisworks.
        /// </summary>
        private void RefreshButton_Click(object sender, EventArgs e)
        {
            RefreshDataAndSettings();
            _summaryLabel.Text = "Данные обновлены.";
        }

        /// <summary>
        /// Формирует HTML-отчёт по текущим настройкам и открывает его в браузере.
        /// </summary>
        private void BuildButton_Click(object sender, EventArgs e)
        {
            var dialog = new SaveFileDialog
            {
                Filter = "HTML Files (*.html)|*.html",
                FileName = "ClashDashboard_" + DateTime.Now.ToString("yyyyMMdd_HHmm") + ".html",
                OverwritePrompt = true
            };
            if (dialog.ShowDialog(this) != DialogResult.OK)
            {
                return;
            }

            ClashDashboardLogic.BuildResult result = ClashDashboardLogic.BuildReport(dialog.FileName, BuildOptionsFromUi());
            if (!result.Success)
            {
                MessageBox.Show(this, result.ErrorMessage, "Статистика пересечений", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            _summaryLabel.Text = "Готово. Записей: " + result.RecordsCount;
            SaveCurrentSettings();

            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = dialog.FileName,
                    UseShellExecute = true
                });
            }
            catch (Exception exception)
            {
                MessageBox.Show(this, "Отчет создан, но не удалось открыть браузер: " + exception.Message, "Статистика пересечений");
            }
        }

        /// <summary>
        /// Возвращает набор опций построения отчёта на основе текущих контролов.
        /// </summary>
        private ClashDashboardLogic.BuildOptions BuildOptionsFromUi()
        {
            return new ClashDashboardLogic.BuildOptions
            {
                SelectedTests = GetSelectedTestNames(),
                EnabledStatuses = GetSelectedStatuses(),
                FloorSource = _floorGridRadio.Checked ? ClashDimensionResolver.FloorSource.Grid : ClashDimensionResolver.FloorSource.Property,
                FloorPropertyName = _floorPropertyCombo.Text?.Trim() ?? string.Empty,
                DisciplineSource = _disciplineModelRadio.Checked ? ClashDimensionResolver.DisciplineSource.ModelFile : ClashDimensionResolver.DisciplineSource.Property,
                DisciplinePropertyName = _disciplinePropertyCombo.Text?.Trim() ?? string.Empty,
                ModelFileDisciplineMappings = GetModelFileDisciplineMappingsFromUi()
            };
        }

        /// <summary>
        /// Запускает отложенное автосохранение при изменении контролов.
        /// </summary>
        private void SettingsControl_Changed(object sender, EventArgs e)
        {
            if (_isApplyingSettings)
            {
                return;
            }

            UpdatePropertyControlsState();
            _autosaveTimer.Stop();
            _autosaveTimer.Start();
        }

        /// <summary>
        /// Запускает отложенное автосохранение для CheckedListBox c учетом ItemCheck.
        /// </summary>
        private void SettingsControl_ChangedDelayed(object sender, ItemCheckEventArgs e)
        {
            if (_isApplyingSettings)
            {
                return;
            }

            BeginInvoke(new Action(() =>
            {
                UpdatePropertyControlsState();
                _autosaveTimer.Stop();
                _autosaveTimer.Start();
            }));
        }

        /// <summary>
        /// Сохраняет текущие настройки после срабатывания таймера debounce.
        /// </summary>
        private void AutosaveTimer_Tick(object sender, EventArgs e)
        {
            _autosaveTimer.Stop();
            SaveCurrentSettings();
        }

        /// <summary>
        /// Сохраняет текущие значения контролов в cfg через settings store.
        /// </summary>
        private void SaveCurrentSettings()
        {
            if (_isApplyingSettings)
            {
                return;
            }

            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["SelectedTests"] = string.Join(";", GetSelectedTestNames()),
                ["Statuses"] = string.Join(";", GetSelectedStatuses().Select(s => s.ToString())),
                ["FloorSource"] = _floorGridRadio.Checked ? "Grid" : "Property",
                ["FloorProperty"] = _floorPropertyCombo.Text ?? string.Empty,
                ["DisciplineSource"] = _disciplineModelRadio.Checked ? "ModelFile" : "Property",
                ["DisciplineProperty"] = _disciplinePropertyCombo.Text ?? string.Empty,
                ["ModelFileDisciplineMappingsJson"] = SerializeModelFileMappings(GetModelFileDisciplineMappingsFromUi())
            };

            ClashDashboardSettingsStore.Save(NavisworksApplication.MainDocument, map);
        }

        /// <summary>
        /// Активирует или блокирует combobox по выбранному источнику измерений.
        /// </summary>
        private void UpdatePropertyControlsState()
        {
            _floorPropertyCombo.Enabled = _floorPropertyRadio.Checked;
            _disciplinePropertyCombo.Enabled = _disciplinePropertyRadio.Checked;
            _modelFileDisciplineMappingsGrid.Enabled = _disciplineModelRadio.Checked;
            _addMappingButton.Enabled = _disciplineModelRadio.Checked;
            _removeMappingButton.Enabled = _disciplineModelRadio.Checked;
        }

        /// <summary>
        /// Обновляет таблицу маппинга из словаря настроек.
        /// </summary>
        private void SetModelFileDisciplineMappingsFromMap(IReadOnlyDictionary<string, string> map)
        {
            _modelFileDisciplineMappingsGrid.Rows.Clear();
            if (map == null)
            {
                return;
            }

            string json = GetValue(map, "ModelFileDisciplineMappingsJson");
            if (string.IsNullOrWhiteSpace(json))
            {
                return;
            }

            try
            {
                var serializer = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
                var mappings = serializer.Deserialize<List<ClashDimensionResolver.ModelFileDisciplineMapping>>(json);
                if (mappings == null)
                {
                    return;
                }

                foreach (ClashDimensionResolver.ModelFileDisciplineMapping mapping in mappings)
                {
                    if (mapping == null)
                    {
                        continue;
                    }

                    string key = mapping.Key ?? string.Empty;
                    string valuesText = string.Join(";", mapping.Values ?? Array.Empty<string>());
                    _modelFileDisciplineMappingsGrid.Rows.Add(key, valuesText);
                }
            }
            catch
            {
                // Если JSON битый, оставляем таблицу пустой.
            }
        }

        /// <summary>
        /// Добавляет новую строку в таблицу маппинга.
        /// </summary>
        private void AddMappingButton_Click(object sender, EventArgs e)
        {
            _modelFileDisciplineMappingsGrid.Rows.Add(string.Empty, string.Empty);
            SettingsControl_Changed(sender, e);
        }

        /// <summary>
        /// Удаляет выделенные строки в таблице маппинга.
        /// </summary>
        private void RemoveMappingButton_Click(object sender, EventArgs e)
        {
            if (_modelFileDisciplineMappingsGrid.SelectedRows.Count == 0)
            {
                return;
            }

            foreach (DataGridViewRow row in _modelFileDisciplineMappingsGrid.SelectedRows)
            {
                if (row == null)
                {
                    continue;
                }

                if (!row.IsNewRow)
                {
                    _modelFileDisciplineMappingsGrid.Rows.Remove(row);
                }
            }

            SettingsControl_Changed(sender, e);
        }

        /// <summary>
        /// Вызывается при изменении таблицы маппинга.
        /// </summary>
        private void ModelMappingsGrid_Changed(object sender, EventArgs e)
        {
            if (_isApplyingSettings)
            {
                return;
            }

            BeginInvoke(new Action(() =>
            {
                _autosaveTimer.Stop();
                _autosaveTimer.Start();
            }));
        }

        /// <summary>
        /// Извлекает правила маппинга из таблицы UI.
        /// </summary>
        private List<ClashDimensionResolver.ModelFileDisciplineMapping> GetModelFileDisciplineMappingsFromUi()
        {
            var result = new List<ClashDimensionResolver.ModelFileDisciplineMapping>();
            foreach (DataGridViewRow row in _modelFileDisciplineMappingsGrid.Rows)
            {
                if (row == null || row.IsNewRow)
                {
                    continue;
                }

                string key = (row.Cells[0].Value ?? string.Empty).ToString().Trim();
                string valuesText = (row.Cells[1].Value ?? string.Empty).ToString();
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
        /// Сериализует правила маппинга в JSON для хранения в cfg.
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
        /// Возвращает список выбранных тестов.
        /// </summary>
        private List<string> GetSelectedTestNames()
        {
            var result = new List<string>();
            foreach (object item in _testsCheckedList.CheckedItems)
            {
                if (!string.IsNullOrWhiteSpace(item?.ToString()))
                {
                    result.Add(item.ToString());
                }
            }

            return result;
        }

        /// <summary>
        /// Возвращает список выбранных статусов в формате enum.
        /// </summary>
        private HashSet<ClashResultStatus> GetSelectedStatuses()
        {
            var statuses = new HashSet<ClashResultStatus>();
            foreach (object item in _statusCheckedList.CheckedItems)
            {
                if (Enum.TryParse(item?.ToString(), out ClashResultStatus status))
                {
                    statuses.Add(status);
                }
            }

            return statuses;
        }

        /// <summary>
        /// Устанавливает выбранные тесты по именам.
        /// </summary>
        private void SetCheckedTests(IEnumerable<string> names)
        {
            var set = new HashSet<string>(names ?? Array.Empty<string>(), StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < _testsCheckedList.Items.Count; i++)
            {
                string item = _testsCheckedList.Items[i]?.ToString() ?? string.Empty;
                _testsCheckedList.SetItemChecked(i, set.Contains(item));
            }
        }

        /// <summary>
        /// Устанавливает выбранные статусы по именам.
        /// </summary>
        private void SetCheckedStatuses(IEnumerable<string> names)
        {
            var set = new HashSet<string>(names ?? Array.Empty<string>(), StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < _statusCheckedList.Items.Count; i++)
            {
                string item = _statusCheckedList.Items[i]?.ToString() ?? string.Empty;
                _statusCheckedList.SetItemChecked(i, set.Contains(item));
            }
        }

        /// <summary>
        /// Реагирует на изменение строки фильтра и обновляет видимость тестов.
        /// </summary>
        private void TestsFilterTextBox_TextChanged(object sender, EventArgs e)
        {
            ApplyTestsFilter();
            SettingsControl_Changed(sender, e);
        }

        /// <summary>
        /// Безопасно получает строковое значение из словаря настроек.
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
        /// Разбивает строку, разделенную ';', в массив значений.
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
        /// Возвращает путь текущего документа или пустую строку.
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
}
