using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Autodesk.Navisworks.Api;
using Autodesk.Navisworks.Api.Clash;

namespace SmartNavisTools
{
    /// <summary>
    /// Список проверок Clash Detective и их перезапуск.
    /// </summary>
    internal static class ClashDetectiveTestsLogic
    {
        internal sealed class ClashTestInfo
        {
            public ClashTestInfo(ClashTest test)
            {
                Test = test;
                DisplayName = test.DisplayName;
            }

            public ClashTest Test { get; }

            public string DisplayName { get; }

            public override string ToString()
            {
                return DisplayName;
            }
        }

        internal sealed class RunTestsResult
        {
            public bool Success { get; set; }
            public string ErrorMessage { get; set; }
            public int RunCount { get; set; }
            public bool WasCanceled { get; set; }
        }

        internal enum ExportFormat
        {
            Xml,
            Html
        }

        internal sealed class ExportTestsResult
        {
            public bool Success { get; set; }
            public string ErrorMessage { get; set; }
            public int ExportedCount { get; set; }
        }

        public static IReadOnlyList<ClashTestInfo> GetTests()
        {
            Document document = Application.MainDocument;
            if (document == null || document.IsClear)
            {
                return Array.Empty<ClashTestInfo>();
            }

            DocumentClashTests testsData = document.GetClash()?.TestsData;
            if (testsData == null)
            {
                return Array.Empty<ClashTestInfo>();
            }

            var result = new List<ClashTestInfo>();
            foreach (SavedItem item in testsData.Tests)
            {
                if (item is ClashTest test)
                {
                    result.Add(new ClashTestInfo(test));
                }
            }

            return result;
        }

        /// <summary>
        /// Проверяет, подходит ли имя проверки под фильтр (подстрока или регулярное выражение).
        /// </summary>
        public static bool MatchesFilter(
            string displayName,
            string pattern,
            bool useRegex,
            out string errorMessage)
        {
            errorMessage = null;

            if (string.IsNullOrWhiteSpace(pattern))
            {
                return true;
            }

            string name = displayName ?? string.Empty;
            string trimmedPattern = pattern.Trim();

            if (!useRegex)
            {
                return name.IndexOf(trimmedPattern, StringComparison.OrdinalIgnoreCase) >= 0;
            }

            try
            {
                return Regex.IsMatch(
                    name,
                    trimmedPattern,
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            }
            catch (ArgumentException ex)
            {
                errorMessage = ex.Message;
                return false;
            }
        }

        public static RunTestsResult RunTests(IEnumerable<ClashTest> tests)
        {
            var selectedTests = tests?.Where(t => t != null).ToArray() ?? Array.Empty<ClashTest>();
            if (selectedTests.Length == 0)
            {
                return new RunTestsResult
                {
                    Success = false,
                    ErrorMessage = "Выберите хотя бы одну проверку."
                };
            }

            Document document = Application.MainDocument;
            if (document == null || document.IsClear)
            {
                return new RunTestsResult
                {
                    Success = false,
                    ErrorMessage = "Нет открытого документа Navisworks."
                };
            }

            DocumentClashTests testsData = document.GetClash()?.TestsData;
            if (testsData == null || testsData.Tests.Count == 0)
            {
                return new RunTestsResult
                {
                    Success = false,
                    ErrorMessage = "Создайте хотя бы одну проверку в Clash Detective."
                };
            }

            int runCount = 0;
            bool wasCanceled = false;

            // TestsRunTest после каждой проверки вызывает RequestAutoSave — временно глушим автосохранение.
            BeginDisableAutoSaveSafe();
            try
            {
                using (Transaction transaction = document.BeginTransaction("SmartNavisTools"))
                {
                    Progress progress = Application.BeginProgress(
                        "Обновление проверок Clash Detective",
                        "Подготовка…");

                    try
                    {
                        for (int index = 0; index < selectedTests.Length; index++)
                        {
                            ClashTest test = selectedTests[index];
                            if (progress.IsCanceled)
                            {
                                wasCanceled = true;
                                break;
                            }

                            progress.Update((double)index / selectedTests.Length);

                            testsData.TestsRunTest(test);
                            runCount++;
                        }

                        if (!wasCanceled)
                        {
                            transaction.Commit();
                        }
                    }
                    finally
                    {
                        Application.EndProgress();
                    }
                }
            }
            finally
            {
                EndDisableAutoSaveSafe();
            }

            return new RunTestsResult
            {
                Success = runCount > 0,
                RunCount = runCount,
                WasCanceled = wasCanceled,
                ErrorMessage = runCount == 0 && !wasCanceled
                    ? "Не удалось обновить выбранные проверки."
                    : null
            };
        }

        /// <summary>
        /// Временно отключает автосохранение Navisworks через внутренний API, если он доступен.
        /// </summary>
        private static void BeginDisableAutoSaveSafe()
        {
            try
            {
                Type type = Type.GetType("Autodesk.Navisworks.Api.Interop.LcOpAutoSave, Autodesk.Navisworks.Api", false);
                type?.GetMethod("BeginDisableAutoSave", Type.EmptyTypes)?.Invoke(null, null);
            }
            catch
            {
                // Автосохранение остаётся как есть.
            }
        }

        /// <summary>
        /// Восстанавливает автосохранение Navisworks после безопасного отключения.
        /// </summary>
        private static void EndDisableAutoSaveSafe()
        {
            try
            {
                Type type = Type.GetType("Autodesk.Navisworks.Api.Interop.LcOpAutoSave, Autodesk.Navisworks.Api", false);
                type?.GetMethod("EndDisableAutoSave", Type.EmptyTypes)?.Invoke(null, null);
            }
            catch
            {
                // Игнорируем ошибки восстановления автосохранения.
            }
        }

        public static ExportTestsResult ExportTests(
            IEnumerable<ClashTest> tests,
            ExportFormat format)
        {
            var selectedTests = tests?.Where(t => t != null).ToArray() ?? Array.Empty<ClashTest>();
            if (selectedTests.Length == 0)
            {
                return new ExportTestsResult
                {
                    Success = false,
                    ErrorMessage = "Выберите хотя бы одну проверку."
                };
            }

            ClashNativeReportBridge.NativeExportResult nativeResult =
                ClashNativeReportBridge.ExportTests(selectedTests, format);

            return new ExportTestsResult
            {
                Success = nativeResult.Success,
                ExportedCount = nativeResult.InvokedCount,
                ErrorMessage = nativeResult.ErrorMessage
            };
        }
    }
}
