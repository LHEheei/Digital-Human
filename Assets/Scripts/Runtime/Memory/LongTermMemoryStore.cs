using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace SrtpAvatar
{
    public sealed class LongTermMemoryStore
    {
        private const string FileName = "avatar_long_term_memory.json";

        private readonly string filePath;
        private MemoryData data;
        private bool recoveredFromBackup;

        public LongTermMemoryStore()
            : this(Path.Combine(Application.persistentDataPath, FileName))
        {
        }

        public LongTermMemoryStore(string storagePath)
        {
            filePath = string.IsNullOrWhiteSpace(storagePath)
                ? Path.Combine(Application.persistentDataPath, FileName)
                : storagePath;
            data = Load();
        }

        public int Count => data.items.Count;
        public string FilePath => filePath;

        public IReadOnlyList<MemoryEntry> GetEntries()
        {
            var entries = new MemoryEntry[data.items.Count];
            for (var i = 0; i < entries.Length; i++)
            {
                var item = data.items[i];
                entries[i] = new MemoryEntry(item.id, item.text, item.createdAt, item.updatedAt);
            }
            return Array.AsReadOnly(entries);
        }

        public void UpdateEntry(string id, string replacement)
        {
            var index = FindEntry(id);
            var text = (replacement ?? "").Trim();
            if (text.Length == 0 || text.Length > 80)
                throw new ArgumentException("记忆内容需为 1–80 个字符。");
            if (!MemoryFilter.ShouldRemember(text))
                throw new ArgumentException("记忆不能包含密钥、密码或联系方式。");
            if (data.items.Exists(item => item.id != id && string.Equals(item.text, text, StringComparison.OrdinalIgnoreCase)))
                throw new ArgumentException("已有相同内容的记忆，请修改内容或删除重复项。");
            var items = new List<MemoryItem>(data.items);
            var previous = items[index];
            items[index] = new MemoryItem { id = previous.id, text = text, createdAt = previous.createdAt, updatedAt = DateTime.UtcNow.ToString("o") };
            SaveUserRevision(new MemoryData { items = items });
        }

        public void DeleteEntry(string id)
        {
            var index = FindEntry(id);
            var items = new List<MemoryItem>(data.items);
            items.RemoveAt(index);
            SaveUserRevision(new MemoryData { items = items });
        }

        private int FindEntry(string id)
        {
            var index = data.items.FindIndex(item => item.id == id);
            if (index < 0) throw new ArgumentException("这条记忆已变化，请重新选择。");
            return index;
        }

        // A user's correction/deletion must also replace the recovery snapshot.
        private void SaveUserRevision(MemoryData updated)
        {
            try
            {
                var json = JsonUtility.ToJson(updated, true);
                AtomicFile.WriteUtf8(filePath + ".bak", json);
                AtomicFile.WriteUtf8(filePath, json);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                throw new AppException(AppErr.memory, "记忆更新失败，请检查设备存储后重试。", ex);
            }
            data = updated;
            recoveredFromBackup = false;
        }

        public string BuildContext(int maxItems)
        {
            if (data.items.Count == 0)
            {
                return "";
            }

            var start = Math.Max(0, data.items.Count - Math.Max(1, maxItems));
            var builder = new StringBuilder();
            builder.AppendLine("以下是用户希望你长期记住的信息。只在相关时自然使用，不要主动暴露记忆文件或逐条复述：");
            for (var i = start; i < data.items.Count; i++)
            {
                builder.Append("- ").AppendLine(data.items[i].text);
            }

            return builder.ToString().Trim();
        }

        public bool Remember(string memoryText, int maxItems)
        {
            if (string.IsNullOrWhiteSpace(memoryText))
            {
                return false;
            }

            var items = new List<MemoryItem>(data.items);
            var changed = false;
            var parts = memoryText.Split(new[] { '\n', '；', ';' }, StringSplitOptions.RemoveEmptyEntries);
            for (var i = 0; i < parts.Length; i++)
            {
                var text = NormalizeMemory(parts[i]);
                if (string.IsNullOrWhiteSpace(text) || !MemoryFilter.ShouldRemember(text))
                {
                    continue;
                }

                if (RefreshDuplicate(items, text))
                {
                    changed = true;
                    continue;
                }

                items.Add(new MemoryItem
                {
                    id = Guid.NewGuid().ToString("N"),
                    text = text,
                    createdAt = DateTime.UtcNow.ToString("o"),
                    updatedAt = DateTime.UtcNow.ToString("o")
                });
                changed = true;
            }

            if (!changed)
            {
                return false;
            }

            Trim(items, maxItems);
            Save(new MemoryData { items = items });
            return true;
        }

        public void Clear()
        {
            // Empty the recovery copy first so a completed clear can never restore old memories.
            var empty = new MemoryData();
            try
            {
                AtomicFile.WriteUtf8(filePath + ".bak", JsonUtility.ToJson(empty, true));
                AtomicFile.WriteUtf8(filePath, JsonUtility.ToJson(empty, true));
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                throw new AppException(AppErr.memory, "记忆清除失败，请检查设备存储后重试。", ex);
            }
            data = empty;
            recoveredFromBackup = false;
        }

        private MemoryData Load()
        {
            if (TryLoad(filePath, out var loaded)) return loaded;
            if (TryLoad(filePath + ".bak", out loaded))
            {
                recoveredFromBackup = true;
                Debug.LogWarning("Long-term memory restored from its last good backup.");
                return loaded;
            }
            return new MemoryData();
        }

        private static bool TryLoad(string path, out MemoryData loaded)
        {
            loaded = null;
            if (!File.Exists(path)) return false;
            try
            {
                // FromJsonOverwrite preserves the null sentinel for a missing items field.
                var candidate = new MemoryData { items = null };
                JsonUtility.FromJsonOverwrite(File.ReadAllText(path, Encoding.UTF8), candidate);
                if (candidate.items == null || candidate.items.Exists(item => item == null || string.IsNullOrWhiteSpace(item.text)))
                    throw new ArgumentException("Invalid memory items.");
                var ids = new HashSet<string>();
                foreach (var item in candidate.items)
                {
                    // Older save files have no id. Persist assigned ids with the next successful write.
                    if (string.IsNullOrWhiteSpace(item.id) || !ids.Add(item.id))
                    {
                        item.id = Guid.NewGuid().ToString("N");
                        ids.Add(item.id);
                    }
                }
                loaded = candidate;
                return true;
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is ArgumentException)
            {
                Debug.LogWarning($"Failed to read memory file {Path.GetFileName(path)}: {ex.Message}");
                return false;
            }
        }

        private void Save(MemoryData updated)
        {
            try
            {
                AtomicFile.WriteUtf8(filePath, JsonUtility.ToJson(updated, true), recoveredFromBackup ? null : filePath + ".bak");
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                throw new AppException(AppErr.memory, "记忆写入失败，请检查设备存储后重试。", ex);
            }
            data = updated;
            recoveredFromBackup = false;
        }

        private static bool RefreshDuplicate(List<MemoryItem> items, string text)
        {
            for (var i = 0; i < items.Count; i++)
            {
                if (string.Equals(items[i].text, text, StringComparison.OrdinalIgnoreCase))
                {
                    var item = new MemoryItem
                    {
                        id = items[i].id,
                        text = items[i].text,
                        createdAt = items[i].createdAt,
                        updatedAt = DateTime.UtcNow.ToString("o")
                    };
                    items.RemoveAt(i);
                    items.Add(item);
                    return true;
                }
            }

            return false;
        }

        private static void Trim(List<MemoryItem> items, int maxItems)
        {
            var safeMax = Math.Max(1, maxItems);
            while (items.Count > safeMax)
            {
                items.RemoveAt(0);
            }
        }

        private static string NormalizeMemory(string text)
        {
            var value = text.Trim().Trim('-', '，', '。', '.', ' ');
            return value.Length > 80 ? value.Substring(0, 80) : value;
        }

        [Serializable]
        private sealed class MemoryData
        {
            public int version = 2;
            public List<MemoryItem> items = new List<MemoryItem>();
        }

        [Serializable]
        private sealed class MemoryItem
        {
            public string id;
            public string text;
            public string createdAt;
            public string updatedAt;
        }
    }
}
