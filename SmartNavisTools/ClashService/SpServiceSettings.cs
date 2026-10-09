using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace SmartNavisTools
{
    /// <summary>Настройки подключения SmartNavisTools к SP-Service.</summary>
    internal sealed class SpServiceSettings
    {
        public string BaseUrl { get; set; }
        public string Login { get; set; }
        public string Password { get; set; }
        public string ProjectId { get; set; }

        public bool HasConnection
        {
            get
            {
                return !string.IsNullOrWhiteSpace(BaseUrl)
                    && !string.IsNullOrWhiteSpace(Login)
                    && !string.IsNullOrWhiteSpace(Password);
            }
        }
    }

    /// <summary>Чтение и запись настроек SP-Service. Пароль хранится через DPAPI.</summary>
    internal static class SpServiceSettingsStore
    {
        private const string BaseUrlKey = "BaseUrl";
        private const string LoginKey = "Login";
        private const string PasswordKey = "Password";
        private const string ProjectIdKey = "ProjectId";

        /// <summary>Загружает настройки из %AppData%\SmartNavisTools\service.cfg.</summary>
        public static SpServiceSettings Load()
        {
            SpServiceSettings settings = new SpServiceSettings
            {
                BaseUrl = string.Empty,
                Login = string.Empty,
                Password = string.Empty,
                ProjectId = string.Empty
            };

            try
            {
                string path = GetFilePath();
                if (!File.Exists(path))
                    return settings;

                string[] lines = File.ReadAllLines(path, Encoding.UTF8);
                for (int i = 0; i < lines.Length; i++)
                {
                    string line = lines[i];
                    int eq = line.IndexOf('=');
                    if (eq <= 0)
                        continue;

                    string key = line.Substring(0, eq).Trim();
                    string value = line.Substring(eq + 1).Trim();
                    if (string.Equals(key, BaseUrlKey, StringComparison.OrdinalIgnoreCase))
                        settings.BaseUrl = value;
                    else if (string.Equals(key, LoginKey, StringComparison.OrdinalIgnoreCase))
                        settings.Login = value;
                    else if (string.Equals(key, PasswordKey, StringComparison.OrdinalIgnoreCase))
                        settings.Password = Unprotect(value);
                    else if (string.Equals(key, ProjectIdKey, StringComparison.OrdinalIgnoreCase))
                        settings.ProjectId = value;
                }
            }
            catch
            {
                settings.Password = string.Empty;
            }

            return settings;
        }

        /// <summary>Сохраняет выбранный проект, не трогая логин и пароль.</summary>
        public static void SaveProjectId(string projectId)
        {
            try
            {
                SpServiceSettings settings = Load();
                settings.ProjectId = projectId ?? string.Empty;
                Save(settings);
            }
            catch
            {
            }
        }

        /// <summary>Сохраняет настройки в service.cfg.</summary>
        public static void Save(SpServiceSettings settings)
        {
            if (settings == null)
                return;

            string directory = Path.GetDirectoryName(GetFilePath());
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                Directory.CreateDirectory(directory);

            string[] lines =
            {
                BaseUrlKey + "=" + (settings.BaseUrl ?? string.Empty).Trim(),
                LoginKey + "=" + (settings.Login ?? string.Empty).Trim(),
                PasswordKey + "=" + Protect(settings.Password ?? string.Empty),
                ProjectIdKey + "=" + (settings.ProjectId ?? string.Empty).Trim()
            };
            File.WriteAllLines(GetFilePath(), lines, Encoding.UTF8);
        }

        private static string GetFilePath()
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            return Path.Combine(appData, "SmartNavisTools", "service.cfg");
        }

        private static string Protect(string plain)
        {
            if (string.IsNullOrEmpty(plain))
                return string.Empty;

            byte[] bytes = Encoding.UTF8.GetBytes(plain);
            byte[] protectedBytes = ProtectedData.Protect(bytes, null, DataProtectionScope.CurrentUser);
            return Convert.ToBase64String(protectedBytes);
        }

        private static string Unprotect(string cipher)
        {
            if (string.IsNullOrWhiteSpace(cipher))
                return string.Empty;

            try
            {
                byte[] protectedBytes = Convert.FromBase64String(cipher);
                byte[] bytes = ProtectedData.Unprotect(protectedBytes, null, DataProtectionScope.CurrentUser);
                return Encoding.UTF8.GetString(bytes);
            }
            catch
            {
                return string.Empty;
            }
        }
    }
}
