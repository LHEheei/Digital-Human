using System.IO;
using UnityEngine;

namespace SrtpAvatar
{
    public static class RuntimeSecrets
    {
        private const string DeepSeekKeyFileName = "deepseek_key.txt";
        private const string DeepSeekSettingsFileName = "deepseek_api_settings.json";
        private const string DeepSeekEditorEnvName = "DEEPSEEK_API_KEY";

        private static bool settingsLoaded;
        private static DeepSeekApiSettings cachedSettings;

        public static string ResolveDeepSeekApiKey(AppConfig config)
        {
            var settings = LoadDeepSeekSettings();
            if (!string.IsNullOrWhiteSpace(settings.apiKey))
            {
                return settings.apiKey.Trim();
            }

            var fromFile = ReadDeepSeekKeyFile();
            if (!string.IsNullOrWhiteSpace(fromFile))
            {
                return fromFile;
            }

#if UNITY_EDITOR
            var fromEnvironment = System.Environment.GetEnvironmentVariable(DeepSeekEditorEnvName);
            if (!string.IsNullOrWhiteSpace(fromEnvironment))
            {
                return fromEnvironment.Trim();
            }
#endif

            return config == null || string.IsNullOrWhiteSpace(config.deepSeekApiKey)
                ? ""
                : config.deepSeekApiKey.Trim();
        }

        public static string ResolveDeepSeekBaseUrl(AppConfig config)
        {
            var settings = LoadDeepSeekSettings();
            if (!string.IsNullOrWhiteSpace(settings.baseUrl))
            {
                return settings.baseUrl.Trim();
            }

            return config == null || string.IsNullOrWhiteSpace(config.deepSeekBaseUrl)
                ? "https://api.deepseek.com"
                : config.deepSeekBaseUrl.Trim();
        }

        public static string ResolveDeepSeekModel(AppConfig config)
        {
            var settings = LoadDeepSeekSettings();
            if (!string.IsNullOrWhiteSpace(settings.model))
            {
                return settings.model.Trim();
            }

            return config == null || string.IsNullOrWhiteSpace(config.deepSeekModel)
                ? "deepseek-v4-flash"
                : config.deepSeekModel.Trim();
        }

        public static string DeepSeekChatCompletionUrl(AppConfig config)
        {
            return $"{ResolveDeepSeekBaseUrl(config).TrimEnd('/')}/chat/completions";
        }

        public static DeepSeekApiSettings LoadDeepSeekSettings()
        {
            if (settingsLoaded)
            {
                return cachedSettings.Copy();
            }

            cachedSettings = ReadDeepSeekSettingsFile();
            settingsLoaded = true;
            return cachedSettings.Copy();
        }

        private static DeepSeekApiSettings ReadDeepSeekSettingsFile()
        {
            var path = DeepSeekSettingsFilePath();
            if (!File.Exists(path))
            {
                return new DeepSeekApiSettings();
            }

            try
            {
                var json = File.ReadAllText(path);
                var settings = JsonUtility.FromJson<DeepSeekApiSettings>(json);
                return settings == null ? new DeepSeekApiSettings() : settings.Normalize();
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning($"Failed to read DeepSeek settings file: {ex.Message}");
                return new DeepSeekApiSettings();
            }
        }

        public static void SaveDeepSeekSettings(string baseUrl, string apiKey, string model)
        {
            var settings = new DeepSeekApiSettings
            {
                baseUrl = string.IsNullOrWhiteSpace(baseUrl) ? "https://api.deepseek.com" : baseUrl.Trim(),
                apiKey = string.IsNullOrWhiteSpace(apiKey) ? "" : apiKey.Trim(),
                model = string.IsNullOrWhiteSpace(model) ? "deepseek-v4-flash" : model.Trim()
            };

            Directory.CreateDirectory(Application.persistentDataPath);
            File.WriteAllText(DeepSeekSettingsFilePath(), JsonUtility.ToJson(settings, true));
            cachedSettings = settings.Normalize();
            settingsLoaded = true;

            var oldKeyPath = DeepSeekKeyFilePath();
            if (File.Exists(oldKeyPath))
            {
                File.Delete(oldKeyPath);
            }
        }

        public static void ClearDeepSeekSettings()
        {
            DeleteIfExists(DeepSeekSettingsFilePath());
            DeleteIfExists(DeepSeekKeyFilePath());
            cachedSettings = new DeepSeekApiSettings();
            settingsLoaded = true;
        }

        public static string DeepSeekKeyFilePath()
        {
            return Path.Combine(Application.persistentDataPath, DeepSeekKeyFileName);
        }

        public static string DeepSeekSettingsFilePath()
        {
            return Path.Combine(Application.persistentDataPath, DeepSeekSettingsFileName);
        }

        private static string ReadDeepSeekKeyFile()
        {
            var path = DeepSeekKeyFilePath();
            if (!File.Exists(path))
            {
                return "";
            }

            try
            {
                return File.ReadAllText(path).Trim();
            }
            catch (IOException ex)
            {
                Debug.LogWarning($"Failed to read DeepSeek key file: {ex.Message}");
                return "";
            }
        }

        private static void DeleteIfExists(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch (IOException ex)
            {
                Debug.LogWarning($"Failed to delete runtime secret file: {ex.Message}");
            }
        }

        [System.Serializable]
        public sealed class DeepSeekApiSettings
        {
            public string baseUrl = "";
            public string apiKey = "";
            public string model = "";

            public DeepSeekApiSettings Copy()
            {
                return new DeepSeekApiSettings
                {
                    baseUrl = baseUrl,
                    apiKey = apiKey,
                    model = model
                };
            }

            public DeepSeekApiSettings Normalize()
            {
                baseUrl = string.IsNullOrWhiteSpace(baseUrl) ? "" : baseUrl.Trim();
                apiKey = string.IsNullOrWhiteSpace(apiKey) ? "" : apiKey.Trim();
                model = string.IsNullOrWhiteSpace(model) ? "" : model.Trim();
                return this;
            }
        }
    }
}
