using System;
using System.Drawing;
using System.Windows.Forms;

namespace SmartNavisTools
{
    /// <summary>
    /// Панель импорта Clash Report XML и обновления статусов.
    /// </summary>
    internal sealed class ClashStatusUpdaterControl : UserControl
    {
        private readonly Button _importButton;
        private readonly Button _updateButton;
        private readonly TextBox _previewTextBox;
        private string[] _loadedFiles = Array.Empty<string>();

        public ClashStatusUpdaterControl()
        {
            _importButton = new Button
            {
                Text = "Импорт отчёта (XML)",
                MinimumSize = new Size(150, 23),
                Anchor = AnchorStyles.Bottom | AnchorStyles.Left
            };
            _importButton.Click += OnImportClick;

            _updateButton = new Button
            {
                Text = "Обновить статусы",
                MinimumSize = new Size(150, 23),
                Anchor = AnchorStyles.Bottom | AnchorStyles.Left
            };
            _updateButton.Click += OnUpdateClick;

            _previewTextBox = new TextBox
            {
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Both,
                BackColor = Color.White,
                BorderStyle = BorderStyle.FixedSingle,
                Dock = DockStyle.Fill
            };
            _previewTextBox.VisibleChanged += (_, __) => _previewTextBox.Clear();

            var buttonPanel = new FlowLayoutPanel
            {
                Dock = DockStyle.Bottom,
                Height = 36,
                FlowDirection = FlowDirection.LeftToRight,
                Padding = new Padding(8, 6, 8, 6),
                WrapContents = false
            };
            buttonPanel.Controls.Add(_importButton);
            buttonPanel.Controls.Add(_updateButton);

            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 2
            };
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.Controls.Add(_previewTextBox, 0, 0);
            layout.Controls.Add(buttonPanel, 0, 1);

            Controls.Add(layout);
            MinimumSize = new Size(300, 200);
        }

        protected override void OnParentChanged(EventArgs e)
        {
            base.OnParentChanged(e);
            Dock = DockStyle.Fill;
        }

        private void OnImportClick(object sender, EventArgs e)
        {
            using (var dialog = new OpenFileDialog
            {
                Multiselect = true,
                Filter = "XML (*.xml)|*.xml"
            })
            {
                if (dialog.ShowDialog() != DialogResult.OK)
                {
                    return;
                }

                ClashStatusUpdaterLogic.ImportResult result =
                    ClashStatusUpdaterLogic.ImportFiles(dialog.FileNames);

                if (!result.Success)
                {
                    _previewTextBox.Clear();
                    _loadedFiles = Array.Empty<string>();
                    MessageBox.Show(
                        result.ErrorMessage,
                        "Ошибка импорта",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Error);
                    return;
                }

                if (!ConfirmPartialExport(dialog.FileNames[0]))
                {
                    _previewTextBox.Clear();
                    _loadedFiles = Array.Empty<string>();
                    return;
                }

                _loadedFiles = result.LoadedFiles;
                _previewTextBox.Text = result.PreviewText;

                string message = result.LoadedFiles.Length == 1
                    ? "Загружен 1 XML-файл."
                    : $"Загружено файлов: {result.LoadedFiles.Length}.";
                MessageBox.Show(message, "Импорт", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }

        private void OnUpdateClick(object sender, EventArgs e)
        {
            if (_loadedFiles.Length == 0 || string.IsNullOrWhiteSpace(_previewTextBox.Text))
            {
                MessageBox.Show(
                    "Сначала загрузите файл Clash Report XML.",
                    "Нет данных",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return;
            }

            ClashStatusUpdaterLogic.UpdateResult result =
                ClashStatusUpdaterLogic.ApplyLoadedFiles(_loadedFiles);

            if (!result.Success)
            {
                MessageBox.Show(
                    result.ErrorMessage,
                    "Ошибка обновления",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return;
            }

            MessageBox.Show("Готово.", "Результат", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

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
                        MessageBoxButtons.YesNo,
                        MessageBoxIcon.Question) == DialogResult.Yes;
                }

                if (hasReviewedInSummary && hasApprovedInSummary &&
                    !hasReviewedInResults && hasApprovedInResults)
                {
                    return MessageBox.Show(
                        "В Included Clashes Report не отмечен «Reviewed». Продолжить?",
                        "Подтверждение",
                        MessageBoxButtons.YesNo,
                        MessageBoxIcon.Question) == DialogResult.Yes;
                }
            }
            catch
            {
                return false;
            }

            return true;
        }

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
