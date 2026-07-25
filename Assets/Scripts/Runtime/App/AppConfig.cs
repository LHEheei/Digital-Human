using UnityEngine;

namespace SrtpAvatar
{
    [CreateAssetMenu(menuName = "SRTP/App Config", fileName = "AppConfig")]
    public sealed class AppConfig : ScriptableObject
    {
        [Header("DeepSeek Dialogue")]
        public string deepSeekBaseUrl = "https://api.deepseek.com";
        public string deepSeekApiKey = "";
        public string deepSeekModel = "deepseek-v4-flash";
        public int deepSeekRequestTimeoutSeconds = 25;

        [Header("Conversation Memory")]
        public bool enableConversationMemory = true;
        public int maxMemoryTurns = 6;
        public bool enableLongTermMemory = true;
        public int maxLongTermMemories = 20;

        [Header("Android Native Speech")]
        public bool preferAndroidNativeSpeech = true;
        public string speechLanguage = "zh-CN";
        public float speechRecognitionTimeoutSeconds = 20f;
        public float ttsBaseTimeoutSeconds = 5f;
        public float ttsSecondsPerCharacter = 0.25f;

        [Header("Car Communication")]
        public bool enableCarCommunication = false;
        public CarCommunicationMode carCommunicationMode = CarCommunicationMode.LogOnly;
        public string carHttpEndpoint = "";
        public string carBluetoothDeviceName = "HC-05";
        public string carBluetoothDeviceAddress = "";
        public int carCommunicationTimeoutSeconds = 8;
        public string carCommandForward = "F";
        public string carCommandBackward = "B";
        public string carCommandLeft = "L";
        public string carCommandRight = "R";
        public string carCommandStop = "S";
        public string carCommandSpeedUp = "U";
        public string carCommandSlowDown = "D";
        public string carBluetoothCommandSuffix = "\\n";

        [Header("Demo Mode")]
        public bool useLocalDemoWhenDeepSeekKeyMissing = true;
        public string localDemoUserText = "你好，请介绍一下你现在能做什么。";

        public bool HasDeepSeekKey => !string.IsNullOrWhiteSpace(RuntimeSecrets.ResolveDeepSeekApiKey(this));

        public string DeepSeekUrl(string path)
        {
            var root = RuntimeSecrets.ResolveDeepSeekBaseUrl(this).TrimEnd('/');
            return $"{root}/{path.TrimStart('/')}";
        }
    }
}
