using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Threading;
using Autodesk.Navisworks.Api;
using Autodesk.Navisworks.Api.Clash;
using Encoder = System.Drawing.Imaging.Encoder;
using NavisworksApplication = Autodesk.Navisworks.Api.Application;

namespace SmartNavisTools
{
    /// <summary>Как включать превью пересечений в снимок для БД.</summary>
    internal enum ClashImageSendMode
    {
        None,
        /// <summary>Снимать картинку, если у этого ClashGuid ещё нет превью на сервере.</summary>
        NewOnly,
        All
    }

    /// <summary>Собирает JSON-снимок выбранных проверок Clash Detective для SP-Service.</summary>
    internal static class SpClashSnapshotBuilder
    {
        private const int PreviewWidth = 320;
        private const int PreviewHeight = 240;
        private const long JpegQuality = 55L;

        /// <summary>
        /// Коэффициент футы → метры (внутренние единицы Navisworks API).
        /// Совпадает с обратным пересчётом в SP_LoadClashes: метры * 1000 / 304.8.
        /// </summary>
        private const double FeetToMeters = 0.3048;

        /// <summary>Строит тело POST snapshot по выбранным тестам.</summary>
        public static Dictionary<string, object> Build(ClashTest[] selectedTests)
        {
            return Build(selectedTests, null, ClashImageSendMode.All, null, null);
        }

        /// <summary>Строит снимок только по выбранным статусам пересечений.</summary>
        public static Dictionary<string, object> Build(
            ClashTest[] selectedTests,
            HashSet<ClashResultStatus> enabledStatuses)
        {
            return Build(selectedTests, enabledStatuses, ClashImageSendMode.All, null, null);
        }

        /// <summary>Строит снимок с фильтром статусов и режимом картинок.</summary>
        public static Dictionary<string, object> Build(
            ClashTest[] selectedTests,
            HashSet<ClashResultStatus> enabledStatuses,
            ClashImageSendMode imageMode,
            HashSet<string> skipPreviewGuids,
            Action<int, int> progress)
        {
            if (selectedTests == null)
                throw new ArgumentNullException("selectedTests");

            string documentName = GetDocumentName();
            string reportName = BuildReportName(selectedTests);

            List<PendingClash> pending = new List<PendingClash>();
            ClashDimensionResolver resolver = new ClashDimensionResolver();
            DocumentClashTests testsData = null;
            try
            {
                Document document = NavisworksApplication.MainDocument;
                if (document != null)
                    testsData = document.GetClash()?.TestsData;
            }
            catch
            {
            }

            for (int i = 0; i < selectedTests.Length; i++)
            {
                ClashTest test = selectedTests[i];
                if (test == null)
                    continue;

                string testName = test.DisplayName ?? string.Empty;
                CollectFromGroup(test, testName, null, enabledStatuses, pending);
            }

            int captureTotal = 0;
            for (int i = 0; i < pending.Count; i++)
            {
                if (ShouldCapturePreview(pending[i].Result, imageMode, skipPreviewGuids))
                    captureTotal++;
            }

            List<Dictionary<string, object>> results = new List<Dictionary<string, object>>(pending.Count);
            for (int i = 0; i < pending.Count; i++)
            {
                PendingClash mappedItem = pending[i];
                results.Add(MapResult(resolver, mappedItem.TestName, mappedItem.GroupName, mappedItem.Result));
            }

            int captureDone = 0;
            int encodeLimit = Math.Max(1, Environment.ProcessorCount);
            Semaphore encodeSlots = new Semaphore(encodeLimit, encodeLimit);
            CountdownEvent encodesDone = new CountdownEvent(1);
            ImageCodecInfo jpegCodec = GetJpegCodec();

            try
            {
                for (int i = 0; i < pending.Count; i++)
                {
                    PendingClash item = pending[i];
                    if (!ShouldCapturePreview(item.Result, imageMode, skipPreviewGuids))
                        continue;

                    Bitmap clone = TryCapturePreviewBitmap(testsData, item.Result);
                    captureDone++;
                    ReportProgress(progress, captureDone, captureTotal);

                    if (clone == null)
                        continue;

                    QueueJpegEncode(encodesDone, encodeSlots, jpegCodec, results[i], clone);
                }

                encodesDone.Signal();
                encodesDone.Wait();
            }
            finally
            {
                encodeSlots.Dispose();
                encodesDone.Dispose();
            }

            return new Dictionary<string, object>
            {
                { "name", reportName },
                { "sourceDocumentName", documentName },
                { "results", results }
            };
        }

