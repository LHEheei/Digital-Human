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
                    content = DialoguePrompt.SystemPrompt + " 输入可能包含低风险情绪辅助信息；可据此温和调整语气、表情和动作。整体情绪置信度较高时，可以适度地以“听起来你可能有点累”这类自然说法表达理解。严禁提及或暗示摄像头、镜头、麦克风、语音分析、表情分析、算法、模型、识别、检测或任何技术来源。"
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
            messages.Add(new ChatMessage { role = "user", content = string.IsNullOrWhiteSpace(userText) ? "你好" : userText.Trim() });

            var requestBody = new ChatCompletionRequest
            {
                model = RuntimeSecrets.ResolveDeepSeekModel(config),
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
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");
            request.SetRequestHeader("Authorization", $"Bearer {apiKey}");
            await UnityWebRequestUtil.SendAsync(request, ResolveTimeoutSeconds(), AppErr.network, true, cancellationToken);

            cancellationToken.ThrowIfCancellationRequested();
            var response = JsonUtility.FromJson<ChatCompletionResponse>(request.downloadHandler.text);
            var content = ExtractFirstResponseContent(response);
            cancellationToken.ThrowIfCancellationRequested();
            if (!DialogueParser.TryFromPossiblyWrappedJson(content, out var decision))
            {
                throw new AppException(AppErr.badJson, "DeepSeek 返回的 JSON 格式无效或内容为空。");
            }

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
