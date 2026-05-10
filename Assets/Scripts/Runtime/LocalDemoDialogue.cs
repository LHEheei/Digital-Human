namespace SrtpAvatar
{
    public static class LocalDemoDialogue
    {
        public static string UserText(OpenAICompatibleConfig config)
        {
            return config == null || string.IsNullOrWhiteSpace(config.localDemoUserText)
                ? "你好，请介绍一下你现在能做什么。"
                : config.localDemoUserText.Trim();
        }

        public static DialogueDecision Decide(string userText)
        {
            var normalized = userText == null ? "" : userText.Trim();
            if (normalized.Contains("挥手") || normalized.Contains("打招呼"))
            {
                return new DialogueDecision { reply = "你好，见到你很高兴。", emotion = "joy", gesture = "wave", state = "speaking", carIntent = "none" };
            }

            if (normalized.Contains("难过") || normalized.Contains("沮丧"))
            {
                return new DialogueDecision { reply = "我会放慢语气陪你一下，也可以帮你整理下一步任务。", emotion = "sorrow", gesture = "nod", state = "speaking", carIntent = "none" };
            }

            if (normalized.Contains("生气") || normalized.Contains("危险"))
            {
                return new DialogueDecision { reply = "我已经进入警觉状态，后续可以把这里映射成小车避障或停止指令。", emotion = "angry", gesture = "explain", state = "speaking", carIntent = "none" };
            }

            if (normalized.Contains("思考") || normalized.Contains("方案"))
            {
                return new DialogueDecision { reply = "我会先理解你的意图，再决定表情、动作和状态反馈。", emotion = "fun", gesture = "think", state = "speaking", carIntent = "none" };
            }

            return new DialogueDecision { reply = "已经准备好了，可以继续对话。", emotion = "joy", gesture = "explain", state = "speaking", carIntent = "none" };
        }
    }
}
