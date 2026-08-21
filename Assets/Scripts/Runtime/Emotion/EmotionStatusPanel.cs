using UnityEngine;
using UnityEngine.UI;

namespace SrtpAvatar
{
    public sealed class EmotionStatusPanel : MonoBehaviour
    {
        private EmotionPerceptionController perception;
        private Text statusText;

        public void Initialize(EmotionPerceptionController controller)
        {
            if (perception != null)
            {
                perception.Updated -= Refresh;
            }

            perception = controller;
            if (perception != null)
            {
                perception.Updated += Refresh;
            }
        }

        private void Start()
        {
            CreateStatusText();
            Refresh();
        }

        private void OnDestroy()
        {
            if (perception != null)
            {
                perception.Updated -= Refresh;
            }
        }

        private void CreateStatusText()
        {
            if (statusText != null)
            {
                return;
            }

            var canvas = Object.FindAnyObjectByType<Canvas>();
            if (canvas == null)
            {
                return;
            }

            var existing = canvas.transform.Find("Emotion Status")?.GetComponent<Text>();
            if (existing != null)
            {
                statusText = existing;
                return;
            }

            var panel = new GameObject("Emotion Status");
            panel.transform.SetParent(canvas.transform, false);
            statusText = panel.AddComponent<Text>();
            statusText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            statusText.fontSize = 18;
            statusText.alignment = TextAnchor.UpperLeft;
            statusText.color = new Color(0.08f, 0.3f, 0.4f, 0.82f);
            var rect = statusText.rectTransform;
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(34f, -250f);
            rect.sizeDelta = new Vector2(450f, 120f);
        }

        private void Refresh()
        {
            if (statusText == null || perception == null)
            {
                return;
            }

            var nowUnixMs = EmotionTime.UtcNowUnixMs;
            statusText.text = $"{perception.ConnectionStatus}\n面部表情线索：{Format(perception.FaceEmotion, perception.FaceStatus, nowUnixMs, EmotionSamplingPolicy.FaceFreshnessMs)}\n语音线索：{Format(perception.VoiceEmotion, perception.VoiceStatus, nowUnixMs, EmotionSamplingPolicy.VoiceFreshnessMs)}\n整体建议：{Format(perception.FusionEmotion, "", nowUnixMs, EmotionSamplingPolicy.FusionFreshnessMs)}";
        }

        private static string Format(UserEmo emotion, string status, long nowUnixMs, long freshnessMs)
        {
            if (emotion != null && emotion.IsUsable)
            {
                return emotion.IsFresh(nowUnixMs, freshnessMs)
                    ? $"{emotion.label} {emotion.score:0.00}"
                    : $"最近 {emotion.label} {emotion.score:0.00}（已过期）";
            }

            return string.IsNullOrWhiteSpace(status) ? "暂无" : $"暂无（{status}）";
        }
    }
}
