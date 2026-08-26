using System;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.UI;

namespace SrtpAvatar
{
    public sealed class EmotionEvaluationPanel : MonoBehaviour
    {
        private static readonly string[] ChineseLabels = { "开心", "生气", "难过", "惊讶", "中性" };
        private readonly EmotionEvaluationSession session = new EmotionEvaluationSession();
        private EmotionPerceptionController perception;
        private GameObject panelObject;
        private Button launcherButton;
        private Button closeButton;
        private Button exportButton;
        private Button clearButton;
        private readonly Button[] labelButtons = new Button[EmotionEvaluationSession.Labels.Length];
        private Text progressText;
        private Text messageText;

        public void Initialize(EmotionPerceptionController controller)
        {
            if (perception != null)
            {
                perception.FaceEmotionObserved -= OnFaceEmotionObserved;
            }

            perception = controller;
            if (perception != null)
            {
                perception.FaceEmotionObserved += OnFaceEmotionObserved;
            }
        }

        private void Start()
        {
            CreateUi();
            Refresh();
        }

        private void OnDestroy()
        {
            if (perception != null)
            {
                perception.FaceEmotionObserved -= OnFaceEmotionObserved;
            }

            launcherButton?.onClick.RemoveListener(Open);
            closeButton?.onClick.RemoveListener(Close);
            exportButton?.onClick.RemoveListener(ExportCsv);
            clearButton?.onClick.RemoveListener(Clear);
            for (var index = 0; index < labelButtons.Length; index++)
            {
                labelButtons[index]?.onClick.RemoveAllListeners();
            }
        }

        private void OnFaceEmotionObserved(UserEmo raw, UserEmo stabilized)
        {
            var activeBefore = session.ActiveLabel;
            if (!session.Observe(raw, stabilized))
            {
                return;
            }

            if (!session.IsCollecting)
            {
                messageText.text = $"{DisplayLabel(activeBefore)} 已完成 {session.TargetSamplesPerLabel} 次采集。";
            }
            Refresh();
        }

        private void Open()
        {
            panelObject.SetActive(true);
            messageText.text = "请选择你准备表现的情绪；选中后保持表情约 10 秒。";
            Refresh();
        }

        private void Close()
        {
            session.Stop();
            panelObject.SetActive(false);
        }

        private void SelectLabel(string label)
        {
            if (session.Start(label))
            {
                messageText.text = $"正在采集：{DisplayLabel(label)}。请自然看向镜头并保持表情。";
            }
            else
            {
                messageText.text = $"{DisplayLabel(label)} 已完成；可先导出或清空数据。";
            }
            Refresh();
        }

        private void Clear()
        {
            session.Clear();
            messageText.text = "本次内存中的评测记录已清空。";
            Refresh();
        }

        private void ExportCsv()
        {
            if (session.Records.Count == 0)
            {
                messageText.text = "还没有可导出的评测记录。";
                return;
            }

            try
            {
                var directory = Path.Combine(Application.persistentDataPath, "EmotionEvaluation");
                Directory.CreateDirectory(directory);
                var fileName = $"emotion-evaluation-{DateTime.Now:yyyyMMdd-HHmmss}.csv";
                var path = Path.Combine(directory, fileName);
                File.WriteAllText(path, session.ToCsv(), new UTF8Encoding(true));
                GUIUtility.systemCopyBuffer = path;
                messageText.text = $"已导出 {session.Records.Count} 条：{fileName}\n文件路径已复制，完整路径见日志。";
                Debug.Log($"Emotion evaluation CSV exported to: {path}");
            }
            catch (Exception ex)
            {
                messageText.text = "CSV 导出失败：" + ex.Message;
                Debug.LogException(ex);
            }
        }

        private void Refresh()
        {
            if (progressText == null)
            {
                return;
            }

            var builder = new StringBuilder();
            for (var index = 0; index < EmotionEvaluationSession.Labels.Length; index++)
            {
                var label = EmotionEvaluationSession.Labels[index];
                builder.Append(ChineseLabels[index]);
                builder.Append("  ");
                builder.Append(session.Count(label));
                builder.Append('/');
                builder.Append(session.TargetSamplesPerLabel);
                if (session.ActiveLabel == label)
                {
                    builder.Append("  ← 正在采集");
                }
                builder.AppendLine();

                if (labelButtons[index] != null)
                {
                    labelButtons[index].interactable = session.Count(label) < session.TargetSamplesPerLabel;
                    var image = labelButtons[index].GetComponent<Image>();
                    image.color = session.ActiveLabel == label
                        ? new Color(0.12f, 0.65f, 0.55f, 1f)
                        : new Color(0.08f, 0.48f, 0.62f, 0.95f);
                }
            }

            progressText.text = builder.ToString();
            exportButton.interactable = session.Records.Count > 0;
            clearButton.interactable = session.Records.Count > 0;
        }

