using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Principal;
using System.Text;
using System.Web.Script.Serialization;

namespace SmartNavisTools
{
    /// <summary>HTTP-клиент SP-Service для отчётов пересечений (C# 7.3).</summary>
    internal sealed class SpClashServiceClient : IDisposable
    {
        private readonly HttpClient _http;
        private readonly JavaScriptSerializer _serializer = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };

        public SpClashServiceClient(string baseUrl)
        {
            if (string.IsNullOrWhiteSpace(baseUrl))
                throw new ArgumentException("Не задан адрес сервера.");

            string trimmed = baseUrl.Trim().TrimEnd('/');
            _http = new HttpClient
            {
                BaseAddress = new Uri(trimmed + "/", UriKind.Absolute),
                Timeout = TimeSpan.FromMinutes(5)
            };
            AttachWindowsUserHeader(_http);
        }

        /// <summary>Вход по логину и паролю.</summary>
        public string Login(string login, string password)
        {
            Dictionary<string, object> body = new Dictionary<string, object>
            {
                { "login", login },
                { "password", password }
            };
            Dictionary<string, object> payload = PostJson("/api/auth/password-login", body);
            if (payload == null)
                throw new InvalidOperationException("Неверный логин или пароль.");

            string token = GetString(payload, "token");
            if (string.IsNullOrWhiteSpace(token))
                throw new InvalidOperationException("Сервер не вернул токен.");

            _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
            return GetString(payload, "displayName") ?? login;
        }

        /// <summary>Список проектов текущего пользователя.</summary>
        public List<ProjectItem> GetProjects()
        {
            object parsed = GetJson("/api/projects");
            List<ProjectItem> list = new List<ProjectItem>();
            foreach (object item in EnumerateArray(parsed))
            {
                Dictionary<string, object> map = item as Dictionary<string, object>;
                if (map == null)
                    continue;

                ProjectItem project = new ProjectItem
                {
                    Id = GetGuid(map, "id"),
                    Name = GetString(map, "name")
                };
                list.Add(project);
            }

            return list;
        }

        /// <summary>Список отчётов проекта.</summary>
        public List<ReportItem> GetReports(Guid projectId)
        {
            object parsed = GetJson("/api/projects/" + projectId.ToString("D") + "/clash-reports");
            List<ReportItem> list = new List<ReportItem>();
            foreach (object item in EnumerateArray(parsed))
            {
                Dictionary<string, object> map = item as Dictionary<string, object>;
                if (map == null)
                    continue;

                list.Add(new ReportItem
                {
                    Id = GetGuid(map, "id"),
                    Name = GetString(map, "name"),
                    ResultCount = GetInt(map, "resultCount"),
                    UpdatedAt = GetDateTimeOffset(map, "updatedAt")
                });
            }

            return list;
        }

        /// <summary>
        /// GUID пересечений, у которых на сервере уже есть превью.
        /// Для режима «картинки только для новых»: не переснимать те, у кого картинка уже есть.
        /// </summary>
        public List<string> GetExistingClashGuids(Guid reportId)
        {
            object parsed = GetJson("/api/clash-reports/" + reportId.ToString("D") + "/preview-guids");
            List<string> list = new List<string>();
            foreach (object item in EnumerateArray(parsed))
            {
                string guid = item as string;
                if (string.IsNullOrWhiteSpace(guid))
                    guid = Convert.ToString(item, CultureInfo.InvariantCulture);
                if (!string.IsNullOrWhiteSpace(guid))
                    list.Add(guid);
            }

            return list;
        }

        /// <summary>Отправка снимка проверок.</summary>
        public void UpsertReport(Guid projectId, Dictionary<string, object> request)
        {
            PostJson("/api/projects/" + projectId.ToString("D") + "/clash-reports", request);
        }

        /// <summary>Скачивает Clash Report XML.</summary>
        public byte[] ExportXml(Guid reportId)
        {
            HttpResponseMessage response = _http.GetAsync("/api/clash-reports/" + reportId.ToString("D") + "/export.xml")
                .GetAwaiter().GetResult();
            using (response)
            {
                EnsureSuccess(response, "Не удалось выгрузить XML отчёта.");
                return response.Content.ReadAsByteArrayAsync().GetAwaiter().GetResult();
            }
        }

        public void Dispose()
        {
            _http.Dispose();
        }

        /// <summary>Пишет в заголовок имя Windows-пользователя на этой машине.</summary>
        private static void AttachWindowsUserHeader(HttpClient http)
        {
            try
            {
                string name = null;
                using (WindowsIdentity identity = WindowsIdentity.GetCurrent())
                {
                    if (identity != null && !string.IsNullOrWhiteSpace(identity.Name))
                        name = identity.Name;
                }

                if (string.IsNullOrWhiteSpace(name))
                {
                    string domain = Environment.UserDomainName;
                    string user = Environment.UserName;
                    if (!string.IsNullOrWhiteSpace(user))
                        name = string.IsNullOrWhiteSpace(domain) ? user : domain + "\\" + user;
                }

                if (!string.IsNullOrWhiteSpace(name))
                    http.DefaultRequestHeaders.TryAddWithoutValidation("X-SP-Windows-User", name);
            }
            catch
            {
            }
        }

