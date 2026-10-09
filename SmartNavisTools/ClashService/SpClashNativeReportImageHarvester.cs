using System;
using System.Collections.Generic;
using System.IO;
using System.Xml;

namespace SmartNavisTools
{
    /// <summary>
    /// Достаёт картинки из XML Write Report Navisworks и кладёт их в снимок для БД.
    /// </summary>
    internal static class SpClashNativeReportImageHarvester
    {
        internal sealed class HarvestResult
        {
            public int AttachedCount;
            public int MissingCount;
            public string ErrorMessage;
        }

        /// <summary>
        /// Читает clashresult/@guid + href (или соседние jpg) и заполняет imageBase64 в results.
        /// </summary>
        public static HarvestResult AttachImages(
            Dictionary<string, object> snapshot,
            IList<string> xmlPaths,
            ClashImageSendMode imageMode,
            HashSet<string> skipPreviewGuids)
        {
            var result = new HarvestResult();
            if (snapshot == null)
            {
                result.ErrorMessage = "Снимок пуст.";
                return result;
            }

            if (xmlPaths == null || xmlPaths.Count == 0)
            {
                result.ErrorMessage = "Не выбран XML Write Report.";
                return result;
            }

            object resultsObj;
            if (!snapshot.TryGetValue("results", out resultsObj))
            {
                result.ErrorMessage = "В снимке нет results.";
                return result;
            }

            List<Dictionary<string, object>> results = resultsObj as List<Dictionary<string, object>>;
            if (results == null || results.Count == 0)
                return result;

            Dictionary<string, string> guidToImagePath = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            Dictionary<string, string> nameToImagePath = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < xmlPaths.Count; i++)
            {
                string xmlPath = xmlPaths[i];
                if (string.IsNullOrWhiteSpace(xmlPath) || !File.Exists(xmlPath))
                    continue;

                try
                {
                    CollectFromXml(xmlPath, guidToImagePath, nameToImagePath);
                }
                catch (Exception ex)
                {
                    result.ErrorMessage = "Ошибка чтения «" + Path.GetFileName(xmlPath) + "»: " + ex.Message;
                    return result;
                }
            }

            for (int i = 0; i < results.Count; i++)
            {
                Dictionary<string, object> item = results[i];
                if (item == null)
                    continue;

                string guid = GetString(item, "clashGuid");
                if (!ShouldAttach(guid, imageMode, skipPreviewGuids))
                    continue;

                string imagePath = null;
                if (!string.IsNullOrWhiteSpace(guid))
                    guidToImagePath.TryGetValue(guid, out imagePath);

                if (string.IsNullOrWhiteSpace(imagePath))
                {
                    string name = GetString(item, "name");
                    if (!string.IsNullOrWhiteSpace(name))
                        nameToImagePath.TryGetValue(name, out imagePath);
                }

                if (string.IsNullOrWhiteSpace(imagePath) || !File.Exists(imagePath))
                {
                    result.MissingCount++;
                    continue;
                }

                try
                {
                    byte[] bytes = File.ReadAllBytes(imagePath);
                    if (bytes == null || bytes.Length == 0)
                    {
                        result.MissingCount++;
                        continue;
                    }

                    item["imageBase64"] = Convert.ToBase64String(bytes);
                    string extension = Path.GetExtension(imagePath);
                    if (string.IsNullOrWhiteSpace(extension))
                        extension = ".jpg";
                    item["imageFileName"] = (string.IsNullOrWhiteSpace(guid) ? "clash" : guid) + extension.ToLowerInvariant();
                    result.AttachedCount++;
                }
                catch
                {
                    result.MissingCount++;
                }
            }

            return result;
        }

        /// <summary>NewOnly: не трогаем GUID, у которых превью уже на сервере.</summary>
        private static bool ShouldAttach(
            string guid,
            ClashImageSendMode imageMode,
            HashSet<string> skipPreviewGuids)
        {
            if (imageMode == ClashImageSendMode.None)
                return false;

            if (imageMode == ClashImageSendMode.All)
                return true;

            if (string.IsNullOrWhiteSpace(guid))
                return true;

            return skipPreviewGuids == null || !skipPreviewGuids.Contains(guid);
        }

