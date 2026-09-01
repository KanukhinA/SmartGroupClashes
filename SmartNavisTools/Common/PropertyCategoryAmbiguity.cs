using System;
using System.Collections.Generic;

namespace SmartNavisTools
{
    /// <summary>
    /// Категории свойств с одинаковым DisplayName, но разными внутренними именами (режим Any в Navisworks).
    /// </summary>
    internal static class PropertyCategoryAmbiguity
    {
        private static readonly HashSet<string> AmbiguousCategoryDisplayNames =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        public static void SetAmbiguousCategories(IEnumerable<string> categoryDisplayNames)
        {
            AmbiguousCategoryDisplayNames.Clear();
            if (categoryDisplayNames == null)
            {
                return;
            }

            foreach (string name in categoryDisplayNames)
            {
                if (!string.IsNullOrWhiteSpace(name))
                {
                    AmbiguousCategoryDisplayNames.Add(name);
                }
            }
        }

        public static bool IsAmbiguous(string categoryDisplayName)
        {
            return !string.IsNullOrWhiteSpace(categoryDisplayName)
                && AmbiguousCategoryDisplayNames.Contains(categoryDisplayName);
        }
    }
}
