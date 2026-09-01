using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Autodesk.Navisworks.Api;

namespace SmartNavisTools
{
    /// <summary>
    /// Хранит и восстанавливает настройки дашборда коллизий для текущего документа.
    /// </summary>
    internal static class ClashDashboardSettingsStore
    {
        /// <summary>
        /// Возвращает сохранённые настройки или <c>null</c>, если файл не найден.
        /// </summary>
        public static Dictionary<string, string> Load(Document document)
        {
            string sidecarPath = GetSidecarPath(document);
            if (!string.IsNullOrWhiteSpace(sidecarPath) && File.Exists(sidecarPath))
            {
                return ReadSettingsMap(sidecarPath);
            }

            string appDataPath = GetFallbackAppDataPath(document);
            if (!string.IsNullOrWhiteSpace(appDataPath) && File.Exists(appDataPath))
            {
                return ReadSettingsMap(appDataPath);
            }

            return null;
        }

        /// <summary>
        /// Сохраняет настройки в sidecar-файл рядом с .nwf или в AppData для unsaved-документа.
        /// </summary>
        public static void Save(Document document, IReadOnlyDictionary<string, string> values)
        {
            if (document == null || values == null)
            {
                return;
            }

            string targetPath = GetSidecarPath(document);
            if (string.IsNullOrWhiteSpace(targetPath))
            {
                targetPath = GetFallbackAppDataPath(document);
            }

            if (string.IsNullOrWhiteSpace(targetPath))
            {
                return;
            }

            string directory = Path.GetDirectoryName(targetPath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var lines = values
                .Select(pair => pair.Key + "=" + EscapeCfgValue(pair.Value))
                .ToArray();
            File.WriteAllLines(targetPath, lines, Encoding.UTF8);
        }

        /// <summary>
        /// Возвращает путь sidecar-файла рядом с .nwf или пустую строку для unsaved-документа.
        /// </summary>
        public static string GetSidecarPath(Document document)
        {
            string fullPath = GetDocumentPath(document);
            if (string.IsNullOrWhiteSpace(fullPath))
            {
                return string.Empty;
            }

            string directory = Path.GetDirectoryName(fullPath);
            string fileNameWithoutExtension = Path.GetFileNameWithoutExtension(fullPath);
            if (string.IsNullOrWhiteSpace(directory) || string.IsNullOrWhiteSpace(fileNameWithoutExtension))
            {
                return string.Empty;
            }

            return Path.Combine(directory, fileNameWithoutExtension + ".SmartNavisTools.dashboard.cfg");
        }

        /// <summary>
        /// Возвращает fallback-путь в AppData для unsaved-документа.
        /// </summary>
        private static string GetFallbackAppDataPath(Document document)
        {
            string key = GetDocumentSettingsKey(document);
            if (string.IsNullOrWhiteSpace(key))
            {
                return string.Empty;
            }

            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string root = Path.Combine(appData, "SmartNavisTools", "settings");
            string safeFileName = SanitizeFileName(key) + ".dashboard.cfg";
            return Path.Combine(root, safeFileName);
        }

        /// <summary>
        /// Читает cfg-файл и преобразует его в словарь key=value.
        /// </summary>
        private static Dictionary<string, string> ReadSettingsMap(string cfgPath)
        {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (string line in File.ReadAllLines(cfgPath, Encoding.UTF8))
            {
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                int separatorIndex = line.IndexOf('=');
                if (separatorIndex <= 0)
                {
                    continue;
                }

                string key = line.Substring(0, separatorIndex).Trim();
                string value = line.Substring(separatorIndex + 1);
                map[key] = UnescapeCfgValue(value);
            }

            return map;
        }

        /// <summary>
        /// Экранирует спецсимволы при сохранении значения в cfg.
        /// </summary>
        private static string EscapeCfgValue(string value)
        {
            return (value ?? string.Empty)
                .Replace("\\", "\\\\")
                .Replace("\r", string.Empty)
                .Replace("\n", "\\n");
        }

        /// <summary>
        /// Восстанавливает экранированные спецсимволы после чтения cfg.
        /// </summary>
        private static string UnescapeCfgValue(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            return value
                .Replace("\\n", Environment.NewLine)
                .Replace("\\\\", "\\");
        }

        /// <summary>
        /// Формирует ключ настроек документа по имени файла без расширения.
        /// </summary>
        private static string GetDocumentSettingsKey(Document document)
        {
            string documentPath = GetDocumentPath(document);
            if (!string.IsNullOrWhiteSpace(documentPath))
            {
                return Path.GetFileNameWithoutExtension(documentPath);
            }

            try
            {
                return document?.Title;
            }
            catch
            {
                return "unsaved";
            }
        }

        /// <summary>
        /// Возвращает абсолютный путь документа Navisworks или пустую строку.
        /// </summary>
        private static string GetDocumentPath(Document document)
        {
            if (document == null || document.IsClear)
            {
                return string.Empty;
            }

            try
            {
                return document.FileName;
            }
            catch
            {
                return string.Empty;
            }
        }

        /// <summary>
        /// Приводит произвольную строку к безопасному имени файла.
        /// </summary>
        private static string SanitizeFileName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return "unnamed";
            }

            var invalidChars = Path.GetInvalidFileNameChars();
            var builder = new StringBuilder(name.Length);
            foreach (char current in name)
            {
                builder.Append(invalidChars.Contains(current) ? '_' : current);
            }

            string cleaned = builder.ToString().Trim();
            return string.IsNullOrWhiteSpace(cleaned) ? "unnamed" : cleaned;
        }
    }
}