        /// <summary>Собирает guid/name → путь к файлу картинки из одного XML.</summary>
        private static void CollectFromXml(
            string xmlPath,
            Dictionary<string, string> guidToImagePath,
            Dictionary<string, string> nameToImagePath)
        {
            string baseDir = Path.GetDirectoryName(xmlPath) ?? string.Empty;
            XmlDocument document = new XmlDocument();
            document.Load(xmlPath);

            XmlNodeList nodes = document.GetElementsByTagName("clashresult");
            for (int i = 0; i < nodes.Count; i++)
            {
                XmlElement element = nodes[i] as XmlElement;
                if (element == null)
                    continue;

                string guid = element.GetAttribute("guid");
                string name = element.GetAttribute("name");
                string href = ResolveHref(element, baseDir);
                if (string.IsNullOrWhiteSpace(href))
                    href = TryFindImageByName(baseDir, name);

                if (string.IsNullOrWhiteSpace(href) || !File.Exists(href))
                    continue;

                if (!string.IsNullOrWhiteSpace(guid) && !guidToImagePath.ContainsKey(guid))
                    guidToImagePath[guid] = href;

                if (!string.IsNullOrWhiteSpace(name) && !nameToImagePath.ContainsKey(name))
                    nameToImagePath[name] = href;
            }
        }

        /// <summary>href у clashresult: атрибут, дочерний элемент или вложенные image/picture.</summary>
        private static string ResolveHref(XmlElement clashResult, string baseDir)
        {
            string href = clashResult.GetAttribute("href");
            string resolved = ResolveExistingPath(baseDir, href);
            if (resolved != null)
                return resolved;

            for (int i = 0; i < clashResult.ChildNodes.Count; i++)
            {
                XmlElement child = clashResult.ChildNodes[i] as XmlElement;
                if (child == null)
                    continue;

                string local = child.LocalName ?? string.Empty;
                if (string.Equals(local, "href", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(local, "image", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(local, "picture", StringComparison.OrdinalIgnoreCase)
                    || local.IndexOf("image", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    string candidate = child.InnerText;
                    if (string.IsNullOrWhiteSpace(candidate))
                        candidate = child.GetAttribute("href");
                    if (string.IsNullOrWhiteSpace(candidate))
                        candidate = child.GetAttribute("src");
                    if (string.IsNullOrWhiteSpace(candidate))
                        candidate = child.GetAttribute("path");

                    resolved = ResolveExistingPath(baseDir, candidate);
                    if (resolved != null)
                        return resolved;
                }
            }

            return null;
        }

        /// <summary>Ищет Name.jpg / Name.png рядом с XML и в подпапках первого уровня.</summary>
        private static string TryFindImageByName(string baseDir, string clashName)
        {
            if (string.IsNullOrWhiteSpace(baseDir) || string.IsNullOrWhiteSpace(clashName))
                return null;

            string safeName = SanitizeFileName(clashName);
            string[] extensions = { ".jpg", ".jpeg", ".png", ".bmp" };

            for (int e = 0; e < extensions.Length; e++)
            {
                string direct = Path.Combine(baseDir, safeName + extensions[e]);
                if (File.Exists(direct))
                    return direct;
            }

            try
            {
                string[] dirs = Directory.GetDirectories(baseDir);
                for (int d = 0; d < dirs.Length; d++)
                {
                    for (int e = 0; e < extensions.Length; e++)
                    {
                        string nested = Path.Combine(dirs[d], safeName + extensions[e]);
                        if (File.Exists(nested))
                            return nested;
                    }
                }
            }
            catch
            {
            }

            return null;
        }

        /// <summary>Превращает относительный href в абсолютный путь, если файл есть.</summary>
        private static string ResolveExistingPath(string baseDir, string href)
        {
            if (string.IsNullOrWhiteSpace(href))
                return null;

            href = href.Trim().Trim('"', '\'');
            if (href.StartsWith("navisworks:", StringComparison.OrdinalIgnoreCase))
                return null;

            string lower = href.ToLowerInvariant();
            bool looksLikeImage = lower.EndsWith(".jpg") || lower.EndsWith(".jpeg")
                || lower.EndsWith(".png") || lower.EndsWith(".bmp") || lower.EndsWith(".gif");
            if (!looksLikeImage && href.IndexOf('.') < 0)
                return null;

            try
            {
                string full = Path.IsPathRooted(href)
                    ? href
                    : Path.GetFullPath(Path.Combine(baseDir ?? string.Empty, href));
                return File.Exists(full) ? full : null;
            }
            catch
            {
                return null;
            }
        }

        private static string SanitizeFileName(string name)
        {
            char[] invalid = Path.GetInvalidFileNameChars();
            char[] chars = name.ToCharArray();
            for (int i = 0; i < chars.Length; i++)
            {
                for (int j = 0; j < invalid.Length; j++)
                {
                    if (chars[i] == invalid[j])
                    {
                        chars[i] = '_';
                        break;
                    }
                }
            }

            return new string(chars);
        }

        private static string GetString(Dictionary<string, object> item, string key)
        {
            object value;
            if (!item.TryGetValue(key, out value) || value == null)
                return null;
            return Convert.ToString(value);
        }
    }
}