        private void CreateUi()
        {
            var canvas = UnityEngine.Object.FindAnyObjectByType<Canvas>();
            if (canvas == null)
            {
                enabled = false;
                return;
            }

            launcherButton = CreateButton("Emotion Evaluation Launcher", canvas.transform, "评测", 20);
            SetRect(launcherButton.GetComponent<RectTransform>(), new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(34f, 42f), new Vector2(112f, 62f), new Vector2(0f, 0f));
            launcherButton.onClick.AddListener(Open);

            panelObject = new GameObject("Emotion Evaluation Panel");
            panelObject.transform.SetParent(canvas.transform, false);
            var background = panelObject.AddComponent<Image>();
            background.color = new Color(0.92f, 0.97f, 0.99f, 0.98f);
            var panelRect = background.rectTransform;
            panelRect.anchorMin = new Vector2(0.07f, 0.23f);
            panelRect.anchorMax = new Vector2(0.93f, 0.82f);
            panelRect.offsetMin = Vector2.zero;
            panelRect.offsetMax = Vector2.zero;

            var title = CreateText("Title", panelObject.transform, "情绪识别评测", 34, TextAnchor.MiddleCenter);
            SetRect(title.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, -42f), new Vector2(-40f, 62f));

            closeButton = CreateButton("Close", panelObject.transform, "关闭", 20);
            SetRect(closeButton.GetComponent<RectTransform>(), new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-24f, -24f), new Vector2(108f, 52f), new Vector2(1f, 1f));
            closeButton.onClick.AddListener(Close);

            var instructions = CreateText("Instructions", panelObject.transform, "每类选中后自动记录 10 次。只保存五类分数、最终标签和时间，不保存图像或声音。", 20, TextAnchor.UpperLeft);
            SetRect(instructions.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(32f, -108f), new Vector2(-64f, 74f), new Vector2(0f, 1f));

            progressText = CreateText("Progress", panelObject.transform, "", 23, TextAnchor.UpperLeft);
            SetRect(progressText.rectTransform, new Vector2(0f, 1f), new Vector2(0.48f, 1f), new Vector2(32f, -198f), new Vector2(-16f, 230f), new Vector2(0f, 1f));

            for (var index = 0; index < EmotionEvaluationSession.Labels.Length; index++)
            {
                var capturedIndex = index;
                labelButtons[index] = CreateButton("Select " + EmotionEvaluationSession.Labels[index], panelObject.transform, ChineseLabels[index], 23);
                var row = index / 2;
                var column = index % 2;
                var x = 0.55f + column * 0.22f;
                var y = -190f - row * 82f;
                SetRect(labelButtons[index].GetComponent<RectTransform>(), new Vector2(x, 1f), new Vector2(x, 1f), new Vector2(0f, y), new Vector2(170f, 62f));
                labelButtons[index].onClick.AddListener(() => SelectLabel(EmotionEvaluationSession.Labels[capturedIndex]));
            }

            messageText = CreateText("Message", panelObject.transform, "", 21, TextAnchor.UpperLeft);
            SetRect(messageText.rectTransform, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(32f, 188f), new Vector2(-64f, 126f), new Vector2(0f, 0f));

            exportButton = CreateButton("Export CSV", panelObject.transform, "导出 CSV", 22);
            SetRect(exportButton.GetComponent<RectTransform>(), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(-125f, 42f), new Vector2(210f, 68f), new Vector2(0.5f, 0f));
            exportButton.onClick.AddListener(ExportCsv);

            clearButton = CreateButton("Clear", panelObject.transform, "清空本次", 22);
            SetRect(clearButton.GetComponent<RectTransform>(), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(125f, 42f), new Vector2(210f, 68f), new Vector2(0.5f, 0f));
            clearButton.onClick.AddListener(Clear);

            panelObject.SetActive(false);
        }

        private static Text CreateText(string name, Transform parent, string content, int fontSize, TextAnchor alignment)
        {
            var target = new GameObject(name);
            target.transform.SetParent(parent, false);
            var text = target.AddComponent<Text>();
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = fontSize;
            text.alignment = alignment;
            text.color = new Color(0.06f, 0.20f, 0.30f, 1f);
            text.text = content;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            return text;
        }

        private static Button CreateButton(string name, Transform parent, string label, int fontSize)
        {
            var target = new GameObject(name);
            target.transform.SetParent(parent, false);
            var image = target.AddComponent<Image>();
            image.color = new Color(0.08f, 0.48f, 0.62f, 0.95f);
            var button = target.AddComponent<Button>();
            var text = CreateText("Label", target.transform, label, fontSize, TextAnchor.MiddleCenter);
            text.color = Color.white;
            text.rectTransform.anchorMin = Vector2.zero;
            text.rectTransform.anchorMax = Vector2.one;
            text.rectTransform.offsetMin = Vector2.zero;
            text.rectTransform.offsetMax = Vector2.zero;
            return button;
        }

        private static void SetRect(RectTransform rect, Vector2 anchorMin, Vector2 anchorMax, Vector2 anchoredPosition, Vector2 sizeDelta, Vector2? pivot = null)
        {
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.pivot = pivot ?? new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = anchoredPosition;
            rect.sizeDelta = sizeDelta;
        }

        private static string DisplayLabel(string label)
        {
            var normalized = UserEmo.NormalizeLabel(label);
            for (var index = 0; index < EmotionEvaluationSession.Labels.Length; index++)
            {
                if (EmotionEvaluationSession.Labels[index] == normalized)
                {
                    return ChineseLabels[index];
                }
            }
            return normalized;
        }
    }
}
