using Autodesk.Navisworks.Api.Plugins;

namespace SmartNavisTools
{
    /// <summary>
    /// Панель для формирования интерактивного дашборда коллизий.
    /// </summary>
    [Plugin("SmartNavisTools.ClashDashboardPane", "SmartNavisTools",
        DisplayName = "Статистика пересечений",
        ToolTip = "Интерактивная статистика пересечений")]
    [DockPanePlugin(520, 520, AutoScroll = true, MinimumHeight = 260, MinimumWidth = 320)]
    internal sealed class ClashDashboardPane : DockPanePlugin
    {
        /// <summary>
        /// Создает пользовательский контрол панели.
        /// </summary>
        public override System.Windows.Forms.Control CreateControlPane()
        {
            return new SmartNavisToolsPaneHost(() =>
                new PluginPaneShell(
                    "Статистика пересечений",
                    "Настройка параметров и формирование интерактивного HTML-отчёта.",
                    new ClashDashboardView()));
        }

        /// <summary>
        /// Освобождает ресурсы пользовательского контрола панели.
        /// </summary>
        public override void DestroyControlPane(System.Windows.Forms.Control pane)
        {
            pane?.Dispose();
        }
    }
}