        /// <summary>Имя отчёта совпадает с тем, что уходит в upsert.</summary>
        public static string BuildReportName(ClashTest[] selectedTests)
        {
            if (selectedTests == null)
                return GetDocumentName() + " / проверки";

            return GetDocumentName() + " / " + BuildTestsPart(selectedTests);
        }

        /// <summary>Обходит тест или группу и собирает пересечения с подходящим статусом.</summary>
        private static void CollectFromGroup(
            GroupItem group,
            string testName,
            string groupName,
            HashSet<ClashResultStatus> enabledStatuses,
            List<PendingClash> pending)
        {
            if (group == null)
                return;

            for (int index = 0; index < group.Children.Count; index++)
            {
                SavedItem child = group.Children[index];
                ClashResult result = child as ClashResult;
                if (result != null)
                {
                    try
                    {
                        if (!IsStatusEnabled(result.Status, enabledStatuses))
                            continue;

                        pending.Add(new PendingClash
                        {
                            Result = result,
                            TestName = testName,
                            GroupName = groupName
                        });
                    }
                    catch
                    {
                    }

                    continue;
                }

                ClashResultGroup nested = child as ClashResultGroup;
                if (nested != null)
                    CollectFromGroup(nested, testName, nested.DisplayName, enabledStatuses, pending);
            }
        }

        /// <summary>null — все статусы; пустой набор — ничего не включать.</summary>
        private static bool IsStatusEnabled(
            ClashResultStatus status,
            HashSet<ClashResultStatus> enabledStatuses)
        {
            if (enabledStatuses == null || enabledStatuses.Count == 0)
                return enabledStatuses == null;

            return enabledStatuses.Contains(status);
        }

        /// <summary>
        /// Нужно ли снимать превью.
        /// NewOnly: пропускаем только те GUID, у которых превью уже есть на сервере (не статус New).
        /// </summary>
        private static bool ShouldCapturePreview(
            ClashResult result,
            ClashImageSendMode imageMode,
            HashSet<string> skipPreviewGuids)
        {
            if (result == null || imageMode == ClashImageSendMode.None)
                return false;

            if (imageMode == ClashImageSendMode.All)
                return true;

            try
            {
                string guid = result.Guid.ToString();
                if (skipPreviewGuids != null && skipPreviewGuids.Contains(guid))
                    return false;
            }
            catch
            {
            }

            return true;
        }

        /// <summary>Преобразует одно пересечение в элемент снимка без картинки.</summary>
        private static Dictionary<string, object> MapResult(
            ClashDimensionResolver resolver,
            string testName,
            string groupName,
            ClashResult result)
        {
            ClashDimensionResolver.ElementIdentity item1 =
                resolver.ResolveElementIdentity(result.CompositeItem1);
            ClashDimensionResolver.ElementIdentity item2 =
                resolver.ResolveElementIdentity(result.CompositeItem2);

            Dictionary<string, object> item = new Dictionary<string, object>
            {
                { "clashGuid", result.Guid.ToString() },
                { "testName", testName ?? string.Empty },
                { "groupName", groupName ?? string.Empty },
                { "name", result.DisplayName ?? string.Empty },
                { "status", MapStatus(result.Status) },
                { "comment", ResolveDescription(result) },
                { "approvedBy", ResolveApprovedBy(result) },
                { "approvedDate", FormatDate(TryGetApprovedTime(result)) },
                { "date", FormatDate(TryGetCreatedTime(result)) },
                { "element1Id", item1 != null ? item1.Id : string.Empty },
                { "element1Name", item1 != null ? item1.Name : string.Empty },
                { "element1Source", resolver.ResolveModelNamePublic(result.CompositeItem1) },
                { "element2Id", item2 != null ? item2.Id : string.Empty },
                { "element2Name", item2 != null ? item2.Name : string.Empty },
                { "element2Source", resolver.ResolveModelNamePublic(result.CompositeItem2) }
            };

            try
            {
                // ClashResult.Center в API всегда в футах; в БД и XML pos3f нужны метры.
                Point3D center = result.Center;
                item["x"] = FeetToMetersValue(center.X);
                item["y"] = FeetToMetersValue(center.Y);
                item["z"] = FeetToMetersValue(center.Z);
            }
            catch
            {
            }

            return item;
        }

