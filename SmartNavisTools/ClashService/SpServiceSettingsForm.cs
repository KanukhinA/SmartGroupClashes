using System;
using System.Drawing;
using System.Windows.Forms;

namespace SmartNavisTools
{
    /// <summary>Диалог настроек подключения SmartNavisTools к SP-Service.</summary>
    internal sealed class SpServiceSettingsForm : Form
    {
        private readonly TextBox _baseUrlBox;
        private readonly TextBox _loginBox;
        private readonly TextBox _passwordBox;
        private readonly Label _statusLabel;

        /// <summary>Создаёт форму с полями URL, логин и пароль.</summary>
        public SpServiceSettingsForm()
        {
            Text = "Связь с сервером";
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            Width = 520;
            Height = 280;
            Font = new Font("Segoe UI", 9F);

            SpServiceSettings stored = SpServiceSettingsStore.Load();

            TableLayoutPanel layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 6,
                Padding = new Padding(12)
            };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 130));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

            _baseUrlBox = CreateTextBox(stored.BaseUrl);
            _loginBox = CreateTextBox(stored.Login);
            _passwordBox = CreateTextBox(stored.Password);
            _passwordBox.UseSystemPasswordChar = true;

            _statusLabel = new Label
            {
                Dock = DockStyle.Fill,
                AutoSize = false,
                Text = "Проект выбирается на вкладке «Проверки Clash Detective»."
            };

            layout.Controls.Add(CreateLabel("Адрес сервера"), 0, 0);
            layout.Controls.Add(_baseUrlBox, 1, 0);
            layout.Controls.Add(CreateLabel("Логин"), 0, 1);
            layout.Controls.Add(_loginBox, 1, 1);
            layout.Controls.Add(CreateLabel("Пароль"), 0, 2);
            layout.Controls.Add(_passwordBox, 1, 2);
            layout.SetColumnSpan(_statusLabel, 2);
            layout.Controls.Add(_statusLabel, 0, 3);

            FlowLayoutPanel buttons = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.RightToLeft,
                WrapContents = false
            };

            Button closeButton = new Button { Text = "Закрыть", DialogResult = DialogResult.Cancel, Width = 100 };
            Button saveButton = new Button { Text = "Сохранить", Width = 100 };
            Button testButton = new Button { Text = "Проверить подключение", AutoSize = true, MinimumSize = new Size(180, 0) };
            saveButton.Click += OnSaveClick;
            testButton.Click += OnTestClick;

            buttons.Controls.Add(closeButton);
            buttons.Controls.Add(saveButton);
            buttons.Controls.Add(testButton);
            layout.SetColumnSpan(buttons, 2);
            layout.Controls.Add(buttons, 0, 4);

            Controls.Add(layout);
            AcceptButton = saveButton;
            CancelButton = closeButton;
        }

        /// <summary>Проверяет логин и пароль на сервере.</summary>
        private void OnTestClick(object sender, EventArgs e)
        {
            try
            {
                _statusLabel.Text = "Проверка...";
                using (SpClashServiceClient client = new SpClashServiceClient(_baseUrlBox.Text))
                {
                    string displayName = client.Login(_loginBox.Text, _passwordBox.Text);
                    _statusLabel.Text = "Вход выполнен: " + displayName + ".";
                }
            }
            catch (Exception ex)
            {
                _statusLabel.Text = "Ошибка: " + ex.Message;
            }
        }

        /// <summary>Сохраняет URL, логин и пароль. Выбранный проект не сбрасывается.</summary>
        private void OnSaveClick(object sender, EventArgs e)
        {
            try
            {
                SpServiceSettings stored = SpServiceSettingsStore.Load();
                SpServiceSettings settings = new SpServiceSettings
                {
                    BaseUrl = _baseUrlBox.Text,
                    Login = _loginBox.Text,
                    Password = _passwordBox.Text,
                    ProjectId = stored != null ? stored.ProjectId : string.Empty
                };
                SpServiceSettingsStore.Save(settings);
                _statusLabel.Text = "Настройки сохранены.";
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    this,
                    "Не удалось сохранить настройки: " + ex.Message,
                    "Связь с сервером",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
        }

        private static Label CreateLabel(string text)
        {
            return new Label
            {
                Text = text,
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft
            };
        }

        private static TextBox CreateTextBox(string value)
        {
            return new TextBox
            {
                Dock = DockStyle.Fill,
                Text = value ?? string.Empty
            };
        }
    }
}
