using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using Autodesk.Navisworks.Api.Clash;
using NavisworksApplication = Autodesk.Navisworks.Api.Application;

namespace SmartNavisTools
{
    /// <summary>
    /// Панель со списком проверок Clash Detective и их обновлением.
    /// </summary>
    internal sealed class ClashDetectiveTestsControl : UserControl
    {
        private readonly ListBox _testsListBox;
        private readonly TextBox _filterTextBox;
        private readonly CheckBox _useRegexCheckBox;
        private readonly Button _clearFilterButton;
        private readonly Label _filterSummaryLabel;
        private readonly Label _selectionLabel;
        private readonly Button _updateButton;
        private readonly Button _refreshButton;
        private readonly Button _exportXmlButton;
        private readonly Button _exportHtmlButton;
        private bool _isSubscribed;
        private int _totalTestCount;

        public ClashDetectiveTestsControl()
        {
            var headerLabel = new Label
            {
                Text = "Проверки Clash Detective",
                Dock = DockStyle.Top,
                Height = 24,
                Padding = new Padding(8, 6, 8, 0),
                AutoSize = false
            };

            _filterTextBox = new TextBox
            {
                Dock = DockStyle.Fill,
                Margin = new Padding(0, 3, 6, 3)
            };
            _filterTextBox.TextChanged += (_, __) => ApplyFilterToList();

            _useRegexCheckBox = new CheckBox
            {
                Text = "Regex",
                AutoSize = true,
                Margin = new Padding(0, 6, 6, 3)
            };
            _useRegexCheckBox.CheckedChanged += (_, __) => ApplyFilterToList();

            _clearFilterButton = new Button
            {
                Text = "Очистить",
                AutoSize = true,
                Margin = new Padding(0, 2, 0, 3),
                MinimumSize = new Size(70, 23)
            };
            _clearFilterButton.Click += OnClearFilterClick;

            _filterSummaryLabel = new Label
            {
                Text = "Показано: 0",
                AutoSize = true,
                ForeColor = SystemColors.GrayText,
                Margin = new Padding(0, 4, 0, 0)
            };

            var filterRow = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                ColumnCount = 4,
                Padding = new Padding(8, 4, 8, 0)
            };
            filterRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            filterRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            filterRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            filterRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

            var searchLabel = new Label
            {
                Text = "Поиск:",
                AutoSize = true,
                Anchor = AnchorStyles.Left,
                Margin = new Padding(0, 6, 6, 3)
            };

            filterRow.Controls.Add(searchLabel, 0, 0);
            filterRow.Controls.Add(_filterTextBox, 1, 0);
            filterRow.Controls.Add(_useRegexCheckBox, 2, 0);
            filterRow.Controls.Add(_clearFilterButton, 3, 0);
            filterRow.Controls.Add(_filterSummaryLabel, 1, 1);
            filterRow.SetColumnSpan(_filterSummaryLabel, 3);

            _testsListBox = new ListBox
            {
                Dock = DockStyle.Fill,
                SelectionMode = SelectionMode.MultiExtended,
                IntegralHeight = false,
                DisplayMember = "DisplayName"
            };
            _testsListBox.SelectedIndexChanged += (_, __) => UpdateSelectionSummary();

            _selectionLabel = new Label
            {
                Text = "Выбрано проверок: 0",
                AutoSize = true,
                Margin = new Padding(0, 0, 12, 0)
            };

            _refreshButton = new Button
            {
                Text = "Обновить список",
                MinimumSize = new Size(120, 23),
                AutoSize = true
            };
            _refreshButton.Click += (_, __) => RefreshTestsList();

            _updateButton = new Button
            {
                Text = "Обновить проверки",
                MinimumSize = new Size(150, 23),
                AutoSize = true
            };
            _updateButton.Click += OnUpdateClick;

