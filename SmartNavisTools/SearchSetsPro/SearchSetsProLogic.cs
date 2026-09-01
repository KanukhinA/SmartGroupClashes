using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Autodesk.Navisworks.Api;
using DocumentSelectionSets = Autodesk.Navisworks.Api.DocumentParts.DocumentSelectionSets;

namespace SmartNavisTools
{
    internal enum SearchSetsProDeleteMode
    {
        /// <summary>Удалить все условия с указанной категорией и свойством.</summary>
        ByProperty,

        /// <summary>Удалить только отмеченные условия из списка, собранного по выбранным наборам.</summary>
        FromSelectedSets
    }

    internal enum SearchSetsProOperation
    {
        Add,
        Replace,
        Delete
    }

    internal enum SearchSetsProApplyMode
    {
        UpdateExisting,
        CreateCopies
    }

    /// <summary>
    /// Пакетное добавление и замена условий в поисковых наборах.
    /// </summary>
    internal static class SearchSetsProLogic
    {
        internal sealed class SearchSetReference
        {
            public SearchSetReference(
                SelectionSet selectionSet,
                GroupItem parent,
                int index,
                string path,
                bool hasSearch)
            {
                SelectionSet = selectionSet;
                Parent = parent;
                Index = index;
                Path = path;
                HasSearch = hasSearch;
            }

            public SelectionSet SelectionSet { get; }

            public GroupItem Parent { get; }

            public int Index { get; }

            public string Path { get; }

            public bool HasSearch { get; }

            public string DisplayName => SelectionSet.DisplayName;

            public Guid Guid => SelectionSet.Guid;
        }

        internal static SearchSetReference RefreshReference(SearchSetReference reference)
        {
            if (reference?.Parent == null
                || reference.Index < 0
                || reference.Index >= reference.Parent.Children.Count)
            {
                return reference;
            }

            if (reference.Parent.Children[reference.Index] is SelectionSet selectionSet)
            {
                return new SearchSetReference(
                    selectionSet,
                    reference.Parent,
                    reference.Index,
                    reference.Path,
                    selectionSet.HasSearch);
            }

            return reference;
        }

        internal sealed class FolderNode
        {
            public FolderNode(string displayName, string path)
            {
                DisplayName = displayName;
                Path = path;
            }

            public string DisplayName { get; }

            public string Path { get; }

            public List<FolderNode> Folders { get; } = new List<FolderNode>();

            public List<SearchSetReference> SearchSets { get; } = new List<SearchSetReference>();
        }

        internal sealed class ApplyRequest
        {
            public SearchSetsProOperation Operation { get; set; }
            public SearchSetsProApplyMode ApplyMode { get; set; }
            public SearchSetsProDeleteMode DeleteMode { get; set; } = SearchSetsProDeleteMode.FromSelectedSets;
            public string CopySuffix { get; set; } = " (копия)";

            public string SourceCategory { get; set; }
            public string SourceProperty { get; set; }
            public string TargetCategory { get; set; }
            public string TargetProperty { get; set; }

            public SearchConditionComparison Comparison { get; set; } = SearchConditionComparison.Equal;
            public string ValueText { get; set; } = string.Empty;
            public bool IgnoreCase { get; set; }
            public bool IgnoreAccents { get; set; }

            public IReadOnlyList<SearchConditionDescriptor> ConditionsToDelete { get; set; }
                = Array.Empty<SearchConditionDescriptor>();
        }

        internal sealed class CollectedConditionInfo
        {
            public CollectedConditionInfo(
                SearchConditionDescriptor descriptor,
                int setCount,
                int occurrenceCount)
            {
                Descriptor = descriptor;
                SetCount = setCount;
                OccurrenceCount = occurrenceCount;
            }

            public SearchConditionDescriptor Descriptor { get; }

            public int SetCount { get; }

            public int OccurrenceCount { get; }

            public string DisplayText => Descriptor.ToDisplayString(SetCount, OccurrenceCount);

            public override string ToString()
            {
                return DisplayText;
            }
        }

        internal sealed class ApplyResult
        {
            public bool Success { get; set; }
            public string ErrorMessage { get; set; }
            public int ProcessedCount { get; set; }
            public int SkippedCount { get; set; }
            public int ReplacedOrAddedCount { get; set; }
            public bool WasCanceled { get; set; }
            public List<string> Details { get; } = new List<string>();
        }

