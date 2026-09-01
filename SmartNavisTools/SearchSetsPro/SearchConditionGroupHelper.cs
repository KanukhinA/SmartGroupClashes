using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Autodesk.Navisworks.Api;

namespace SmartNavisTools
{
    /// <summary>
    /// Уникальное описание условия поиска для сравнения и удаления.
    /// </summary>
    internal sealed class SearchConditionDescriptor
    {
        public SearchConditionDescriptor(
            string category,
            string property,
            SearchConditionComparison comparison,
            string valueText,
            bool ignoreCase,
            bool ignoreAccents,
            bool useAnyCategory)
        {
            Category = category ?? string.Empty;
            Property = property ?? string.Empty;
            Comparison = comparison;
            ValueText = valueText ?? string.Empty;
            IgnoreCase = ignoreCase;
            IgnoreAccents = ignoreAccents;
            UseAnyCategory = useAnyCategory;
            SignatureKey = BuildSignatureKey(
                Category,
                Property,
                Comparison,
                ValueText,
                ignoreCase,
                ignoreAccents,
                useAnyCategory);
        }

        public string Category { get; }

        public string Property { get; }

        public SearchConditionComparison Comparison { get; }

        public string ValueText { get; }

        public bool IgnoreCase { get; }

        public bool IgnoreAccents { get; }

        public bool UseAnyCategory { get; }

        public string SignatureKey { get; }

        public static SearchConditionDescriptor FromCondition(SearchCondition condition)
        {
            return new SearchConditionDescriptor(
                SearchConditionGroupHelper.GetCategoryDisplayName(condition),
                SearchConditionGroupHelper.GetPropertyDisplayName(condition),
                condition.Comparison,
                SearchConditionGroupHelper.GetConditionValueText(condition),
                SearchConditionGroupHelper.GetIgnoreCase(condition),
                SearchConditionGroupHelper.GetIgnoreAccents(condition),
                SearchConditionGroupHelper.GetUseAnyCategory(condition));
        }

        public bool Matches(SearchCondition condition)
        {
            return SearchConditionGroupHelper.MatchesCondition(
                condition,
                Category,
                Property,
                Comparison,
                ValueText,
                IgnoreCase,
                IgnoreAccents,
                UseAnyCategory);
        }

        public string ToDisplayString(int setCount, int occurrenceCount)
        {
            var builder = new StringBuilder();
            builder.Append(Category);
            builder.Append(" / ");
            builder.Append(Property);
            builder.Append(" | ");
            builder.Append(SearchConditionGroupHelper.GetComparisonDisplayName(Comparison));
            builder.Append(" | «");
            builder.Append(ValueText);
            builder.Append('»');
            if (IgnoreCase)
            {
                builder.Append(" [без регистра]");
            }

            if (IgnoreAccents)
            {
                builder.Append(" [без акцентов]");
            }

            builder.Append(" (наборов: ");
            builder.Append(setCount);
            builder.Append(", вхождений: ");
            builder.Append(occurrenceCount);
            builder.Append(')');
            return builder.ToString();
        }

        private static string BuildSignatureKey(
            string category,
            string property,
            SearchConditionComparison comparison,
            string valueText,
            bool ignoreCase,
            bool ignoreAccents,
            bool useAnyCategory)
        {
            return category + "\u001F"
                + property + "\u001F"
                + (int)comparison + "\u001F"
                + valueText + "\u001F"
                + (ignoreCase ? "1" : "0") + "\u001F"
                + (ignoreAccents ? "1" : "0") + "\u001F"
                + (useAnyCategory ? "1" : "0");
        }
    }

