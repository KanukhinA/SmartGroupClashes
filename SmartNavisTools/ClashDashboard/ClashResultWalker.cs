using System.Collections.Generic;
using Autodesk.Navisworks.Api;
using Autodesk.Navisworks.Api.Clash;

namespace SmartNavisTools
{
    /// <summary>
    /// Обходит дерево результатов коллизий и возвращает плоский список пересечений.
    /// </summary>
    internal static class ClashResultWalker
    {
        /// <summary>
        /// Возвращает все результаты коллизий указанного теста с учётом вложенных групп.
        /// </summary>
        public static IEnumerable<ClashResult> EnumerateResults(ClashTest test)
        {
            if (test == null)
            {
                yield break;
            }

            foreach (ClashResult result in EnumerateFromGroup(test))
            {
                yield return result;
            }
        }

        /// <summary>
        /// Рекурсивно обходит потомков группы и извлекает только реальные пересечения.
        /// </summary>
        private static IEnumerable<ClashResult> EnumerateFromGroup(GroupItem group)
        {
            for (int index = 0; index < group.Children.Count; index++)
            {
                SavedItem child = group.Children[index];
                if (child is ClashResult result)
                {
                    yield return result;
                    continue;
                }

                if (child is ClashResultGroup nestedGroup)
                {
                    foreach (ClashResult nested in EnumerateFromGroup(nestedGroup))
                    {
                        yield return nested;
                    }
                }
            }
        }
    }
}
