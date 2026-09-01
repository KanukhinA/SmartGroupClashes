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
        private const string ApprovedByElementName = "approvedby";
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
                    ClashResultXmlData xmlData = ParseResultElement(resultElement);
                    if (string.Equals(xmlData.Status, "resolved", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    UpdateMatchingResult(testsData, matchingTest, xmlData);
                }
            }
        }

        private static ClashResultXmlData ParseResultElement(XmlElement resultElement)
        {
            return new ClashResultXmlData
            {
                Name = resultElement.GetAttribute(ResultNameAttribute),
                Guid = resultElement.GetAttribute(ResultGuidAttribute),
                Status = resultElement.GetAttribute(ResultStatusAttribute),
                ApprovedBy = GetChildElementText(resultElement, ApprovedByElementName),
                Description = GetCommentDescription(resultElement)
            };
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
                if (!string.IsNullOrWhiteSpace(body))
                {
                    bodies.Add(body);
                }
            }

            return string.Join(Environment.NewLine, bodies);
        }

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

            if (string.Equals(xmlData.Status, "approved", StringComparison.OrdinalIgnoreCase))
            {
                SetResultStatus(testsData, clashResult, ClashResultStatus.Approved, xmlData.ApprovedBy);
            }
            else if (string.Equals(xmlData.Status, "reviewed", StringComparison.OrdinalIgnoreCase))
            {
                SetResultStatus(testsData, clashResult, ClashResultStatus.Reviewed, xmlData.ApprovedBy);
            }

            if (!string.IsNullOrWhiteSpace(xmlData.Description))
            {
                testsData.TestsEditResultDescription(clashResult, xmlData.Description);
            }

            if (!string.IsNullOrWhiteSpace(xmlData.ApprovedBy))
            {
                SetApprovedBy(testsData, clashResult, xmlData.ApprovedBy);
            }
        }

        /// <summary>
        /// Ищет пересечение в тесте: сначала по GUID из отчёта, затем по имени (в т.ч. внутри групп).
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
                SavedItem resolvedItem = testsData.ResolveGuid(parsedGuid);
                if (resolvedItem is ClashResult resolvedResult &&
                    string.Equals(resolvedResult.DisplayName, displayName, StringComparison.Ordinal))
                {
                    return resolvedResult;
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

            bool hasReviewedInSummary = HasPositiveSummaryValue(document, "reviewed");
            bool hasApprovedInSummary = HasPositiveSummaryValue(document, "approved");
            bool hasReviewedInResults = HasStatusValue(document, "reviewed");
            bool hasApprovedInResults = HasStatusValue(document, "approved");

            if (HasAttribute(document, ResultElementName, ResultStatusAttribute) &&
                hasReviewedInSummary &&
                hasApprovedInSummary &&
                !hasReviewedInResults &&
                !hasApprovedInResults)
            {
                return "В Included Clashes Report отметьте хотя бы «Reviewed» или «Approved».";
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

        private static bool HasStatusValue(XmlDocument document, string statusValue)
        {
            foreach (XmlElement element in document.GetElementsByTagName(ResultElementName))
            {
                if (string.Equals(element.GetAttribute(ResultStatusAttribute), statusValue, StringComparison.OrdinalIgnoreCase))
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
}