    /// <summary>
    /// Парсинг, изменение и сборка групп условий поиска (AND внутри группы, OR между группами).
    /// </summary>
    internal static class SearchConditionGroupHelper
    {
        public static List<List<SearchCondition>> ParseGroups(SearchConditionCollection conditions)
        {
            var groups = new List<List<SearchCondition>>();
            if (conditions == null || conditions.Count == 0)
            {
                return groups;
            }

            var current = new List<SearchCondition>();
            foreach (SearchCondition condition in conditions)
            {
                if (current.Count > 0 && HasStartGroup(condition))
                {
                    groups.Add(current);
                    current = new List<SearchCondition>();
                }

                current.Add(condition);
            }

            if (current.Count > 0)
            {
                groups.Add(current);
            }

            return groups;
        }

        public static Search BuildSearch(Search sourceSearch, List<List<SearchCondition>> groups)
        {
            var search = new Search();
            if (sourceSearch != null)
            {
                search.Selection.CopyFrom(sourceSearch.Selection);
                search.Locations = sourceSearch.Locations;
                search.PruneBelowMatch = sourceSearch.PruneBelowMatch;
            }
            else
            {
                search.Selection.SelectAll();
                search.Locations = SearchLocations.DescendantsAndSelf;
            }

            if (groups == null || groups.Count == 0)
            {
                return search;
            }

            if (groups.Count == 1)
            {
                foreach (SearchCondition condition in groups[0])
                {
                    search.SearchConditions.Add(StripStartGroup(condition));
                }
            }
            else
            {
                foreach (List<SearchCondition> group in groups)
                {
                    var groupCopy = new List<SearchCondition>(group.Count);
                    for (int index = 0; index < group.Count; index++)
                    {
                        groupCopy.Add(index == 0 ? EnsureStartGroup(group[index]) : StripStartGroup(group[index]));
                    }

                    search.SearchConditions.AddGroup(groupCopy);
                }
            }

            return search;
        }

        public static int AddConditionToGroups(
            List<List<SearchCondition>> groups,
            SearchCondition newCondition)
        {
            if (groups.Count == 0)
            {
                groups.Add(new List<SearchCondition> { newCondition });
                return 1;
            }

            int added = 0;
            foreach (List<SearchCondition> group in groups)
            {
                group.Add(CloneCondition(newCondition));
                added++;
            }

            return added;
        }

        public static int ReplaceConditionsInGroups(
            List<List<SearchCondition>> groups,
            string sourceCategory,
            string sourceProperty,
            string targetCategory,
            string targetProperty,
            SearchConditionComparison comparison,
            string valueText,
            bool ignoreCase,
            bool ignoreAccents)
        {
            int replaced = 0;
            foreach (List<SearchCondition> group in groups)
            {
                for (int index = 0; index < group.Count; index++)
                {
                    SearchCondition condition = group[index];
                    if (!MatchesProperty(condition, sourceCategory, sourceProperty))
                    {
                        continue;
                    }

                    group[index] = CreateCondition(
                        targetCategory,
                        targetProperty,
                        comparison,
                        valueText,
                        ignoreCase,
                        ignoreAccents);
                    replaced++;
                }
            }

            return replaced;
        }

        public static int DeleteConditionsInGroups(
            List<List<SearchCondition>> groups,
            string categoryDisplay,
            string propertyDisplay,
            SearchConditionComparison comparison,
            string valueText,
            bool ignoreCase,
            bool ignoreAccents)
        {
            int deleted = 0;
            foreach (List<SearchCondition> group in groups)
            {
                for (int index = group.Count - 1; index >= 0; index--)
                {
                    if (!MatchesCondition(
                            group[index],
                            categoryDisplay,
                            propertyDisplay,
                            comparison,
                            valueText,
                            ignoreCase,
                            ignoreAccents))
                    {
                        continue;
                    }

                    group.RemoveAt(index);
                    deleted++;
                }
            }

            groups.RemoveAll(group => group.Count == 0);
            return deleted;
        }

        public static int DeleteConditionsByPropertyInGroups(
            List<List<SearchCondition>> groups,
            string categoryDisplay,
            string propertyDisplay)
        {
            int deleted = 0;
            foreach (List<SearchCondition> group in groups)
            {
                for (int index = group.Count - 1; index >= 0; index--)
                {
                    if (!MatchesProperty(group[index], categoryDisplay, propertyDisplay))
                    {
                        continue;
                    }

                    group.RemoveAt(index);
                    deleted++;
                }
            }

            groups.RemoveAll(group => group.Count == 0);
            return deleted;
        }

