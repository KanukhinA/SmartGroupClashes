using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Input;
using Autodesk.Navisworks.Api.Clash;

namespace SmartNavisTools
{
    /// <summary>
    /// Вызов стандартного экспорта Clash Detective (Write Report) через внутренний ReportTabVM.
    /// </summary>
    internal static class ClashNativeReportBridge
    {
        private const string ClashPluginAssemblyName = "Navisworks.Clash.Plugin";
        private const string ReportTabVmTypeName = "Autodesk.Navisworks.Clash.ReportTabVM";
        private const string ReportTabTypeName = "Autodesk.Navisworks.Clash.ReportTab";

        private static readonly string XmlFormatterUid = "lcclash_report_xml";
        private static readonly string HtmlFormatterUid = "lcclash_report_html_tabular";

        internal sealed class NativeExportResult
        {
            public bool Success { get; set; }
            public string ErrorMessage { get; set; }
            public string WarningMessage { get; set; }
            public int InvokedCount { get; set; }
        }

        public static NativeExportResult ExportTests(
            ClashTest[] tests,
            ClashDetectiveTestsLogic.ExportFormat format,
            bool includeImages = true)
        {
            if (tests == null || tests.Length == 0)
            {
                return new NativeExportResult
                {
                    Success = false,
                    ErrorMessage = "Выберите хотя бы одну проверку."
                };
            }

            object reportTabVm = TryFindReportTabVm();
            if (reportTabVm == null)
            {
                return new NativeExportResult
                {
                    Success = false,
                    ErrorMessage = "Откройте Clash Detective и перейдите на вкладку Report — оттуда берутся настройки экспорта."
                };
            }

            List<BoolPropertyBackup> imageBackups = null;
            List<NullableBoolPropertyBackup> reportFieldBackups = null;
            try
            {
                TryRefreshClashUnits(reportTabVm);

                if (!includeImages)
                {
                    imageBackups = TryDisableImageFlags(reportTabVm);
                    reportFieldBackups = TrySetImageReportFields(reportTabVm, false);
                }
                else
                {
                    // Для режима «картинки из Write Report» принудительно включаем Image в полях отчёта.
                    imageBackups = TryEnableImageFlags(reportTabVm);
                    reportFieldBackups = TrySetImageReportFields(reportTabVm, true);
                }

                if (!TrySetReportFormatter(reportTabVm, format))
                {
                    return new NativeExportResult
                    {
                        Success = false,
                        ErrorMessage = "Не удалось выбрать формат отчёта в Clash Detective."
                    };
                }

                ICommand writeReport = GetWriteReportCommand(reportTabVm);
                if (writeReport == null || !writeReport.CanExecute(null))
                {
                    return new NativeExportResult
                    {
                        Success = false,
                        ErrorMessage = "Команда Write Report недоступна. Проверьте настройки на вкладке Report в Clash Detective."
                    };
                }

                int invoked = 0;
                foreach (ClashTest test in tests)
                {
                    if (!TrySelectTest(reportTabVm, test))
                    {
                        return new NativeExportResult
                        {
                            Success = false,
                            ErrorMessage = "Не удалось выбрать проверку «" + (test.DisplayName ?? string.Empty) + "» в Clash Detective."
                        };
                    }

                    writeReport.Execute(null);
                    invoked++;
                }

                string warning = null;
                if (!includeImages && (imageBackups == null || imageBackups.Count == 0))
                {
                    warning = "Не найден флажок картинок на вкладке Report. Содержимое отчёта задаётся настройками Clash Detective.";
                }

                return new NativeExportResult
                {
                    Success = invoked > 0,
                    InvokedCount = invoked,
                    WarningMessage = warning
                };
            }
            catch (Exception ex)
            {
                return new NativeExportResult
                {
                    Success = false,
                    ErrorMessage = "Ошибка стандартного экспорта Clash Detective: " + ex.Message
                };
            }
            finally
            {
                RestoreBoolProperties(imageBackups);
                RestoreNullableBoolProperties(reportFieldBackups);
            }
        }

        private static object TryFindReportTabVm()
        {
            Assembly pluginAssembly = TryLoadClashPluginAssembly();
            if (pluginAssembly == null)
            {
                return null;
            }

