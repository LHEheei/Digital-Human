using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;

namespace SrtpAvatar
{
    public sealed class ConversationPanel : MonoBehaviour
    {
        private DigitalHumanAppController app;
        private Canvas canvas;
        private RectTransform root, dock, content;
        private Text status, feedback, empty;
        private readonly Dictionary<int, TurnView> rows = new Dictionary<int, TurnView>();
        private bool dirty;
        private bool forceBottom;
        private bool wasAtBottom = true;
        private ConversationHistory displayedHistory;
        private Vector2 lastSize;
        private Rect lastSafeArea;
        private float lastKeyboardHeight;
        private MemoryManagementPanel memory;
        public InputField MessageInput { get; private set; }
        public Button SendButton { get; private set; }
        public Button StopButton { get; private set; }
        public Button RetryButton { get; private set; }
        public Button MemoryButton { get; private set; }
        public ScrollRect HistoryScroll { get; private set; }
        public MemoryManagementPanel MemoryPanel => memory;

        public void Bind(DigitalHumanAppController owner, Canvas targetCanvas)
        {
            if (app != null) app.ConversationChanged -= MarkDirty;
            app = owner; canvas = targetCanvas;
            Build();
            if (isActiveAndEnabled) app.ConversationChanged += MarkDirty;
            RefreshLayout(); Refresh();
        }
        private void OnEnable()
        {
            if (app == null) return;
            app.ConversationChanged -= MarkDirty; app.ConversationChanged += MarkDirty;
            if (root != null) root.gameObject.SetActive(true);
            MarkDirty();
        }
        private void OnDisable()
        {
            if (app != null) app.ConversationChanged -= MarkDirty;
            if (memory != null && memory.IsOpen) memory.Hide();
            if (root != null) root.gameObject.SetActive(false);
        }
        private void OnDestroy() { if (root != null) Destroy(root.gameObject); }
        private void MarkDirty() => dirty = true;
        private void LateUpdate()
        {
            if (root == null) return;
            var size = canvas.pixelRect.size;
            var keyboardHeight = TouchScreenKeyboard.visible ? TouchScreenKeyboard.area.height : 0;
            if (size != lastSize || Screen.safeArea != lastSafeArea || keyboardHeight != lastKeyboardHeight) RefreshLayout();
            if (dirty) Refresh();
            wasAtBottom = HistoryScroll.verticalNormalizedPosition <= .04f;
        }
        public void RefreshLayout()
        {
            if (root == null) return;
            forceBottom |= wasAtBottom;
            lastSize = canvas.pixelRect.size; lastSafeArea = Screen.safeArea;
            lastKeyboardHeight = TouchScreenKeyboard.visible ? TouchScreenKeyboard.area.height : 0;
            var width = Mathf.Max(1, Screen.width); var height = Mathf.Max(1, Screen.height);
            var minimum = new Vector2(lastSafeArea.xMin / width, Mathf.Max(lastSafeArea.yMin, lastKeyboardHeight) / height);
            var maximum = new Vector2(lastSafeArea.xMax / width, lastSafeArea.yMax / height);
            ConversationUi.Place(root, minimum, maximum, Vector2.zero, Vector2.zero);
            var portrait = lastSize.y >= lastSize.x;
            ConversationUi.Place(dock, portrait ? new Vector2(.025f, .018f) : new Vector2(.52f, .035f),
                portrait ? new Vector2(.975f, .43f) : new Vector2(.98f, .88f), Vector2.zero, Vector2.zero);
            GetComponent<MobilePresentationLayout>()?.ApplyConversationLayout(portrait);
            dirty = true;
        }
        private void Build()
        {
            if (root != null) return;
            root = ConversationUi.Rect("Conversation Interface", canvas.transform);
            var title = ConversationUi.Text("Title", root, "和我聊聊", 32);
            ConversationUi.Place(title.rectTransform, new Vector2(0, 1), new Vector2(.65f, 1), new Vector2(30, -68), new Vector2(0, -18));
            status = ConversationUi.Text("State", root, "等待输入", 22);
            ConversationUi.Place(status.rectTransform, new Vector2(0, 1), new Vector2(.75f, 1), new Vector2(30, -112), new Vector2(0, -68));
            dock = ConversationUi.Box("Chat Dock", root, new Color(.94f, .98f, 1f, .97f));
            var historyTitle = ConversationUi.Text("History Title", dock, "聊天记录", 25);
            ConversationUi.Place(historyTitle.rectTransform, new Vector2(0, 1), new Vector2(.5f, 1), new Vector2(22, -68), new Vector2(0, -12));
            MemoryButton = ConversationUi.Button("Memory Button", dock, "记忆", () => memory.Show());
            ConversationUi.Place((RectTransform)MemoryButton.transform, Vector2.one, Vector2.one, new Vector2(-140, -68), new Vector2(-20, -12));
            HistoryScroll = ConversationUi.Scroll("Chat History", dock, out content);
            ConversationUi.Place((RectTransform)HistoryScroll.transform, Vector2.zero, Vector2.one, new Vector2(14, 142), new Vector2(-14, -80));
            empty = ConversationUi.Text("Empty History", content, "说句话，或在下方输入消息。", 24);
            ConversationUi.Row("Composer", dock, out var composer);
            ConversationUi.Place(composer, Vector2.zero, new Vector2(1, 0), new Vector2(20, 18), new Vector2(-20, 100));
            MessageInput = ConversationUi.Input("Message Input", composer, "输入消息…", 500);
            MessageInput.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1;
            MessageInput.gameObject.AddComponent<ConversationInputFocus>().Bind(app);
            MessageInput.onValueChanged.AddListener(_ => MarkDirty());
            SendButton = ConversationUi.Button("Send Button", composer, "发送", Send);
            StopButton = ConversationUi.Button("Stop Button", composer, "停止", app.CancelCurrentConversation);
            RetryButton = ConversationUi.Button("Retry Button", composer, "重试", Retry, 100);
            feedback = ConversationUi.Text("Feedback", dock, "", 20);
            ConversationUi.Place(feedback.rectTransform, Vector2.zero, new Vector2(1, 0), new Vector2(24, 106), new Vector2(-24, 138));
            memory = root.gameObject.AddComponent<MemoryManagementPanel>();
            memory.Bind(app, root);
        }
        private async void Send()
        {
            var text = MessageInput.text;
            if (string.IsNullOrWhiteSpace(text) || !app.CanSubmitText) return;
            MessageInput.SetTextWithoutNotify("");
            MessageInput.DeactivateInputField();
            app.SetTextInputFocused(false);
            forceBottom = true;
            await Observe(app.SubmitTextAsync(text));
        }
        private async void Retry()
        {
            if (!app.CanRetryLastTurn) return;
            forceBottom = true;
            await Observe(app.RetryLastTurnAsync());
        }
        private async Task Observe(Task operation)
        {
            try { await operation; }
            catch (Exception ex) { if (feedback != null) feedback.text = ex.Message; }
            MarkDirty();
        }
        private void Refresh()
        {
            dirty = false;
            if (app == null) return;
            status.text = app.IsBusy ? app.CurrentState == AvatarState.speaking ? "正在回答" : "正在思考"
                : app.ListeningPhase == SpeechListeningPhase.Preparing ? "正在准备语音服务"
                : app.ListeningPhase == SpeechListeningPhase.Ready ? "正在聆听"
                : app.ListeningPhase == SpeechListeningPhase.Failed ? "语音暂不可用" : "等待输入";
            MessageInput.interactable = app.CanSubmitText;
            SendButton.gameObject.SetActive(!app.IsBusy); StopButton.gameObject.SetActive(app.IsBusy);
            SendButton.interactable = app.CanSubmitText && !string.IsNullOrWhiteSpace(MessageInput.text);
            RetryButton.gameObject.SetActive(app.CanRetryLastTurn);
            MemoryButton.interactable = app.CanSubmitText;
            feedback.text = !app.IsBusy && !string.IsNullOrEmpty(app.ListeningErrorMessage) ? app.ListeningErrorMessage
                : app.LastPlaybackFailed ? "语音暂不可用，文字回复已保留。"
                : app.CanRetryLastTurn ? "本轮未完成，可以重试。" : "";
            empty.gameObject.SetActive(app.History.Turns.Count == 0);
            var atBottom = displayedHistory != app.History || forceBottom || HistoryScroll.verticalNormalizedPosition <= .04f || rows.Count == 0;
            displayedHistory = app.History;
            var previousPosition = content.anchoredPosition;
            var retained = new HashSet<int>();
            var sibling = 1; // The empty-state label remains the first child.
            foreach (var turn in app.History.Turns)
            {
                retained.Add(turn.Id);
                if (!rows.TryGetValue(turn.Id, out var row)) rows[turn.Id] = row = CreateTurnView(turn.Id);
                row.Root.SetSiblingIndex(sibling++);
                row.User.text = "你  ·  " + turn.UserText;
                row.Reply.text = "数字人  ·  " + (turn.Reply.Length > 0 ? turn.Reply : "…");
                row.Note.text = turn.Status == ConversationTurnStatus.Pending ? "回答中…"
                    : turn.Status == ConversationTurnStatus.Failed ? "未完成 · " + turn.ErrorMessage
                    : turn.Status == ConversationTurnStatus.Cancelled ? "已停止" : "";
                row.Note.gameObject.SetActive(row.Note.text.Length > 0);
            }
            var removed = new List<int>();
            foreach (var pair in rows) if (!retained.Contains(pair.Key))
            { pair.Value.Root.gameObject.SetActive(false); Destroy(pair.Value.Root.gameObject); removed.Add(pair.Key); }
            foreach (var id in removed) rows.Remove(id);
            LayoutRebuilder.ForceRebuildLayoutImmediate(content);
            if (atBottom) HistoryScroll.verticalNormalizedPosition = 0;
            else content.anchoredPosition = previousPosition;
            forceBottom = false;
            if (memory.IsOpen) memory.RefreshEntries();
        }
        private TurnView CreateTurnView(int id)
        {
            var rect = ConversationUi.Box("Turn " + id, content, new Color(1, 1, 1, .96f));
            var layout = rect.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(18, 18, 14, 14); layout.spacing = 10;
            layout.childControlWidth = true; layout.childControlHeight = true;
            layout.childForceExpandWidth = true; layout.childForceExpandHeight = false;
            var user = ConversationUi.Text("User", rect, "", 23); user.color = ConversationUi.Accent;
            var reply = ConversationUi.Text("Reply", rect, "", 25);
            var note = ConversationUi.Text("Turn Status", rect, "", 20); note.color = new Color(.50f, .32f, .18f);
            return new TurnView { Root = rect, User = user, Reply = reply, Note = note };
        }
        private sealed class TurnView { public RectTransform Root; public Text User, Reply, Note; }
    }
}