        public static int DeleteSelectedConditionsInGroups(
            List<List<SearchCondition>> groups,
            IReadOnlyList<SearchConditionDescriptor> descriptors)
        {
            if (descriptors == null || descriptors.Count == 0)
            {
                return 0;
            }

            int deleted = 0;
            foreach (List<SearchCondition> group in groups)
            {
                for (int index = group.Count - 1; index >= 0; index--)
                {
                    SearchCondition condition = group[index];
                    foreach (SearchConditionDescriptor descriptor in descriptors)
                    {
                        if (!descriptor.Matches(condition))
                        {
                            continue;
                        }

                        group.RemoveAt(index);
                        deleted++;
                        break;
                    }
                }
            }

            groups.RemoveAll(group => group.Count == 0);
            return deleted;
        }

        public static IReadOnlyList<string> FindConditionsByProperty(
            List<List<SearchCondition>> groups,
            string categoryDisplay,
            string propertyDisplay)
        {
            var sameCategory = new List<string>();
            var anyConditions = new List<string>();

            foreach (List<SearchCondition> group in groups)
            {
                foreach (SearchCondition condition in group)
                {
                    string summary = FormatConditionSummary(condition);
                    anyConditions.Add(summary);

                    if (string.Equals(
                            GetCategoryDisplayName(condition),
                            categoryDisplay,
                            StringComparison.OrdinalIgnoreCase))
                    {
                        sameCategory.Add(summary);
                    }
                }
            }

            if (sameCategory.Count > 0)
            {
                return sameCategory
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Take(5)
                    .ToArray();
            }

            return anyConditions
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(5)
                .ToArray();
        }

        public static IReadOnlyList<string> FindMatchingDescriptors(
            List<List<SearchCondition>> groups,
            IReadOnlyList<SearchConditionDescriptor> descriptors)
        {
            if (descriptors == null || descriptors.Count == 0)
            {
                return Array.Empty<string>();
            }

            var found = new List<string>();
            foreach (List<SearchCondition> group in groups)
            {
                foreach (SearchCondition condition in group)
                {
                    foreach (SearchConditionDescriptor descriptor in descriptors)
                    {
                        if (descriptor.Matches(condition))
                        {
                            found.Add(FormatConditionSummary(condition));
                        }
                    }
                }
            }

            return found
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(5)
                .ToArray();
        }

