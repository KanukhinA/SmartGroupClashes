using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Navisworks.Api;
using Autodesk.Navisworks.Api.Clash;

namespace SmartNavisTools
{
    /// <summary>
    /// Вычисляет измерения для пересечения: этаж, модель и раздел.
    /// Кэширует тяжёлые обращения к ModelItem внутри одного прохода построения отчёта.
    /// </summary>
    internal sealed class ClashDimensionResolver
    {
        private readonly Dictionary<ModelItem, string> _modelNameCache =
            new Dictionary<ModelItem, string>();

        private readonly Dictionary<Guid, string> _modelNameByGuid =
            new Dictionary<Guid, string>();

        private readonly Dictionary<ModelItem, ElementIdentity> _identityCache =
            new Dictionary<ModelItem, ElementIdentity>();

        private readonly Dictionary<Guid, ElementIdentity> _identityByGuid =
            new Dictionary<Guid, ElementIdentity>();

        private readonly Dictionary<string, string> _mappedDisciplineCache =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        private readonly Dictionary<string, string> _propertyValueCache =
            new Dictionary<string, string>(StringComparer.Ordinal);

        private GridSystem _cachedActiveGridSystem;
        private bool _activeGridSystemResolved;

        /// <summary>
        /// Описание правила маппинга для раздела по имени файла модели.
        /// </summary>
        internal sealed class ModelFileDisciplineMapping
        {
            public string Key { get; set; }

            public string[] Values { get; set; }
        }

        /// <summary>
        /// Стратегия получения этажа.
        /// </summary>
        internal enum FloorSource
        {
            Grid,
            Property
        }

        /// <summary>
        /// Стратегия получения раздела.
        /// </summary>
        internal enum DisciplineSource
        {
            ModelFile,
            Property
        }

        /// <summary>
        /// Идентификатор и имя элемента, участвующего в пересечении.
        /// </summary>
        internal sealed class ElementIdentity
        {
            public string Id { get; set; }

            public string Name { get; set; }
        }

        /// <summary>
        /// Результат извлечения измерений для одного пересечения.
        /// </summary>
        internal sealed class Dimensions
        {
            public string FloorName { get; set; }

            public double FloorOrder { get; set; }

            public string ModelName { get; set; }

            public string DisciplineName { get; set; }

            public string ModelDiscipline { get; set; }

            public string GridAxes { get; set; }
        }

        /// <summary>
        /// Вычисляет все измерения для переданного результата коллизии.
        /// </summary>
        public Dimensions Resolve(
            ClashResult result,
            FloorSource floorSource,
            string floorPropertyName,
            DisciplineSource disciplineSource,
            string disciplinePropertyName,
            IReadOnlyList<ModelFileDisciplineMapping> modelFileDisciplineMappings)
        {
            var dimensions = new Dimensions
            {
                FloorName = "Без уровня",
                FloorOrder = double.MaxValue,
                ModelName = ResolveModelName(result),
                DisciplineName = "Без раздела",
                ModelDiscipline = "Без раздела",
                GridAxes = "Нет пересечения осей"
            };

            if (result == null)
            {
                return dimensions;
            }

            ResolveFloor(result, floorSource, floorPropertyName, dimensions);
            ResolveGridAxes(result, dimensions);
            ResolveDiscipline(
                result,
                disciplineSource,
                disciplinePropertyName,
                modelFileDisciplineMappings,
                dimensions);
            return dimensions;
        }

        /// <summary>
        /// Возвращает русское название статуса пересечения для отчёта и UI.
        /// </summary>
        public static string ResolveStatus(ClashResultStatus status)
        {
            switch (status)
            {
                case ClashResultStatus.New:
                    return "Новый";
                case ClashResultStatus.Active:
                    return "Активный";
                case ClashResultStatus.Reviewed:
                    return "Проверенный";
                case ClashResultStatus.Approved:
                    return "Утверждённый";
                case ClashResultStatus.Resolved:
                    return "Исправленный";
                default:
                    return "Не указано";
            }
        }

