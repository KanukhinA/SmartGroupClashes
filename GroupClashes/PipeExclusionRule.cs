using System;
using System.Globalization;
using System.Text.RegularExpressions;

namespace SmartGroupClashes
{
    /// <summary>Правило для центров габаритов труб; все размеры передаются в миллиметрах.</summary>
    internal static class PipeExclusionRule
    {
        internal const double DiameterLimitMm = 50;
        internal const double CenterDistanceLimitMm = 200;

        private static readonly string[] VisCodes =
        {
            "VK", "EOM", "SS", "OV", "ВК", "ЕОМ", "СС", "ОМ",
        };

        private static readonly Regex LengthPattern = new Regex(
            @"^\s*(?<number>\+?(?:\d+(?:[.,]\d+)?|[.,]\d+)(?:[eE][+-]?\d+)?)\s*(?<unit>mm|мм|cm|см|dm|дм|m|м|ft|in)\s*$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        internal static bool IsPipeCategory(string value)
        {
            string category = (value ?? string.Empty).Trim();
            return category.Equals("Трубы", StringComparison.OrdinalIgnoreCase)
                || category.Equals("Pipes", StringComparison.OrdinalIgnoreCase);
        }

        internal static bool ContainsVisCode(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return false;
            foreach (string code in VisCodes)
            {
                if (value.IndexOf(code, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            }
            return false;
        }

        /// <summary>Без единицы измерения строка считается неоднозначной и не используется.</summary>
        internal static bool TryParseDiameterMm(string text, out double diameterMm)
        {
            diameterMm = 0;
            Match match = LengthPattern.Match(text ?? string.Empty);
            double number;
            if (!match.Success || !double.TryParse(match.Groups["number"].Value.Replace(',', '.'),
                NumberStyles.Float, CultureInfo.InvariantCulture, out number)) return false;

            double scale;
            switch (match.Groups["unit"].Value.ToLowerInvariant())
            {
                case "mm": case "мм": scale = 1; break;
                case "cm": case "см": scale = 10; break;
                case "dm": case "дм": scale = 100; break;
                case "m": case "м": scale = 1000; break;
                case "ft": scale = 304.8; break;
                case "in": scale = 25.4; break;
                default: return false;
            }
            diameterMm = number * scale;
            return IsPositiveFinite(diameterMm);
        }

        internal static bool IsSmallPipe(PipeExclusionCandidate pipe)
        {
            return pipe != null && IsPositiveFinite(pipe.DiameterMm) && pipe.DiameterMm < DiameterLimitMm;
        }

        internal static bool HasValidCenter(PipeExclusionCandidate pipe)
        {
            return pipe != null && IsFinite(pipe.X) && IsFinite(pipe.Y) && IsFinite(pipe.Z);
        }

        internal static bool Matches(PipeExclusionCandidate a, PipeExclusionCandidate b,
            bool includeSinglePipe = false, bool aIsIsolated = false, bool bIsIsolated = false)
        {
            // В режиме одиночной трубы соседство блокирует утверждение, без перехода к правилу пары.
            if (includeSinglePipe)
                return (IsSmallPipe(a) && HasValidCenter(a) && aIsIsolated)
                    || (IsSmallPipe(b) && HasValidCenter(b) && bIsIsolated);
            if (a == null || b == null
                || !IsPositiveFinite(a.DiameterMm) || !IsPositiveFinite(b.DiameterMm)
                || a.DiameterMm >= DiameterLimitMm || b.DiameterMm >= DiameterLimitMm
                || !IsFinite(a.X) || !IsFinite(a.Y) || !IsFinite(a.Z)
                || !IsFinite(b.X) || !IsFinite(b.Y) || !IsFinite(b.Z)) return false;

            double dx = a.X - b.X;
            double dy = a.Y - b.Y;
            double dz = a.Z - b.Z;
            return dx * dx + dy * dy + dz * dz < CenterDistanceLimitMm * CenterDistanceLimitMm;
        }

        internal static bool IsPositiveFinite(double value)
        {
            return IsFinite(value) && value > 0;
        }

        private static bool IsFinite(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }
    }

    internal sealed class PipeExclusionCandidate
    {
        internal double DiameterMm { get; set; }
        internal double X { get; set; }
        internal double Y { get; set; }
        internal double Z { get; set; }
    }
}
