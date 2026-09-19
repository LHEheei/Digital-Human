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
        [Tooltip("按句接收并播报回复；兼容服务不支持 SSE 时可关闭。")]
        public bool enableStreamingReplies = true;
        [Header("Diagnostics")]
        public bool enableDiagnostics = false;
        public bool ShowDiagnostics => enableDiagnostics && (Application.isEditor || Debug.isDebugBuild);

        [Header("Conversation Memory")]
        public bool enableConversationMemory = true;
        public int maxMemoryTurns = 6;
        public bool enableLongTermMemory = true;
        public int maxLongTermMemories = 20;

        [Header("Android Native Speech")]
        public bool preferAndroidNativeSpeech = true;
        [Tooltip("优先使用 Android 设备端语音识别；不可用时自动回退到系统语音服务。")]
        public bool preferOnDeviceSpeechRecognition = true;
        public string speechLanguage = "zh-CN";
        public float speechRecognitionTimeoutSeconds = 20f;
        [Tooltip("在前台空闲时自动开启系统语音识别；数字人播报期间会暂停，避免识别到自身语音。")]
        public bool enableAutoConversation = true;
        [Tooltip("自动识别结束或未听到语音后，重新开始聆听前的等待时间。")]
        public float autoConversationRetryDelaySeconds = 1.2f;
        public float ttsBaseTimeoutSeconds = 5f;
        public float ttsSecondsPerCharacter = 0.25f;

        [Header("Emotion Perception")]
        public bool enableEmotionPerception = false;
        [Tooltip("局域网服务仅用于开发；Android 本地模式不依赖电脑或自建服务器。")]
        public EmotionRuntimeMode emotionRuntimeMode = EmotionRuntimeMode.LanService;
        public string emotionServiceBaseUrl = "http://127.0.0.1:8000";
        public int emotionRequestTimeoutSeconds = 10;
        public float emotionCameraSampleIntervalSeconds = 1f;
        public float emotionVoiceWindowSeconds = 4f;
        public bool preferFrontEmotionCamera = true;
        public bool showEmotionCameraPreview = false;
        [Tooltip("显示仅用于研发采样的情绪评测面板；评测数据不包含图像或音频。")]
        public bool enableEmotionEvaluationPanel = false;

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


        public bool HasDeepSeekKey => !string.IsNullOrWhiteSpace(RuntimeSecrets.ResolveDeepSeekApiKey(this));


    }

    public enum EmotionRuntimeMode
    {
        LanService,
        AndroidOnDeviceFace,
        Disabled
    }
}