            _exportXmlButton = new Button
            {
                Text = "Экспорт XML",
                MinimumSize = new Size(110, 23),
                AutoSize = true
            };
            _exportXmlButton.Click += (_, __) => OnExportClick(ClashDetectiveTestsLogic.ExportFormat.Xml);

            _exportHtmlButton = new Button
            {
                Text = "Экспорт HTML",
                MinimumSize = new Size(110, 23),
                AutoSize = true
            };
            _exportHtmlButton.Click += (_, __) => OnExportClick(ClashDetectiveTestsLogic.ExportFormat.Html);

            var bottomPanel = new FlowLayoutPanel
            {
                Dock = DockStyle.Bottom,
                AutoSize = true,
                FlowDirection = FlowDirection.LeftToRight,
                Padding = new Padding(8, 6, 8, 8),
                WrapContents = true
            };
            bottomPanel.Controls.Add(_selectionLabel);
            bottomPanel.Controls.Add(_refreshButton);
            bottomPanel.Controls.Add(_updateButton);
            bottomPanel.Controls.Add(_exportXmlButton);
            bottomPanel.Controls.Add(_exportHtmlButton);

            Controls.Add(_testsListBox);
            Controls.Add(bottomPanel);
            Controls.Add(filterRow);
            Controls.Add(headerLabel);

            MinimumSize = new Size(280, 260);
        }

