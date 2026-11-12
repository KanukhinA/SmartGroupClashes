// Только для изолированных тестов чтения каталога. Плагин собирается с настоящим Autodesk API.
using System;
using System.Collections.Generic;

namespace Autodesk.Navisworks.Api
{
    public class SavedItem
    {
        public virtual Guid Guid { get; set; }
        public virtual string DisplayName { get; set; }
    }
    public class GroupItem : SavedItem
    {
        public List<SavedItem> Children { get; } = new List<SavedItem>();
    }
    public class ModelItem
    {
        public string DisplayName { get; set; }
    }
}

namespace Autodesk.Navisworks.Api.Clash
{
    public class ClashTest : Autodesk.Navisworks.Api.GroupItem
    {
        public ClashSelection SelectionA { get; } = new ClashSelection();
        public ClashSelection SelectionB { get; } = new ClashSelection();
    }
    public class ClashSelection
    {
        public SelectionData Selection { get; } = new SelectionData();
    }
    public class SelectionData
    {
        public bool HasSelectionSources => SelectionSources.Count > 0;
        public List<string> SelectionSources { get; } = new List<string>();
        public List<Autodesk.Navisworks.Api.ModelItem> GetSelectedItems()
        {
            return new List<Autodesk.Navisworks.Api.ModelItem>();
        }
    }
}
