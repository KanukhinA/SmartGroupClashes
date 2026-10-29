using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Navisworks.Api;
using Autodesk.Navisworks.Api.Clash;

namespace SmartGroupClashes
{
    /// <summary>Читает исходные элементы и утверждает отдельные коллизии без изменения групп.</summary>
    internal static class PipeClashExclusion
    {
        internal sealed class Summary
        {
            internal int Examined { get; set; }
            internal int Approved { get; set; }
            internal int AlreadyClosed { get; set; }
            internal int MissingData { get; set; }
            internal int OutsideRule => Examined - Approved - AlreadyClosed - MissingData;
            private readonly Dictionary<string, int> _skipReasons = new Dictionary<string, int>();

            internal void Skip(string reason, bool missingData = false)
            {
                if (missingData) MissingData++;
                int count;
                _skipReasons.TryGetValue(reason, out count);
                _skipReasons[reason] = count + 1;
            }

            internal string DescribeSkips()
            {
                return string.Join("\n", _skipReasons.OrderByDescending(pair => pair.Value)
                    .Select(pair => pair.Key + ": " + pair.Value));
            }
        }

        private sealed class PipeReadResult
        {
            internal ModelItem Item;
            internal PipeExclusionCandidate Candidate;
            internal bool MissingData;
            internal string SkipReason;
        }

        internal static Summary Apply(Document document, IEnumerable<ClashTest> tests, bool includeSinglePipe)
        {
            double scaleToMm = UnitConversion.ScaleFactor(document.Units, Units.Millimeters);
            if (!PipeExclusionRule.IsPositiveFinite(scaleToMm))
                throw new InvalidOperationException("Не удалось определить единицы документа.");

            var summary = new Summary();
            var cache = new Dictionary<ModelItem, PipeReadResult>();
            PipeNeighborhoodIndex<ModelItem> neighborhood = null;
            var matches = new List<ClashResult>();
            var seen = new HashSet<Guid>();
            foreach (ClashResult result in tests.SelectMany(test => Walk(test)))
            {
                if (result.Guid != Guid.Empty && !seen.Add(result.Guid)) continue;
                summary.Examined++;
                if (result.Status == ClashResultStatus.Approved || result.Status == ClashResultStatus.Resolved)
                {
                    summary.AlreadyClosed++;
                    continue;
                }

                if (result.Item1 == null || result.Item2 == null)
                {
                    summary.Skip("Нет элемента коллизии", true);
                    continue;
                }
                PipeReadResult a = ReadPipe(result.Item1, scaleToMm, cache);
                PipeReadResult b = ReadPipe(result.Item2, scaleToMm, cache);
                if (result.Item1.Equals(result.Item2) || (a.Item != null && a.Item.Equals(b.Item)))
                {
                    summary.Skip("Обе стороны ссылаются на одну трубу");
                    continue;
                }
                bool hasSmallPipe = PipeExclusionRule.IsSmallPipe(a.Candidate)
                    || PipeExclusionRule.IsSmallPipe(b.Candidate);
                bool aIsIsolated = false;
                bool bIsIsolated = false;
                if (includeSinglePipe && hasSmallPipe)
                {
                    // Индекс строится один раз по всей модели, без фильтра по диаметру и разделу ВИС.
                    if (neighborhood == null) neighborhood = BuildNeighborhood(document, scaleToMm);
                    if (!neighborhood.IsComplete)
                    {
                        summary.Skip("Не удалось определить положение всех труб модели", true);
                        continue;
                    }
                    if ((PipeExclusionRule.IsSmallPipe(a.Candidate) && !neighborhood.Contains(a.Item))
                        || (PipeExclusionRule.IsSmallPipe(b.Candidate) && !neighborhood.Contains(b.Item)))
                    {
                        summary.Skip("Труба не найдена в общем поиске труб модели", true);
                        continue;
                    }
                    aIsIsolated = PipeExclusionRule.IsSmallPipe(a.Candidate)
                        && neighborhood.IsIsolated(a.Item, a.Candidate);
                    bIsIsolated = PipeExclusionRule.IsSmallPipe(b.Candidate)
                        && neighborhood.IsIsolated(b.Item, b.Candidate);
                }

                // Неполные свойства второй стороны не мешают правилу: она может быть любым элементом.
                if (PipeExclusionRule.Matches(a.Candidate, b.Candidate, includeSinglePipe, aIsIsolated, bIsIsolated))
                {
                    matches.Add(result);
                    continue;
                }
                if (includeSinglePipe && hasSmallPipe)
                {
                    summary.Skip("Рядом с трубой есть другая труба (радиус 200 мм)");
                    continue;
                }
                if (a.MissingData || b.MissingData)
                {
                    summary.Skip(a.MissingData ? a.SkipReason : b.SkipReason, true);
                    continue;
                }
                if (a.Candidate == null || b.Candidate == null)
                    summary.Skip(a.Candidate == null ? a.SkipReason : b.SkipReason);
                else if (a.Candidate.DiameterMm >= PipeExclusionRule.DiameterLimitMm
                    || b.Candidate.DiameterMm >= PipeExclusionRule.DiameterLimitMm)
                    summary.Skip(includeSinglePipe ? "Нет трубы ВИС диаметром меньше 50 мм"
                        : "Диаметр хотя бы одной трубы не меньше 50 мм");
                else summary.Skip("Расстояние между центрами не меньше 200 мм");
            }

            if (matches.Count == 0) return summary;
            DocumentClashTests data = document.GetClash().TestsData;
            using (Transaction transaction = document.BeginTransaction("SmartGroupClashes — исключение труб ВИС"))
            {
                foreach (ClashResult result in matches)
                {
                    // Поле «Утвердил» сохраняется: правило меняет только статус.
#if Version2026
                    data.TestsEditResultStatus(result, ClashResultStatus.Approved, result.ApprovedBy);
#else
                    string approvedBy = result.ApprovedBy;
                    data.TestsEditResultStatus(result, ClashResultStatus.Approved);
                    data.TestsEditResultApprovedBy(result, approvedBy);
#endif
                }
                transaction.Commit();
            }
            summary.Approved = matches.Count;
            return summary;
        }