        public static IReadOnlyList<string> ListAllConditionSummaries(List<List<SearchCondition>> groups)
        {
            var summaries = new List<string>();
            foreach (List<SearchCondition> group in groups)
            {
                foreach (SearchCondition condition in group)
                {
                    summaries.Add(FormatConditionSummary(condition));
                }
            }

            return summaries
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        public static string GetComparisonDisplayName(SearchConditionComparison comparison)
        {
            switch (comparison)
            {
                case SearchConditionComparison.Equal:
                    return "Равно";
                case SearchConditionComparison.NotEqual:
                    return "Не равно";
                case SearchConditionComparison.DisplayStringContains:
                    return "Содержит";
                case SearchConditionComparison.DisplayStringWildcard:
                    return "Маска";
                case SearchConditionComparison.NumericLessThan:
                    return "Меньше";
                case SearchConditionComparison.NumericLessThanOrEqual:
                    return "Меньше или равно";
                case SearchConditionComparison.NumericGreaterThanOrEqual:
                    return "Больше или равно";
                case SearchConditionComparison.NumericGreaterThan:
                    return "Больше";
                default:
                    return comparison.ToString();
            }
        }

        public static int GetTotalConditionCount(List<List<SearchCondition>> groups)
        {
            int total = 0;
            if (groups == null)
            {
                return total;
            }

            foreach (List<SearchCondition> group in groups)
            {
                total += group.Count;
            }

            return total;
        }

        public static SearchCondition CreateCondition(
            string categoryDisplay,
            string propertyDisplay,
            SearchConditionComparison comparison,
            string valueText,
            bool ignoreCase,
            bool ignoreAccents)
        {
            SearchCondition condition = SearchCondition.HasPropertyByDisplayName(categoryDisplay, propertyDisplay);
            if (ignoreCase)
            {
                condition = condition.IgnoreStringValueCase();
            }

            if (ignoreAccents)
            {
                condition = condition.IgnoreStringValueAccents();
            }

            condition = ApplyCategoryAnyModeIfNeeded(condition, categoryDisplay);

            VariantData value = VariantData.FromDisplayString(valueText ?? string.Empty);
            return ApplyComparison(condition, comparison, value);
        }

        public static SearchCondition CloneWithProperty(
            SearchCondition source,
            string categoryDisplay,
            string propertyDisplay)
        {
            SearchCondition condition = SearchCondition.HasPropertyByDisplayName(categoryDisplay, propertyDisplay);
            condition = ApplyOptions(condition, source.Options);
            condition = ApplyCategoryAnyModeIfNeeded(condition, categoryDisplay);
            return ApplyComparison(condition, source.Comparison, source.Value);
        }

        public static SearchCondition CloneCondition(SearchCondition source)
        {
            string category = GetCategoryDisplayName(source);
            string property = GetPropertyDisplayName(source);
            return CloneWithProperty(source, category, property);
        }

        public static bool MatchesProperty(
            SearchCondition condition,
            string categoryDisplay,
            string propertyDisplay)
        {
            if (!string.Equals(GetPropertyDisplayName(condition), propertyDisplay, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            return string.Equals(GetCategoryDisplayName(condition), categoryDisplay, StringComparison.OrdinalIgnoreCase);
        }

        public static bool MatchesCondition(
            SearchCondition condition,
            string categoryDisplay,
            string propertyDisplay,
            SearchConditionComparison comparison,
            string valueText,
            bool ignoreCase,
            bool ignoreAccents)
        {
            return MatchesCondition(
                condition,
                categoryDisplay,
                propertyDisplay,
                comparison,
                valueText,
                ignoreCase,
                ignoreAccents,
                GetUseAnyCategory(condition));
        }

        public static bool MatchesCondition(
            SearchCondition condition,
            string categoryDisplay,
            string propertyDisplay,
            SearchConditionComparison comparison,
            string valueText,
            bool ignoreCase,
            bool ignoreAccents,
            bool useAnyCategory)
        {
            if (!MatchesProperty(condition, categoryDisplay, propertyDisplay))
            {
                return false;
            }

            if (GetUseAnyCategory(condition) != useAnyCategory)
            {
                return false;
            }

            if (condition.Comparison != comparison)
            {
                return false;
            }

            bool conditionIgnoreCase = HasOption(condition, SearchConditionOptions.IgnoreDisplayStringValueCase);
            bool conditionIgnoreAccents = HasOption(condition, SearchConditionOptions.IgnoreDisplayStringValueAccents);
            if (conditionIgnoreCase != ignoreCase || conditionIgnoreAccents != ignoreAccents)
            {
                return false;
            }

            string expectedValue = valueText ?? string.Empty;
            string actualValue = condition.Value?.ToDisplayString() ?? string.Empty;
            StringComparison stringComparison = ignoreCase
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;
            return string.Equals(actualValue, expectedValue, stringComparison);
        }

        public static string GetConditionValueText(SearchCondition condition)
        {
            return condition?.Value?.ToDisplayString() ?? string.Empty;
        }

        public static bool GetIgnoreCase(SearchCondition condition)
        {
            return HasOption(condition, SearchConditionOptions.IgnoreDisplayStringValueCase);
        }

        public static bool GetIgnoreAccents(SearchCondition condition)
        {
            return HasOption(condition, SearchConditionOptions.IgnoreDisplayStringValueAccents);
        }

        public static bool GetUseAnyCategory(SearchCondition condition)
        {
            return HasOption(condition, SearchConditionOptions.IgnoreCategoryDisplayName);
        }

        public static string FormatConditionSummary(SearchCondition condition)
        {
            if (condition == null)
            {
                return string.Empty;
            }

            return GetCategoryDisplayName(condition)
                + " / "
                + GetPropertyDisplayName(condition)
                + " | "
                + GetComparisonDisplayName(condition.Comparison)
                + " | «"
                + GetConditionValueText(condition)
                + "»";
        }

        private static SearchCondition ApplyCategoryAnyModeIfNeeded(
            SearchCondition condition,
            string categoryDisplay)
        {
            if (condition == null || !PropertyCategoryAmbiguity.IsAmbiguous(categoryDisplay))
            {
                return condition;
            }

            if (HasOption(condition, SearchConditionOptions.IgnoreCategoryDisplayName))
            {
                return condition;
            }

            return new SearchCondition(
                condition.CategoryCombinedName,
                condition.PropertyCombinedName,
                condition.Options | SearchConditionOptions.IgnoreCategoryDisplayName,
                condition.Comparison,
                condition.Value);
        }

        private static bool HasOption(SearchCondition condition, SearchConditionOptions option)
        {
            return condition != null && (condition.Options & option) != SearchConditionOptions.None;
        }

        public static string GetCategoryDisplayName(SearchCondition condition)
        {
            return condition?.CategoryCombinedName?.DisplayName ?? string.Empty;
        }

        public static string GetPropertyDisplayName(SearchCondition condition)
        {
            return condition?.PropertyCombinedName?.DisplayName ?? string.Empty;
        }

        private static bool HasStartGroup(SearchCondition condition)
        {
            return (condition.Options & SearchConditionOptions.StartGroup) != SearchConditionOptions.None;
        }

        private static SearchCondition StripStartGroup(SearchCondition condition)
        {
            if (!HasStartGroup(condition))
            {
                return condition;
            }

            SearchConditionOptions options = condition.Options & ~SearchConditionOptions.StartGroup;
            return new SearchCondition(
                condition.CategoryCombinedName,
                condition.PropertyCombinedName,
                options,
                condition.Comparison,
                condition.Value);
        }

        private static SearchCondition EnsureStartGroup(SearchCondition condition)
        {
            if (HasStartGroup(condition))
            {
                return condition;
            }

            return new SearchCondition(
                condition.CategoryCombinedName,
                condition.PropertyCombinedName,
                condition.Options | SearchConditionOptions.StartGroup,
                condition.Comparison,
                condition.Value);
        }

        private static SearchCondition ApplyOptions(SearchCondition condition, SearchConditionOptions options)
        {
            SearchCondition result = condition;
            if ((options & SearchConditionOptions.IgnoreDisplayStringValueCase) != SearchConditionOptions.None)
            {
                result = result.IgnoreStringValueCase();
            }

            if ((options & SearchConditionOptions.IgnoreDisplayStringValueAccents) != SearchConditionOptions.None)
            {
                result = result.IgnoreStringValueAccents();
            }

            return result;
        }

        private static SearchCondition ApplyComparison(
            SearchCondition condition,
            SearchConditionComparison comparison,
            VariantData value)
        {
            switch (comparison)
            {
                case SearchConditionComparison.Equal:
                    return condition.EqualValue(value);
                case SearchConditionComparison.DisplayStringContains:
                    return condition.DisplayStringContains(value.ToDisplayString());
                case SearchConditionComparison.DisplayStringWildcard:
                    return condition.DisplayStringWildcard(value.ToDisplayString());
                default:
                    return new SearchCondition(
                        condition.CategoryCombinedName,
                        condition.PropertyCombinedName,
                        condition.Options,
                        comparison,
                        value);
            }
        }
    }
}
