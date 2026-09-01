using System;
using System.Windows.Forms.Integration;
using Forms = System.Windows.Forms;
using WpfControls = System.Windows.Controls;

namespace SmartNavisTools
{
    /// <summary>
    /// Универсальный WinForms-хост для Navisworks, который встраивает WPF-контрол в dock pane через `ElementHost`.
    /// </summary>
    internal sealed class SmartNavisToolsPaneHost : Forms.UserControl
    {
        private readonly Func<WpfControls.UserControl> _contentFactory;
        private ElementHost _elementHost;
        private Forms.Panel _hostPanel;
        private bool _isInitialized;

        /// <summary>
        /// Сохраняет фабрику контента и откладывает создание WPF до загрузки pane.
        /// </summary>
        public SmartNavisToolsPaneHost(Func<WpfControls.UserControl> contentFactory)
        {
            _contentFactory = contentFactory ?? throw new ArgumentNullException(nameof(contentFactory));
            Load += OnHostLoad;
        }

        /// <summary>
        /// Создаёт `ElementHost` ровно один раз и безопасно монтирует в него WPF-контрол.
        /// </summary>
        private void OnHostLoad(object sender, EventArgs e)
        {
            if (_isInitialized)
            {
                return;
            }

            try
            {
                _elementHost = new ElementHost
                {
                    Dock = Forms.DockStyle.Fill,
                    AutoSize = false,
                    Child = _contentFactory()
                };

                Controls.Add(_elementHost);
                _isInitialized = true;
                ResizeHost();
            }
            catch (Exception exception)
            {
                Controls.Clear();
                Controls.Add(new Forms.Label
                {
                    Dock = Forms.DockStyle.Fill,
                    Text = "Не удалось создать WPF-панель.\r\n" + exception.Message,
                    Padding = new Forms.Padding(12)
                });
            }
        }

        /// <summary>
        /// Подписывается на resize родительской панели Navisworks и удерживает хост во всю доступную область.
        /// </summary>
        protected override void OnVisibleChanged(EventArgs e)
        {
            base.OnVisibleChanged(e);

            if (Parent is Forms.Panel parentPanel)
            {
                if (!ReferenceEquals(_hostPanel, parentPanel))
                {
                    if (_hostPanel != null)
                    {
                        _hostPanel.SizeChanged -= OnHostPanelSizeChanged;
                    }

                    _hostPanel = parentPanel;
                    _hostPanel.SizeChanged += OnHostPanelSizeChanged;
                }

                ResizeHost();
            }
        }

        /// <summary>
        /// Освобождает подписки на изменение размера и ресурсы вложенного `ElementHost`.
        /// </summary>
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (_hostPanel != null)
                {
                    _hostPanel.SizeChanged -= OnHostPanelSizeChanged;
                    _hostPanel = null;
                }

                if (_elementHost != null)
                {
                    _elementHost.Dispose();
                    _elementHost = null;
                }
            }

            base.Dispose(disposing);
        }

        /// <summary>
        /// Обновляет размер host-контрола при изменении области dock pane.
        /// </summary>
        private void OnHostPanelSizeChanged(object sender, EventArgs e)
        {
            ResizeHost();
        }

        /// <summary>
        /// Подгоняет размеры хоста под текущие габариты родительской панели Navisworks.
        /// </summary>
        private void ResizeHost()
        {
            if (_hostPanel == null)
            {
                return;
            }

            Width = _hostPanel.Width;
            Height = _hostPanel.Height;
        }
    }
}
