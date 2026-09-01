using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Navisworks.Api;
using Autodesk.Navisworks.Api.Clash;

namespace SmartNavisTools
{
    /// <summary>
    /// Готовит данные для интерактивного дашборда коллизий.
    /// </summary>
    internal static class ClashDashboardLogic
    {
        /// <summary>
        /// Параметры построения отчёта.
        /// </summary>
        internal sealed class BuildOptions
        {
            public IReadOnlyList<string> SelectedTests { get; set; } = Array.Empty<string>();

            public HashSet<ClashResultStatus> EnabledStatuses { get; set; } = new HashSet<ClashResultStatus>();

            public ClashDimensionResolver.FloorSource FloorSource { get; set; } = ClashDimensionResolver.FloorSource.Grid;

            public string FloorPropertyName { get; set; } = string.Empty;

            public ClashDimensionResolver.DisciplineSource DisciplineSource { get; set; } = ClashDimensionResolver.DisciplineSource.ModelFile;

            public string DisciplinePropertyName { get; set; } = string.Empty;

            public IReadOnlyList<ClashDimensionResolver.ModelFileDisciplineMapping> ModelFileDisciplineMappings
            {
                get;
                set;
            } = Array.Empty<ClashDimensionResolver.ModelFileDisciplineMapping>();
        }

        /// <summary>
        /// Результат генерации HTML-отчёта.
        /// </summary>
        internal sealed class BuildResult
        {
            public bool Success { get; set; }

            public string ErrorMessage { get; set; }

            public int RecordsCount { get; set; }
        }

        /// <summary>
        /// Плоская запись для клиентской агрегации в Plotly.
        /// </summary>
        internal sealed class DashboardRecord
        {
            public string testName { get; set; }

            public string modelName { get; set; }

            public string floor { get; set; }

            public double floorOrder { get; set; }

            public string gridAxes { get; set; }

            /// <summary>
            /// Смежный раздел (второй участник пересечения). Используется как цвет сегмента полоски.
            /// </summary>
            public string discipline { get; set; }

            /// <summary>
            /// Раздел модели (сторона 1). По нему строится отдельный график.
            /// </summary>
            public string modelDiscipline { get; set; }

            public string status { get; set; }

            public string clashName { get; set; }

            public string guid { get; set; }

            public string item1Id { get; set; }

            public string item2Id { get; set; }

            public string item1Name { get; set; }

            public string item2Name { get; set; }

            public string item1Model { get; set; }

            public string item2Model { get; set; }

            /// <summary>
            /// Зеркальная перспектива той же коллизии (сторона 2 как «раздел модели»).
            /// Нужна, чтобы пересечение ВК↔ОВ2 попадало и на график ВК, и на график ОВ2.
            /// </summary>
            public bool isMirror { get; set; }
        }