        /// <summary>
        /// Вычисляет этаж пересечения: по активной сетке (как колонка Level в Clash Detective)
        /// или по свойству элемента модели.
        /// </summary>
        private void ResolveFloor(
            ClashResult result,
            FloorSource floorSource,
            string floorPropertyName,
            Dimensions dimensions)
        {
            try
            {
                // Режим «по сетке» = то же поле Level, что показывает Clash Detective в таблице.
                if (floorSource == FloorSource.Grid)
                {
                    GridSystem activeSystem = GetActiveGridSystem();
                    if (activeSystem != null)
                    {
                        GridIntersection intersection = activeSystem.ClosestIntersection(result.Center);
                        ApplyGridIntersection(intersection, dimensions, applyFloor: true, applyAxes: true);
                    }

                    return;
                }

                // Режим «по свойству»: берём уровень из свойств элементов коллизии.
                string propertyFloor = TryResolveFloorFromProperties(result, floorPropertyName);
                if (!string.IsNullOrWhiteSpace(propertyFloor))
                {
                    dimensions.FloorName = propertyFloor.Trim();
                    double parsedOrder = TryParseFloorOrder(propertyFloor);
                    if (!double.IsNaN(parsedOrder))
                    {
                        dimensions.FloorOrder = parsedOrder;
                    }
                }
            }
            catch
            {
                // Игнорируем единичные ошибки API и оставляем fallback-значения.
            }
        }

        /// <summary>
        /// Определяет пересечение осей по активной сетке, если оси ещё не заполнены.
        /// </summary>
        private void ResolveGridAxes(ClashResult result, Dimensions dimensions)
        {
            if (dimensions == null || result == null)
            {
                return;
            }

            if (!string.IsNullOrWhiteSpace(dimensions.GridAxes)
                && !string.Equals(dimensions.GridAxes, "Нет пересечения осей", StringComparison.Ordinal))
            {
                return;
            }

            try
            {
                GridSystem activeSystem = GetActiveGridSystem();
                if (activeSystem == null)
                {
                    return;
                }

                GridIntersection intersection = activeSystem.ClosestIntersection(result.Center);
                ApplyGridIntersection(intersection, dimensions, applyFloor: false, applyAxes: true);
            }
            catch
            {
                // Оставляем значение по умолчанию.
            }
        }

        /// <summary>
        /// Записывает этаж и/или оси из одного ClosestIntersection без повторного запроса к сетке.
        /// </summary>
        private static void ApplyGridIntersection(
            GridIntersection intersection,
            Dimensions dimensions,
            bool applyFloor,
            bool applyAxes)
        {
            if (intersection == null || dimensions == null)
            {
                return;
            }

            if (applyFloor && intersection.Level != null)
            {
                string gridLevelName = intersection.Level.DisplayName;
                dimensions.FloorName = string.IsNullOrWhiteSpace(gridLevelName)
                    ? "Уровень без имени"
                    : gridLevelName.Trim();
                dimensions.FloorOrder = intersection.Level.Elevation;
            }

            if (applyAxes)
            {
                dimensions.GridAxes = FormatGridAxes(intersection);
            }
        }

        /// <summary>
        /// Возвращает активную сетку документа (один раз на проход построения отчёта).
        /// </summary>
        private GridSystem GetActiveGridSystem()
        {
            if (_activeGridSystemResolved)
            {
                return _cachedActiveGridSystem;
            }

            try
            {
                _cachedActiveGridSystem = Application.MainDocument?.Grids?.ActiveSystem;
            }
            catch
            {
                _cachedActiveGridSystem = null;
            }

            _activeGridSystemResolved = true;
            return _cachedActiveGridSystem;
        }

        /// <summary>
        /// Формирует подпись осей без суффикса уровня (как в группировке GroupClashes).
        /// </summary>
        private static string FormatGridAxes(GridIntersection intersection)
        {
            string displayName = intersection?.DisplayName ?? string.Empty;
            if (string.IsNullOrWhiteSpace(displayName))
            {
                return "Нет пересечения осей";
            }

            int separatorIndex = displayName.IndexOf(':');
            if (separatorIndex >= 0)
            {
                string axes = displayName.Substring(0, separatorIndex).Trim();
                return string.IsNullOrWhiteSpace(axes) ? "Нет пересечения осей" : axes;
            }

            return displayName.Trim();
        }

