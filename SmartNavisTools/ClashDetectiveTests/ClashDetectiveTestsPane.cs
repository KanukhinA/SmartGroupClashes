using Autodesk.Navisworks.Api.Plugins;

namespace SmartNavisTools
{
    /// <summary>
    /// Панель списка проверок Clash Detective и их обновления.
    /// </summary>
    [Plugin("SmartNavisTools.ClashDetectiveTestsPane", "SmartNavisTools",
        DisplayName = "Проверки Clash Detective",
        ToolTip = "Список проверок Clash Detective и их обновление")]
    [DockPanePlugin(500, 450, AutoScroll = false, MinimumHeight = 280, MinimumWidth = 280)]
    internal class ClashDetectiveTestsPane : DockPanePlugin
    {
        /// <inheritdoc />
        public override System.Windows.Forms.Control CreateControlPane()
        {
            return new SmartNavisToolsPaneHost(() =>
                new PluginPaneShell(
                    "Проверки Clash Detective",
                    "Список проверок, выбор статусов и проекта SP-Service, обновление и экспорт отчётов.",
                    new ClashDetectiveTestsView(),
                    scrollContent: false));
        }

        /// <inheritdoc />
        public override void DestroyControlPane(System.Windows.Forms.Control pane)
        {
            pane?.Dispose();
        }
    }
}
