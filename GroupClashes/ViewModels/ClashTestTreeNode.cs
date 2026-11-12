using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Text.RegularExpressions;

namespace SmartGroupClashes.ViewModels
{
    public interface INumberedClashTestItem : INotifyPropertyChanged
    {
        string DisplayName { get; }
        bool IsSelected { get; set; }
    }

    /// <summary>Группа по паре разделов либо ссылка на проверку. Не изменяет структуру Clash Detective.</summary>
    public sealed class ClashTestTreeNode : ViewModelBase, IDisposable
    {
        private readonly string _label;
        private readonly INumberedClashTestItem _test;
        private readonly List<ClashTestTreeNode> _allChildren = new List<ClashTestTreeNode>();
        private ClashTestTreeNode _parent;
        private bool _isExpanded;

        internal ClashTestTreeNode(string label, string key, INumberedClashTestItem test = null)
        {
            _label = label;
            Key = key;
            _test = test;
            Children = new ObservableCollection<ClashTestTreeNode>();
            if (_test != null) _test.PropertyChanged += TestChanged;
        }

        internal string Key { get; }
        public bool IsGroup => _test == null;
        public string DisplayName => IsGroup ? _label + " (" + VisibleTests().Count() + ")" : _test.DisplayName;
        public ObservableCollection<ClashTestTreeNode> Children { get; }

        public bool IsExpanded
        {
            get { return _isExpanded; }
            set { SetProperty(ref _isExpanded, value); }
        }

        public bool? IsChecked
        {
            get
            {
                if (!IsGroup) return _test.IsSelected;
                bool selected = false;
                bool unselected = false;
                foreach (INumberedClashTestItem test in VisibleTests())
                {
                    if (test.IsSelected) selected = true;
                    else unselected = true;
                    if (selected && unselected) return null;
                }
                return selected;
            }
            set
            {
                if (!value.HasValue) return;
                foreach (INumberedClashTestItem test in VisibleTests()) test.IsSelected = value.Value;
                NotifySelectionChanged();
            }
        }

        internal void Add(ClashTestTreeNode child)
        {
            child._parent = this;
            _allChildren.Add(child);
        }

        internal void SortChildren()
        {
            _allChildren.Sort(ClashTestTreeBuilder.CompareNodes);
            foreach (ClashTestTreeNode child in _allChildren) child.SortChildren();
        }

        internal bool ApplyFilter(string filter)
        {
            if (!IsGroup)
                return string.IsNullOrEmpty(filter)
                    || (_test.DisplayName ?? string.Empty).IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0;

            Children.Clear();
            bool matchesGroup = !string.IsNullOrEmpty(filter)
                && _label.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0;
            foreach (ClashTestTreeNode child in _allChildren)
                if (child.ApplyFilter(matchesGroup ? string.Empty : filter)) Children.Add(child);
            if (!string.IsNullOrEmpty(filter) && Children.Count > 0) IsExpanded = true;
            OnPropertyChanged(nameof(DisplayName));
            OnPropertyChanged(nameof(IsChecked));
            return Children.Count > 0;
        }

        internal IEnumerable<INumberedClashTestItem> VisibleTests()
        {
            if (!IsGroup) yield return _test;
            else
                foreach (ClashTestTreeNode child in Children)
                    foreach (INumberedClashTestItem test in child.VisibleTests()) yield return test;
        }

        internal IEnumerable<ClashTestTreeNode> AllNodes()
        {
            yield return this;
            foreach (ClashTestTreeNode child in _allChildren)
                foreach (ClashTestTreeNode node in child.AllNodes()) yield return node;
        }

        private void TestChanged(object sender, PropertyChangedEventArgs e)
        {
            if (string.IsNullOrEmpty(e.PropertyName) || e.PropertyName == nameof(INumberedClashTestItem.IsSelected))
                NotifySelectionChanged();
        }

        private void NotifySelectionChanged()
        {
            OnPropertyChanged(nameof(IsChecked));
            _parent?.NotifySelectionChanged();
        }

        public void Dispose()
        {
            if (_test != null) _test.PropertyChanged -= TestChanged;
            foreach (ClashTestTreeNode child in _allChildren) child.Dispose();
            _parent = null;
        }
    }

    internal static class ClashTestTreeBuilder
    {
        private static readonly Regex Prefix = new Regex(@"^\s*[0-9]+(?:\.[0-9]+)*", RegexOptions.CultureInvariant);
        private static readonly HashSet<string> Qualifiers = new HashSet<string>(new[] { "П", "Д", "P", "D" },
            StringComparer.OrdinalIgnoreCase);
        private static readonly string[,] SectionAliases =
        {
            { "ЕОМ", "EOM" }, { "ВК", "VK" }, { "СС", "SS" }, { "ОВ", "OV" },
            { "ОМ", "OM" }, { "АР", "AR" }, { "КР", "KR" }, { "ИТП", "ITP" },
            { "ВИС", "VIS" }, { "АОВ", "AOV" }, { "КЖ", "KZH" }, { "КМ", "KM" },
        };
        internal const string UnassignedKey = "unassigned";
        private static readonly string[] GroupOrder = { "ВИС_ВИС", "ВИС_АР", "ВИС_КР", "АР_АР", UnassignedKey };

