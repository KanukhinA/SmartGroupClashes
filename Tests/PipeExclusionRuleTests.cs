using System;
using System.Globalization;

namespace SmartGroupClashes
{
    internal static class PipeExclusionRuleTests
    {
        private static int _checks;

        private static void Check(bool condition, string description)
        {
            _checks++;
            if (!condition) throw new Exception(description);
        }

        private static PipeExclusionCandidate Pipe(double diameter, double x = 0, double y = 0, double z = 0)
        {
            return new PipeExclusionCandidate { DiameterMm = diameter, X = x, Y = y, Z = z };
        }

        private static bool SingleMatches(PipeExclusionCandidate a, PipeExclusionCandidate b,
            bool aIsIsolated = false, bool bIsIsolated = false,
            string modelNameA = "SP_VK.nwc", string modelNameB = "SP_OV.nwc")
        {
            return PipeExclusionRule.Matches(a, b, true, aIsIsolated, bIsIsolated, modelNameA, modelNameB);
        }

        private static void ModelNameTests()
        {
            foreach (string code in new[] { "VK", "EOM", "SS", "OV", "ВК", "ЕОМ", "СС", "ОМ" })
            {
                Check(PipeExclusionRule.IsVisModelName("SP1_" + code.ToLowerInvariant() + "_RD_R23.nwc"),
                    "VIS code in model filename: " + code);
                Check(SingleMatches(Pipe(20), null, true, false, "SP_VK.nwc", "SP1_" + code + ".nwc"),
                    "VIS partner model: " + code);
            }
            Check(PipeExclusionRule.IsVisModelName(@"C:\Models\SP1_OV_RD_R23.nwc"), "Windows path basename");
            Check(PipeExclusionRule.IsVisModelName("/models/SP1_ВК.rvt"), "Forward slash basename");
            Check(!PipeExclusionRule.IsVisModelName(@"C:\VK\SP1_KR.nwc"), "Folder code does not qualify model");
            Check(!PipeExclusionRule.IsVisModelName("/OV/SP1_AR.nwc"), "Forward slash folder code ignored");
            Check(!PipeExclusionRule.IsVisModelName(null), "Missing model name");
            Check(!PipeExclusionRule.IsVisModelName("  "), "Empty model name");
            Check(!PipeExclusionRule.IsVisModelName("SP1_KR_RD_R23.nwc"), "Structural model");
            Check(!SingleMatches(Pipe(20), null, true, false, "SP_VK.nwc", "SP_KR.nwc"),
                "Own pipe model is VIS but opposite model is structural");
            Check(!SingleMatches(null, Pipe(20), false, true, "SP_KR.nwc", "SP_VK.nwc"),
                "Reverse sides: partner must be VIS");
            Check(!SingleMatches(Pipe(20), null, true, false, "SP_VK.nwc", null), "Unknown partner model");
            Check(!SingleMatches(Pipe(20), null, true, false, "SP_VK.nwc", @"C:\SS\SP_KR.nwc"),
                "Partner model code only in directory");
            Check(!PipeExclusionRule.Matches(Pipe(20), null, true, true), "No model qualification by default");
            Check(PipeExclusionRule.Matches(Pipe(20), Pipe(20, 100), false, false, false, "SP_KR.nwc", "SP_AR.nwc"),
                "Model restriction applies only to single-pipe option");
        }

        private static void Diameter(string text, double expected)
        {
            double actual;
            Check(PipeExclusionRule.TryParseDiameterMm(text, out actual) && Math.Abs(actual - expected) < 1e-9,
                "Diameter: " + text);
        }

        private static void RejectDiameter(string text)
        {
            double ignored;
            Check(!PipeExclusionRule.TryParseDiameterMm(text, out ignored), "Reject diameter: " + text);
        }

        private static void Neighbor(double x, double y, double z, bool isolated, string description,
            double radiusMm = PipeExclusionRule.DefaultDistanceMm)
        {
            var index = new PipeNeighborhoodIndex<string>(radiusMm);
            var source = Pipe(20);
            index.Add("source", source);
            // Сосед любого диаметра, не обязательно участник выбранной проверки или труба ВИС.
            index.Add("pipe_outside_selected_tests", Pipe(500, x, y, z));
            Check(index.IsIsolated("source", source) == isolated, description);
        }