        private object GetJson(string url)
        {
            HttpResponseMessage response = _http.GetAsync(url).GetAwaiter().GetResult();
            using (response)
            {
                EnsureSuccess(response, "Ошибка запроса к серверу.");
                string json = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                return _serializer.DeserializeObject(json);
            }
        }

        private Dictionary<string, object> PostJson(string url, object body)
        {
            string json = _serializer.Serialize(body);
            ByteArrayContent content = CreateGzipJsonContent(json);
            HttpResponseMessage response = _http.PostAsync(url, content).GetAwaiter().GetResult();
            using (content)
            using (response)
            {
                if ((int)response.StatusCode == 401)
                    throw new InvalidOperationException("Неверный логин или пароль.");

                EnsureSuccess(response, "Ошибка запроса к серверу.");
                string responseJson = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                if (string.IsNullOrWhiteSpace(responseJson))
                    return null;

                return _serializer.DeserializeObject(responseJson) as Dictionary<string, object>;
            }
        }

        /// <summary>Сжимает JSON gzip, чтобы не гонять десятки мегабайт base64 как есть.</summary>
        private static ByteArrayContent CreateGzipJsonContent(string json)
        {
            byte[] raw = Encoding.UTF8.GetBytes(json ?? string.Empty);
            using (MemoryStream compressed = new MemoryStream())
            {
                using (GZipStream gzip = new GZipStream(compressed, CompressionLevel.Fastest, true))
                    gzip.Write(raw, 0, raw.Length);

                ByteArrayContent content = new ByteArrayContent(compressed.ToArray());
                content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
                content.Headers.ContentEncoding.Add("gzip");
                return content;
            }
        }

        private static void EnsureSuccess(HttpResponseMessage response, string message)
        {
            if (response.IsSuccessStatusCode)
                return;

            throw new InvalidOperationException(message + " Код: " + (int)response.StatusCode + ".");
        }

        private static string GetString(Dictionary<string, object> map, string name)
        {
            object value = GetValue(map, name);
            return value == null ? null : Convert.ToString(value, CultureInfo.InvariantCulture);
        }

        private static Guid GetGuid(Dictionary<string, object> map, string name)
        {
            string text = GetString(map, name);
            Guid id;
            return Guid.TryParse(text, out id) ? id : Guid.Empty;
        }

        private static int GetInt(Dictionary<string, object> map, string name)
        {
            object value = GetValue(map, name);
            if (value == null)
                return 0;

            try
            {
                return Convert.ToInt32(value, CultureInfo.InvariantCulture);
            }
            catch
            {
                return 0;
            }
        }

        /// <summary>Читает дату обновления отчёта из JSON (ISO-строка или DateTime).</summary>
        private static DateTimeOffset GetDateTimeOffset(Dictionary<string, object> map, string name)
        {
            object value = GetValue(map, name);
            if (value == null)
                return DateTimeOffset.MinValue;

            try
            {
                DateTimeOffset parsedOffset;
                if (value is DateTimeOffset alreadyOffset)
                    return alreadyOffset;

                if (value is DateTime dateTime)
                    return new DateTimeOffset(dateTime);

                string text = Convert.ToString(value, CultureInfo.InvariantCulture);
                if (string.IsNullOrWhiteSpace(text))
                    return DateTimeOffset.MinValue;

                if (DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out parsedOffset))
                    return parsedOffset;

                DateTime parsedDate;
                if (DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out parsedDate))
                    return new DateTimeOffset(parsedDate);
            }
            catch
            {
            }

            return DateTimeOffset.MinValue;
        }

        private static object GetValue(Dictionary<string, object> map, string name)
        {
            if (map == null)
                return null;

            foreach (KeyValuePair<string, object> pair in map)
            {
                if (string.Equals(pair.Key, name, StringComparison.OrdinalIgnoreCase))
                    return pair.Value;
            }

            return null;
        }

        /// <summary>Обходит JSON-массив, который JavaScriptSerializer отдаёт как ArrayList.</summary>
        private static IEnumerable<object> EnumerateArray(object parsed)
        {
            IEnumerable items = parsed as IEnumerable;
            if (items == null || parsed is string)
                yield break;

            foreach (object item in items)
                yield return item;
        }

        internal sealed class ProjectItem
        {
            public Guid Id { get; set; }
            public string Name { get; set; }

            public override string ToString()
            {
                return Name;
            }
        }

        internal sealed class ReportItem
        {
            public Guid Id { get; set; }
            public string Name { get; set; }
            public int ResultCount { get; set; }
            public DateTimeOffset UpdatedAt { get; set; }

            public override string ToString()
            {
                return FormatCaption(UpdatedAt, Name, ResultCount);
            }
        }

        /// <summary>Подпись отчёта: дата слева от названия.</summary>
        internal static string FormatCaption(DateTimeOffset updatedAt, string name, int resultCount)
        {
            string title = string.IsNullOrWhiteSpace(name) ? "отчёт" : name;
            string suffix = " (" + resultCount.ToString(CultureInfo.InvariantCulture) + ")";
            if (updatedAt == DateTimeOffset.MinValue)
                return title + suffix;

            string stamp = updatedAt.ToLocalTime().ToString("dd.MM.yyyy HH:mm", CultureInfo.CurrentCulture);
            return stamp + "  " + title + suffix;
        }
    }
}
