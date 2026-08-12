using UnityEngine;

#if UNITY_EDITOR
using System.Collections;
using UnityEngine.UI;
#endif

namespace SrtpAvatar
{
#if UNITY_EDITOR
    public sealed class AvatarTestPanel : MonoBehaviour
    {
        [SerializeField] private DigitalHumanAvatarBase avatarDriver;
        [SerializeField] private AudioSource audioSource;
        [SerializeField] private Canvas targetCanvas;
        [SerializeField] private bool showOnStart = true;

        private GameObject panelRoot;
        private Coroutine speechRoutine;

        private static readonly Color PanelColor = new Color(1f, 1f, 1f, 0.82f);
        private static readonly Color TextColor = new Color(0.08f, 0.18f, 0.28f);
        private static readonly Color ButtonColor = new Color(0.08f, 0.58f, 0.68f, 0.92f);
        private static readonly Color SecondaryButtonColor = new Color(0.68f, 0.82f, 0.9f, 0.92f);

        private void Start()
        {
            if (!Application.isEditor)
            {
                enabled = false;
                return;
            }

            ResolveReferences();
            BuildPanel();
            panelRoot.SetActive(showOnStart);
        }

        private void Update()
        {
            if (Application.isEditor && panelRoot != null && Input.GetKeyDown(KeyCode.F2))
            {
                panelRoot.SetActive(!panelRoot.activeSelf);
            }
        }

        public void Bind(DigitalHumanAvatarBase driver, AudioSource source)
        {
            avatarDriver = driver;
            audioSource = source;
        }

        private void ResolveReferences()
        {
            if (targetCanvas == null)
            {
                targetCanvas = Object.FindAnyObjectByType<Canvas>();
            }

            if (audioSource == null)
            {
                var controller = Object.FindAnyObjectByType<DigitalHumanAppController>();
                if (controller != null)
                {
                    audioSource = controller.GetComponent<AudioSource>();
                }
            }

            if (avatarDriver == null)
            {
                avatarDriver = Object.FindAnyObjectByType<VrmAvatarBridge>();
            }
        }

        private void BuildPanel()
        {
            if (targetCanvas == null || panelRoot != null)
            {
                return;
            }

            panelRoot = new GameObject("Avatar Test Panel");
            panelRoot.transform.SetParent(targetCanvas.transform, false);

            var rect = panelRoot.AddComponent<RectTransform>();
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(24f, -24f);
            rect.sizeDelta = new Vector2(390f, 310f);

            var image = panelRoot.AddComponent<Image>();
            image.color = PanelColor;

            var layout = panelRoot.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(14, 14, 12, 12);
            layout.spacing = 8f;
            layout.childControlHeight = true;
            layout.childControlWidth = true;
            layout.childForceExpandHeight = false;
            layout.childForceExpandWidth = true;

            AddLabel(panelRoot.transform, "\u6570\u5b57\u4eba\u6d4b\u8bd5\u9762\u677f");

            var expressionRowA = AddRow("Expression Row A");
            AddButton(expressionRowA, "\u4e2d\u6027", () => SetEmotion(AvatarEmotion.neutral), SecondaryButtonColor);
            AddButton(expressionRowA, "\u5f00\u5fc3", () => SetEmotion(AvatarEmotion.joy), ButtonColor);
            AddButton(expressionRowA, "\u751f\u6c14", () => SetEmotion(AvatarEmotion.angry), ButtonColor);

            var expressionRowB = AddRow("Expression Row B");
            AddButton(expressionRowB, "\u96be\u8fc7", () => SetEmotion(AvatarEmotion.sorrow), SecondaryButtonColor);
            AddButton(expressionRowB, "\u6709\u8da3", () => SetEmotion(AvatarEmotion.fun), SecondaryButtonColor);
            AddButton(expressionRowB, "\u60ca\u8bb6", () => SetEmotion(AvatarEmotion.surprised), SecondaryButtonColor);

            var gestureRow = AddRow("Gesture Row");
            AddButton(gestureRow, "\u70b9\u5934", () => PlayGesture(AvatarGesture.nod), ButtonColor);
            AddButton(gestureRow, "\u6325\u624b", () => PlayGesture(AvatarGesture.wave), ButtonColor);
            AddButton(gestureRow, "\u601d\u8003", () => PlayGesture(AvatarGesture.think), ButtonColor);
            AddButton(gestureRow, "\u89e3\u91ca", () => PlayGesture(AvatarGesture.explain), ButtonColor);

            var speechRow = AddRow("Speech Row");
            AddButton(speechRow, "\u8bf4\u8bdd\u6a21\u62df", SimulateSpeech, ButtonColor);
            AddButton(speechRow, "\u9690\u85cf\u9762\u677f", () => panelRoot.SetActive(false), SecondaryButtonColor);
        }