        internal static List<ClashTestTreeNode> Build(IEnumerable<INumberedClashTestItem> tests)
        {
            var roots = new List<ClashTestTreeNode>();
            var groups = new Dictionary<string, ClashTestTreeNode>(StringComparer.Ordinal);
            foreach (INumberedClashTestItem test in tests)
            {
                string key = GroupName(test.DisplayName) ?? UnassignedKey;
                ClashTestTreeNode parent;
                if (!groups.TryGetValue(key, out parent))
                {
                    parent = new ClashTestTreeNode(key == UnassignedKey ? "Нераспределенные" : key, key)
                        { IsExpanded = true };
                    groups.Add(key, parent);
                    roots.Add(parent);
                }
                parent.Add(new ClashTestTreeNode(test.DisplayName, string.Empty, test));
            }
            roots.Sort(CompareNodes);
            foreach (ClashTestTreeNode root in roots)
            {
                root.SortChildren();
                root.ApplyFilter(string.Empty);
            }
            return roots;
        }

        internal static string[] NumberParts(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return Array.Empty<string>();
            Match match = Prefix.Match(name);
            if (!match.Success) return Array.Empty<string>();
            if (match.Length < name.Length)
            {
                char next = name[match.Length];
                if (!char.IsWhiteSpace(next) && next != '_' && next != '-' && next != ':'
                    && next != ')' && next != ']' && next != '.') return Array.Empty<string>();
                if (next == '.' && match.Length + 1 < name.Length
                    && (name[match.Length + 1] == '.' || char.IsDigit(name[match.Length + 1]))) return Array.Empty<string>();
            }
            return match.Value.Trim().Split('.').Select(part =>
            {
                string normalized = part.TrimStart('0');
                return normalized.Length == 0 ? "0" : normalized;
            }).ToArray();
        }

        private static string[] SectionTokens(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return Array.Empty<string>();
            // Номер может оставаться частью исходного имени, но больше не создаёт ветки дерева.
            string section = NumberParts(name).Length > 0 ? name.Substring(Prefix.Match(name).Length) : name;
            section = section.TrimStart(' ', '\t', '_', '-', ':', '.', ')', ']');
            int separator = section.IndexOfAny(new[] { '—', '–' });
            if (separator >= 0) section = section.Substring(0, separator);
            int dash = section.IndexOf(" - ", StringComparison.Ordinal);
            if (dash >= 0) section = section.Substring(0, dash);
            return section.Split('_').Select(token => Regex.Replace(token.Trim(), @"\s+", " "))
                .Where(token => !string.IsNullOrWhiteSpace(token) && !Qualifiers.Contains(token)).ToArray();
        }

        internal static string GroupName(string name)
        {
            string[] tokens = SectionTokens(name);
            if (tokens.Length < 2) return null;
            string a = SectionKind(tokens[0]);
            string b = SectionKind(tokens[1]);
            if (a == "ВИС" && b == "ВИС") return "ВИС_ВИС";
            if ((a == "ВИС" && b == "АР") || (a == "АР" && b == "ВИС")) return "ВИС_АР";
            if ((a == "ВИС" && b == "КР") || (a == "КР" && b == "ВИС")) return "ВИС_КР";
            if (a == "АР" && b == "АР") return "АР_АР";
            return null;
        }

        private static string SectionKind(string token)
        {
            Match code = Regex.Match(NormalizeSection(token),
                @"^(?<code>VIS|MEP|EOM|VK|SS|OV|OM|ITP|AOV|AR|KR|KZH|KM)[0-9]*(?:\s*\([^)]*\))?$",
                RegexOptions.CultureInvariant);
            if (!code.Success) return null;
            switch (code.Groups["code"].Value)
            {
                case "AR": return "АР";
                case "KR": case "KZH": case "KM": return "КР";
                default: return "ВИС";
            }
        }

        private static string NormalizeSection(string token)
        {
            string normalized = token.ToUpperInvariant().Replace(" (", "(");
            for (int i = 0; i < SectionAliases.GetLength(0); i++)
            {
                string alias = SectionAliases[i, 0];
                if (normalized.StartsWith(alias, StringComparison.Ordinal)
                    && (normalized.Length == alias.Length || char.IsDigit(normalized[alias.Length])
                        || normalized[alias.Length] == '('))
                    return SectionAliases[i, 1] + normalized.Substring(alias.Length);
            }
            return normalized;
        }

        internal static int CompareNodes(ClashTestTreeNode a, ClashTestTreeNode b)
        {
            if (a.IsGroup || b.IsGroup)
            {
                if (a.IsGroup != b.IsGroup) return a.IsGroup ? -1 : 1;
                return Array.IndexOf(GroupOrder, a.Key).CompareTo(Array.IndexOf(GroupOrder, b.Key));
            }
            string[] left = NumberParts(a.DisplayName);
            string[] right = NumberParts(b.DisplayName);
            for (int i = 0; i < Math.Min(left.Length, right.Length); i++)
            {
                int length = left[i].Length.CompareTo(right[i].Length);
                if (length != 0) return length;
                int digits = string.CompareOrdinal(left[i], right[i]);
                if (digits != 0) return digits;
            }
            int depth = left.Length.CompareTo(right.Length);
            return depth != 0 ? depth : string.Compare(a.DisplayName, b.DisplayName, StringComparison.CurrentCultureIgnoreCase);
        }
    }
}