            Type reportTabType = pluginAssembly.GetType(ReportTabTypeName, throwOnError: false);
            Type reportTabVmType = pluginAssembly.GetType(ReportTabVmTypeName, throwOnError: false);
            if (reportTabType == null || reportTabVmType == null)
            {
                return null;
            }

            Type applicationType = Type.GetType("System.Windows.Application, PresentationFramework", throwOnError: false);
            Type visualType = Type.GetType("System.Windows.Media.Visual, PresentationFramework", throwOnError: false);
            Type helperType = Type.GetType("System.Windows.Media.VisualTreeHelper, PresentationFramework", throwOnError: false);
            if (applicationType == null || visualType == null || helperType == null)
            {
                return null;
            }

            object application = applicationType.GetProperty("Current")?.GetValue(null, null);
            if (application == null)
            {
                return null;
            }

            IEnumerable windows = applicationType.GetProperty("Windows")?.GetValue(application, null) as IEnumerable;
            if (windows == null)
            {
                return null;
            }

            MethodInfo getChild = helperType.GetMethod("GetChild", new[] { visualType, typeof(int) });
            MethodInfo getChildrenCount = helperType.GetMethod("GetChildrenCount", new[] { visualType });
            if (getChild == null || getChildrenCount == null)
            {
                return null;
            }

            foreach (object window in windows)
            {
                object found = FindReportTabVmInVisual(
                    window,
                    reportTabType,
                    reportTabVmType,
                    visualType,
                    getChild,
                    getChildrenCount);
                if (found != null)
                {
                    return found;
                }
            }

            return null;
        }

        private static object FindReportTabVmInVisual(
            object visual,
            Type reportTabType,
            Type reportTabVmType,
            Type visualType,
            MethodInfo getChild,
            MethodInfo getChildrenCount)
        {
            if (visual == null)
            {
                return null;
            }

            if (reportTabType.IsInstanceOfType(visual))
            {
                object dataContext = visual.GetType().GetProperty("DataContext")?.GetValue(visual, null);
                if (dataContext != null && reportTabVmType.IsInstanceOfType(dataContext))
                {
                    return dataContext;
                }
            }

            if (!visualType.IsInstanceOfType(visual))
            {
                return null;
            }

            int childCount = (int)getChildrenCount.Invoke(null, new[] { visual });
            for (int index = 0; index < childCount; index++)
            {
                object child = getChild.Invoke(null, new[] { visual, index });
                object found = FindReportTabVmInVisual(
                    child,
                    reportTabType,
                    reportTabVmType,
                    visualType,
                    getChild,
                    getChildrenCount);
                if (found != null)
                {
                    return found;
                }
            }

            return null;
        }

        private static Assembly TryLoadClashPluginAssembly()
        {
            Assembly loaded = AppDomain.CurrentDomain.GetAssemblies()
                .FirstOrDefault(assembly => string.Equals(assembly.GetName().Name, ClashPluginAssemblyName, StringComparison.OrdinalIgnoreCase));
            if (loaded != null)
            {
                return loaded;
            }

            string navisworksFolder = Path.GetDirectoryName(
                typeof(Autodesk.Navisworks.Api.Application).Assembly.Location);
            if (string.IsNullOrEmpty(navisworksFolder))
            {
                return null;
            }

            string pluginPath = Path.Combine(navisworksFolder, ClashPluginAssemblyName + ".dll");
            return File.Exists(pluginPath) ? Assembly.LoadFrom(pluginPath) : null;
        }

