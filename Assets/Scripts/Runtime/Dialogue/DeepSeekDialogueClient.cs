using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

namespace SrtpAvatar
{
    public sealed class DeepSeekDialogueClient : IDialogueClient
    {
        private readonly AppConfig config;

        public DeepSeekDialogueClient(AppConfig config)
        {
            this.config = config;
        }

        public bool IsAvailable => config.HasDeepSeekKey;

        public async Task<DialogueDecision> CompleteAsync(TalkInput input, string recentContext, string longTermContext, CancellationToken cancellationToken, Action<string> onReplySentence = null)
        {
            if (input == null || string.IsNullOrWhiteSpace(input.text))
                throw new ArgumentException("请输入对话内容。", nameof(input));
            cancellationToken.ThrowIfCancellationRequested();
            var apiKey = RuntimeSecrets.ResolveDeepSeekApiKey(config);
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                throw new AppException(AppErr.auth, "DeepSeek Key 未配置。");
            }

            if (!RuntimeSecrets.TryResolveDeepSeekBaseUrl(config, out var baseUrl, out var warning, out var error))
            {
                throw new AppException(AppErr.config, error);
            }

            if (!string.IsNullOrWhiteSpace(warning))
            {
                Debug.LogWarning(warning);
            }

            var messages = new List<ChatMessage>
            {
                new ChatMessage
                {
                    role = "system",
                    content = DialoguePrompt.SystemPrompt
                }
            };

            if (!string.IsNullOrWhiteSpace(longTermContext))
            {
                messages.Add(new ChatMessage { role = "system", content = longTermContext });
            }

            if (!string.IsNullOrWhiteSpace(recentContext))
            {
                messages.Add(new ChatMessage { role = "system", content = recentContext });
            }

            var emotionContext = input.BuildEmotionContext();
            if (!string.IsNullOrWhiteSpace(emotionContext))
            {
                messages.Add(new ChatMessage { role = "system", content = emotionContext });
            }

            var userText = input.text;
            messages.Add(new ChatMessage { role = "user", content = userText.Trim() });

            var requestBody = new ChatCompletionRequest
            {
                model = RuntimeSecrets.ResolveDeepSeekModel(config),
                stream = onReplySentence != null,
                temperature = 0.7f,
                // A mobile avatar needs a prompt reply, not a long chain of thought.
                // DeepSeek V4 enables thinking by default, which can delay the first
                // response beyond the UI timeout for a simple dialogue turn.
                thinking = new ThinkingOption { type = "disabled" },
                max_tokens = 320,
                response_format = new ResponseFormat { type = "json_object" },
                messages = messages.ToArray()
            };

            using var request = new UnityWebRequest(RuntimeSecrets.DeepSeekChatCompletionUrl(baseUrl), UnityWebRequest.kHttpVerbPOST);
            request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(JsonUtility.ToJson(requestBody)));
            var stream = onReplySentence == null ? null : new DialogueStreamDecoder(onReplySentence);
            var streamHandler = stream == null ? null : new DialogueStreamDownloadHandler(stream);
            request.downloadHandler = streamHandler == null ? (DownloadHandler)new DownloadHandlerBuffer() : streamHandler;
            request.SetRequestHeader("Content-Type", "application/json");
            request.SetRequestHeader("Authorization", $"Bearer {apiKey}");
            try
            {
                await UnityWebRequestUtil.SendAsync(request, ResolveTimeoutSeconds(), AppErr.network, true, cancellationToken);
            }
            catch (AppException) when (streamHandler?.Failure != null)
            {
                throw new AppException(AppErr.badJson, "流式回复解析失败。", streamHandler.Failure);
            }

            cancellationToken.ThrowIfCancellationRequested();
            if (stream != null) return stream.CompleteDecision();
            var response = JsonUtility.FromJson<ChatCompletionResponse>(request.downloadHandler.text);
            var content = ExtractFirstResponseContent(response);
            if (!DialogueParser.TryFromPossiblyWrappedJson(content, out var decision))
                throw new AppException(AppErr.badJson, "DeepSeek 返回的 JSON 格式无效或内容为空。");
            cancellationToken.ThrowIfCancellationRequested();
            return decision;
        }

        private int ResolveTimeoutSeconds()
        {
            return Mathf.Clamp(config.deepSeekRequestTimeoutSeconds, 5, 60);
        }

        private static string ExtractFirstResponseContent(ChatCompletionResponse response)
        {
            if (response?.choices == null || response.choices.Length == 0)
            {
                return "";
            }

            var firstChoice = response.choices[0];
            return firstChoice?.message?.content ?? "";
        }

        [Serializable]
        private sealed class ChatCompletionRequest
        {
            public string model;
            public bool stream;
            public ChatMessage[] messages;
            public float temperature;
            public ThinkingOption thinking;
            public int max_tokens;
            public ResponseFormat response_format;
        }

        [Serializable]
        private sealed class ThinkingOption
        {
            public string type;
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