        private static void DistanceSettingsTests()
        {
            foreach (string culture in new[] { "ru-RU", "en-US" })
            {
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
                foreach (string text in new[] { "150,5", "150.5", " 150.5 " })
                {
                    double actual;
                    Check(PipeExclusionRule.TryParseDistanceMm(text, out actual) && actual == 150.5,
                        "Distance input independent of culture: " + text);
                    string saved = actual.ToString("R", CultureInfo.InvariantCulture);
                    Check(PipeExclusionRule.TryParseDistanceMm(saved, out actual) && actual == 150.5,
                        "Settings value round-trip: " + culture);
                }
                foreach (string invalid in new[] { null, "", "  ", "0", "-1", "abc", "200 мм", "1,2.3", "NaN",
                    "Infinity", "1e999", "1e200", "1e-200" })
                {
                    double ignored;
                    Check(!PipeExclusionRule.TryParseDistanceMm(invalid, out ignored), "Invalid distance: " + invalid);
                }
            }
            Check(PipeExclusionRule.Matches(Pipe(20), Pipe(20, 99.999), distanceLimitMm: 100), "Pair below custom threshold");
            Check(!PipeExclusionRule.Matches(Pipe(20), Pipe(20, 100), distanceLimitMm: 100), "Pair at custom threshold");
            Check(!PipeExclusionRule.Matches(Pipe(20), Pipe(20, 150), distanceLimitMm: 100), "Reduced pair threshold applied");
            Check(PipeExclusionRule.Matches(Pipe(20), Pipe(20, 300), distanceLimitMm: 350), "Increased pair threshold applied");
            Check(!PipeExclusionRule.Matches(Pipe(20), Pipe(20, 350), distanceLimitMm: 350), "Increased pair boundary");
            Check(PipeExclusionRule.Matches(Pipe(20), Pipe(20, 150), distanceLimitMm: 150.5), "Fractional pair threshold");
            foreach (double invalid in new[] { 0d, -1d, double.NaN, double.PositiveInfinity, double.MaxValue, double.Epsilon })
            {
                Check(!PipeExclusionRule.Matches(Pipe(20), Pipe(20, 10), distanceLimitMm: invalid), "Invalid pair threshold");
                bool rejected = false;
                try { new PipeNeighborhoodIndex<string>(invalid); }
                catch (ArgumentOutOfRangeException) { rejected = true; }
                Check(rejected, "Index must reject invalid radius");
            }
            Neighbor(100, 0, 0, false, "Custom neighbor boundary", 100);
            Neighbor(100.001, 0, 0, true, "Outside reduced radius", 100);
            Neighbor(300, 0, 0, false, "Neighbor found beyond old radius", 350);
            Neighbor(350, 0, 0, false, "Increased neighbor boundary", 350);
            Neighbor(350.001, 0, 0, true, "Outside increased radius", 350);
            Neighbor(-150.5, 0, 0, false, "Fractional radius and negative cells", 150.5);
        }

