using System;
using System.Collections.Generic;
using System.Windows.Forms;

namespace SmartNavisTools
{
    /// <summary>Выбор отчёта пересечений на сервере.</summary>
    internal sealed class SpClashReportPickForm : Form
    {
        private readonly ListBox _list;

        /// <summary>Выбранный отчёт или null.</summary>
        public SpClashServiceClient.ReportItem SelectedReport { get; private set; }

        /// <summary>Создаёт диалог со списком отчётов проекта.</summary>
        public SpClashReportPickForm(IEnumerable<SpClashServiceClient.ReportItem> reports)
        {
            Text = "Отчёты на сервере";
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            Width = 560;
            Height = 360;

            _list = new ListBox
            {
                Dock = DockStyle.Fill,
                HorizontalScrollbar = true
            };
            _list.DoubleClick += OnOpenClick;

            foreach (SpClashServiceClient.ReportItem report in reports)
                _list.Items.Add(report);

            if (_list.Items.Count > 0)
                _list.SelectedIndex = 0;

            Button openButton = new Button { Text = "Открыть", Width = 100, DialogResult = DialogResult.None };
            Button cancelButton = new Button { Text = "Отмена", Width = 100, DialogResult = DialogResult.Cancel };
            openButton.Click += OnOpenClick;

            FlowLayoutPanel buttons = new FlowLayoutPanel
            {
                Dock = DockStyle.Bottom,
                FlowDirection = FlowDirection.RightToLeft,
                Height = 40,
                Padding = new Padding(8)
            };
            buttons.Controls.Add(cancelButton);
            buttons.Controls.Add(openButton);

            Controls.Add(_list);
            Controls.Add(buttons);
            AcceptButton = openButton;
            CancelButton = cancelButton;
        }

        /// <summary>Подтверждает выбранный отчёт.</summary>
        private void OnOpenClick(object sender, EventArgs e)
        {
            SelectedReport = _list.SelectedItem as SpClashServiceClient.ReportItem;
            if (SelectedReport == null)
            {
                MessageBox.Show(this, "Выберите отчёт.", "Отчёты на сервере",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            DialogResult = DialogResult.OK;
        }
    }
}
