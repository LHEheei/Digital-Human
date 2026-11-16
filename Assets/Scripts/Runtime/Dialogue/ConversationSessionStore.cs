using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace SrtpAvatar
{
    public sealed class ConversationSessionStore
    {
        private const int CurrentVersion = 1;
        private readonly string filePath;
        private Archive archive;

        public ConversationSessionStore(string filePath)
        {
            this.filePath = filePath ?? throw new ArgumentNullException(nameof(filePath));
            archive = Load();
            RestoreActive();
        }

        public string ActiveId => archive.activeId;
        public string LoadError { get; private set; }
        public ConversationHistory History { get; private set; }
        public ConversationMemory Memory { get; private set; }

        public IReadOnlyList<ConversationSessionInfo> GetSessions()
        {
            var sessions = new List<ConversationSessionInfo>(archive.sessions.Count);
            foreach (var item in archive.sessions)
            {
                var title = item.id == ActiveId ? ActiveTitle(item) : item.title;
                sessions.Add(new ConversationSessionInfo(item.id, string.IsNullOrEmpty(title) ? "新会话" : title, item.updatedAt));
            }
            sessions.Sort((left, right) => right.UpdatedAt.CompareTo(left.UpdatedAt));
            return sessions;
        }

        public void SaveActive() => Write(CaptureActive(true));

        public void Create()
        {
            var next = CaptureActive(false);
            var session = SessionData.Empty();
            next.sessions.Insert(0, session);
            next.activeId = session.id;
            Write(next);
            RestoreActive();
        }

        public void Open(string id)
        {
            if (id == ActiveId) return;
            FindIndex(id);
            var next = CaptureActive(false);
            next.activeId = id;
            Write(next);
            RestoreActive();
        }

        public void Delete(string id)
        {
            var index = FindIndex(id);
            var deletingActive = ActiveId == id;
            var next = deletingActive ? CopyArchive() : CaptureActive(false);
            next.sessions.RemoveAt(index);
            if (next.sessions.Count == 0) next.sessions.Add(SessionData.Empty());
            if (deletingActive)
            {
                var latest = next.sessions[0];
                foreach (var item in next.sessions)
                    if (item.updatedAt > latest.updatedAt) latest = item;
                next.activeId = latest.id;
            }
            Write(next);
            if (deletingActive) RestoreActive();
        }

        public void ClearAllContexts()
        {
            if (LoadError != null) throw new AppException(AppErr.sessionStorage, "本地会话文件无法读取，暂时无法清除全部会话的上下文。");
            var next = CaptureActive(false);
            for (var i = 0; i < next.sessions.Count; i++)
            {
                var item = next.sessions[i];
                next.sessions[i] = new SessionData { id = item.id, title = item.title, updatedAt = item.updatedAt,
                    history = item.history, recent = new List<ConversationMemory.Turn>() };
            }
            Write(next);
            Memory.Clear();
        }

        private void RestoreActive()
        {
            var saved = archive.sessions[FindIndex(ActiveId)];
            History = ConversationHistory.Restore(saved.history);
            Memory = new ConversationMemory(saved.recent);
        }

        private int FindIndex(string id)
        {
            var index = archive.sessions.FindIndex(item => item.id == id);
            if (index < 0) throw new AppException(AppErr.sessionStorage, "这条会话已不存在。");
            return index;
        }

        private Archive CopyArchive() => new Archive { activeId = ActiveId, sessions = new List<SessionData>(archive.sessions) };

        private Archive CaptureActive(bool recordActivity)
        {
            var next = CopyArchive();
            var index = FindIndex(ActiveId);
            var item = next.sessions[index];
            next.sessions[index] = new SessionData { id = item.id, title = ActiveTitle(item),
                updatedAt = recordActivity ? DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() : item.updatedAt,
                history = History.Capture(), recent = Memory.Capture() };
            return next;
        }

        private string ActiveTitle(SessionData session)
        {
            if (!string.IsNullOrEmpty(session.title)) return session.title;
            foreach (var turn in History.Turns)
            {
                if (turn.Status == ConversationTurnStatus.Pending) continue;
                var title = turn.UserText.Replace('\r', ' ').Replace('\n', ' ').Trim();
                const int maxLength = 28;
                if (title.Length <= maxLength) return title;
                return title.Substring(0, char.IsHighSurrogate(title[maxLength - 1]) ? maxLength - 1 : maxLength) + "…";
            }
            return "";
        }

        private Archive Load()
        {
            try
            {
                var loaded = new Archive { version = 0, sessions = null };
                JsonUtility.FromJsonOverwrite(File.ReadAllText(filePath), loaded);
                if (!IsValid(loaded)) throw new ArgumentException("Invalid conversation archive.");
                return loaded;
            }
            catch (FileNotFoundException) { }
            catch (DirectoryNotFoundException) { }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is ArgumentException)
            {
                LoadError = "本地会话无法读取，原文件已保留。本次会话仅在运行期间保留。";
                Debug.LogWarning("Could not load local conversations: " + ex.GetType().Name);
            }
            var session = SessionData.Empty();
            return new Archive { activeId = session.id, sessions = new List<SessionData> { session } };
        }

        private static bool IsValid(Archive loaded)
        {
            if (loaded.version != CurrentVersion || loaded.sessions == null) return false;
            var ids = new HashSet<string>();
            foreach (var item in loaded.sessions)
            {
                if (item == null || string.IsNullOrWhiteSpace(item.id) || !ids.Add(item.id) || item.history == null || item.recent == null)
                    return false;
                if (item.history.Exists(turn => turn == null || string.IsNullOrWhiteSpace(turn.userText) ||
                    turn.status < ConversationTurnStatus.Completed || turn.status > ConversationTurnStatus.Cancelled)) return false;
                if (item.recent.Exists(turn => turn == null || string.IsNullOrWhiteSpace(turn.userText) || string.IsNullOrWhiteSpace(turn.reply)))
                    return false;
            }
            return ids.Contains(loaded.activeId);
        }

        private void Write(Archive next)
        {
            if (LoadError == null)
            {
                try { AtomicFile.WriteUtf8(filePath, JsonUtility.ToJson(next)); }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
                {
                    throw new AppException(AppErr.sessionStorage, "本地会话保存失败，请检查设备存储后重试。", ex);
                }
            }
            archive = next;
        }

        [Serializable]
        private sealed class Archive
        {
            public int version = CurrentVersion;
            public string activeId;
            public List<SessionData> sessions;
        }

        [Serializable]
        private sealed class SessionData
        {
            public string id, title;
            public long updatedAt;
            public List<ConversationHistory.SavedTurn> history = new List<ConversationHistory.SavedTurn>();
            public List<ConversationMemory.Turn> recent = new List<ConversationMemory.Turn>();

            public static SessionData Empty() => new SessionData { id = Guid.NewGuid().ToString("N"), title = "",
                updatedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() };
        }
    }

    public readonly struct ConversationSessionInfo
    {
        public readonly string Id, Title;
        public readonly long UpdatedAt;

        internal ConversationSessionInfo(string id, string title, long updatedAt)
        { Id = id; Title = title; UpdatedAt = updatedAt; }
    }
}