        private static void NeighborhoodTests()
        {
            Neighbor(199.999, 0, 0, false, "Neighbor below radius");
            Neighbor(200, 0, 0, false, "Neighbor exactly at radius");
            Neighbor(200.001, 0, 0, true, "Neighbor above radius");
            Neighbor(-200, 0, 0, false, "Negative cell boundary");
            Neighbor(0, 0, 200, false, "Vertical neighbor");
            Neighbor(120, 160, 0, false, "3D radius equality");
            Neighbor(150, 150, 0, true, "Diagonal outside sphere inside neighboring cells");
            Neighbor(100, 100, 100, false, "3D neighbor");
            Neighbor(0, 0, 0, false, "Different pipe at same center");

            var self = Pipe(20, -0.1);
            var index = new PipeNeighborhoodIndex<string>();
            index.Add("self", self);
            index.Add(new string(new[] { 's', 'e', 'l', 'f' }), self);
            Check(index.IsIsolated("self", self), "Same pipe is not its own neighbor, including duplicate wrappers");
            Check(!index.IsIsolated("not_indexed", self), "Unknown target must not be considered isolated");
            index.Add("other", Pipe(double.NaN, 0.1));
            Check(!index.IsIsolated("self", self), "Neighbor across zero and without diameter blocks exclusion");

            var incomplete = new PipeNeighborhoodIndex<string>();
            incomplete.Add("self", self);
            incomplete.Add("unknown_position", null);
            Check(!incomplete.IsComplete && !incomplete.IsIsolated("self", self), "Unknown geometry blocks certification");
            Check(!incomplete.IsIsolated("self", Pipe(20, double.NaN)), "Invalid query center");

            var huge = new PipeNeighborhoodIndex<string>();
            huge.Add("huge", Pipe(20, double.MaxValue));
            Check(!huge.IsComplete, "Unrepresentable cell cannot certify isolation");

            // Сравнение пространственного индекса с полным перебором на общей выборке.
            var random = new Random(7413);
            var points = new PipeExclusionCandidate[300];
            var ids = new string[points.Length];
            for (int i = 0; i < points.Length; i++)
            {
                ids[i] = "pipe_" + i;
                points[i] = Pipe(random.Next(1, 600), random.NextDouble() * 2000 - 1000,
                    random.NextDouble() * 2000 - 1000, random.NextDouble() * 2000 - 1000);
            }
            foreach (double radiusMm in new[] { 100d, 200d, 350.5 })
            {
                var spatial = new PipeNeighborhoodIndex<string>(radiusMm);
                for (int i = 0; i < points.Length; i++) spatial.Add(ids[i], points[i]);
                for (int i = 0; i < points.Length; i++)
                {
                    bool expected = true;
                    for (int j = 0; j < points.Length; j++)
                    {
                        if (i == j) continue;
                        double dx = points[i].X - points[j].X;
                        double dy = points[i].Y - points[j].Y;
                        double dz = points[i].Z - points[j].Z;
                        if (dx * dx + dy * dy + dz * dz <= radiusMm * radiusMm) { expected = false; break; }
                    }
                    Check(spatial.IsIsolated(ids[i], points[i]) == expected,
                        "Spatial index versus brute force: " + radiusMm + ", point " + i);
                }
            }
        }