        /// <summary>
        /// Строит отчёт и сохраняет его в указанный HTML-файл.
        /// </summary>
        public static BuildResult BuildReport(string htmlPath, BuildOptions options)
        {
            if (string.IsNullOrWhiteSpace(htmlPath))
            {
                return new BuildResult { Success = false, ErrorMessage = "Не задан путь для HTML-отчёта." };
            }

            Document document = Application.MainDocument;
            if (document == null || document.IsClear)
            {
                return new BuildResult { Success = false, ErrorMessage = "Нет открытого документа Navisworks." };
            }

            DocumentClashTests testsData = document.GetClash()?.TestsData;
            if (testsData == null)
            {
                return new BuildResult { Success = false, ErrorMessage = "Clash Detective недоступен в текущем документе." };
            }

            List<ClashTest> selectedTests = SelectTests(testsData, options?.SelectedTests);
            if (selectedTests.Count == 0)
            {
                return new BuildResult { Success = false, ErrorMessage = "Выберите хотя бы одну проверку Clash Detective." };
            }

            HashSet<ClashResultStatus> enabledStatuses = options?.EnabledStatuses ?? new HashSet<ClashResultStatus>();
            if (enabledStatuses.Count == 0)
            {
                enabledStatuses = new HashSet<ClashResultStatus>
                {
                    ClashResultStatus.New,
                    ClashResultStatus.Active,
                    ClashResultStatus.Reviewed,
                    ClashResultStatus.Approved,
                    ClashResultStatus.Resolved
                };
            }

            var resolver = new ClashDimensionResolver();
            var records = new List<DashboardRecord>();

            foreach (ClashTest test in selectedTests)
            {
                foreach (ClashResult result in ClashResultWalker.EnumerateResults(test))
                {
                    try
                    {
                        if (!enabledStatuses.Contains(result.Status))
                        {
                            continue;
                        }

                        ClashDimensionResolver.Dimensions dimensions = resolver.Resolve(
                            result,
                            options?.FloorSource ?? ClashDimensionResolver.FloorSource.Grid,
                            options?.FloorPropertyName,
                            options?.DisciplineSource ?? ClashDimensionResolver.DisciplineSource.ModelFile,
                            options?.DisciplinePropertyName,
                            options?.ModelFileDisciplineMappings);

                        ClashDimensionResolver.ElementIdentity item1 =
                            resolver.ResolveElementIdentity(result.CompositeItem1);
                        ClashDimensionResolver.ElementIdentity item2 =
                            resolver.ResolveElementIdentity(result.CompositeItem2);

                        // Имя модели стороны 1 уже посчитано в dimensions; сторону 2 берём из кэша resolver.
                        string item1Model = dimensions.ModelName ?? "Без модели";
                        string item2Model = resolver.ResolveModelNamePublic(result.CompositeItem2);
                        string modelDiscipline = dimensions.ModelDiscipline ?? "Без раздела";
                        string partnerDiscipline = dimensions.DisciplineName ?? "Без раздела";

                        // Одна запись на коллизию. Зеркальная перспектива для графиков добавляется в HTML.
                        records.Add(new DashboardRecord
                        {
                            testName = test.DisplayName ?? string.Empty,
                            modelName = item1Model,
                            floor = dimensions.FloorName ?? "Без уровня",
                            floorOrder = dimensions.FloorOrder,
                            gridAxes = dimensions.GridAxes ?? "Нет пересечения осей",
                            discipline = partnerDiscipline,
                            modelDiscipline = modelDiscipline,
                            status = ClashDimensionResolver.ResolveStatus(result.Status),
                            clashName = result.DisplayName ?? string.Empty,
                            guid = result.Guid.ToString(),
                            item1Id = item1.Id ?? string.Empty,
                            item2Id = item2.Id ?? string.Empty,
                            item1Name = item1.Name ?? string.Empty,
                            item2Name = item2.Name ?? string.Empty,
                            item1Model = item1Model ?? string.Empty,
                            item2Model = item2Model ?? string.Empty,
                            isMirror = false
                        });
                    }
                    catch
                    {
                        // Ошибки отдельных результатов не должны прерывать построение общего отчёта.
                    }
                }
            }

            try
            {
                ClashDashboardReportBuilder.WriteReport(htmlPath, records);
            }
            catch (Exception exception)
            {
                return new BuildResult
                {
                    Success = false,
                    ErrorMessage = "Не удалось сформировать HTML-отчёт: " + exception.Message
                };
            }

            return new BuildResult
            {
                Success = true,
                RecordsCount = records.Count
            };
        }

        /// <summary>
        /// Возвращает список доступных проверок для UI.
        /// </summary>
        public static IReadOnlyList<ClashDetectiveTestsLogic.ClashTestInfo> GetTests()
        {
            return ClashDetectiveTestsLogic.GetTests();
        }

        /// <summary>
        /// Отбирает тесты по их отображаемым именам.
        /// </summary>
        private static List<ClashTest> SelectTests(DocumentClashTests testsData, IReadOnlyList<string> selectedTestNames)
        {
            var selectedSet = new HashSet<string>(
                selectedTestNames ?? Array.Empty<string>(),
                StringComparer.OrdinalIgnoreCase);

            var tests = new List<ClashTest>();
            foreach (SavedItem item in testsData.Tests)
            {
                if (!(item is ClashTest test))
                {
                    continue;
                }

                if (selectedSet.Count == 0 || selectedSet.Contains(test.DisplayName))
                {
                    tests.Add(test);
                }
            }

            return tests;
        }
    }
}
