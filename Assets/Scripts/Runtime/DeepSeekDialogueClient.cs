using System;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

namespace SrtpAvatar
{
    public sealed class DeepSeekDialogueClient
    {
        private const string SystemPrompt =
            "你是SRTP桌面级智能小车App中的3D数字人。请只输出一个JSON对象，不要Markdown。字段必须是reply、emotion、gesture、state、carIntent。emotion只能是neutral、joy、angry、sorrow、fun、surprised。gesture只能是none、nod、wave、think、explain。state只能是idle、listening、thinking、speaking。carIntent本阶段固定为none。reply用自然、简短、适合手机端语音播报的中文。不要在回复开头自称小助手、数字人、AI，也不要说“我是……”，除非用户明确要求自我介绍。";

        private readonly OpenAICompatibleConfig config;

        public DeepSeekDialogueClient(OpenAICompatibleConfig config)
        {
            this.config = config;
        }

        public async Task<DialogueDecision> CompleteAsync(string userText)
        {
            var requestBody = new ChatCompletionRequest
            {
                model = string.IsNullOrWhiteSpace(config.deepSeekModel) ? "deepseek-v4-flash" : config.deepSeekModel.Trim(),
                temperature = 0.7f,
                response_format = new ResponseFormat { type = "json_object" },
                messages = new[]
                {
                    new ChatMessage { role = "system", content = SystemPrompt },
                    new ChatMessage { role = "user", content = string.IsNullOrWhiteSpace(userText) ? "你好" : userText.Trim() }
                }
            };

            using var request = new UnityWebRequest(config.DeepSeekUrl("chat/completions"), UnityWebRequest.kHttpVerbPOST);
            request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(JsonUtility.ToJson(requestBody)));
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");
            request.SetRequestHeader("Authorization", $"Bearer {config.deepSeekApiKey.Trim()}");
            await UnityWebRequestUtil.SendAsync(request);

            var response = JsonUtility.FromJson<ChatCompletionResponse>(request.downloadHandler.text);
            var content = response?.choices != null && response.choices.Length > 0 ? response.choices[0].message.content : "";
            return DialogueDecision.FromPossiblyWrappedJson(content);
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
