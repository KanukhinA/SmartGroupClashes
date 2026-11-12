using System;
using System.Collections.Generic;
using System.Linq;
using SmartGroupClashes.ViewModels;
using Autodesk.Navisworks.Api;
using Autodesk.Navisworks.Api.Clash;

namespace SmartGroupClashes.Tests
{
    internal static class ClashTestTreeTests
    {
        internal sealed class TestItem : ViewModelBase, INumberedClashTestItem
        {
            private bool _selected;
            internal TestItem(string name) { DisplayName = name; }
            public string DisplayName { get; }
            public bool IsSelected { get { return _selected; } set { SetProperty(ref _selected, value); } }
        }

        private static int _checks;
        private static void Check(bool condition, string description)
        {
            _checks++;
            if (!condition) throw new Exception(description);
        }

        private static ClashTestTreeNode Group(IEnumerable<ClashTestTreeNode> roots, string key)
        {
            return roots.Single(root => root.Key == key);
        }

        private sealed class NoGuidTest : ClashTest
        {
            public override Guid Guid { get { throw new NotSupportedException("Guid is not supported"); } set { } }
        }

        private sealed class BadTest : ClashTest
        {
            public override string DisplayName { get { throw new InvalidOperationException("Unreadable test"); } set { } }
        }

        private static void CatalogTests()
        {
            var folder = new GroupItem();
            folder.Children.Add(new NoGuidTest { DisplayName = "14.1_СС_AR" });
            folder.Children.Add(new BadTest());
            folder.Children.Add(new ClashTest { Guid = Guid.NewGuid(), DisplayName = "14.2_EOM_OV" });
            var source = new SavedItem[]
            {
                folder, new NoGuidTest { DisplayName = "ВИС_КР_duplicate" },
                new NoGuidTest { DisplayName = "ВИС_КР_duplicate" },
            };
            var catalog = ClashTestCatalog.Read(source, null);
            Check(catalog.Items.Count == 4, "Unavailable GUID does not hide checks");
            Check(catalog.Errors.Count == 1, "One bad check does not abort catalog loading");
            Check(catalog.Items[0].Id == Guid.Empty && catalog.Items[1].Id != Guid.Empty, "Optional GUID is handled");
            Check(catalog.Items.Select(item => item.SelectionKey).Distinct().Count() == 4, "Duplicate names have distinct fallback keys");
            var selected = new HashSet<string> { catalog.Items[0].SelectionKey, catalog.Items[2].SelectionKey };
            var refreshed = ClashTestCatalog.Read(source, selected);
            Check(refreshed.Items[0].IsSelected && refreshed.Items[2].IsSelected && !refreshed.Items[3].IsSelected,
                "Catalog reload preserves selection");
            var roots = ClashTestTreeBuilder.Build(refreshed.Items);
            Check(Group(roots, "ВИС_АР").VisibleTests().Count() == 1, "Native folder check assigned by disciplines");
            Check(Group(roots, "ВИС_КР").VisibleTests().Count() == 2, "Unnumbered explicit prefixes are recognized");
            foreach (var root in roots) root.Dispose();
            Check(ClashTestCatalog.Read(null, null).Items.Count == 0, "Empty document is supported");
        }

        private static void ClassificationTests()
        {
            string[,] cases =
            {
                { "ВИС_ВИС_Проверка", "ВИС_ВИС" }, { "ВИС_АР_Проверка", "ВИС_АР" },
                { "ВИС_КР_Проверка", "ВИС_КР" }, { "АР_АР_Проверка", "АР_АР" },
                { "АР_ВИС_Проверка", "ВИС_АР" }, { "КР_ВИС_Проверка", "ВИС_КР" },
                { "9.1_EOM_OV1", "ВИС_ВИС" }, { "8.5_СС_AR", "ВИС_АР" },
                { "8.5_СС_VK", "ВИС_ВИС" }, { "8.1_П_СС_СС", "ВИС_ВИС" },
                { "3.2_Д_ВК_ВК", "ВИС_ВИС" }, { "9.4_EOM_KR", "ВИС_КР" },
                { "9.5_EOM_AR", "ВИС_АР" }, { "13.1_AR(Пространства)_АР", "АР_АР" },
                { "13.1_АР (Пространства)_AR", "АР_АР" }, { "10.1_OV1_ОВ2", "ВИС_ВИС" },
                { "ВИС_КР_АР_дополнительный текст", "ВИС_КР" },
                { "VK_КЖ", "ВИС_КР" }, { "КМ_ЕОМ", "ВИС_КР" },
                { "VIS_AR", "ВИС_АР" }, { "MEP_KR", "ВИС_КР" },
                { "  вис _ ар _ описание", "ВИС_АР" }, { "П_Д_СС_АР", "ВИС_АР" },
                { "ВИС_АР — описание", "ВИС_АР" }, { "СС_АР - описание", "ВИС_АР" },
            };
            for (int i = 0; i < cases.GetLength(0); i++)
                Check(ClashTestTreeBuilder.GroupName(cases[i, 0]) == cases[i, 1], "Category: " + cases[i, 0]);

            foreach (string vis in new[] { "ВИС", "VIS", "VK", "ВК", "EOM", "ЕОМ", "SS", "СС", "OV", "ОВ",
                "OV1", "ОВ2", "OM", "ОМ", "ITP", "ИТП", "AOV", "АОВ" })
            {
                Check(ClashTestTreeBuilder.GroupName(vis + "_AR") == "ВИС_АР", "VIS versus AR: " + vis);
                Check(ClashTestTreeBuilder.GroupName("KR_" + vis) == "ВИС_КР", "Reversed VIS versus KR: " + vis);
                Check(ClashTestTreeBuilder.GroupName("СС_" + vis) == "ВИС_ВИС", "Two engineering disciplines: " + vis);
            }
            foreach (string unknown in new[] { null, "", "No known disciplines", "8.5", "СС", "ВИС_UNKNOWN",
                "UNKNOWN_СС_AR", "ВИС_UNKNOWN_AR", "ПАР_23_A.14_КЖ_Стены", "KR_KR", "AR_KR", "12..3_СС_AR" })
                Check(ClashTestTreeBuilder.GroupName(unknown) == null, "Unassigned: " + unknown);
        }

