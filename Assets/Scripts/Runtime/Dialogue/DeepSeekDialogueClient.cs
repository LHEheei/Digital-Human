using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
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

        public async Task<DialogueDecision> CompleteAsync(TalkInput input, string recentContext, string longTermContext, CancellationToken cancellationToken)
        {
            var apiKey = RuntimeSecrets.ResolveDeepSeekApiKey(config);
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                throw new AppException(AppErr.auth, "DeepSeek Key 未配置。");
            }

            if (!RuntimeSecrets.TryNormalizeDeepSeekBaseUrl(RuntimeSecrets.ResolveDeepSeekBaseUrl(config), out _, out var warning, out var error))
            {
                throw new AppException(AppErr.config, error);
            }

            if (!string.IsNullOrWhiteSpace(warning))
            {
                Debug.LogWarning(warning);
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

            var emotionContext = input == null ? "" : input.BuildEmotionContext();
            if (!string.IsNullOrWhiteSpace(emotionContext))
            {
                messages.Add(new ChatMessage { role = "system", content = emotionContext });
            }

            var userText = input == null ? "" : input.text;
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
            await UnityWebRequestUtil.SendAsync(request, ResolveTimeoutSeconds(), AppErr.network, true, cancellationToken);

            var response = JsonUtility.FromJson<ChatCompletionResponse>(request.downloadHandler.text);
            var content = response?.choices != null && response.choices.Length > 0 ? response.choices[0].message.content : "";
            if (!DialogueParser.TryFromPossiblyWrappedJson(content, out var decision))
            {
                throw new AppException(AppErr.badJson, "DeepSeek 返回的 JSON 格式无效或内容为空。");
            }

            return decision;
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
