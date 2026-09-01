using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Navisworks.Api;

namespace SmartNavisTools
{
    /// <summary>
    /// Каталог категорий и свойств: собирается из поисковых наборов и метаданных документа без обхода модели.
    /// </summary>
    internal sealed class PropertyCatalog
    {
        private static readonly Dictionary<string, CatalogSnapshot> Cache =
            new Dictionary<string, CatalogSnapshot>(StringComparer.OrdinalIgnoreCase);

        private readonly Dictionary<string, SortedSet<string>> _propertiesByCategory =
            new Dictionary<string, SortedSet<string>>(StringComparer.OrdinalIgnoreCase);

        private readonly Dictionary<string, HashSet<string>> _internalNamesByCategoryDisplay =
            new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);

        private string _documentKey = string.Empty;
        private bool _isLoaded;

        public bool HasData => _propertiesByCategory.Count > 0;

        public IReadOnlyCollection<string> Categories =>
            _propertiesByCategory.Keys.OrderBy(name => name, StringComparer.OrdinalIgnoreCase).ToArray();

        public void BindDocument(Document document)
        {
            string key = GetDocumentKey(document);
            if (string.Equals(_documentKey, key, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            _documentKey = key;
            _propertiesByCategory.Clear();
            _internalNamesByCategoryDisplay.Clear();
            _isLoaded = false;

            if (string.IsNullOrEmpty(key))
            {
                return;
            }

            if (Cache.TryGetValue(key, out CatalogSnapshot snapshot))
            {
                RestoreSnapshot(snapshot);
            }
        }

        public void InvalidateCurrentDocument()
        {
            if (!string.IsNullOrEmpty(_documentKey))
            {
                Cache.Remove(_documentKey);
            }

            _propertiesByCategory.Clear();
            _isLoaded = false;
            PropertyCategoryAmbiguity.SetAmbiguousCategories(Array.Empty<string>());
        }

        public void EnsureLoaded(Document document)
        {
            BindDocument(document);
            if (document == null || document.IsClear || _isLoaded)
            {
                return;
            }

            CollectAllSources(document, clearExisting: false);
            _isLoaded = true;
            SaveSnapshot();
        }

        public void Refresh(Document document)
        {
            BindDocument(document);
            if (document == null || document.IsClear)
            {
                return;
            }

            CollectAllSources(document, clearExisting: true);
            _isLoaded = true;
            SaveSnapshot();
        }

        public IReadOnlyCollection<string> GetProperties(string categoryDisplayName)
        {
            if (string.IsNullOrWhiteSpace(categoryDisplayName)
                || !_propertiesByCategory.TryGetValue(categoryDisplayName, out SortedSet<string> properties))
            {
                return Array.Empty<string>();
            }

            return properties.ToArray();
        }

        public void EnsureCategory(string categoryDisplayName)
        {
            if (string.IsNullOrWhiteSpace(categoryDisplayName))
            {
                return;
            }

            if (!_propertiesByCategory.ContainsKey(categoryDisplayName))
            {
                _propertiesByCategory[categoryDisplayName] =
                    new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
            }
        }

        public void EnsureProperty(string categoryDisplayName, string propertyDisplayName)
        {
            EnsureCategory(categoryDisplayName);
            if (!string.IsNullOrWhiteSpace(propertyDisplayName))
            {
                _propertiesByCategory[categoryDisplayName].Add(propertyDisplayName);
            }
        }

        private void CollectAllSources(Document document, bool clearExisting)
        {
            if (clearExisting)
            {
                _propertiesByCategory.Clear();
                _internalNamesByCategoryDisplay.Clear();
            }

            CollectFromSearchSets(document.SelectionSets?.RootItem);
            CollectFromSearch(document.CurrentSearch);
            CollectFromDocumentInfo(document);
            RecomputeAmbiguousCategories();
        }

        private void CollectFromSearchSets(SavedItem root)
        {
            if (root == null)
            {
                return;
            }

            WalkSavedItem(root);
        }

        private void WalkSavedItem(SavedItem item)
        {
            if (item is SelectionSet selectionSet)
            {
                CollectFromSearch(selectionSet.Search);
            }

            if (!item.IsGroup)
            {
                return;
            }

            foreach (SavedItem child in ((GroupItem)item).Children)
            {
                WalkSavedItem(child);
            }
        }

        private void CollectFromSearch(Search search)
        {
            if (search == null || search.IsClear)
            {
                return;
            }

            foreach (SearchCondition condition in search.SearchConditions)
            {
                AddConditionProperty(condition);
            }
        }

        private void CollectFromDocumentInfo(Document document)
        {
            try
            {
                DocumentInfo documentInfo = document.DocumentInfo?.Value;
                if (documentInfo?.PropertyCategories == null)
                {
                    return;
                }

                foreach (InfoPropertyCategory category in documentInfo.PropertyCategories)
                {
                    if (category == null || string.IsNullOrWhiteSpace(category.DisplayName))
                    {
                        continue;
                    }

                    foreach (DataProperty property in category.Properties)
                    {
                        if (!string.IsNullOrWhiteSpace(property?.DisplayName))
                        {
                            EnsureProperty(category.DisplayName, property.DisplayName);
                        }
                    }
                }
            }
            catch
            {
                // Метаданные документа необязательны; основной источник — поисковые наборы.
            }
        }

        private void AddConditionProperty(SearchCondition condition)
        {
            if (condition == null)
            {
                return;
            }

            string category = condition.CategoryCombinedName?.DisplayName;
            string property = condition.PropertyCombinedName?.DisplayName;
            if (!string.IsNullOrWhiteSpace(category) && !string.IsNullOrWhiteSpace(property))
            {
                EnsureProperty(category, property);
            }

            TrackCategoryInternalName(condition);
        }

        private void TrackCategoryInternalName(SearchCondition condition)
        {
            string displayName = condition.CategoryCombinedName?.DisplayName;
            string internalName = condition.CategoryCombinedName?.Name;
            if (string.IsNullOrWhiteSpace(displayName) || string.IsNullOrWhiteSpace(internalName))
            {
                return;
            }

            if (!_internalNamesByCategoryDisplay.TryGetValue(displayName, out HashSet<string> internalNames))
            {
                internalNames = new HashSet<string>(StringComparer.Ordinal);
                _internalNamesByCategoryDisplay[displayName] = internalNames;
            }

            internalNames.Add(internalName);
        }

        private void RecomputeAmbiguousCategories()
        {
            var ambiguous = new List<string>();
            foreach (KeyValuePair<string, HashSet<string>> entry in _internalNamesByCategoryDisplay)
            {
                if (entry.Value.Count > 1)
                {
                    ambiguous.Add(entry.Key);
                }
            }

            PropertyCategoryAmbiguity.SetAmbiguousCategories(ambiguous);
        }

        private void RestoreSnapshot(CatalogSnapshot snapshot)
        {
            foreach (KeyValuePair<string, SortedSet<string>> entry in snapshot.PropertiesByCategory)
            {
                _propertiesByCategory[entry.Key] = new SortedSet<string>(entry.Value, StringComparer.OrdinalIgnoreCase);
            }

            _internalNamesByCategoryDisplay.Clear();
            foreach (KeyValuePair<string, HashSet<string>> entry in snapshot.InternalNamesByCategoryDisplay)
            {
                _internalNamesByCategoryDisplay[entry.Key] = new HashSet<string>(entry.Value, StringComparer.Ordinal);
            }

            _isLoaded = snapshot.IsLoaded;
            RecomputeAmbiguousCategories();
        }

        private void SaveSnapshot()
        {
            if (string.IsNullOrEmpty(_documentKey))
            {
                return;
            }

            var snapshot = new CatalogSnapshot
            {
                IsLoaded = _isLoaded
            };

            foreach (KeyValuePair<string, SortedSet<string>> entry in _propertiesByCategory)
            {
                snapshot.PropertiesByCategory[entry.Key] = new SortedSet<string>(entry.Value, StringComparer.OrdinalIgnoreCase);
            }

            foreach (KeyValuePair<string, HashSet<string>> entry in _internalNamesByCategoryDisplay)
            {
                snapshot.InternalNamesByCategoryDisplay[entry.Key] = new HashSet<string>(entry.Value, StringComparer.Ordinal);
            }

            Cache[_documentKey] = snapshot;
        }

        private static string GetDocumentKey(Document document)
        {
            if (document == null || document.IsClear)
            {
                return string.Empty;
            }

            return (document.FileName ?? string.Empty) + "|" + (document.Title ?? string.Empty);
        }

        private sealed class CatalogSnapshot
        {
            public Dictionary<string, SortedSet<string>> PropertiesByCategory { get; } =
                new Dictionary<string, SortedSet<string>>(StringComparer.OrdinalIgnoreCase);

            public Dictionary<string, HashSet<string>> InternalNamesByCategoryDisplay { get; } =
                new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);

            public bool IsLoaded { get; set; }
        }
    }
}