        private static IEnumerable<ClashResult> Walk(GroupItem group)
        {
            foreach (SavedItem child in group.Children)
            {
                var result = child as ClashResult;
                if (result != null) yield return result;
                else
                {
                    var subgroup = child as GroupItem;
                    if (subgroup != null)
                        foreach (ClashResult nested in Walk(subgroup)) yield return nested;
                }
            }
        }

        private static PipeReadResult ReadPipe(ModelItem source, double scaleToMm,
            Dictionary<ModelItem, PipeReadResult> cache)
        {
            if (source == null) return Missing("Нет элемента коллизии");
            PipeReadResult cached;
            if (cache.TryGetValue(source, out cached)) return cached;

            PipeReadResult value;
            try
            {
                value = ReadPipeCore(source, scaleToMm);
            }
            catch (Exception ex) when (ex is InvalidOperationException || ex is ArgumentException
                || ex is NotSupportedException || ex is System.Runtime.InteropServices.COMException)
            {
                value = Missing("Ошибка чтения свойств или геометрии");
            }
            cache[source] = value;
            return value;
        }

        private static PipeReadResult ReadPipeCore(ModelItem source, double scaleToMm)
        {
            // Коллизия может ссылаться на геометрию внутри составной трубы.
            // Останавливаемся на первом элементе с категорией, не подменяя его родительской моделью.
            for (ModelItem item = source; item != null; item = item.Parent)
            {
                DataProperty category = FindObjectProperty(item, "Категория", "Category");
                if (category == null) continue;
                if (!PipeExclusionRule.IsPipeCategory(ReadText(category.Value)))
                    return new PipeReadResult { SkipReason = "Участник коллизии не является трубой" };
                if (!HasVisCode(item))
                    return new PipeReadResult { SkipReason = "В свойствах «Элемент» не найден код ВИС" };

                DataProperty diameter = FindObjectProperty(item, "Диаметр", "Diameter");
                double diameterMm;
                if (diameter == null || !TryReadDiameter(diameter.Value, scaleToMm, out diameterMm))
                    return Missing("Диаметр не найден или его единицы не определены");

                PipeExclusionCandidate candidate = ReadCenter(item, scaleToMm);
                if (candidate == null) return Missing("Нет геометрии или корректных координат трубы");
                candidate.DiameterMm = diameterMm;
                return new PipeReadResult { Item = item, Candidate = candidate };
            }
            return Missing("Не найдено свойство «Объект → Категория»");
        }

