using System;
using System.Windows.Forms;
using Autodesk.Navisworks.Api.Plugins;

namespace SmartNavisTools
{
    /// <summary>
    /// Обработчик команд ленты SmartNavisTools.
    /// </summary>
    [Plugin("SmartNavisTools", "SmartNavisTools", DisplayName = "SmartNavisTools")]
    [Strings("SmartNavisTools.name")]
    [RibbonLayout("SmartNavisTools.xaml")]
    [RibbonTab("ID_SmartNavisToolsTab", DisplayName = "Smart")]
    [Command("ID_SmartNavisToolsClashStatusButton",
        Icon = "ClashStatusIcon_Small.ico",
        LargeIcon = "ClashStatusIcon_Large.ico",
        DisplayName = "Статусы",
        ToolTip = "Импорт Clash Report XML и обновление статусов в Clash Detective")]
    [Command("ID_SmartNavisToolsClashTestsButton",
        Icon = "ClashTestsIcon_Small.ico",
        LargeIcon = "ClashTestsIcon_Large.ico",
        DisplayName = "Проверки",
        ToolTip = "Список проверок Clash Detective и их обновление")]
    [Command("ID_SmartNavisToolsSearchSetsProButton",
        Icon = "SearchSetsProIcon_Small.ico",
        LargeIcon = "SearchSetsProIcon_Large.ico",
        DisplayName = "Поисковые наборы PRO",
        ToolTip = "Пакетное добавление и замена условий в поисковых наборах")]
    [Command("ID_SmartNavisToolsClashDashboardButton",
        Icon = "ClashDashboardIcon_Small.ico",
        LargeIcon = "ClashDashboardIcon_Large.ico",
        DisplayName = "Статистика пересечений",
        ToolTip = "Интерактивная статистика пересечений")]
    internal class RibbonHandler : CommandHandlerPlugin
    {
        private const string ClashStatusPanePluginId =
            "SmartNavisTools.ClashStatusPane.SmartNavisTools";

        private const string ClashDetectiveTestsPanePluginId =
            "SmartNavisTools.ClashDetectiveTestsPane.SmartNavisTools";

        private const string SearchSetsProPanePluginId =
            "SmartNavisTools.SearchSetsProPane.SmartNavisTools";

        private const string ClashDashboardPanePluginId =
            "SmartNavisTools.ClashDashboardPane.SmartNavisTools";

        /// <inheritdoc />
        public override int ExecuteCommand(string commandId, params string[] parameters)
        {
            if (Autodesk.Navisworks.Api.Application.IsAutomated)
            {
                throw new InvalidOperationException("Недопустимо при запуске через автоматизацию.");
            }

            if (commandId == "ID_SmartNavisToolsClashStatusButton")
            {
                ToggleDockPane(ClashStatusPanePluginId);
            }
            else if (commandId == "ID_SmartNavisToolsClashTestsButton")
            {
                ToggleDockPane(ClashDetectiveTestsPanePluginId);
            }
            else if (commandId == "ID_SmartNavisToolsSearchSetsProButton")
            {
                ToggleDockPane(SearchSetsProPanePluginId);
            }
            else if (commandId == "ID_SmartNavisToolsClashDashboardButton")
            {
                ToggleDockPane(ClashDashboardPanePluginId);
            }

            return 0;
        }

        /// <inheritdoc />
        public override CommandState CanExecuteCommand(string commandId)
        {
            return new CommandState
            {
                IsVisible = true,
                IsEnabled = true,
                IsChecked = false
            };
        }

        /// <inheritdoc />
        public override bool CanExecuteRibbonTab(string name)
        {
            return true;
        }

        /// <summary>
        /// Находит dock pane по ID, загружает при необходимости и переключает видимость.
        /// </summary>
        private static void ToggleDockPane(string pluginId)
        {
            try
            {
                PluginRecord record = Autodesk.Navisworks.Api.Application.Plugins.FindPlugin(pluginId);
                if (record == null)
                {
                    MessageBox.Show(
                        "Панель не зарегистрирована в Navisworks.\r\nID: " + pluginId +
                        "\r\nПерезапустите Navisworks после обновления SmartNavisTools.",
                        "SmartNavisTools",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);
                    return;
                }

                if (!(record is DockPanePluginRecord) || !record.IsEnabled)
                {
                    MessageBox.Show(
                        "Панель отключена или недоступна.\r\nID: " + pluginId,
                        "SmartNavisTools",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);
                    return;
                }

                if (record.LoadedPlugin == null)
                {
                    record.LoadPlugin();
                }

                if (record.LoadedPlugin is DockPanePlugin dockPane)
                {
                    dockPane.Visible = !dockPane.Visible;
                    return;
                }

                MessageBox.Show(
                    "Не удалось получить dock pane после загрузки.\r\nID: " + pluginId,
                    "SmartNavisTools",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
            }
            catch (Exception exception)
            {
                MessageBox.Show(
                    "Ошибка открытия панели.\r\nID: " + pluginId + "\r\n" + exception.Message,
                    "SmartNavisTools",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
        }
    }
}