        /// <summary>
        /// Синхронизирует единицы Clash Detective с текущими единицами документа перед экспортом.
        /// </summary>
        private static void TryRefreshClashUnits(object reportTabVm)
        {
            try
            {
                object clashService = GetPropertyValue<object>(reportTabVm, "Service");
                if (clashService == null)
                {
                    return;
                }

                MethodInfo refreshUnits = clashService.GetType().GetMethod(
                    "RefreshUnits",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                refreshUnits?.Invoke(clashService, null);
            }
            catch
            {
                // Не блокируем экспорт, если синхронизация недоступна.
            }
        }

        private static bool TrySetReportFormatter(object reportTabVm, ClashDetectiveTestsLogic.ExportFormat format)
        {
            string formatterUid = format == ClashDetectiveTestsLogic.ExportFormat.Xml
                ? XmlFormatterUid
                : HtmlFormatterUid;

            IEnumerable formatters = GetPropertyValue<IEnumerable>(reportTabVm, "ReportFormatters");
            if (formatters == null)
            {
                return false;
            }

            object formatter = formatters.Cast<object>()
                .FirstOrDefault(item => string.Equals(GetPropertyValue<string>(item, "Uid"), formatterUid, StringComparison.OrdinalIgnoreCase));
            if (formatter == null)
            {
                return false;
            }

            object currentFormatter = reportTabVm.GetType()
                .GetProperty("CurrentReportFormatter")
                ?.GetValue(reportTabVm, null);
            if (currentFormatter != null
                && string.Equals(GetPropertyValue<string>(currentFormatter, "Uid"), formatterUid, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            PropertyInfo formatterProperty = reportTabVm.GetType().GetProperty("CurrentReportFormatter");
            if (formatterProperty == null)
            {
                return false;
            }

            formatterProperty.SetValue(reportTabVm, formatter, null);
            return true;
        }

        private static bool TrySelectTest(object reportTabVm, ClashTest test)
        {
            object clashService = GetPropertyValue<object>(reportTabVm, "Service");
            object uiService = GetPropertyValue<object>(reportTabVm, "UIService");
            if (clashService == null || uiService == null || test == null)
            {
                return false;
            }

            object tests = clashService.GetType().GetProperty("Tests")?.GetValue(clashService, null);
            if (tests == null)
            {
                return false;
            }

            MethodInfo findTestVm = tests.GetType().GetMethod("FindTestVmFromTest");
            if (findTestVm == null)
            {
                return false;
            }

            object testVm = findTestVm.Invoke(tests, new object[] { test });
            if (testVm == null)
            {
                return false;
            }

            uiService.GetType().GetProperty("CurrentTest")?.SetValue(uiService, testVm, null);
            return true;
        }

        private static ICommand GetWriteReportCommand(object reportTabVm)
        {
            return GetPropertyValue<ICommand>(reportTabVm, "WriteReportCmd");
        }

        private static T GetPropertyValue<T>(object instance, string propertyName)
        {
            if (instance == null)
            {
                return default;
            }

            object value = instance.GetType().GetProperty(propertyName)?.GetValue(instance, null);
            return value is T typed ? typed : default;
        }

        /// <summary>Выключает bool-свойства картинок на ReportTabVM и вложенных объектах.</summary>
        private static List<BoolPropertyBackup> TryDisableImageFlags(object reportTabVm)
        {
            return WalkImageBoolFlags(reportTabVm, enable: false);
        }

        /// <summary>Включает bool-свойства картинок на ReportTabVM (с бэкапом для restore).</summary>
        private static List<BoolPropertyBackup> TryEnableImageFlags(object reportTabVm)
        {
            return WalkImageBoolFlags(reportTabVm, enable: true);
        }

        /// <summary>Обходит свойства и выставляет флаги изображений в нужное состояние.</summary>
        private static List<BoolPropertyBackup> WalkImageBoolFlags(object reportTabVm, bool enable)
        {
            var backups = new List<BoolPropertyBackup>();
            try
            {
                WalkAndSetImageFlags(reportTabVm, 0, new HashSet<object>(), backups, enable);
            }
            catch
            {
            }

            return backups;
        }

        /// <summary>Включает/выключает ReportFields с Image в Text/Uid.</summary>
        private static List<NullableBoolPropertyBackup> TrySetImageReportFields(object reportTabVm, bool enable)
        {
            var backups = new List<NullableBoolPropertyBackup>();
            try
            {
                IEnumerable fields = GetPropertyValue<IEnumerable>(reportTabVm, "ReportFields");
                if (fields == null)
                    return backups;

                foreach (object field in fields)
                {
                    if (field == null)
                        continue;

                    string text = GetPropertyValue<string>(field, "Text") ?? string.Empty;
                    string uid = GetPropertyValue<string>(field, "Uid") ?? string.Empty;
                    if (text.IndexOf("Image", StringComparison.OrdinalIgnoreCase) < 0
                        && uid.IndexOf("Image", StringComparison.OrdinalIgnoreCase) < 0
                        && text.IndexOf("Изображ", StringComparison.OrdinalIgnoreCase) < 0)
                    {
                        continue;
                    }

                    PropertyInfo checkedProperty = field.GetType().GetProperty("IsChecked");
                    if (checkedProperty == null || !checkedProperty.CanRead || !checkedProperty.CanWrite)
                        continue;

                    object raw = checkedProperty.GetValue(field, null);
                    bool? current = raw as bool?;
                    if (raw is bool)
                        current = (bool)raw;

                    backups.Add(new NullableBoolPropertyBackup
                    {
                        Target = field,
                        Property = checkedProperty,
                        Value = current
                    });

                    checkedProperty.SetValue(field, enable, null);
                }
            }
            catch
            {
            }

            return backups;
        }

        /// <summary>Обходит публичные свойства и выставляет флаги изображений.</summary>
        private static void WalkAndSetImageFlags(
            object instance,
            int depth,
            HashSet<object> visited,
            List<BoolPropertyBackup> backups,
            bool enable)
        {
            if (instance == null || depth > 3 || !visited.Add(instance))
                return;

            PropertyInfo[] properties;
            try
            {
                properties = instance.GetType().GetProperties(
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            }
            catch
            {
                return;
            }

            for (int i = 0; i < properties.Length; i++)
            {
                PropertyInfo property = properties[i];
                if (property.GetIndexParameters().Length > 0)
                    continue;

                try
                {
                    if (property.PropertyType == typeof(bool) && property.CanRead && property.CanWrite
                        && IsImageFlagName(property.Name))
                    {
                        object raw = property.GetValue(instance, null);
                        bool current = raw is bool && (bool)raw;
                        backups.Add(new BoolPropertyBackup
                        {
                            Target = instance,
                            Property = property,
                            Value = current
                        });
                        if (current != enable)
                            property.SetValue(instance, enable, null);

                        continue;
                    }

                    if (depth >= 2 || property.PropertyType.IsPrimitive || property.PropertyType == typeof(string)
                        || property.PropertyType.IsEnum || typeof(ICommand).IsAssignableFrom(property.PropertyType)
                        || !ShouldWalkNestedProperty(property.Name))
                    {
                        continue;
                    }

                    object nested = property.GetValue(instance, null);
                    WalkAndSetImageFlags(nested, depth + 1, visited, backups, enable);
                }
                catch
                {
                }
            }
        }

        /// <summary>Имена вроде IncludeImage / IncludeClashImages / IncludeViewpointImage.</summary>
        private static bool IsImageFlagName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return false;

            return name.IndexOf("Image", StringComparison.OrdinalIgnoreCase) >= 0
                || (name.IndexOf("Viewpoint", StringComparison.OrdinalIgnoreCase) >= 0
                    && name.IndexOf("Include", StringComparison.OrdinalIgnoreCase) >= 0);
        }

        /// <summary>Не обходим весь Clash Service, только ветки настроек отчёта.</summary>
        private static bool ShouldWalkNestedProperty(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return false;

            return name.IndexOf("Image", StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("Content", StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("Setting", StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("Option", StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("Report", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        /// <summary>Возвращает флаги картинок, как были до экспорта.</summary>
        private static void RestoreBoolProperties(List<BoolPropertyBackup> backups)
        {
            if (backups == null)
                return;

            for (int i = 0; i < backups.Count; i++)
            {
                BoolPropertyBackup backup = backups[i];
                try
                {
                    if (backup.Target != null && backup.Property != null)
                        backup.Property.SetValue(backup.Target, backup.Value, null);
                }
                catch
                {
                }
            }
        }

        /// <summary>Возвращает IsChecked полей отчёта, как были до экспорта.</summary>
        private static void RestoreNullableBoolProperties(List<NullableBoolPropertyBackup> backups)
        {
            if (backups == null)
                return;

            for (int i = 0; i < backups.Count; i++)
            {
                NullableBoolPropertyBackup backup = backups[i];
                try
                {
                    if (backup.Target != null && backup.Property != null)
                        backup.Property.SetValue(backup.Target, backup.Value, null);
                }
                catch
                {
                }
            }
        }

        /// <summary>Сохранённое bool-свойство ReportTabVM.</summary>
        private sealed class BoolPropertyBackup
        {
            public object Target;
            public PropertyInfo Property;
            public bool Value;
        }

        /// <summary>Сохранённое bool? (IsChecked у ReportFieldVM).</summary>
        private sealed class NullableBoolPropertyBackup
        {
            public object Target;
            public PropertyInfo Property;
            public bool? Value;
        }
    }
}