        private static int Main()
        {
            try
            {
                foreach (string culture in new[] { "ru-RU", "en-US" })
                {
                    CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
                    Diameter("0,020 м", 20);
                    Diameter("0.020 m", 20);
                    Diameter("20 мм", 20);
                    Diameter("20\u00a0мм", 20);
                    Diameter("2 cm", 20);
                    Diameter("0,2 дм", 20);
                    Diameter("2e-2 m", 20);
                    Diameter("1 in", 25.4);
                    Diameter("0.1 ft", 30.48);
                    Diameter("50 MM", 50);
                    RejectDiameter(null);
                    RejectDiameter("");
                    RejectDiameter("20"); // Без единиц нельзя отличить метры от миллиметров.
                    RejectDiameter("DoubleLength: 20");
                    RejectDiameter("-20 мм");
                    RejectDiameter("0 м");
                    RejectDiameter("NaN м");
                    RejectDiameter("Infinity м");
                    RejectDiameter("1e999 m");
                    RejectDiameter("0,020 м (пример)");
                    RejectDiameter("20 mm2");
                }

                Check(PipeExclusionRule.IsPipeCategory(" Трубы "), "Russian category");
                Check(PipeExclusionRule.IsPipeCategory("Pipes"), "English category");
                Check(!PipeExclusionRule.IsPipeCategory("Соединительные детали трубопроводов"), "Not fittings");
                Check(!PipeExclusionRule.IsPipeCategory("Pipe Insulations"), "Not insulation");
                Check(!PipeExclusionRule.IsPipeCategory(null), "Missing category");
                foreach (string code in new[] { "VK", "EOM", "SS", "OV", "ВК", "ЕОМ", "СС", "ОМ" })
                    Check(PipeExclusionRule.ContainsVisCode("model_" + code.ToLowerInvariant() + ".nwc"), code);
                Check(!PipeExclusionRule.ContainsVisCode("model_AR.nwc"), "Other discipline");
                Check(!PipeExclusionRule.ContainsVisCode(null), "Missing discipline");

                Check(PipeExclusionRule.Matches(Pipe(20), Pipe(49.999, 199.999)), "Both below thresholds");
                Check(!PipeExclusionRule.Matches(Pipe(50), Pipe(20)), "First diameter exactly 50");
                Check(!PipeExclusionRule.Matches(Pipe(20), Pipe(50)), "Second diameter exactly 50");
                Check(!PipeExclusionRule.Matches(Pipe(20), Pipe(100)), "Both must be small");
                Check(!PipeExclusionRule.Matches(Pipe(20), Pipe(20, 200)), "Distance exactly 200");
                Check(!PipeExclusionRule.Matches(Pipe(20), Pipe(20, 201)), "Distance above 200");
                Check(!PipeExclusionRule.Matches(Pipe(20), Pipe(20, 120, 160)), "3D boundary 200");
                Check(!PipeExclusionRule.Matches(Pipe(20), Pipe(20, 0, 0, 200)), "Vertical boundary");
                Check(PipeExclusionRule.Matches(Pipe(20, 1000, 2000, 3000), Pipe(20, 1100, 2100, 3100)),
                    "3D distance independent of origin");
                Check(PipeExclusionRule.Matches(Pipe(20), Pipe(20)), "Coincident centers");
                Check(!PipeExclusionRule.Matches(null, Pipe(20)), "Missing first pipe");
                Check(!PipeExclusionRule.Matches(Pipe(20), null), "Missing second pipe");
                Check(!PipeExclusionRule.Matches(Pipe(0), Pipe(20)), "Zero diameter");
                Check(!PipeExclusionRule.Matches(Pipe(-1), Pipe(20)), "Negative diameter");
                Check(!PipeExclusionRule.Matches(Pipe(double.NaN), Pipe(20)), "Invalid diameter");
                Check(!PipeExclusionRule.Matches(Pipe(20), Pipe(20, double.NaN)), "Invalid center");
                Check(!PipeExclusionRule.Matches(Pipe(20), Pipe(20, double.PositiveInfinity)), "Infinite center");
                Check(!PipeExclusionRule.Matches(Pipe(20, double.MaxValue), Pipe(20, -double.MaxValue)),
                    "Distance overflow must not match");

                Check(SingleMatches(Pipe(20), null, true), "Isolated small pipe versus element of VIS model");
                Check(SingleMatches(null, Pipe(20), false, true), "Isolated small pipe on second side");
                Check(SingleMatches(Pipe(20), Pipe(100, 10000), true), "Distant second pipe may be large");
                Check(SingleMatches(Pipe(20), Pipe(20, 10000), true, true), "Distant isolated pipes");
                Check(!SingleMatches(Pipe(20, double.NaN, double.NaN, double.NaN), null, true),
                    "Single pipe requires geometry to check neighbors");
                Check(!SingleMatches(Pipe(50), null, true), "Single diameter exactly 50 is excluded");
                Check(!SingleMatches(Pipe(100), Pipe(50), true, true), "No small pipes");
                Check(!SingleMatches(null, null), "No known pipes");
                Check(!SingleMatches(Pipe(0), null, true), "Single zero diameter");
                Check(!SingleMatches(Pipe(double.NaN), null, true), "Single invalid diameter");
                Check(!SingleMatches(Pipe(double.PositiveInfinity), null, true), "Single infinite diameter");
                Check(!PipeExclusionRule.Matches(Pipe(20), null, false), "Original pair mode requires both pipes");
                Check(!PipeExclusionRule.Matches(Pipe(20), Pipe(20, 10000), false), "Original mode retains distance check");
                Check(!SingleMatches(Pipe(20), null), "Unverified isolation must not approve");
                Check(!SingleMatches(Pipe(20), Pipe(500, 100)), "Large neighbor blocks small pipe");
                Check(!SingleMatches(Pipe(20), Pipe(20, 100)),
                    "Small neighbor blocks single mode without fallback to pair rule");
                ModelNameTests();
                DistanceSettingsTests();
                NeighborhoodTests();

                Console.WriteLine("PASS: " + _checks + " checks");
                return 0;
            }
            catch (Exception error)
            {
                Console.Error.WriteLine("FAIL: " + error.Message);
                return 1;
            }
        }
    }
}
