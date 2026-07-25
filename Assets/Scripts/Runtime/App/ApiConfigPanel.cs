using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace SrtpAvatar
{
    public sealed class ApiConfigPanel : MonoBehaviour
    {
        [SerializeField] private AppConfig config;

        private Canvas canvas;
        private GameObject panelRoot;
        private InputField baseUrlInput;
        private InputField modelInput;
        private InputField apiKeyInput;
        private Text feedbackText;

        private static readonly Color PanelColor = new Color(0.96f, 0.99f, 1f, 0.97f);
        private static readonly Color ButtonColor = new Color(0.08f, 0.57f, 0.66f, 0.94f);
        private static readonly Color TextColor = new Color(0.06f, 0.22f, 0.36f);

        private void Start()
        {
            LoadConfigIfNeeded();
            EnsureEventSystem();
            canvas = Object.FindFirstObjectByType<Canvas>();
            if (canvas == null)
            {
                return;
            }

            BuildButton();
            BuildPanel();
            RefreshFields();
        }

        private void LoadConfigIfNeeded()
        {
            if (config == null)
            {
                config = Resources.Load<AppConfig>("OpenAIConfig");
                if (config == null)
                {
                    config = ScriptableObject.CreateInstance<AppConfig>();
                }
            }
        }

        private void BuildButton()
        {
            if (canvas.transform.Find("API Config Button") != null)
            {
                return;
            }

            var buttonObject = new GameObject("API Config Button");
            buttonObject.transform.SetParent(canvas.transform, false);
            var image = buttonObject.AddComponent<Image>();
            image.color = new Color(0.08f, 0.43f, 0.62f, 0.9f);
            var button = buttonObject.AddComponent<Button>();
            button.onClick.AddListener(ShowPanel);

            var rect = buttonObject.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(1f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(1f, 1f);
            rect.anchoredPosition = new Vector2(-34f, -34f);
            rect.sizeDelta = new Vector2(92f, 54f);

            var label = CreateText("Label", buttonObject.transform, "API", 24, TextAnchor.MiddleCenter, Color.white);
            Stretch(label.rectTransform);
        }

        private void BuildPanel()
        {
            if (panelRoot != null)
            {
                return;
            }

            panelRoot = new GameObject("API Config Panel");
            panelRoot.transform.SetParent(canvas.transform, false);
            var image = panelRoot.AddComponent<Image>();
            image.color = PanelColor;

            var rect = panelRoot.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = new Vector2(860f, 620f);

            var title = CreateText("Title", panelRoot.transform, "API 设置", 38, TextAnchor.MiddleLeft, TextColor);
            SetRect(title.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(44f, -54f), new Vector2(-88f, 64f));

            baseUrlInput = CreateField("Base URL", "https://api.deepseek.com", -150f, InputField.ContentType.Standard);
            modelInput = CreateField("Model", "deepseek-v4-flash", -270f, InputField.ContentType.Standard);
            apiKeyInput = CreateField("API Key", "留空表示保持已有 Key", -390f, InputField.ContentType.Password);

            feedbackText = CreateText("Feedback", panelRoot.transform, "", 20, TextAnchor.MiddleLeft, new Color(0.12f, 0.36f, 0.46f));
            SetRect(feedbackText.rectTransform, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(44f, 126f), new Vector2(-88f, 44f));

            CreateActionButton("Save Button", "保存", new Vector2(-248f, 52f), SaveSettings, ButtonColor);
            CreateActionButton("Clear Button", "清除", new Vector2(0f, 52f), ClearSettings, new Color(0.62f, 0.72f, 0.78f, 0.95f));
            CreateActionButton("Close Button", "关闭", new Vector2(248f, 52f), HidePanel, new Color(0.28f, 0.42f, 0.5f, 0.95f));

            panelRoot.SetActive(false);
        }

        private InputField CreateField(string labelText, string placeholder, float y, InputField.ContentType contentType)
        {
            var label = CreateText($"{labelText} Label", panelRoot.transform, labelText, 22, TextAnchor.MiddleLeft, TextColor);
            SetRect(label.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(44f, y + 42f), new Vector2(250f, 40f));

            var fieldObject = new GameObject($"{labelText} Input");
            fieldObject.transform.SetParent(panelRoot.transform, false);
            var image = fieldObject.AddComponent<Image>();
            image.color = new Color(1f, 1f, 1f, 0.96f);
            var field = fieldObject.AddComponent<InputField>();
            field.contentType = contentType;

            var rect = fieldObject.GetComponent<RectTransform>();
            SetRect(rect, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(44f, y), new Vector2(-88f, 58f));

            var text = CreateText("Text", fieldObject.transform, "", 22, TextAnchor.MiddleLeft, TextColor);
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            SetRect(text.rectTransform, Vector2.zero, Vector2.one, new Vector2(18f, 0f), new Vector2(-36f, 0f));

            var placeholderText = CreateText("Placeholder", fieldObject.transform, placeholder, 22, TextAnchor.MiddleLeft, new Color(0.32f, 0.47f, 0.55f, 0.62f));
            placeholderText.fontStyle = FontStyle.Italic;
            SetRect(placeholderText.rectTransform, Vector2.zero, Vector2.one, new Vector2(18f, 0f), new Vector2(-36f, 0f));

            field.textComponent = text;
            field.placeholder = placeholderText;
            return field;
        }

        private void CreateActionButton(string name, string label, Vector2 position, UnityEngine.Events.UnityAction action, Color color)
        {
            var buttonObject = new GameObject(name);
            buttonObject.transform.SetParent(panelRoot.transform, false);
            var image = buttonObject.AddComponent<Image>();
            image.color = color;
            var button = buttonObject.AddComponent<Button>();
            button.onClick.AddListener(action);

            var rect = buttonObject.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 0f);
            rect.anchorMax = new Vector2(0.5f, 0f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = position;
            rect.sizeDelta = new Vector2(180f, 64f);

            var text = CreateText("Label", buttonObject.transform, label, 24, TextAnchor.MiddleCenter, Color.white);
            Stretch(text.rectTransform);
        }

        private void ShowPanel()
        {
            RefreshFields();
            panelRoot.SetActive(true);
        }

        private void HidePanel()
        {
            panelRoot.SetActive(false);
        }

        private void SaveSettings()
        {
            var existing = RuntimeSecrets.LoadDeepSeekSettings();
            var key = string.IsNullOrWhiteSpace(apiKeyInput.text)
                ? RuntimeSecrets.ResolveDeepSeekApiKey(config)
                : apiKeyInput.text.Trim();
            var baseUrl = string.IsNullOrWhiteSpace(baseUrlInput.text) ? RuntimeSecrets.ResolveDeepSeekBaseUrl(config) : baseUrlInput.text.Trim();
            var model = string.IsNullOrWhiteSpace(modelInput.text) ? RuntimeSecrets.ResolveDeepSeekModel(config) : modelInput.text.Trim();

            if (!RuntimeSecrets.TryNormalizeDeepSeekBaseUrl(baseUrl, out var safeBaseUrl, out var warning, out var error))
            {
                SetFeedback(error);
                return;
            }

            if (string.IsNullOrWhiteSpace(key) && !string.IsNullOrWhiteSpace(existing.apiKey))
            {
                key = existing.apiKey;
            }

            RuntimeSecrets.SaveDeepSeekSettings(safeBaseUrl, key, model);
            apiKeyInput.text = "";
            var saved = string.IsNullOrWhiteSpace(key) ? "已保存基础配置；未填写 Key 时会继续使用演示模式。" : "已保存 API 配置，重新点击 MIC 即可使用。";
            SetFeedback(string.IsNullOrWhiteSpace(warning) ? saved : $"{saved}\n警告：{warning}");
        }

        private void ClearSettings()
        {
            RuntimeSecrets.ClearDeepSeekSettings();
            RefreshFields();
            SetFeedback("已清除本机 API 配置，当前会回到演示模式或 Inspector 配置。");
        }

        private void RefreshFields()
        {
            var settings = RuntimeSecrets.LoadDeepSeekSettings();
            var baseUrl = string.IsNullOrWhiteSpace(settings.baseUrl) ? RuntimeSecrets.ResolveDeepSeekBaseUrl(config) : settings.baseUrl;
            var model = string.IsNullOrWhiteSpace(settings.model) ? RuntimeSecrets.ResolveDeepSeekModel(config) : settings.model;

            if (baseUrlInput != null)
            {
                baseUrlInput.text = baseUrl;
            }

            if (modelInput != null)
            {
                modelInput.text = model;
            }

            if (apiKeyInput != null)
            {
                apiKeyInput.text = "";
            }

            SetFeedback(string.IsNullOrWhiteSpace(RuntimeSecrets.ResolveDeepSeekApiKey(config))
                ? "当前未检测到 Key，点击 MIC 会使用演示模式。"
                : "当前已检测到 Key，可直接测试真实对话。");
        }

        private void SetFeedback(string message)
        {
            if (feedbackText != null)
            {
                feedbackText.text = message;
            }
        }

        private static Text CreateText(string name, Transform parent, string content, int size, TextAnchor anchor, Color color)
        {
            var obj = new GameObject(name);
            obj.transform.SetParent(parent, false);
            var text = obj.AddComponent<Text>();
            text.text = content;
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = size;
            text.alignment = anchor;
            text.color = color;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            return text;
        }

        private static void SetRect(RectTransform rect, Vector2 anchorMin, Vector2 anchorMax, Vector2 anchoredPosition, Vector2 sizeDelta)
        {
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.anchoredPosition = anchoredPosition;
            rect.sizeDelta = sizeDelta;
        }

        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = Vector2.zero;
        }

        private static void EnsureEventSystem()
        {
            if (Object.FindFirstObjectByType<EventSystem>() != null)
            {
                return;
            }

            var eventSystem = new GameObject("EventSystem");
            eventSystem.AddComponent<EventSystem>();
            eventSystem.AddComponent<StandaloneInputModule>();
        }
    }
}
