using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Xml;
using Autodesk.Navisworks.Api;
using Autodesk.Navisworks.Api.Clash;

namespace SmartNavisTools
{
    /// <summary>
    /// Импорт Clash Report XML и обновление статусов, описания и утвердившего в Clash Detective.
    /// Reviewed («Исправлено») и Resolved («Исправленный») при подгрузке не заменяются;
    /// New/Active из XML игнорируются.
    /// </summary>
    internal static class ClashStatusUpdaterLogic
    {
        private const string RootElementName = "clashresults";
        private const string TestElementName = "clashtest";
        private const string ResultElementName = "clashresult";
        private const string TestNameAttribute = "name";
        private const string ResultNameAttribute = "name";
        private const string ResultGuidAttribute = "guid";
        private const string ResultStatusAttribute = "status";
        private const string ResultStatusElementName = "resultstatus";
        private const string ApprovedByElementName = "approvedby";

        private static readonly Dictionary<string, string> StatusAliases =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "new", "new" },
                { "active", "active" },
                { "reviewed", "reviewed" },
                { "approved", "approved" },
                { "resolved", "resolved" },
                { "новая", "new" },
                { "активн.", "active" },
                { "активная", "active" },
                { "проверенные", "reviewed" },
                { "проверенный", "reviewed" },
                { "проверенное", "reviewed" },
                { "проверено", "reviewed" },
                { "проверено (исправлено)", "reviewed" },
                { "утверждённые", "approved" },
                { "утвержденные", "approved" },
                { "утверждённое", "approved" },
                { "утвержденное", "approved" },
                { "утверждено", "approved" },
                { "утверждено (исключить)", "approved" },
                { "решено", "resolved" },
                { "исправленный", "resolved" },
                { "исправленные", "resolved" }
            };
        private const string CommentsElementName = "comments";
        private const string CommentElementName = "comment";
        private const string CommentBodyElementName = "body";

        internal sealed class ClashResultXmlData
        {
            public string Name { get; set; }
            public string Guid { get; set; }
            public string Status { get; set; }
            public string ApprovedBy { get; set; }
            public string Description { get; set; }
        }

        internal sealed class ImportResult
        {
            public bool Success { get; set; }
            public string PreviewText { get; set; } = string.Empty;
            public string[] LoadedFiles { get; set; } = Array.Empty<string>();
            public string ErrorMessage { get; set; }
        }

        internal sealed class UpdateResult
        {
            public bool Success { get; set; }
            public string ErrorMessage { get; set; }
        }

        public static ImportResult ImportFiles(IEnumerable<string> filePaths)
        {
            var paths = filePaths?.Where(p => !string.IsNullOrWhiteSpace(p)).ToArray() ?? Array.Empty<string>();
            if (paths.Length == 0)
            {
                return new ImportResult
                {
                    Success = false,
                    ErrorMessage = "Не выбран ни один файл."
                };
            }

            var preview = new StringBuilder();
            var loaded = new List<string>();

            foreach (string path in paths)
            {
                try
                {
                    XmlDocument document = LoadXml(path);
                    string validationError = ValidateImportDocument(document);
                    if (validationError != null)
                    {
                        return new ImportResult
                        {
                            Success = false,
                            ErrorMessage = validationError
                        };
                    }

                    AppendPreview(document, preview);
                    loaded.Add(path);
                }
                catch (XmlException)
                {
                    return new ImportResult
                    {
                        Success = false,
                        ErrorMessage = "Выберите только файлы Clash Report XML."
                    };
                }
            }

            return new ImportResult
            {
                Success = true,
                PreviewText = preview.ToString(),
                LoadedFiles = loaded.ToArray()
            };
        }

        public static UpdateResult ApplyLoadedFiles(string[] filePaths)
        {
            if (filePaths == null || filePaths.Length == 0)
            {
                return new UpdateResult
                {
                    Success = false,
                    ErrorMessage = "Сначала загрузите файл Clash Report XML."
                };
            }

            Document document = Application.MainDocument;
            if (document == null)
            {
                return new UpdateResult
                {
                    Success = false,
                    ErrorMessage = "Нет открытого документа Navisworks."
                };
            }

            DocumentClashTests testsData = document.GetClash().TestsData;
            if (testsData.Tests.Count == 0)
            {
                return new UpdateResult
                {
                    Success = false,
                    ErrorMessage = "Создайте хотя бы один тест в Clash Detective."
                };
            }

            using (Transaction transaction = document.BeginTransaction("SmartNavisTools — обновление статусов"))
            {
                foreach (string path in filePaths)
                {
                    try
                    {
                        XmlDocument xmlDocument = LoadXml(path);
                        if (IsMasterReport(xmlDocument))
                        {
                            return new UpdateResult
                            {
                                Success = false,
                                ErrorMessage = "Отчёты с типом «All tests (combined)» не поддерживаются."
                            };
                        }

                        if (!IsValidReport(xmlDocument))
                        {
                            return new UpdateResult
                            {
                                Success = false,
                                ErrorMessage = "Сначала загрузите файл Clash Report XML."
                            };
                        }

                        ApplyDocument(xmlDocument, testsData);
                    }
                    catch (XmlException)
                    {
                        return new UpdateResult
                        {
                            Success = false,
                            ErrorMessage = "Повреждённый XML-файл."
                        };
                    }
                }

                transaction.Commit();
            }

            return new UpdateResult { Success = true };
        }

        private static void ApplyDocument(XmlDocument xmlDocument, DocumentClashTests testsData)
        {
            foreach (XmlElement testElement in xmlDocument.GetElementsByTagName(TestElementName))
            {
                string testName = testElement.GetAttribute(TestNameAttribute);
                ClashTest matchingTest = testsData.Tests
                    .FirstOrDefault(test => string.Equals(test.DisplayName, testName, StringComparison.Ordinal)) as ClashTest;

                if (matchingTest == null)
                {
                    continue;
                }

                foreach (XmlElement resultElement in testElement.GetElementsByTagName(ResultElementName))
                {
                    try
                    {
                        ClashResultXmlData xmlData = ParseResultElement(resultElement);
                        if (string.Equals(NormalizeStatusCode(xmlData.Status), "resolved", StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }

                        UpdateMatchingResult(testsData, matchingTest, xmlData);
                    }
                    catch
                    {
                    }
                }
            }
        }

        private static ClashResultXmlData ParseResultElement(XmlElement resultElement)
        {
            return new ClashResultXmlData
            {
                Name = resultElement.GetAttribute(ResultNameAttribute),
                Guid = resultElement.GetAttribute(ResultGuidAttribute),
                Status = ReadNormalizedResultStatus(resultElement),
                ApprovedBy = GetChildElementText(resultElement, ApprovedByElementName),
                Description = GetCommentDescription(resultElement)
            };
        }

        /// <summary>Есть ли в документе пересечение с указанным нормализованным статусом.</summary>
        internal static bool DocumentHasResultStatus(XmlDocument document, string statusCode)
        {
            foreach (XmlElement element in document.GetElementsByTagName(ResultElementName))
            {
                if (string.Equals(ReadNormalizedResultStatus(element), statusCode, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>Положительное значение атрибута в узлах summary (как в отчёте Navisworks).</summary>
        internal static bool DocumentHasPositiveSummary(XmlDocument document, string attributeName)
        {
            return HasPositiveSummaryValue(document, attributeName);
        }

        /// <summary>XML, собранный SP-Service (exchange/batchtest), без summary Navisworks.</summary>
        internal static bool IsSpServiceClashReport(XmlDocument document)
        {
            return document.GetElementsByTagName("exchange").Count > 0
                && document.GetElementsByTagName("batchtest").Count > 0;
        }

        /// <summary>Читает status из атрибута или resultstatus и приводит к коду Navisworks.</summary>
        private static string ReadNormalizedResultStatus(XmlElement resultElement)
        {
            string raw = resultElement.GetAttribute(ResultStatusAttribute);
            if (string.IsNullOrWhiteSpace(raw))
            {
                raw = GetChildElementText(resultElement, ResultStatusElementName);
            }

            return NormalizeStatusCode(raw);
        }

        private static string NormalizeStatusCode(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                return string.Empty;
            }

            string trimmed = raw.Trim();
            if (StatusAliases.TryGetValue(trimmed, out string mapped))
            {
                return mapped;
            }

            return trimmed.ToLowerInvariant();
        }

        private static string GetChildElementText(XmlElement parent, string elementName)
        {
            foreach (XmlNode child in parent.ChildNodes)
            {
                if (child is XmlElement element &&
                    string.Equals(element.LocalName, elementName, StringComparison.OrdinalIgnoreCase))
                {
                    return element.InnerText?.Trim() ?? string.Empty;
                }
            }

            return string.Empty;
        }

        private static string GetCommentDescription(XmlElement resultElement)
        {
            XmlElement commentsElement = null;
            foreach (XmlNode child in resultElement.ChildNodes)
            {
                if (child is XmlElement element &&
                    string.Equals(element.LocalName, CommentsElementName, StringComparison.OrdinalIgnoreCase))
                {
                    commentsElement = element;
                    break;
                }
            }

            if (commentsElement == null)
            {
                return string.Empty;
            }

            var bodies = new List<string>();
            foreach (XmlNode commentNode in commentsElement.ChildNodes)
            {
                if (!(commentNode is XmlElement commentElement) ||
                    !string.Equals(commentElement.LocalName, CommentElementName, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                string body = GetChildElementText(commentElement, CommentBodyElementName);
                if (string.IsNullOrWhiteSpace(body) || ClashAutoResolveComment.IsMatch(body))
                    continue;

                bodies.Add(body);
            }

            return string.Join(Environment.NewLine, bodies);
        }

        /// <summary>
        /// Обновляет найденное пересечение: статус (только Reviewed/Approved),
        /// описание и утвердившего. Resolved и уже выставленный Reviewed («Исправлено») не трогаем.
        /// </summary>
        private static void UpdateMatchingResult(
            DocumentClashTests testsData,
            ClashTest test,
            ClashResultXmlData xmlData)
        {
            IClashResult clashResult = FindClashResult(testsData, test, xmlData.Name, xmlData.Guid);
            if (clashResult == null)
            {
                return;
            }

            // Исправленный (Resolved) и Проверенный / «Исправлено» (Reviewed): статус не меняем.
            // Описание и «утвердил» для них тоже не перезаписываем.
            if (ShouldPreserveExistingStatus(clashResult))
            {
                return;
            }

            try
            {
                ApplyStatusFromXml(testsData, clashResult, xmlData);
            }
            catch
            {
            }

            try
            {
                if (!string.IsNullOrWhiteSpace(xmlData.Description))
                {
                    testsData.TestsEditResultDescription(clashResult, xmlData.Description);
                }
            }
            catch
            {
            }

            try
            {
                if (!string.IsNullOrWhiteSpace(xmlData.ApprovedBy))
                {
                    SetApprovedBy(testsData, clashResult, xmlData.ApprovedBy);
                }
            }
            catch
            {
            }
        }

        /// <summary>
        /// Ставит статус из XML. Не перезаписывает Resolved/Reviewed и не применяет New/Active.
        /// </summary>
        private static void ApplyStatusFromXml(
            DocumentClashTests testsData,
            IClashResult clashResult,
            ClashResultXmlData xmlData)
        {
            if (ShouldPreserveExistingStatus(clashResult))
            {
                return;
            }

            if (ShouldIgnoreXmlStatus(xmlData.Status))
            {
                return;
            }

            string status = NormalizeStatusCode(xmlData.Status);
            if (string.Equals(status, "approved", StringComparison.OrdinalIgnoreCase))
            {
                SetResultStatus(testsData, clashResult, ClashResultStatus.Approved, xmlData.ApprovedBy);
            }
            else if (string.Equals(status, "reviewed", StringComparison.OrdinalIgnoreCase))
            {
                // Не даунгрейдим Approved → Reviewed.
                if (clashResult.Status == ClashResultStatus.Approved)
                {
                    return;
                }

                SetResultStatus(testsData, clashResult, ClashResultStatus.Reviewed, xmlData.ApprovedBy);
            }
        }

        /// <summary>
        /// Resolved («Исправленный») и Reviewed («Исправлено» / Проверенный) при подгрузке не заменяем.
        /// </summary>
        private static bool ShouldPreserveExistingStatus(IClashResult clashResult)
        {
            if (clashResult == null)
            {
                return false;
            }

            return clashResult.Status == ClashResultStatus.Resolved
                || clashResult.Status == ClashResultStatus.Reviewed;
        }

        /// <summary>
        /// Статусы XML, которые нельзя записывать в Clash Detective:
        /// resolved не меняем, new/active не должны снимать «устранено».
        /// </summary>
        private static bool ShouldIgnoreXmlStatus(string xmlStatus)
        {
            string normalized = NormalizeStatusCode(xmlStatus);
            return string.Equals(normalized, "resolved", StringComparison.OrdinalIgnoreCase)
                || string.Equals(normalized, "new", StringComparison.OrdinalIgnoreCase)
                || string.Equals(normalized, "active", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Ищет пересечение в тесте: сначала по GUID из отчёта (имя не требуется —
        /// после группировки DisplayName часто меняется), затем по имени внутри групп.
        /// </summary>
        private static IClashResult FindClashResult(
            DocumentClashTests testsData,
            ClashTest test,
            string displayName,
            string guid)
        {
            if (!string.IsNullOrWhiteSpace(guid) &&
                Guid.TryParse(guid, out Guid parsedGuid))
            {
                SavedItem byGuid = testsData.ResolveGuid(parsedGuid);
                if (byGuid is ClashResult clashByGuid)
                {
                    return clashByGuid;
                }
            }

            return FindClashResultByName(test, displayName);
        }

        /// <summary>
        /// Рекурсивный поиск по имени среди прямых потомков и внутри <see cref="ClashResultGroup"/>.
        /// Имя достаточно: после группировки Navisworks часто выдаёт новый GUID, не совпадающий с отчётом.
        /// </summary>
        private static IClashResult FindClashResultByName(GroupItem parent, string displayName)
        {
            for (int index = 0; index < parent.Children.Count; index++)
            {
                SavedItem child = parent.Children[index];

                ClashResultGroup group = child as ClashResultGroup;
                if (group != null)
                {
                    IClashResult nested = FindClashResultByName(group, displayName);
                    if (nested != null)
                    {
                        return nested;
                    }

                    continue;
                }

                ClashResult clashResult = child as ClashResult;
                if (clashResult != null &&
                    string.Equals(clashResult.DisplayName, displayName, StringComparison.Ordinal))
                {
                    return clashResult;
                }
            }

            return null;
        }

        private static void SetResultStatus(
            DocumentClashTests testsData,
            IClashResult clashResult,
            ClashResultStatus status,
            string approvedBy)
        {
#if Version2026
            testsData.TestsEditResultStatus(clashResult, status, CreateAssignee(approvedBy));
#else
            testsData.TestsEditResultStatus(clashResult, status);
#endif
        }

        private static void SetApprovedBy(
            DocumentClashTests testsData,
            IClashResult clashResult,
            string approvedBy)
        {
#if Version2026
            testsData.TestsEditResultApprovedBy(clashResult, CreateAssignee(approvedBy));
#else
            testsData.TestsEditResultApprovedBy(clashResult, approvedBy);
#endif
        }

#if Version2026
        private static Assignee CreateAssignee(string displayName)
        {
            var assignee = new Assignee();
            if (!string.IsNullOrWhiteSpace(displayName))
            {
                assignee.DisplayName = displayName;
            }

            return assignee;
        }
#endif

        private static string ValidateImportDocument(XmlDocument document)
        {
            if (!IsValidReport(document))
            {
                return "Выберите только файлы Clash Report XML.";
            }

            if (!HasAttribute(document, ResultElementName, ResultStatusAttribute) &&
                HasPositiveSummaryValue(document, "total"))
            {
                return "Вкладка Contents отчёта: отметьте флажок «Status».";
            }

            return null;
        }

        private static void AppendPreview(XmlDocument document, StringBuilder preview)
        {
            foreach (XmlElement testElement in document.GetElementsByTagName(TestElementName))
            {
                string testName = testElement.GetAttribute(TestNameAttribute);
                preview.AppendLine("Тест: " + testName);

                foreach (XmlElement resultElement in testElement.GetElementsByTagName(ResultElementName))
                {
                    ClashResultXmlData xmlData = ParseResultElement(resultElement);
                    string statusLabel = Capitalize(xmlData.Status);
                    preview.AppendLine($"{xmlData.Name} status: {statusLabel}");

                    if (!string.IsNullOrWhiteSpace(xmlData.ApprovedBy))
                    {
                        preview.AppendLine($"  approvedby: {xmlData.ApprovedBy}");
                    }

                    if (!string.IsNullOrWhiteSpace(xmlData.Description))
                    {
                        preview.AppendLine($"  описание: {xmlData.Description}");
                    }
                }

                preview.AppendLine(new string('-', 87));
            }

            preview.AppendLine();
        }

        private static XmlDocument LoadXml(string path)
        {
            var document = new XmlDocument();
            document.Load(path);
            return document;
        }

        private static bool IsValidReport(XmlDocument document)
        {
            return document.GetElementsByTagName(RootElementName).Count > 0 ||
                   document.GetElementsByTagName(ResultElementName).Count > 0;
        }

        private static bool IsMasterReport(XmlDocument document)
        {
            return document.GetElementsByTagName(RootElementName).Count > 2;
        }

        private static bool HasAttribute(XmlDocument document, string elementName, string attributeName)
        {
            foreach (XmlElement element in document.GetElementsByTagName(elementName))
            {
                if (element.HasAttribute(attributeName))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool HasPositiveSummaryValue(XmlDocument document, string attributeName)
        {
            foreach (XmlNode node in document.GetElementsByTagName("summary"))
            {
                XmlAttribute attribute = node.Attributes?[attributeName];
                if (attribute != null &&
                    int.TryParse(attribute.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value) &&
                    value > 0)
                {
                    return true;
                }
            }

            return false;
        }

        private static string Capitalize(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return value;
            }

            return char.ToUpper(value[0], CultureInfo.CurrentCulture) + value.Substring(1);
        }
    }

    /// <summary>Служебный автокомментарий Clash Detective, его не пишем в БД и не возвращаем в Navisworks.</summary>
    internal static class ClashAutoResolveComment
    {
        private const string Russian =
            "Конфликт решен автоматически, так как объекты больше не конфликтуют.";
        private const string English =
            "Clash resolved automatically because the objects no longer clash.";

        /// <summary>True, если текст — автокомментарий Navisworks после пересчёта проверки.</summary>
        internal static bool IsMatch(string text)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(text))
                    return false;

                string trimmed = text.Trim();
                return string.Equals(trimmed, Russian, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(trimmed, English, StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }
    }
}
