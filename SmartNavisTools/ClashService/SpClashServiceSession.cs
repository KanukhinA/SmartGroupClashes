using System;

namespace SmartNavisTools
{
    /// <summary>Создаёт авторизованный клиент SP-Service из сохранённых настроек.</summary>
    internal static class SpClashServiceSession
    {
        /// <summary>Открывает клиент и логинится. При ошибке возвращает false.</summary>
        public static bool TryOpen(out SpClashServiceClient client, out string error)
        {
            client = null;
            error = null;

            try
            {
                SpServiceSettings settings = SpServiceSettingsStore.Load();
                if (!settings.HasConnection)
                {
                    error = "Сначала укажите адрес сервера, логин и пароль в Настройках.";
                    return false;
                }

                client = new SpClashServiceClient(settings.BaseUrl);
                client.Login(settings.Login, settings.Password);
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                if (client != null)
                {
                    client.Dispose();
                    client = null;
                }

                return false;
            }
        }

        /// <summary>Открывает клиент и читает сохранённый проект.</summary>
        public static bool TryOpen(out SpClashServiceClient client, out Guid projectId, out string error)
        {
            projectId = Guid.Empty;
            if (!TryOpen(out client, out error))
                return false;

            try
            {
                SpServiceSettings settings = SpServiceSettingsStore.Load();
                if (!Guid.TryParse(settings.ProjectId, out projectId) || projectId == Guid.Empty)
                {
                    error = "Сначала выберите проект на вкладке «Проверки Clash Detective».";
                    client.Dispose();
                    client = null;
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                if (client != null)
                {
                    client.Dispose();
                    client = null;
                }

                return false;
            }
        }
    }
}