        private static PipeNeighborhoodIndex<ModelItem> BuildNeighborhood(Document document, double scaleToMm)
        {
            var index = new PipeNeighborhoodIndex<ModelItem>();
            var search = new Search();
            search.Selection.SelectAll();
            search.Locations = SearchLocations.DescendantsAndSelf;
            search.PruneBelowMatch = false;
            // Проверяем наличие свойства, затем читаем его значение сами: поддерживаются и строки,
            // и NamedConstant. Никакие текущие выделения, видимость или выбранные тесты не используются.
            foreach (string category in new[] { "Объект", "Object", "Element" })
                foreach (string property in new[] { "Категория", "Category" })
                    search.SearchConditions.AddGroup(new[] { SearchCondition.HasPropertyByDisplayName(category, property) });

            foreach (ModelItem item in search.FindAll(document, true))
            {
                try
                {
                    DataProperty category = FindObjectProperty(item, "Категория", "Category");
                    if (category == null || !PipeExclusionRule.IsPipeCategory(ReadText(category.Value))) continue;
                    // Диаметр и код ВИС соседней трубы не нужны.
                    index.Add(item, ReadCenter(item, scaleToMm));
                }
                catch (Exception ex) when (ex is InvalidOperationException || ex is ArgumentException
                    || ex is NotSupportedException || ex is System.Runtime.InteropServices.COMException)
                {
                    index.MarkIncomplete();
                }
            }
            return index;
        }

        private static PipeExclusionCandidate ReadCenter(ModelItem item, double scaleToMm)
        {
            BoundingBox3D bounds = item.BoundingBox();
            if (bounds == null || bounds.IsEmpty) return null;
            Point3D center = bounds.Center;
            var candidate = new PipeExclusionCandidate
            {
                X = center.X * scaleToMm, Y = center.Y * scaleToMm, Z = center.Z * scaleToMm,
            };
            return PipeExclusionRule.HasValidCenter(candidate) ? candidate : null;
        }

        private static PipeReadResult Missing(string reason)
        {
            return new PipeReadResult { MissingData = true, SkipReason = reason };
        }

        private static DataProperty FindObjectProperty(ModelItem item, params string[] names)
        {
            foreach (PropertyCategory category in item.PropertyCategories)
            {
                if (!IsName(category.DisplayName, "Объект", "Object", "Element")) continue;
                foreach (DataProperty property in category.Properties)
                    if (IsName(property.DisplayName, names)) return property;
            }
            return null;
        }

        private static bool HasVisCode(ModelItem pipe)
        {
            // Код раздела может храниться в имени исходного файла на родителе трубы.
            for (ModelItem item = pipe; item != null; item = item.Parent)
            {
                foreach (PropertyCategory category in item.PropertyCategories)
                {
                    if (!IsName(category.DisplayName, "Элемент", "Item")) continue;
                    foreach (DataProperty property in category.Properties)
                        if (PipeExclusionRule.ContainsVisCode(ReadText(property.Value))) return true;
                }
            }
            return false;
        }

        private static bool TryReadDiameter(VariantData value, double scaleToMm, out double diameterMm)
        {
            // DoubleLength хранится в единицах документа; текст обязан содержать единицу.
            if (value.IsDoubleLength)
            {
                diameterMm = value.ToDoubleLength() * scaleToMm;
                return PipeExclusionRule.IsPositiveFinite(diameterMm);
            }
            return PipeExclusionRule.TryParseDiameterMm(ReadText(value), out diameterMm);
        }

        private static string ReadText(VariantData value)
        {
            if (value.IsDisplayString) return value.ToDisplayString();
            if (value.IsNamedConstant) return value.ToNamedConstant().DisplayName;
            if (value.IsIdentifierString) return value.ToIdentifierString();
            return string.Empty;
        }

        private static bool IsName(string actual, params string[] names)
        {
            return names.Any(name => string.Equals((actual ?? string.Empty).Trim(), name,
                StringComparison.OrdinalIgnoreCase));
        }
    }
}
