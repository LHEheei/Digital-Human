using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace SrtpAvatar
{
    public sealed class ConversationSessionsPanel : MonoBehaviour
    {
        private DigitalHumanAppController app;
        private RectTransform overlay, content;
        private ScrollRect scroll;
        private Text feedback, deleteLabel;
        private string selectedId, selectedTitle;
        private bool confirmingDelete;
        private readonly Dictionary<string, Image> rows = new Dictionary<string, Image>();

        public bool IsOpen => overlay != null && overlay.gameObject.activeSelf;
        public Button NewButton { get; private set; }
        public Button OpenButton { get; private set; }
        public Button DeleteButton { get; private set; }
        public Button CloseButton { get; private set; }

        internal void Bind(DigitalHumanAppController owner, Transform parent)
        {
            app = owner;
            overlay = ConversationUi.Box("Sessions Overlay", parent, new Color(.02f, .08f, .12f, .65f));
            ConversationUi.Fill(overlay);
            var panel = ConversationUi.Box("Sessions Manager", overlay, new Color(.95f, .98f, 1f));
            ConversationUi.Place(panel, new Vector2(.055f, .10f), new Vector2(.945f, .91f), Vector2.zero, Vector2.zero);
            var title = ConversationUi.Text("Title", panel, "本地会话", 32);
            ConversationUi.Place(title.rectTransform, new Vector2(.04f, .90f), new Vector2(.7f, .985f), Vector2.zero, Vector2.zero);
            CloseButton = ConversationUi.Button("Close", panel, "关闭", Hide);
            ConversationUi.Place((RectTransform)CloseButton.transform, new Vector2(.78f, .91f), new Vector2(.96f, .97f), Vector2.zero, Vector2.zero);
            scroll = ConversationUi.Scroll("Sessions List", panel, out content);
            ConversationUi.Place((RectTransform)scroll.transform, new Vector2(.035f, .24f), new Vector2(.965f, .89f), Vector2.zero, Vector2.zero);

            var actions = ConversationUi.Row("Session Actions", panel, out var actionRow);
            ConversationUi.Place(actionRow, new Vector2(.04f, .12f), new Vector2(.96f, .23f), Vector2.zero, Vector2.zero);
            actions.childForceExpandWidth = true;
            NewButton = ConversationUi.Button("New Session", actionRow, "新建会话", () => Perform(app.CreateSession, true));
            OpenButton = ConversationUi.Button("Open Session", actionRow, "打开", () => Perform(() => app.OpenSession(selectedId), true));
            DeleteButton = ConversationUi.Button("Delete Session", actionRow, "删除", Delete);
            DeleteButton.GetComponent<Image>().color = new Color(.64f, .28f, .26f);
            deleteLabel = DeleteButton.GetComponentInChildren<Text>();
            feedback = ConversationUi.Text("Session Feedback", panel, "", 22);
            ConversationUi.Place(feedback.rectTransform, new Vector2(.04f, .025f), new Vector2(.96f, .115f), Vector2.zero, Vector2.zero);
            overlay.gameObject.SetActive(false);
        }

        public void Show()
        {
            if (!app.CanSubmitText || IsOpen) return;
            app.SetSessionsPanelOpen(true);
            overlay.SetAsLastSibling();
            overlay.gameObject.SetActive(true);
            RefreshRows();
        }

        public void Hide()
        {
            if (!IsOpen) return;
            overlay.gameObject.SetActive(false);
            app.SetSessionsPanelOpen(false);
        }

        private void RefreshRows()
        {
            foreach (Transform child in content)
            {
                child.gameObject.SetActive(false);
                Destroy(child.gameObject);
            }
            rows.Clear();
            foreach (var entry in app.GetSessions())
            {
                var active = entry.Id == app.ActiveSessionId;
                var label = active ? "当前 · " + entry.Title : entry.Title;
                var button = ConversationUi.Button("Session " + entry.Id, content, label, () => Select(entry));
                button.GetComponent<LayoutElement>().preferredHeight = 78;
                var caption = button.GetComponentInChildren<Text>();
                caption.color = ConversationUi.Ink;
                caption.alignment = TextAnchor.MiddleLeft;
                rows.Add(entry.Id, button.GetComponent<Image>());
                if (active) { selectedId = entry.Id; selectedTitle = entry.Title; }
            }
            confirmingDelete = false;
            RefreshSelection();
            LayoutRebuilder.ForceRebuildLayoutImmediate(content);
            scroll.verticalNormalizedPosition = 1;
        }

        private void Select(ConversationSessionInfo entry)
        {
            selectedId = entry.Id;
            selectedTitle = entry.Title;
            confirmingDelete = false;
            RefreshSelection();
        }

        private void RefreshSelection()
        {
            foreach (var row in rows)
                row.Value.color = row.Key == selectedId ? new Color(.70f, .88f, .92f) : Color.white;
            OpenButton.interactable = selectedId != app.ActiveSessionId;
            deleteLabel.text = confirmingDelete ? "确认删除" : "删除";
            feedback.text = confirmingDelete ? $"删除「{selectedTitle}」及其聊天记录？" : app.SessionStorageError;
        }

        private void Delete()
        {
            if (confirmingDelete) Perform(() => app.DeleteSession(selectedId), false);
            else
            {
                confirmingDelete = true;
                RefreshSelection();
            }
        }

        private void Perform(Action action, bool close)
        {
            try
            {
                action();
                if (close) Hide();
                else RefreshRows();
            }
            catch (Exception ex) { feedback.text = ex.Message; }
        }
    }
}