        public static FolderNode GetSearchSetTree()
        {
            Document document = Application.MainDocument;
            var root = new FolderNode("Поисковые наборы", string.Empty);
            if (document == null || document.IsClear)
            {
                return root;
            }

            DocumentSelectionSets selectionSets = document.SelectionSets;
            if (selectionSets?.RootItem == null)
            {
                return root;
            }

            foreach (SavedItem child in selectionSets.RootItem.Children)
            {
                CollectSavedItem(child, selectionSets.RootItem, GetChildIndex(selectionSets.RootItem, child), root, child.DisplayName);
            }

            return root;
        }

        public static IReadOnlyList<CollectedConditionInfo> CollectConditionsFromReferences(
            IEnumerable<SearchSetReference> references)
        {
            var selected = references?.Where(r => r != null && r.HasSearch).ToArray() ?? Array.Empty<SearchSetReference>();
            var stats = new Dictionary<string, (SearchConditionDescriptor Descriptor, HashSet<Guid> SetGuids, int Occurrences)>(
                StringComparer.Ordinal);

            foreach (SearchSetReference reference in selected)
            {
                Search search = reference.SelectionSet?.Search;
                if (search?.SearchConditions == null || search.SearchConditions.Count == 0)
                {
                    continue;
                }

                foreach (SearchCondition condition in search.SearchConditions)
                {
                    if (condition == null)
                    {
                        continue;
                    }

                    try
                    {
                        SearchConditionDescriptor descriptor = SearchConditionDescriptor.FromCondition(condition);
                        if (!stats.TryGetValue(descriptor.SignatureKey, out var entry))
                        {
                            entry = (descriptor, new HashSet<Guid>(), 0);
                        }

                        entry.SetGuids.Add(reference.Guid);
                        entry.Occurrences++;
                        stats[descriptor.SignatureKey] = entry;
                    }
                    catch
                    {
                        // Пропускаем повреждённые или нечитаемые условия.
                    }
                }
            }

            return stats.Values
                .Select(entry => new CollectedConditionInfo(
                    entry.Descriptor,
                    entry.SetGuids.Count,
                    entry.Occurrences))
                .OrderBy(info => info.DisplayText, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        public static ApplyResult Apply(IEnumerable<SearchSetReference> references, ApplyRequest request)
        {
            var selected = references?.Where(r => r != null).ToArray() ?? Array.Empty<SearchSetReference>();
            if (selected.Length == 0)
            {
                return new ApplyResult
                {
                    Success = false,
                    ErrorMessage = "Выберите хотя бы один поисковый набор."
                };
            }

            string validationError = ValidateRequest(request);
            if (validationError != null)
            {
                return new ApplyResult
                {
                    Success = false,
                    ErrorMessage = validationError
                };
            }

            Document document = Application.MainDocument;
            if (document == null || document.IsClear)
            {
                return new ApplyResult
                {
                    Success = false,
                    ErrorMessage = "Нет открытого документа Navisworks."
                };
            }

            DocumentSelectionSets selectionSets = document.SelectionSets;
            var result = new ApplyResult();
            Progress progress = Application.BeginProgress(
                "Поисковые наборы PRO",
                "Подготовка…");

            try
            {
                using (Transaction transaction = document.BeginTransaction("SmartNavisTools"))
                {
                    for (int index = 0; index < selected.Length; index++)
                    {
                        SearchSetReference reference = selected[index];
                        if (progress.IsCanceled)
                        {
                            result.WasCanceled = true;
                            break;
                        }

                        progress.Update((double)index / selected.Length);

                        if (!reference.HasSearch)
                        {
                            result.SkippedCount++;
                            result.Details.Add(reference.DisplayName + ": пропущен (нет условий поиска).");
                            continue;
                        }

                        bool changed = TryModifyReference(selectionSets, reference, request, result);
                        if (!changed)
                        {
                            continue;
                        }

                        result.ProcessedCount++;
                    }

                    if (!result.WasCanceled && result.ProcessedCount > 0)
                    {
                        transaction.Commit();
                    }
                }
            }
            finally
            {
                Application.EndProgress();
            }

            result.Success = result.ProcessedCount > 0 || result.SkippedCount > 0;
            if (!result.Success && string.IsNullOrEmpty(result.ErrorMessage))
            {
                result.ErrorMessage = "Не удалось применить изменения к выбранным наборам.";
            }

            return result;
        }

        public static string FormatResultMessage(ApplyResult result)
        {
            var builder = new StringBuilder();
            builder.AppendLine("Обработано наборов: " + result.ProcessedCount);
            builder.AppendLine("Пропущено: " + result.SkippedCount);
            builder.AppendLine("Изменений условий: " + result.ReplacedOrAddedCount);

            if (result.WasCanceled)
            {
                builder.AppendLine("Операция прервана пользователем.");
            }

            if (result.Details.Count > 0)
            {
                builder.AppendLine();
                foreach (string detail in result.Details.Take(result.SkippedCount > 0 ? 50 : 20))
                {
                    builder.AppendLine(detail);
                }

                if (result.Details.Count > 20)
                {
                    builder.AppendLine("… и ещё " + (result.Details.Count - 20) + " записей.");
                }
            }

            return builder.ToString().TrimEnd();
        }

        private static bool TryModifyReference(
            DocumentSelectionSets selectionSets,
            SearchSetReference reference,
            ApplyRequest request,
            ApplyResult result)
        {
            SelectionSet original = reference.SelectionSet;
            Search sourceSearch = original.Search;
            if (sourceSearch == null)
            {
                result.SkippedCount++;
                result.Details.Add(reference.DisplayName + ": пропущен (пустой поиск).");
                return false;
            }

            List<List<SearchCondition>> groups = SearchConditionGroupHelper.ParseGroups(sourceSearch.SearchConditions);
            int changeCount = 0;

            if (request.Operation == SearchSetsProOperation.Add)
            {
                SearchCondition newCondition = SearchConditionGroupHelper.CreateCondition(
                    request.TargetCategory,
                    request.TargetProperty,
                    request.Comparison,
                    request.ValueText,
                    request.IgnoreCase,
                    request.IgnoreAccents);
                changeCount = SearchConditionGroupHelper.AddConditionToGroups(groups, newCondition);
            }
            else if (request.Operation == SearchSetsProOperation.Replace)
            {
                changeCount = SearchConditionGroupHelper.ReplaceConditionsInGroups(
                    groups,
                    request.SourceCategory,
                    request.SourceProperty,
                    request.TargetCategory,
                    request.TargetProperty,
                    request.Comparison,
                    request.ValueText,
                    request.IgnoreCase,
                    request.IgnoreAccents);
            }
            else
            {
                if (request.DeleteMode == SearchSetsProDeleteMode.ByProperty)
                {
                    changeCount = SearchConditionGroupHelper.DeleteConditionsByPropertyInGroups(
                        groups,
                        request.SourceCategory,
                        request.SourceProperty);
                }
                else
                {
                    changeCount = SearchConditionGroupHelper.DeleteSelectedConditionsInGroups(
                        groups,
                        request.ConditionsToDelete);
                }
            }

            if (changeCount == 0)
            {
                result.SkippedCount++;
                if (request.Operation == SearchSetsProOperation.Delete)
                {
                    result.Details.Add(reference.DisplayName + ": " + ExplainDeleteSkip(groups, request));
                }
                else if (request.Operation == SearchSetsProOperation.Replace)
                {
                    result.Details.Add(
                        reference.DisplayName
                        + ": пропущен — исходное условие «"
                        + request.SourceCategory
                        + " / "
                        + request.SourceProperty
                        + "» не найдено в наборе.");
                }
                else
                {
                    result.Details.Add(reference.DisplayName + ": пропущен — не удалось добавить условие.");
                }

                return false;
            }

            if (SearchConditionGroupHelper.GetTotalConditionCount(groups) == 0)
            {
                result.SkippedCount++;
                result.Details.Add(
                    reference.DisplayName
                    + ": пропущен — удаление затронуло бы все условия набора ("
                    + changeCount
                    + " совпад., после удаления останется 0).");
                return false;
            }

            Search modifiedSearch = SearchConditionGroupHelper.BuildSearch(sourceSearch, groups);
            SelectionSet modifiedSet = new SelectionSet(modifiedSearch)
            {
                DisplayName = original.DisplayName
            };

            if (request.ApplyMode == SearchSetsProApplyMode.CreateCopies)
            {
                modifiedSet.DisplayName = original.DisplayName + (request.CopySuffix ?? string.Empty);
                selectionSets.InsertCopy(reference.Parent, reference.Index + 1, modifiedSet);
                result.Details.Add(reference.DisplayName + ": создана копия «" + modifiedSet.DisplayName + "».");
            }
            else
            {
                selectionSets.ReplaceWithCopy(reference.Parent, reference.Index, modifiedSet);
                string action = request.Operation == SearchSetsProOperation.Delete ? "удалено" : "обновлён";
                result.Details.Add(reference.DisplayName + ": " + action + " (" + changeCount + " услов.).");
            }

            result.ReplacedOrAddedCount += changeCount;
            return true;
        }

        private static string ValidateRequest(ApplyRequest request)
        {
            if (request == null)
            {
                return "Не заданы параметры операции.";
            }

            if (request.Operation == SearchSetsProOperation.Replace
                || request.Operation == SearchSetsProOperation.Delete)
            {
                if (request.Operation == SearchSetsProOperation.Delete
                    && request.DeleteMode == SearchSetsProDeleteMode.FromSelectedSets)
                {
                    if (request.ConditionsToDelete == null || request.ConditionsToDelete.Count == 0)
                    {
                        return "Отметьте хотя бы одно условие в списке для удаления.";
                    }
                }
                else if (string.IsNullOrWhiteSpace(request.SourceCategory)
                    || string.IsNullOrWhiteSpace(request.SourceProperty))
                {
                    return "Укажите категорию и свойство условия для удаления.";
                }
            }

            if (request.Operation == SearchSetsProOperation.Add
                || request.Operation == SearchSetsProOperation.Replace)
            {
                if (string.IsNullOrWhiteSpace(request.TargetCategory)
                    || string.IsNullOrWhiteSpace(request.TargetProperty))
                {
                    return "Укажите категорию и свойство нового условия.";
                }
            }

            if (request.Operation == SearchSetsProOperation.Add
                && string.IsNullOrWhiteSpace(request.ValueText))
            {
                return "Укажите значение для нового условия.";
            }

            if (request.Operation == SearchSetsProOperation.Replace
                && string.IsNullOrWhiteSpace(request.ValueText))
            {
                return "Укажите значение и оператор для нового условия.";
            }

            if (request.ApplyMode == SearchSetsProApplyMode.CreateCopies
                && request.CopySuffix == null)
            {
                request.CopySuffix = " (копия)";
            }

            return null;
        }

        private static string ExplainDeleteSkip(
            List<List<SearchCondition>> groups,
            ApplyRequest request)
        {
            if (request.DeleteMode == SearchSetsProDeleteMode.ByProperty)
            {
                IReadOnlyList<string> conditionsInSet = SearchConditionGroupHelper.FindConditionsByProperty(
                    groups,
                    request.SourceCategory,
                    request.SourceProperty);
                if (conditionsInSet.Count == 0)
                {
                    return "пропущен — свойство «"
                        + request.SourceCategory
                        + " / "
                        + request.SourceProperty
                        + "» не найдено в наборе.";
                }

                return "пропущен — свойство «"
                    + request.SourceCategory
                    + " / "
                    + request.SourceProperty
                    + "» не найдено. В наборе есть: "
                    + string.Join("; ", conditionsInSet);
            }

            int selectedCount = request.ConditionsToDelete?.Count ?? 0;
            IReadOnlyList<string> presentInSet = SearchConditionGroupHelper.FindMatchingDescriptors(
                groups,
                request.ConditionsToDelete);
            if (presentInSet.Count > 0)
            {
                return "пропущен — выбранные условия не удалены (неожиданно: в наборе найдены "
                    + string.Join("; ", presentInSet)
                    + ").";
            }

            IReadOnlyList<string> available = SearchConditionGroupHelper.ListAllConditionSummaries(groups);
            if (available.Count == 0)
            {
                return "пропущен — в наборе нет условий поиска.";
            }

            return "пропущен — ни одно из "
                + selectedCount
                + " выбранных условий не найдено. В наборе: "
                + string.Join("; ", available.Take(3))
                + (available.Count > 3 ? " …" : string.Empty);
        }

        private static void CollectSavedItem(
            SavedItem item,
            GroupItem parent,
            int index,
            FolderNode folderNode,
            string path)
        {
            if (item is SelectionSet selectionSet)
            {
                folderNode.SearchSets.Add(new SearchSetReference(
                    selectionSet,
                    parent,
                    index,
                    path,
                    selectionSet.HasSearch));
                return;
            }

            if (!item.IsGroup)
            {
                return;
            }

            var childFolder = new FolderNode(item.DisplayName, path);
            folderNode.Folders.Add(childFolder);

            var group = (GroupItem)item;
            int childIndex = 0;
            foreach (SavedItem child in group.Children)
            {
                string childPath = string.IsNullOrEmpty(path)
                    ? child.DisplayName
                    : path + " / " + child.DisplayName;
                CollectSavedItem(child, group, childIndex, childFolder, childPath);
                childIndex++;
            }
        }

        private static int GetChildIndex(GroupItem parent, SavedItem child)
        {
            for (int index = 0; index < parent.Children.Count; index++)
            {
                if (parent.Children[index] == child)
                {
                    return index;
                }
            }

            return -1;
        }
    }
}
