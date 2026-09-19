using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UI;

namespace SrtpAvatar
{
    public sealed class MemoryManagementPanel : MonoBehaviour
    {
        private DigitalHumanAppController app;
        private RectTransform overlay, content;
        private Text count, feedback, selection;
        private string selectedId;
        private string snapshot = "";
        private readonly Dictionary<string, Button> entries = new Dictionary<string, Button>();
        public bool IsOpen => overlay != null && overlay.gameObject.activeSelf;
        public InputField EditInput { get; private set; }
        public Button SaveButton { get; private set; }
        public Button DeleteButton { get; private set; }
        public Button CloseButton { get; private set; }
        public string SelectedId => selectedId;

        public void Bind(DigitalHumanAppController owner, Transform parent)
        {
            app = owner;
            overlay = ConversationUi.Box("Memory Overlay", parent, new Color(.02f, .08f, .12f, .65f));
            ConversationUi.Fill(overlay);
            var panel = ConversationUi.Box("Memory Manager", overlay, new Color(.95f, .98f, 1f));
            ConversationUi.Place(panel, new Vector2(.055f, .10f), new Vector2(.945f, .91f), Vector2.zero, Vector2.zero);
            var title = ConversationUi.Text("Title", panel, "长期记忆", 32);
            ConversationUi.Place(title.rectTransform, new Vector2(.04f, .90f), new Vector2(.7f, .985f), Vector2.zero, Vector2.zero);
            CloseButton = ConversationUi.Button("Close", panel, "关闭", Hide);
            ConversationUi.Place((RectTransform)CloseButton.transform, new Vector2(.78f, .91f), new Vector2(.96f, .97f), Vector2.zero, Vector2.zero);
            count = ConversationUi.Text("Count", panel, "", 21);
            ConversationUi.Place(count.rectTransform, new Vector2(.04f, .845f), new Vector2(.96f, .90f), Vector2.zero, Vector2.zero);
            var scroll = ConversationUi.Scroll("Memory List", panel, out content);
            ConversationUi.Place((RectTransform)scroll.transform, new Vector2(.03f, .45f), new Vector2(.97f, .835f), Vector2.zero, Vector2.zero);
            selection = ConversationUi.Text("Selection", panel, "选择一条记忆后修改或删除", 22);
            ConversationUi.Place(selection.rectTransform, new Vector2(.04f, .39f), new Vector2(.96f, .445f), Vector2.zero, Vector2.zero);
            EditInput = ConversationUi.Input("Memory Text", panel, "选择上方的一条记忆", 80);
            ConversationUi.Place((RectTransform)EditInput.transform, new Vector2(.04f, .21f), new Vector2(.96f, .385f), Vector2.zero, Vector2.zero);
            ConversationUi.Row("Memory Actions", panel, out var actions);
            ConversationUi.Place(actions, new Vector2(.04f, .105f), new Vector2(.96f, .185f), Vector2.zero, Vector2.zero);
            SaveButton = ConversationUi.Button("Save Memory", actions, "保存修改", Save, 180);
            DeleteButton = ConversationUi.Button("Delete Memory", actions, "删除这条", Delete, 180);
            DeleteButton.GetComponent<Image>().color = new Color(.64f, .28f, .26f);
            feedback = ConversationUi.Text("Memory Feedback", panel, "", 21);
            ConversationUi.Place(feedback.rectTransform, new Vector2(.04f, .015f), new Vector2(.96f, .095f), Vector2.zero, Vector2.zero);
            overlay.gameObject.SetActive(false);
        }
        public void Show()
        {
            if (!app.CanSubmitText) return;
            app.SetMemoryPanelOpen(true);
            overlay.SetAsLastSibling(); overlay.gameObject.SetActive(true);
            feedback.text = "";
            RefreshEntries();
        }
        public void Hide()
        {
            overlay.gameObject.SetActive(false);
            app.SetMemoryPanelOpen(false);
        }
        private void OnDisable() { if (app != null) app.SetMemoryPanelOpen(false); }
        public void RefreshEntries()
        {
            var values = app.GetMemoryEntries();
            count.text = values.Count == 0 ? "暂时没有长期记忆。" : $"共 {values.Count} 条 · 选择后可修改或删除";
            var key = new StringBuilder();
            foreach (var value in values) key.Append(value.Id).Append(value.Text).Append(value.UpdatedAt);
            if (snapshot != key.ToString())
            {
                snapshot = key.ToString();
                foreach (Transform child in content) { child.gameObject.SetActive(false); Destroy(child.gameObject); }
                entries.Clear();
                for (var i = values.Count - 1; i >= 0; i--)
                {
                    var entry = values[i];
                    var button = ConversationUi.Button("Memory " + entry.Id, content, entry.Text, () => SelectEntry(entry.Id));
                    button.GetComponent<LayoutElement>().preferredHeight = 112;
                    var label = button.GetComponentInChildren<Text>(); label.alignment = TextAnchor.MiddleLeft;
                    label.color = ConversationUi.Ink; label.fontSize = 24;
                    entries.Add(entry.Id, button);
                }
                if (selectedId != null && !entries.ContainsKey(selectedId))
                { selectedId = null; EditInput.SetTextWithoutNotify(""); }
            }
            var editable = selectedId != null && app.CanSubmitText;
            EditInput.interactable = editable; SaveButton.interactable = editable; DeleteButton.interactable = editable;
            selection.text = selectedId == null ? "选择一条记忆后修改或删除" : "编辑选中的记忆（最多 80 字）";
            foreach (var pair in entries) pair.Value.GetComponent<Image>().color = pair.Key == selectedId
                ? new Color(.70f, .88f, .92f) : Color.white;
            LayoutRebuilder.ForceRebuildLayoutImmediate(content);
        }
        public void SelectEntry(string id)
        {
            foreach (var entry in app.GetMemoryEntries())
            {
                if (entry.Id != id) continue;
                selectedId = id; EditInput.SetTextWithoutNotify(entry.Text); feedback.text = "";
                RefreshEntries(); return;
            }
        }
        private void Save()
        {
            try { app.UpdateMemoryEntry(selectedId, EditInput.text); feedback.text = "修改已保存。"; RefreshEntries(); }
            catch (Exception ex) { feedback.text = ex.Message; }
        }
        private void Delete()
        {
            try { app.DeleteMemoryEntry(selectedId); feedback.text = "这条记忆已删除。"; RefreshEntries(); }
            catch (Exception ex) { feedback.text = ex.Message; }
        }
    }
}
