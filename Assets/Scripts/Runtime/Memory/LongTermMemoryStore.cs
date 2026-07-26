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

        public string BuildContext(int maxItems)
        {
            Trim(maxItems);
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

            var changed = false;
            var parts = memoryText.Split(new[] { '\n', '；', ';' }, StringSplitOptions.RemoveEmptyEntries);
            for (var i = 0; i < parts.Length; i++)
            {
                var text = NormalizeMemory(parts[i]);
                if (string.IsNullOrWhiteSpace(text) || !MemoryFilter.ShouldRemember(text))
                {
                    continue;
                }

                if (RefreshDuplicate(text))
                {
                    changed = true;
                    continue;
                }

                data.items.Add(new MemoryItem
                {
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

            changed |= Trim(maxItems);
            Save();
            return true;
        }

        public void Clear()
        {
            data.items.Clear();
            Save();
        }

        private MemoryData Load()
        {
            try
            {
                if (!File.Exists(filePath))
                {
                    return new MemoryData();
                }

                var json = File.ReadAllText(filePath, Encoding.UTF8);
                var loaded = JsonUtility.FromJson<MemoryData>(json);
                if (loaded == null || loaded.items == null)
                {
                    Debug.LogWarning("Long-term memory file is empty or invalid; a new memory store will be used.");
                    return new MemoryData();
                }

                return loaded;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"Failed to load long-term memory: {ex.Message}");
                return new MemoryData();
            }
        }

        private void Save()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(filePath));
                File.WriteAllText(filePath, JsonUtility.ToJson(data, true), Encoding.UTF8);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"Failed to save long-term memory: {ex.Message}");
            }
        }

        private bool RefreshDuplicate(string text)
        {
            for (var i = 0; i < data.items.Count; i++)
            {
                if (string.Equals(data.items[i].text, text, StringComparison.OrdinalIgnoreCase))
                {
                    var item = data.items[i];
                    item.updatedAt = DateTime.UtcNow.ToString("o");
                    data.items.RemoveAt(i);
                    data.items.Add(item);
                    return true;
                }
            }

            return false;
        }

        private bool Trim(int maxItems)
        {
            var changed = false;
            var safeMax = Math.Max(1, maxItems);
            while (data.items.Count > safeMax)
            {
                data.items.RemoveAt(0);
                changed = true;
            }

            return changed;
        }

        private static string NormalizeMemory(string text)
        {
            var value = text.Trim().Trim('-', '，', '。', '.', ' ');
            return value.Length > 80 ? value.Substring(0, 80) : value;
        }

        [Serializable]
        private sealed class MemoryData
        {
            public List<MemoryItem> items = new List<MemoryItem>();
        }

        [Serializable]
        private sealed class MemoryItem
        {
            public string text;
            public string createdAt;
            public string updatedAt;
        }
    }
}