        private static void TreeTests()
        {
            var items = new[]
            {
                new TestItem("9.1_EOM_OV1"), new TestItem("8.5_СС_AR"), new TestItem("8.5_СС_VK"),
                new TestItem("9.4_EOM_KR"), new TestItem("13.1_AR(Пространства)_AR"),
                new TestItem("ВИС_ВИС_Трубы"), new TestItem("Неопределённая проверка"),
                new TestItem("АР_ВИС_Проверка"), new TestItem("ВИС_КР_Повтор"),
                new TestItem("ВИС_КР_Повтор"), new TestItem("КР_АР_Нет такой группы"),
            };
            var roots = ClashTestTreeBuilder.Build(items);
            Check(roots.Select(root => root.Key).SequenceEqual(new[]
                { "ВИС_ВИС", "ВИС_АР", "ВИС_КР", "АР_АР", ClashTestTreeBuilder.UnassignedKey }), "Unique categories in requested order");
            Check(roots.All(root => root.Children.All(child => !child.IsGroup)), "No numerical or intermediate groups");
            Check(roots.All(root => root.IsExpanded), "Category branches expanded initially");
            Check(roots.SelectMany(root => root.VisibleTests()).Count() == items.Length, "No checks lost or merged");
            Check(Group(roots, "ВИС_ВИС").DisplayName == "ВИС_ВИС (3)", "Engineering category count");
            Check(Group(roots, "ВИС_АР").DisplayName == "ВИС_АР (2)", "Architecture category count");
            Check(Group(roots, "ВИС_КР").DisplayName == "ВИС_КР (3)", "Repeated requested category not duplicated");
            Check(Group(roots, "АР_АР").DisplayName == "АР_АР (1)", "Architecture versus architecture");
            Check(Group(roots, ClashTestTreeBuilder.UnassignedKey).DisplayName == "Нераспределенные (2)", "Unknown pairs remain visible");
            Check(items[1] != items[2] && Group(roots, "ВИС_АР").VisibleTests().Contains(items[1])
                && Group(roots, "ВИС_ВИС").VisibleTests().Contains(items[2]), "Same numbers can belong to different categories");

            var branch = Group(roots, "ВИС_ВИС");
            branch.IsChecked = true;
            Check(branch.VisibleTests().All(item => item.IsSelected) && !items[1].IsSelected, "Category selects only its checks");
            items[0].IsSelected = false;
            Check(branch.IsChecked == null, "Partial selection propagates to category");
            int notifications = 0;
            branch.PropertyChanged += (sender, e) => { if (e.PropertyName == "IsChecked") notifications++; };
            items[0].IsSelected = true;
            Check(notifications > 0 && branch.IsChecked == true, "Source selection updates category binding");
            branch.ApplyFilter("OV1");
            Check(branch.VisibleTests().Single() == items[0] && branch.DisplayName == "ВИС_ВИС (1)", "Leaf search retains category");
            branch.IsChecked = false;
            branch.ApplyFilter(string.Empty);
            Check(!items[0].IsSelected && items[2].IsSelected && items[5].IsSelected, "Filtered bulk change preserves hidden selection");
            Check(!branch.ApplyFilter("no such test") && !branch.VisibleTests().Any(), "No matching leaves gives empty branch");
            branch.ApplyFilter("вис_вис");
            Check(branch.VisibleTests().Count() == 3, "Category search matches its decoded label case insensitively");
            var ar = Group(roots, "ВИС_АР");
            Check(ar.ApplyFilter("ВИС_АР") && ar.VisibleTests().Count() == 2, "Category search includes aliases without literal prefix");
            foreach (var root in roots) root.ApplyFilter(string.Empty);
            foreach (var item in roots.SelectMany(root => root.VisibleTests())) item.IsSelected = false;
            Check(roots.All(root => root.IsChecked == false), "Clear selection resets categories");
            foreach (var root in roots) root.Dispose();
            notifications = 0;
            items[0].IsSelected = true;
            Check(notifications == 0, "Disposed tree releases source subscriptions");
            Check(!ClashTestTreeBuilder.Build(Array.Empty<INumberedClashTestItem>()).Any(), "Empty document supported");

            var sorted = ClashTestTreeBuilder.Build(new[]
            {
                new TestItem("2.10_СС_VK"), new TestItem("2.2_СС_VK"), new TestItem("1_EOM_OV"),
            });
            Check(sorted.Count == 1 && sorted[0].VisibleTests().Select(item => item.DisplayName)
                .SequenceEqual(new[] { "1_EOM_OV", "2.2_СС_VK", "2.10_СС_VK" }),
                "Original numbers only sort leaves, without creating branches");
            foreach (var root in sorted) root.Dispose();
        }

        private static int Main()
        {
            try
            {
                CatalogTests();
                ClassificationTests();
                TreeTests();
                Console.WriteLine("PASS: " + _checks + " tree checks");
                return 0;
            }
            catch (Exception error)
            {
                Console.Error.WriteLine("FAIL: " + error.Message);
                return 1;
            }
        }
    }
}
