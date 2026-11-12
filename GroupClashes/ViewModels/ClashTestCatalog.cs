using System;
using System.Collections.Generic;
using Autodesk.Navisworks.Api;
using Autodesk.Navisworks.Api.Clash;

namespace SmartGroupClashes.ViewModels
{
    /// <summary>Читает проверки независимо от доступности GUID и ошибок отдельных объектов API.</summary>
    internal sealed class ClashTestCatalog
    {
        internal List<ClashTestItemViewModel> Items { get; } = new List<ClashTestItemViewModel>();
        internal List<string> Errors { get; } = new List<string>();

        internal static ClashTestCatalog Read(IEnumerable<SavedItem> items, ISet<string> selectedKeys)
        {
            var catalog = new ClashTestCatalog();
            var occurrences = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (ClashTest test in EnumerateTests(items))
            {
                try
                {
                    string name = test.DisplayName ?? string.Empty;
                    int occurrence;
                    occurrences.TryGetValue(name, out occurrence);
                    occurrences[name] = occurrence + 1;
                    var item = new ClashTestItemViewModel(test, occurrence);
                    item.IsSelected = selectedKeys != null && selectedKeys.Contains(item.SelectionKey);
                    catalog.Items.Add(item);
                }
                catch (Exception ex) { catalog.Errors.Add(ex.Message); }
            }
            return catalog;
        }

        private static IEnumerable<ClashTest> EnumerateTests(IEnumerable<SavedItem> items)
        {
            if (items == null) yield break;
            foreach (SavedItem item in items)
            {
                var test = item as ClashTest;
                if (test != null) yield return test;
                else
                {
                    var group = item as GroupItem;
                    if (group != null)
                        foreach (ClashTest child in EnumerateTests(group.Children)) yield return child;
                }
            }
        }
    }
}
