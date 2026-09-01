using Autodesk.Navisworks.Api.Plugins;

namespace SmartNavisTools
{
    /// <summary>
    /// Док-панель инструмента «Поисковые наборы PRO».
    /// </summary>
    [Plugin("SmartNavisTools.SearchSetsProPane", "SmartNavisTools",
        DisplayName = "Поисковые наборы PRO",
        ToolTip = "Пакетное добавление и замена условий в поисковых наборах")]
    [DockPanePlugin(720, 620, AutoScroll = false, MinimumHeight = 420, MinimumWidth = 520)]
    internal class SearchSetsProPane : DockPanePlugin
    {
        /// <inheritdoc />
        public override System.Windows.Forms.Control CreateControlPane()
        {
            return new SmartNavisToolsPaneHost(() =>
                new PluginPaneShell(
                    "Поисковые наборы PRO",
                    "Пакетное добавление, замена и удаление условий в поисковых наборах.",
                    new SearchSetsProView()));
        }

        /// <inheritdoc />
        public override void DestroyControlPane(System.Windows.Forms.Control pane)
        {
            pane?.Dispose();
        }
    }
}
