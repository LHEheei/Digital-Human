using UnityEngine;

namespace SrtpAvatar
{
    [CreateAssetMenu(menuName = "SRTP/App Config", fileName = "OpenAIConfig")]
    public sealed class OpenAICompatibleConfig : ScriptableObject
    {
        [Header("DeepSeek Dialogue")]
        public string deepSeekBaseUrl = "https://api.deepseek.com";
        public string deepSeekApiKey = "";
        public string deepSeekModel = "deepseek-v4-flash";

        [Header("Android Native Speech")]
        public bool preferAndroidNativeSpeech = true;
        public string speechLanguage = "zh-CN";
        public float speechRecognitionTimeoutSeconds = 20f;
        public float ttsBaseTimeoutSeconds = 5f;
        public float ttsSecondsPerCharacter = 0.25f;

        [Header("Demo Mode")]
        public bool useLocalDemoWhenDeepSeekKeyMissing = true;
        public string localDemoUserText = "你好，请介绍一下你现在能做什么。";

        public bool HasDeepSeekKey => !string.IsNullOrWhiteSpace(deepSeekApiKey);

        public string DeepSeekUrl(string path)
        {
            var root = string.IsNullOrWhiteSpace(deepSeekBaseUrl) ? "https://api.deepseek.com" : deepSeekBaseUrl.TrimEnd('/');
            return $"{root}/{path.TrimStart('/')}";
        }
    }
}
