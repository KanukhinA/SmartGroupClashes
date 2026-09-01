using Autodesk.Navisworks.Api.Plugins;

namespace SmartNavisTools
{
    /// <summary>
    /// Панель обновления статусов коллизий из Clash Report XML.
    /// </summary>
    [Plugin("SmartNavisTools.ClashStatusPane", "SmartNavisTools",
        DisplayName = "Обновление статусов коллизий",
        ToolTip = "Импорт Clash Report XML и обновление статусов")]
    [DockPanePlugin(650, 550, AutoScroll = true, MinimumHeight = 200, MinimumWidth = 300)]
    internal class ClashStatusPane : DockPanePlugin
    {
        /// <inheritdoc />
        public override System.Windows.Forms.Control CreateControlPane()
        {
            return new SmartNavisToolsPaneHost(() =>
                new PluginPaneShell(
                    "Обновление статусов коллизий",
                    "Импорт Clash Report XML и применение статусов в Clash Detective.",
                    new ClashStatusView()));
        }

        /// <inheritdoc />
        public override void DestroyControlPane(System.Windows.Forms.Control pane)
        {
            pane?.Dispose();
        }
    }
}