        private Transform AddRow(string name)
        {
            var row = new GameObject(name);
            row.transform.SetParent(panelRoot.transform, false);

            var layoutElement = row.AddComponent<LayoutElement>();
            layoutElement.minHeight = 42f;
            layoutElement.preferredHeight = 42f;
            layoutElement.flexibleHeight = 0f;

            var layout = row.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = 8f;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;

            var rect = row.GetComponent<RectTransform>();
            rect.sizeDelta = new Vector2(0f, 42f);
            return row.transform;
        }

        private void AddLabel(Transform parent, string label)
        {
            var obj = new GameObject("Title");
            obj.transform.SetParent(parent, false);
            var text = obj.AddComponent<Text>();
            text.text = label;
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = 22;
            text.alignment = TextAnchor.MiddleLeft;
            text.color = TextColor;
            text.raycastTarget = false;

            var rect = obj.GetComponent<RectTransform>();
            rect.sizeDelta = new Vector2(0f, 34f);

            var layoutElement = obj.AddComponent<LayoutElement>();
            layoutElement.minHeight = 34f;
            layoutElement.preferredHeight = 34f;
            layoutElement.flexibleHeight = 0f;
        }

        private void AddButton(Transform parent, string label, UnityEngine.Events.UnityAction action, Color color)
        {
            var obj = new GameObject(label);
            obj.transform.SetParent(parent, false);

            var image = obj.AddComponent<Image>();
            image.color = color;

            var button = obj.AddComponent<Button>();
            button.onClick.AddListener(action);

            var rect = obj.GetComponent<RectTransform>();
            rect.sizeDelta = new Vector2(92f, 42f);

            var layoutElement = obj.AddComponent<LayoutElement>();
            layoutElement.minHeight = 42f;
            layoutElement.preferredHeight = 42f;
            layoutElement.flexibleWidth = 1f;

            var labelObject = new GameObject("Label");
            labelObject.transform.SetParent(obj.transform, false);
            var text = labelObject.AddComponent<Text>();
            text.text = label;
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = 18;
            text.alignment = TextAnchor.MiddleCenter;
            text.color = Color.white;
            text.raycastTarget = false;

            var labelRect = labelObject.GetComponent<RectTransform>();
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = Vector2.zero;
            labelRect.offsetMax = Vector2.zero;
        }

        private void SetEmotion(AvatarEmotion emotion)
        {
            ResolveReferences();
            avatarDriver?.SetEmotion(emotion);
        }

        private void PlayGesture(AvatarGesture gesture)
        {
            ResolveReferences();
            avatarDriver?.PlayGesture(gesture);
        }

        private void SimulateSpeech()
        {
            ResolveReferences();
            if (speechRoutine != null)
            {
                StopCoroutine(speechRoutine);
            }

            speechRoutine = StartCoroutine(SimulateSpeechRoutine());
        }

        private IEnumerator SimulateSpeechRoutine()
        {
            if (avatarDriver == null)
            {
                yield break;
            }

            var clip = AudioFallback.CreatePromptTone(2.4f);
            if (audioSource != null)
            {
                audioSource.clip = clip;
                audioSource.Play();
            }

            avatarDriver.SetState(AvatarState.speaking);
            var duration = clip.length;
            var elapsed = 0f;

            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                var level = 0.35f + Mathf.Abs(Mathf.Sin(elapsed * 12f)) * 0.65f;
                avatarDriver.SetSpeechLevel(level);
                yield return null;
            }

            avatarDriver.SetSpeechLevel(0f);
            avatarDriver.SetState(AvatarState.idle);
            speechRoutine = null;
        }
    }
#else
    public sealed class AvatarTestPanel : MonoBehaviour
    {
    }
#endif
}