        /// <summary>
        /// Пытается определить уровень по свойствам обоих участников коллизии.
        /// </summary>
        private string TryResolveFloorFromProperties(ClashResult result, string floorPropertyName)
        {
            if (result == null)
            {
                return string.Empty;
            }

            string[] candidates = BuildPropertyCandidates(
                floorPropertyName,
                new[]
                {
                    "Уровень",
                    "Level",
                    "Этаж",
                    "Story",
                    "Building Story",
                    "Базовый уровень",
                    "Base Constraint",
                    "Опорный уровень",
                    "Reference Level",
                    "Floor"
                });

            string value1 = TryGetPropertyByDisplayNames(result.CompositeItem1, candidates);
            if (!string.IsNullOrWhiteSpace(value1))
            {
                return value1;
            }

            return TryGetPropertyByDisplayNames(result.CompositeItem2, candidates);
        }

        /// <summary>
        /// Собирает список имён свойств для поиска (пользовательское + запасные).
        /// </summary>
        private static string[] BuildPropertyCandidates(string preferredName, IEnumerable<string> fallbacks)
        {
            var names = new List<string>();
            if (!string.IsNullOrWhiteSpace(preferredName))
            {
                names.Add(preferredName.Trim());
            }

            if (fallbacks != null)
            {
                foreach (string fallback in fallbacks)
                {
                    if (!string.IsNullOrWhiteSpace(fallback))
                    {
                        names.Add(fallback.Trim());
                    }
                }
            }

            return names
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        /// <summary>
        /// Пытается извлечь числовой порядок уровня из строки (например, "Этаж 3").
        /// </summary>
        private static double TryParseFloorOrder(string floorName)
        {
            if (string.IsNullOrWhiteSpace(floorName))
            {
                return double.NaN;
            }

            try
            {
                string cleaned = new string(floorName
                    .Where(ch => char.IsDigit(ch) || ch == '.' || ch == ',' || ch == '-' || ch == '+')
                    .ToArray());
                if (string.IsNullOrWhiteSpace(cleaned))
                {
                    return double.NaN;
                }

                cleaned = cleaned.Replace(',', '.');
                if (double.TryParse(cleaned, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double parsed))
                {
                    return parsed;
                }
            }
            catch
            {
                // Ошибки парсинга не критичны, оставляем порядок по умолчанию.
            }

            return double.NaN;
        }

        /// <summary>
        /// Вычисляет раздел модели графика (сторона 1) и смежный раздел (сторона 2, сегмент полоски).
        /// </summary>
        private void ResolveDiscipline(
            ClashResult result,
            DisciplineSource disciplineSource,
            string disciplinePropertyName,
            IReadOnlyList<ModelFileDisciplineMapping> modelFileDisciplineMappings,
            Dimensions dimensions)
        {
            try
            {
                if (disciplineSource == DisciplineSource.ModelFile)
                {
                    dimensions.ModelDiscipline = ResolveMappedDisciplineName(
                        dimensions.ModelName,
                        modelFileDisciplineMappings);

                    string partnerModelName = ResolveModelName(result.CompositeItem2);
                    dimensions.DisciplineName = ResolveMappedDisciplineName(
                        partnerModelName,
                        modelFileDisciplineMappings);
                    return;
                }

                string[] disciplineCandidates = BuildPropertyCandidates(
                    disciplinePropertyName,
                    new[] { "Раздел", "Discipline" });

                string modelValue = TryGetPropertyByDisplayNames(
                    result.CompositeItem1,
                    disciplineCandidates);
                if (!string.IsNullOrWhiteSpace(modelValue))
                {
                    dimensions.ModelDiscipline = modelValue.Trim();
                }

                string value = TryGetPropertyByDisplayNames(
                    result.CompositeItem2,
                    disciplineCandidates);
                if (!string.IsNullOrWhiteSpace(value))
                {
                    dimensions.DisciplineName = value.Trim();
                }
            }
            catch
            {
                // Игнорируем единичные ошибки API и оставляем fallback-значения.
            }
        }

        /// <summary>
        /// Применяет правила маппинга и возвращает итоговое имя раздела (с кэшем по имени модели).
        /// </summary>
        private string ResolveMappedDisciplineName(
            string modelName,
            IReadOnlyList<ModelFileDisciplineMapping> modelFileDisciplineMappings)
        {
            if (string.IsNullOrWhiteSpace(modelName))
            {
                return "Без раздела";
            }

            if (_mappedDisciplineCache.TryGetValue(modelName, out string cached))
            {
                return cached;
            }

            string resolved = ResolveMappedDisciplineNameUncached(modelName, modelFileDisciplineMappings);
            _mappedDisciplineCache[modelName] = resolved;
            return resolved;
        }

        /// <summary>
        /// Применяет правила маппинга без обращения к кэшу.
        /// </summary>
        private static string ResolveMappedDisciplineNameUncached(
            string modelName,
            IReadOnlyList<ModelFileDisciplineMapping> modelFileDisciplineMappings)
        {
            if (modelFileDisciplineMappings == null || modelFileDisciplineMappings.Count == 0)
            {
                return modelName;
            }

            foreach (ModelFileDisciplineMapping mapping in modelFileDisciplineMappings)
            {
                if (mapping == null)
                {
                    continue;
                }

                if (string.IsNullOrWhiteSpace(mapping.Key))
                {
                    continue;
                }

                string[] patterns = mapping.Values ?? Array.Empty<string>();
                foreach (string pattern in patterns)
                {
                    if (string.IsNullOrWhiteSpace(pattern))
                    {
                        continue;
                    }

                    // Матч по подстроке удобнее для имен файлов вида "Arch-OV_2026.nwd".
                    if (modelName.IndexOf(pattern, StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        return mapping.Key.Trim();
                    }
                }
            }

            return modelName;
        }

        /// <summary>
        /// Возвращает имя корневой модели для элемента (для записи в отчёт).
        /// </summary>
        public string ResolveModelNamePublic(ModelItem item)
        {
            return ResolveModelName(item);
        }

        /// <summary>
        /// Возвращает имя корневой модели для пересечения.
        /// </summary>
        private string ResolveModelName(ClashResult result)
        {
            if (result == null)
            {
                return "Без модели";
            }

            return ResolveModelName(result.CompositeItem1);
        }

        /// <summary>
        /// Возвращает имя корневой модели для элемента модели (с кэшем).
        /// </summary>
        private string ResolveModelName(ModelItem item)
        {
            if (item == null)
            {
                return "Без модели";
            }

            Guid itemGuid = TryGetInstanceGuid(item);
            if (itemGuid != Guid.Empty
                && _modelNameByGuid.TryGetValue(itemGuid, out string cachedByGuid))
            {
                return cachedByGuid;
            }

            try
            {
                if (_modelNameCache.TryGetValue(item, out string cached))
                {
                    return cached;
                }
            }
            catch
            {
                // ModelItem как ключ недоступен — считаем без кэша по ссылке.
            }

            string name = "Без модели";
            try
            {
                ModelItem ancestor = GetFileAncestor(item);
                string displayName = ancestor?.DisplayName;
                name = string.IsNullOrWhiteSpace(displayName) ? "Без модели" : displayName;
            }
            catch
            {
                name = "Без модели";
            }

            try
            {
                _modelNameCache[item] = name;
            }
            catch
            {
                // Игнорируем сбой записи в кэш.
            }

            if (itemGuid != Guid.Empty)
            {
                _modelNameByGuid[itemGuid] = name;
            }

            return name;
        }

        /// <summary>
        /// Безопасно читает InstanceGuid элемента модели.
        /// </summary>
        private static Guid TryGetInstanceGuid(ModelItem item)
        {
            if (item == null)
            {
                return Guid.Empty;
            }

            try
            {
                return item.InstanceGuid;
            }
            catch
            {
                return Guid.Empty;
            }
        }

        /// <summary>
        /// Поднимается по дереву до ближайшего узла с привязкой к файлу модели.
        /// </summary>
        private static ModelItem GetFileAncestor(ModelItem item)
        {
            ModelItem originalItem = item;
            while (item != null)
            {
                ModelItem parent;
                try
                {
                    parent = item.Parent;
                }
                catch
                {
                    break;
                }

                if (parent == null)
                {
                    break;
                }

                item = parent;
                if (item.HasModel)
                {
                    return item;
                }
            }

            return originalItem;
        }

        /// <summary>
        /// Извлекает числовой Id элемента (не GUID) и отображаемое имя для детализации отчёта.
        /// Результат кэшируется на время построения отчёта.
        /// </summary>
        public ElementIdentity ResolveElementIdentity(ModelItem item)
        {
            if (item == null)
            {
                return new ElementIdentity { Id = string.Empty, Name = string.Empty };
            }

            Guid itemGuid = TryGetInstanceGuid(item);
            if (itemGuid != Guid.Empty
                && _identityByGuid.TryGetValue(itemGuid, out ElementIdentity cachedByGuid)
                && cachedByGuid != null)
            {
                return cachedByGuid;
            }

            try
            {
                if (_identityCache.TryGetValue(item, out ElementIdentity cached) && cached != null)
                {
                    return cached;
                }
            }
            catch
            {
                // Продолжаем без кэша.
            }

            var identity = ResolveElementIdentityUncached(item);

            try
            {
                _identityCache[item] = identity;
            }
            catch
            {
                // Игнорируем сбой записи в кэш.
            }

            if (itemGuid != Guid.Empty)
            {
                _identityByGuid[itemGuid] = identity;
            }

            return identity;
        }

        /// <summary>
        /// Извлекает Id/имя элемента без кэша.
        /// </summary>
        private ElementIdentity ResolveElementIdentityUncached(ModelItem item)
        {
            var identity = new ElementIdentity
            {
                Id = string.Empty,
                Name = string.Empty
            };

            if (item == null)
            {
                return identity;
            }

            try
            {
                identity.Name = item.DisplayName ?? string.Empty;
            }
            catch
            {
                identity.Name = string.Empty;
            }

            try
            {
                // Сначала узкий поиск по известным display-именам без ограничения категории.
                string explicitId = TryGetPropertyByDisplayNames(
                    item,
                    new[]
                    {
                        "Element Id",
                        "Element ID",
                        "Id элемента",
                        "Идентификатор элемента"
                    });
                if (LooksLikeNumericElementId(explicitId))
                {
                    identity.Id = explicitId.Trim();
                    return identity;
                }

                string numericId = TryFindNumericElementId(item);
                if (!string.IsNullOrWhiteSpace(numericId))
                {
                    identity.Id = numericId;
                }
            }
            catch
            {
                // Оставляем Id пустым, GUID в отчёт не пишем.
            }

            return identity;
        }

        /// <summary>
        /// Ищет числовой Element Id у узла и предков по display/internal именам свойств.
        /// </summary>
        private static string TryFindNumericElementId(ModelItem item)
        {
            if (item == null)
            {
                return string.Empty;
            }

            string[] idDisplayNames =
            {
                "Element Id",
                "Element ID",
                "Id элемента",
                "Идентификатор элемента",
                "Id",
                "ID"
            };

            string[] idInternalNames =
            {
                "lcrevitid",
                "lcldrevit_element_id",
                "elementid",
                "id"
            };

            ModelItem current = item;
            while (current != null)
            {
                try
                {
                    foreach (PropertyCategory category in current.PropertyCategories)
                    {
                        string categoryDisplay = category.DisplayName ?? string.Empty;
                        string categoryInternal = string.Empty;
                        try
                        {
                            categoryInternal = category.Name ?? string.Empty;
                        }
                        catch
                        {
                            categoryInternal = string.Empty;
                        }

                        if (!IsElementIdentityCategory(categoryDisplay, categoryInternal))
                        {
                            continue;
                        }

                        foreach (DataProperty property in category.Properties)
                        {
                            string propertyDisplay = property.DisplayName ?? string.Empty;
                            string propertyInternal = string.Empty;
                            try
                            {
                                propertyInternal = property.Name ?? string.Empty;
                            }
                            catch
                            {
                                propertyInternal = string.Empty;
                            }

                            bool nameMatches =
                                idDisplayNames.Any(n => string.Equals(propertyDisplay, n, StringComparison.OrdinalIgnoreCase))
                                || idInternalNames.Any(n =>
                                    propertyInternal.IndexOf(n, StringComparison.OrdinalIgnoreCase) >= 0);

                            if (!nameMatches)
                            {
                                continue;
                            }

                            string candidate = ConvertPropertyValueToIdString(property);
                            if (LooksLikeNumericElementId(candidate))
                            {
                                return candidate.Trim();
                            }
                        }
                    }
                }
                catch
                {
                    // Переходим к родителю.
                }

                try
                {
                    current = current.Parent;
                }
                catch
                {
                    break;
                }
            }

            return string.Empty;
        }

        /// <summary>
        /// Преобразует значение свойства в строку Id с приоритетом целочисленных типов.
        /// </summary>
        private static string ConvertPropertyValueToIdString(DataProperty property)
        {
            if (property == null)
            {
                return string.Empty;
            }

            try
            {
                VariantData value = property.Value;
                try
                {
                    if (value.IsInt32)
                    {
                        return value.ToInt32().ToString(System.Globalization.CultureInfo.InvariantCulture);
                    }
                }
                catch
                {
                    // Продолжаем через ToDisplayString.
                }

                return (value.ToDisplayString() ?? string.Empty).Trim();
            }
            catch
            {
                return string.Empty;
            }
        }

        /// <summary>
        /// Проверяет, что строка похожа на обычный числовой Id элемента, а не на GUID.
        /// </summary>
        private static bool LooksLikeNumericElementId(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            string trimmed = value.Trim();
            if (trimmed.IndexOf('-') >= 0 || trimmed.IndexOf('{') >= 0)
            {
                return false;
            }

            if (trimmed.Length >= 32 && trimmed.Any(ch => char.IsLetter(ch)))
            {
                return false;
            }

            return long.TryParse(
                trimmed,
                System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.InvariantCulture,
                out _);
        }

        /// <summary>
        /// Проверяет, что категория относится к идентификатору элемента.
        /// </summary>
        private static bool IsElementIdentityCategory(string categoryDisplayName, string categoryInternalName)
        {
            if (!string.IsNullOrWhiteSpace(categoryDisplayName))
            {
                if (string.Equals(categoryDisplayName, "Element", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(categoryDisplayName, "Элемент", StringComparison.OrdinalIgnoreCase)
                    || categoryDisplayName.IndexOf("LcRevitData_Element", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }

                // Узкие категории Revit Element, без слишком широкого "любой Element*".
                if (string.Equals(categoryDisplayName, "Revit Element", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(categoryDisplayName, "Элемент Revit", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            if (!string.IsNullOrWhiteSpace(categoryInternalName))
            {
                if (categoryInternalName.IndexOf("LcRevitData_Element", StringComparison.OrdinalIgnoreCase) >= 0
                    || string.Equals(categoryInternalName, "LcRevitData_Element", StringComparison.OrdinalIgnoreCase)
                    || categoryInternalName.IndexOf("lcrevitdata_element", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Ищет значение свойства по отображаемым именам в текущем узле и предках (с кэшем).
        /// </summary>
        private string TryGetPropertyByDisplayNames(ModelItem item, IEnumerable<string> displayNames)
        {
            if (item == null)
            {
                return string.Empty;
            }

            string[] candidates = displayNames
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Select(name => name.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            if (candidates.Length == 0)
            {
                return string.Empty;
            }

            string cacheKey = BuildPropertyCacheKey(item, candidates);
            if (!string.IsNullOrEmpty(cacheKey)
                && _propertyValueCache.TryGetValue(cacheKey, out string cachedValue))
            {
                return cachedValue;
            }

            string value = TryGetPropertyByDisplayNamesUncached(item, candidates);
            if (!string.IsNullOrEmpty(cacheKey))
            {
                _propertyValueCache[cacheKey] = value ?? string.Empty;
            }

            return value ?? string.Empty;
        }

        /// <summary>
        /// Ключ кэша свойств: InstanceGuid элемента + набор имён свойств.
        /// </summary>
        private static string BuildPropertyCacheKey(ModelItem item, string[] candidates)
        {
            try
            {
                Guid instanceGuid = item.InstanceGuid;
                if (instanceGuid == Guid.Empty)
                {
                    return string.Empty;
                }

                return instanceGuid.ToString("N") + "|" + string.Join("|", candidates);
            }
            catch
            {
                return string.Empty;
            }
        }

        /// <summary>
        /// Поиск свойства без кэша.
        /// </summary>
        private static string TryGetPropertyByDisplayNamesUncached(ModelItem item, string[] candidates)
        {
            var candidateSet = new HashSet<string>(candidates, StringComparer.OrdinalIgnoreCase);
            ModelItem current = item;
            while (current != null)
            {
                try
                {
                    foreach (PropertyCategory category in current.PropertyCategories)
                    {
                        foreach (DataProperty property in category.Properties)
                        {
                            if (!candidateSet.Contains(property.DisplayName ?? string.Empty))
                            {
                                continue;
                            }

                            string value = property.Value.ToDisplayString();
                            if (!string.IsNullOrWhiteSpace(value))
                            {
                                return value.Trim();
                            }
                        }
                    }
                }
                catch
                {
                    // Переходим к родителю, если категория недоступна у текущего узла.
                }

                try
                {
                    current = current.Parent;
                }
                catch
                {
                    break;
                }
            }

            return string.Empty;
        }
    }
}
