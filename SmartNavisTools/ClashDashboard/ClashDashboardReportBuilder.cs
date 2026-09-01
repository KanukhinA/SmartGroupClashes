using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using System.Web.Script.Serialization;

namespace SmartNavisTools
{
    /// <summary>
    /// Собирает итоговый HTML-документ дашборда и подготавливает ресурсы.
    /// </summary>
    internal static class ClashDashboardReportBuilder
    {
        private const string TemplateFileName = "clash-dashboard.html";
        private const string StyleFileName = "clash-dashboard.css";
        private const string PlotlyFileName = "plotly.min.js";
        private const string DataPlaceholder = "__CLASH_DATA_JSON__";

        /// <summary>
        /// Записывает отчёт на диск и копирует рядом статические ресурсы.
        /// </summary>
        public static void WriteReport(string htmlPath, IReadOnlyList<ClashDashboardLogic.DashboardRecord> records)
        {
            string baseDir = GetPluginBaseDirectory();
            string templatePath = TryFindFile(
                baseDir,
                TemplateFileName,
                new[]
                {
                    Path.Combine("Reports", TemplateFileName),
                    Path.Combine("ClashDashboard", "Reports", TemplateFileName),
                    TemplateFileName
                });

            if (string.IsNullOrWhiteSpace(templatePath))
            {
                throw new FileNotFoundException(
                    "Не найден шаблон отчёта. Проверьте наличие `clash-dashboard.html` рядом с плагином.",
                    TemplateFileName);
            }

            string templateDir = Path.GetDirectoryName(templatePath);
            string stylePath = Path.Combine(templateDir, StyleFileName);
            string plotlyPath = Path.Combine(templateDir, PlotlyFileName);

            string html = File.ReadAllText(templatePath, Encoding.UTF8);
            string serializedData = SerializeData(records);
            html = html.Replace(DataPlaceholder, serializedData);

            string targetDirectory = Path.GetDirectoryName(htmlPath);
            if (!string.IsNullOrWhiteSpace(targetDirectory))
            {
                Directory.CreateDirectory(targetDirectory);
            }

            File.WriteAllText(htmlPath, html, Encoding.UTF8);

            if (File.Exists(stylePath))
            {
                CopyIfChanged(stylePath, Path.Combine(targetDirectory, StyleFileName));
            }

            if (File.Exists(plotlyPath))
            {
                // plotly.min.js ~4 МБ: не копируем повторно, если файл уже актуален.
                CopyIfChanged(plotlyPath, Path.Combine(targetDirectory, PlotlyFileName));
            }
        }

        /// <summary>
        /// Копирует файл только если целевой отсутствует или отличается по размеру/дате.
        /// </summary>
        private static void CopyIfChanged(string sourcePath, string targetPath)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(sourcePath) || string.IsNullOrWhiteSpace(targetPath))
                {
                    return;
                }

                if (!File.Exists(sourcePath))
                {
                    return;
                }

                if (File.Exists(targetPath))
                {
                    FileInfo sourceInfo = new FileInfo(sourcePath);
                    FileInfo targetInfo = new FileInfo(targetPath);
                    if (sourceInfo.Length == targetInfo.Length
                        && sourceInfo.LastWriteTimeUtc <= targetInfo.LastWriteTimeUtc)
                    {
                        return;
                    }
                }

                string targetDir = Path.GetDirectoryName(targetPath);
                if (!string.IsNullOrWhiteSpace(targetDir))
                {
                    Directory.CreateDirectory(targetDir);
                }

                File.Copy(sourcePath, targetPath, true);
            }
            catch
            {
                try
                {
                    File.Copy(sourcePath, targetPath, true);
                }
                catch
                {
                    // Ошибка копирования ресурса не должна ломать уже записанный HTML.
                }
            }
        }

        /// <summary>
        /// Ищет файл в нескольких ожидаемых относительных папках относительно <paramref name="baseDir"/>.
        /// Если не найден, делает более глубокий поиск внутри <paramref name="baseDir"/>.
        /// </summary>
        private static string TryFindFile(string baseDir, string fileName, IEnumerable<string> relativeCandidates)
        {
            if (string.IsNullOrWhiteSpace(baseDir) || string.IsNullOrWhiteSpace(fileName))
            {
                return string.Empty;
            }

            if (relativeCandidates != null)
            {
                foreach (string relative in relativeCandidates)
                {
                    try
                    {
                        string candidatePath = Path.Combine(baseDir, relative);
                        if (File.Exists(candidatePath))
                        {
                            return candidatePath;
                        }
                    }
                    catch
                    {
                        // Продолжаем поиск в других кандидатах.
                    }
                }
            }

            try
            {
                string[] found = Directory.GetFiles(baseDir, fileName, SearchOption.AllDirectories);
                if (found != null && found.Length > 0)
                {
                    return found[0];
                }
            }
            catch
            {
                // Если глубокий поиск не удался, возвращаем пустую строку.
            }

            // Fallback: если шаблон не скопировался в папку конкретной версии (например, Contents/2022),
            // но есть в корне пакета SmartNavisTools.bundle для другой версии, попробуем найти его там.
            try
            {
                DirectoryInfo baseInfo = new DirectoryInfo(baseDir);
                DirectoryInfo bundleRoot = baseInfo.Parent?.Parent?.Parent;
                if (bundleRoot != null && bundleRoot.Exists)
                {
                    string[] bundleFound = Directory.GetFiles(
                        bundleRoot.FullName,
                        fileName,
                        SearchOption.AllDirectories);
                    if (bundleFound != null && bundleFound.Length > 0)
                    {
                        return bundleFound[0];
                    }
                }
            }
            catch
            {
                // Если и этот поиск не удался, возвращаем пустую строку.
            }

            return string.Empty;
        }

        /// <summary>
        /// Возвращает каталог, где лежит SmartNavisTools.dll (а не каталог Roamer.exe).
        /// </summary>
        private static string GetPluginBaseDirectory()
        {
            try
            {
                string assemblyPath = typeof(ClashDashboardReportBuilder).Assembly.Location;
                if (!string.IsNullOrWhiteSpace(assemblyPath))
                {
                    string pluginDir = Path.GetDirectoryName(assemblyPath);
                    if (!string.IsNullOrWhiteSpace(pluginDir) && Directory.Exists(pluginDir))
                    {
                        return pluginDir;
                    }
                }
            }
            catch
            {
                // Переходим к fallback ниже.
            }

            return AppDomain.CurrentDomain.BaseDirectory ?? string.Empty;
        }

        /// <summary>
        /// Сериализует записи в JSON-объект, который читается JavaScript-кодом шаблона.
        /// </summary>
        private static string SerializeData(IReadOnlyList<ClashDashboardLogic.DashboardRecord> records)
        {
            var serializer = new JavaScriptSerializer
            {
                MaxJsonLength = int.MaxValue
            };

            var payload = new
            {
                generatedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                percentMode = false,
                records = records ?? Array.Empty<ClashDashboardLogic.DashboardRecord>()
            };

            return serializer.Serialize(payload);
        }
    }
}
