using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

namespace SrtpAvatar
{
    public sealed class DeepSeekDialogueClient
    {
        private readonly AppConfig config;

        public DeepSeekDialogueClient(AppConfig config)
        {
            this.config = config;
        }

        public async Task<DialogueDecision> CompleteAsync(string userText, string recentContext, string longTermContext)
        {
            var apiKey = RuntimeSecrets.ResolveDeepSeekApiKey(config);
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                throw new InvalidOperationException("DeepSeek Key 未配置。");
            }

            var messages = new List<ChatMessage>
            {
                new ChatMessage { role = "system", content = DialoguePrompt.SystemPrompt }
            };

            if (!string.IsNullOrWhiteSpace(longTermContext))
            {
                messages.Add(new ChatMessage { role = "system", content = longTermContext });
            }

            if (!string.IsNullOrWhiteSpace(recentContext))
            {
                messages.Add(new ChatMessage { role = "system", content = recentContext });
            }

            messages.Add(new ChatMessage { role = "user", content = string.IsNullOrWhiteSpace(userText) ? "你好" : userText.Trim() });

            var requestBody = new ChatCompletionRequest
            {
                model = RuntimeSecrets.ResolveDeepSeekModel(config),
                temperature = 0.7f,
                response_format = new ResponseFormat { type = "json_object" },
                messages = messages.ToArray()
            };

            using var request = new UnityWebRequest(RuntimeSecrets.DeepSeekChatCompletionUrl(config), UnityWebRequest.kHttpVerbPOST);
            request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(JsonUtility.ToJson(requestBody)));
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");
            request.SetRequestHeader("Authorization", $"Bearer {apiKey}");
            await UnityWebRequestUtil.SendAsync(request, ResolveTimeoutSeconds());

            var response = JsonUtility.FromJson<ChatCompletionResponse>(request.downloadHandler.text);
            var content = response?.choices != null && response.choices.Length > 0 ? response.choices[0].message.content : "";
            return DialogueParser.FromPossiblyWrappedJson(content);
        }

        private int ResolveTimeoutSeconds()
        {
            return config == null ? 25 : Mathf.Clamp(config.deepSeekRequestTimeoutSeconds, 5, 60);
        }

        [Serializable]
        private sealed class ChatCompletionRequest
        {
            public string model;
            public ChatMessage[] messages;
            public float temperature;
            public ResponseFormat response_format;
        }

        [Serializable]
        private sealed class ResponseFormat
        {
            public string type;
        }

        [Serializable]
        private sealed class ChatMessage
        {
            public string role;
            public string content;
        }

        [Serializable]
        private sealed class ChatCompletionResponse
        {
            public ChatChoice[] choices;
        }

        [Serializable]
        private sealed class ChatChoice
        {
            public ChatMessage message;
        }
    }
}
