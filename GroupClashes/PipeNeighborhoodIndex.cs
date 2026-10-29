using System;
using System.Collections.Generic;

namespace SmartGroupClashes
{
    /// <summary>Поиск других труб по центрам габаритов в радиусе 200 мм, включая границу.</summary>
    internal sealed class PipeNeighborhoodIndex<T> where T : class
    {
        private sealed class Entry
        {
            internal T Item;
            internal PipeExclusionCandidate Center;
        }

        private readonly Dictionary<Tuple<long, long, long>, List<Entry>> _cells =
            new Dictionary<Tuple<long, long, long>, List<Entry>>();
        private readonly HashSet<T> _items = new HashSet<T>();
        internal bool IsComplete { get; private set; } = true;

        internal void MarkIncomplete()
        {
            IsComplete = false;
        }

        internal bool Contains(T item)
        {
            return item != null && _items.Contains(item);
        }

        internal void Add(T item, PipeExclusionCandidate center)
        {
            Tuple<long, long, long> cell;
            if (item == null || !TryGetCell(center, out cell))
            {
                MarkIncomplete();
                return;
            }
            if (!_items.Add(item)) return;
            List<Entry> entries;
            if (!_cells.TryGetValue(cell, out entries))
            {
                entries = new List<Entry>();
                _cells.Add(cell, entries);
            }
            entries.Add(new Entry { Item = item, Center = center });
        }

        internal bool IsIsolated(T item, PipeExclusionCandidate center)
        {
            Tuple<long, long, long> cell;
            if (!IsComplete || !Contains(item) || !TryGetCell(center, out cell)) return false;
            double radiusSquared = PipeExclusionRule.CenterDistanceLimitMm * PipeExclusionRule.CenterDistanceLimitMm;
            // Размер ячейки равен радиусу: достаточно текущей и 26 соседних ячеек.
            for (int x = -1; x <= 1; x++)
                for (int y = -1; y <= 1; y++)
                    for (int z = -1; z <= 1; z++)
                    {
                        List<Entry> entries;
                        if (!_cells.TryGetValue(Tuple.Create(cell.Item1 + x, cell.Item2 + y, cell.Item3 + z),
                            out entries)) continue;
                        foreach (Entry entry in entries)
                        {
                            if (EqualityComparer<T>.Default.Equals(item, entry.Item)) continue;
                            double dx = center.X - entry.Center.X;
                            double dy = center.Y - entry.Center.Y;
                            double dz = center.Z - entry.Center.Z;
                            if (dx * dx + dy * dy + dz * dz <= radiusSquared) return false;
                        }
                    }
            return true;
        }

        private static bool TryGetCell(PipeExclusionCandidate center, out Tuple<long, long, long> cell)
        {
            cell = null;
            if (!PipeExclusionRule.HasValidCenter(center)) return false;
            double x = Math.Floor(center.X / PipeExclusionRule.CenterDistanceLimitMm);
            double y = Math.Floor(center.Y / PipeExclusionRule.CenterDistanceLimitMm);
            double z = Math.Floor(center.Z / PipeExclusionRule.CenterDistanceLimitMm);
            // Запас исключает переполнение при переборе соседних ячеек.
            if (x <= long.MinValue + 1024d || x >= long.MaxValue - 1024d
                || y <= long.MinValue + 1024d || y >= long.MaxValue - 1024d
                || z <= long.MinValue + 1024d || z >= long.MaxValue - 1024d) return false;
            cell = Tuple.Create((long)x, (long)y, (long)z);
            return true;
        }
    }
}