        protected override void OnParentChanged(EventArgs e)
        {
            base.OnParentChanged(e);
            Dock = DockStyle.Fill;

            if (Parent != null)
            {
                SubscribeToDocumentChanges();
                RefreshTestsList();
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

        private void OnDocumentChanged(object sender, EventArgs e)
        {
            if (IsDisposed)
            {
                return;
            }

            if (InvokeRequired)
            {
                BeginInvoke(new Action(RefreshTestsList));
                return;
            }

            RefreshTestsList();
        }

        private void OnClearFilterClick(object sender, EventArgs e)
        {
            _filterTextBox.Clear();
            _useRegexCheckBox.Checked = false;
        }

        private void RefreshTestsList()
        {
            ApplyFilterToList();
        }

        private void ApplyFilterToList()
        {
            var selectedNames = new HashSet<string>(
                _testsListBox.SelectedItems
                    .OfType<ClashDetectiveTestsLogic.ClashTestInfo>()
                    .Select(item => item.DisplayName),
                StringComparer.Ordinal);

            IReadOnlyList<ClashDetectiveTestsLogic.ClashTestInfo> allTests = ClashDetectiveTestsLogic.GetTests();
            _totalTestCount = allTests.Count;

            string pattern = _filterTextBox.Text;
            bool useRegex = _useRegexCheckBox.Checked;
            string filterError = null;
            bool hasFilter = !string.IsNullOrWhiteSpace(pattern);

            if (hasFilter && useRegex)
            {
                ClashDetectiveTestsLogic.MatchesFilter(string.Empty, pattern, true, out filterError);
                if (filterError != null)
                {
                    _testsListBox.Items.Clear();
                    UpdateFilterSummary(0, true, filterError);
                    _refreshButton.Enabled = NavisworksApplication.MainDocument != null
                        && !NavisworksApplication.MainDocument.IsClear;
                    UpdateSelectionSummary();
                    return;
                }
            }

            _testsListBox.BeginUpdate();
            try
            {
                _testsListBox.Items.Clear();

                int visibleCount = 0;
                foreach (ClashDetectiveTestsLogic.ClashTestInfo test in allTests)
                {
                    if (!ClashDetectiveTestsLogic.MatchesFilter(test.DisplayName, pattern, useRegex, out filterError))
                    {
                        continue;
                    }

                    int index = _testsListBox.Items.Add(test);
                    visibleCount++;
                    if (selectedNames.Contains(test.DisplayName))
                    {
                        _testsListBox.SetSelected(index, true);
                    }
                }

                UpdateFilterSummary(visibleCount, hasFilter, filterError);
            }
            finally
            {
                _testsListBox.EndUpdate();
            }

            bool hasTests = _testsListBox.Items.Count > 0;
            _updateButton.Enabled = hasTests;
            _exportXmlButton.Enabled = hasTests;
            _exportHtmlButton.Enabled = hasTests;
            _refreshButton.Enabled = NavisworksApplication.MainDocument != null
                && !NavisworksApplication.MainDocument.IsClear;
            UpdateSelectionSummary();
        }

        private void UpdateFilterSummary(int visibleCount, bool hasFilter, string filterError)
        {
            if (!string.IsNullOrEmpty(filterError))
            {
                _filterSummaryLabel.Text = "Ошибка regex: " + filterError;
                _filterSummaryLabel.ForeColor = Color.DarkRed;
                return;
            }

            _filterSummaryLabel.ForeColor = SystemColors.GrayText;
            if (!hasFilter)
            {
                _filterSummaryLabel.Text = "Показано: " + visibleCount;
                return;
            }

            _filterSummaryLabel.Text = "Показано: " + visibleCount + " из " + _totalTestCount;
        }

        private void UpdateSelectionSummary()
        {
            int selectedCount = _testsListBox.SelectedItems.Count;
            int visibleCount = _testsListBox.Items.Count;

            if (visibleCount < _totalTestCount && _totalTestCount > 0)
            {
                _selectionLabel.Text = "Выбрано: " + selectedCount + " (видно " + visibleCount + " из " + _totalTestCount + ")";
            }
            else
            {
                _selectionLabel.Text = "Выбрано проверок: " + selectedCount;
            }

            bool hasSelection = visibleCount > 0 && selectedCount > 0;
            _updateButton.Enabled = hasSelection;
            _exportXmlButton.Enabled = hasSelection;
            _exportHtmlButton.Enabled = hasSelection;
        }

        private void OnUpdateClick(object sender, EventArgs e)
        {
            var selectedTests = _testsListBox.SelectedItems
                .OfType<ClashDetectiveTestsLogic.ClashTestInfo>()
                .Select(item => item.Test)
                .ToArray();

            ClashDetectiveTestsLogic.RunTestsResult result =
                ClashDetectiveTestsLogic.RunTests(selectedTests);

            if (!result.Success)
            {
                MessageBox.Show(
                    result.ErrorMessage ?? "Не удалось обновить проверки.",
                    "Ошибка",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return;
            }

            string message = result.WasCanceled
                ? "Обновление прервано. Обновлено проверок: " + result.RunCount + "."
                : "Обновлено проверок: " + result.RunCount + ".";

            MessageBox.Show(message, "Результат", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private void OnExportClick(ClashDetectiveTestsLogic.ExportFormat format)
        {
            var selectedTests = _testsListBox.SelectedItems
                .OfType<ClashDetectiveTestsLogic.ClashTestInfo>()
                .Select(item => item.Test)
                .ToArray();

            if (selectedTests.Length == 0)
            {
                MessageBox.Show(
                    "Выберите хотя бы одну проверку.",
                    "Нет выбора",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return;
            }

            ClashDetectiveTestsLogic.ExportTestsResult result =
                ClashDetectiveTestsLogic.ExportTests(selectedTests, format);

            if (!result.Success)
            {
                MessageBox.Show(
                    result.ErrorMessage ?? "Не удалось экспортировать отчёты.",
                    "Ошибка экспорта",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return;
            }

            string formatName = format == ClashDetectiveTestsLogic.ExportFormat.Xml ? "XML" : "HTML";
            MessageBox.Show(
                "Запущен стандартный экспорт Clash Detective (Write Report).\n"
                + "Проверок: " + result.ExportedCount
                + "\nФормат: " + formatName
                + "\n\nУкажите файл в диалоге Navisworks. Содержимое отчёта — по настройкам вкладки Report.",
                "Экспорт",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }
    }
}