        /// <summary>Переводит длину из внутренних футов Navisworks в метры для SP-Service.</summary>
        private static double FeetToMetersValue(double feet)
        {
            try
            {
                return feet * FeetToMeters;
            }
            catch
            {
                return feet;
            }
        }

        /// <summary>Снимает Bitmap на UI-потоке и сразу клонирует его для кодирования.</summary>
        private static Bitmap TryCapturePreviewBitmap(DocumentClashTests testsData, ClashResult result)
        {
            if (testsData == null || result == null)
                return null;

            try
            {
#if Version2026
                using (Bitmap bitmap = testsData.TestsImageForResult(
                    result, ImageGenerationStyle.ScenePlusOverlay, PreviewWidth, PreviewHeight))
                {
                    if (bitmap == null)
                        return null;

                    return (Bitmap)bitmap.Clone();
                }
#else
                // TestsImageForResult недоступен в Navisworks 2022 API.
                return null;
#endif
            }
            catch
            {
                return null;
            }
        }

        /// <summary>JPEG+base64 в пуле потоков, пока UI снимает следующий кадр.</summary>
        private static void QueueJpegEncode(
            CountdownEvent encodesDone,
            Semaphore encodeSlots,
            ImageCodecInfo jpegCodec,
            Dictionary<string, object> item,
            Bitmap bitmap)
        {
            encodesDone.AddCount();
            ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    encodeSlots.WaitOne();
                    try
                    {
                        string base64 = EncodeJpegBase64(bitmap, jpegCodec);
                        if (!string.IsNullOrEmpty(base64))
                        {
                            lock (item)
                            {
                                item["imageBase64"] = base64;
                                object guidObj;
                                string fileName = "clash.jpg";
                                if (item.TryGetValue("clashGuid", out guidObj) && guidObj != null)
                                    fileName = guidObj + ".jpg";
                                item["imageFileName"] = fileName;
                            }
                        }
                    }
                    finally
                    {
                        encodeSlots.Release();
                    }
                }
                catch
                {
                }
                finally
                {
                    try
                    {
                        bitmap.Dispose();
                    }
                    catch
                    {
                    }

                    encodesDone.Signal();
                }
            });
        }

        /// <summary>Кодирует JPEG с заданным качеством в base64.</summary>
        private static string EncodeJpegBase64(Bitmap bitmap, ImageCodecInfo jpegCodec)
        {
            if (bitmap == null)
                return null;

            try
            {
                using (MemoryStream stream = new MemoryStream())
                {
                    if (jpegCodec != null)
                    {
                        using (EncoderParameters parameters = new EncoderParameters(1))
                        {
                            parameters.Param[0] = new EncoderParameter(Encoder.Quality, JpegQuality);
                            bitmap.Save(stream, jpegCodec, parameters);
                        }
                    }
                    else
                    {
                        bitmap.Save(stream, ImageFormat.Jpeg);
                    }

                    return Convert.ToBase64String(stream.ToArray());
                }
            }
            catch
            {
                return null;
            }
        }

        /// <summary>Ищет кодек JPEG в GDI+.</summary>
        private static ImageCodecInfo GetJpegCodec()
        {
            try
            {
                ImageCodecInfo[] codecs = ImageCodecInfo.GetImageEncoders();
                for (int i = 0; i < codecs.Length; i++)
                {
                    if (string.Equals(codecs[i].MimeType, "image/jpeg", StringComparison.OrdinalIgnoreCase))
                        return codecs[i];
                }
            }
            catch
            {
            }

            return null;
        }

        /// <summary>Обновляет текст прогресса съёмки превью.</summary>
        private static void ReportProgress(Action<int, int> progress, int done, int total)
        {
            if (progress == null || total <= 0)
                return;

            try
            {
                progress(done, total);
            }
            catch
            {
            }
        }

        /// <summary>Описание из Clash Detective, без Comments.Body и автокомментария Navisworks.</summary>
        private static string ResolveDescription(ClashResult result)
        {
            string description = SafeDescription(result);
            if (string.IsNullOrWhiteSpace(description))
                return string.Empty;

            if (ClashAutoResolveComment.IsMatch(description))
                return string.Empty;

            return description;
        }

        /// <summary>Описание пересечения в сетке Clash Detective.</summary>
        private static string SafeDescription(ClashResult result)
        {
            try
            {
                return result.Description;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>Имя утвердившего. В 2026 ApprovedBy — Assignee.</summary>
        private static string ResolveApprovedBy(ClashResult result)
        {
            try
            {
#if Version2026
                if (result.ApprovedBy != null)
                    return result.ApprovedBy.DisplayName;
                return null;
#else
                return result.ApprovedBy;
#endif
            }
            catch
            {
                return null;
            }
        }

        /// <summary>Дата создания пересечения в Clash Detective.</summary>
        private static DateTime? TryGetCreatedTime(ClashResult result)
        {
            try
            {
                return NullIfUnsetDate(result.CreatedTime);
            }
            catch
            {
                return null;
            }
        }

        /// <summary>Дата утверждения, если задана.</summary>
        private static DateTime? TryGetApprovedTime(ClashResult result)
        {
            try
            {
                return NullIfUnsetDate(result.ApprovedTime);
            }
            catch
            {
                return null;
            }
        }

        /// <summary>Пустые даты Navisworks: Unix-эпоха, MinValue, OLE-ноль.</summary>
        private static DateTime? NullIfUnsetDate(DateTime? value)
        {
            if (!value.HasValue)
                return null;

            DateTime date = value.Value;
            if (IsUnsetNavisworksDate(date))
                return null;

            return date;
        }

        /// <summary>Признак служебной даты, которую нельзя писать в БД.</summary>
        private static bool IsUnsetNavisworksDate(DateTime value)
        {
            try
            {
                if (value.Year <= 1970)
                    return true;

                DateTime utc = value.Kind == DateTimeKind.Local ? value.ToUniversalTime() : value;
                if (utc.Year <= 1970)
                    return true;
            }
            catch
            {
                return true;
            }

            return false;
        }

        /// <summary>Формат даты как в XML-отчёте Navisworks.</summary>
        private static string FormatDate(DateTime? value)
        {
            if (!value.HasValue || IsUnsetNavisworksDate(value.Value))
                return string.Empty;

            return value.Value.ToString("dd.MM.yyyy HH:mm", CultureInfo.InvariantCulture);
        }

        /// <summary>Английский код статуса для API и Clash Report XML.</summary>
        private static string MapStatus(ClashResultStatus status)
        {
            switch (status)
            {
                case ClashResultStatus.New:
                    return "new";
                case ClashResultStatus.Active:
                    return "active";
                case ClashResultStatus.Reviewed:
                    return "reviewed";
                case ClashResultStatus.Approved:
                    return "approved";
                case ClashResultStatus.Resolved:
                    return "resolved";
                default:
                    return "active";
            }
        }

        /// <summary>Имя открытого документа Navisworks.</summary>
        private static string GetDocumentName()
        {
            Document document = NavisworksApplication.MainDocument;
            if (document == null)
                return "Navisworks";

            try
            {
                string path = document.FileName;
                if (!string.IsNullOrWhiteSpace(path))
                    return Path.GetFileNameWithoutExtension(path);
            }
            catch
            {
            }

            try
            {
                if (!string.IsNullOrWhiteSpace(document.Title))
                    return document.Title;
            }
            catch
            {
            }

            return "Navisworks";
        }

        /// <summary>Краткое описание выбранных проверок для имени отчёта.</summary>
        private static string BuildTestsPart(ClashTest[] selectedTests)
        {
            if (selectedTests.Length == 1 && selectedTests[0] != null)
                return selectedTests[0].DisplayName ?? "проверка";

            List<string> names = new List<string>();
            for (int i = 0; i < selectedTests.Length; i++)
            {
                if (selectedTests[i] == null)
                    continue;
                names.Add(selectedTests[i].DisplayName ?? string.Empty);
            }

            string joined = string.Join(", ", names.ToArray());
            if (joined.Length > 80)
                return selectedTests.Length + " проверок";

            return string.IsNullOrWhiteSpace(joined) ? "проверки" : joined;
        }

        /// <summary>Пересечение, отложенное для снимка.</summary>
        private sealed class PendingClash
        {
            public ClashResult Result;
            public string TestName;
            public string GroupName;
        }
    }
}
