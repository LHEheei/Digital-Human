namespace SrtpAvatar
{
    public static class LocalDemoDialogue
    {
        public static string UserText(AppConfig config)
        {
            return config == null || string.IsNullOrWhiteSpace(config.localDemoUserText)
                ? "你好，请介绍一下你现在能做什么。"
                : config.localDemoUserText.Trim();
        }

        public static DialogueDecision Decide(string userText)
        {
            var normalized = userText == null ? "" : userText.Trim();
            if (normalized.Contains("前进") || normalized.Contains("向前"))
            {
                return new DialogueDecision { reply = "收到，小车准备前进。", emotion = "joy", gesture = "explain", carIntent = "forward" };
            }

            if (normalized.Contains("后退") || normalized.Contains("倒车"))
            {
                return new DialogueDecision { reply = "收到，小车准备后退。", emotion = "neutral", gesture = "explain", carIntent = "backward" };
            }

            if (normalized.Contains("左转") || normalized.Contains("向左"))
            {
                return new DialogueDecision { reply = "收到，小车准备左转。", emotion = "neutral", gesture = "explain", carIntent = "left" };
            }

            if (normalized.Contains("右转") || normalized.Contains("向右"))
            {
                return new DialogueDecision { reply = "收到，小车准备右转。", emotion = "neutral", gesture = "explain", carIntent = "right" };
            }

            if (normalized.Contains("停止") || normalized.Contains("停车") || normalized.Contains("刹车"))
            {
                return new DialogueDecision { reply = "收到，小车停止。", emotion = "surprised", gesture = "nod", carIntent = "stop" };
            }

            if (normalized.Contains("加速"))
            {
                return new DialogueDecision { reply = "收到，小车速度上调。", emotion = "joy", gesture = "nod", carIntent = "speedUp" };
            }

            if (normalized.Contains("减速"))
            {
                return new DialogueDecision { reply = "收到，小车速度下调。", emotion = "neutral", gesture = "nod", carIntent = "slowDown" };
            }

            if (normalized.Contains("挥手") || normalized.Contains("打招呼"))
            {
                return new DialogueDecision { reply = "你好，见到你很高兴。", emotion = "joy", gesture = "wave", carIntent = "none" };
            }

            if (normalized.Contains("难过") || normalized.Contains("沮丧"))
            {
                return new DialogueDecision { reply = "我会放慢语气陪你一下，也可以帮你整理下一步任务。", emotion = "sorrow", gesture = "nod", carIntent = "none" };
            }

            if (normalized.Contains("生气") || normalized.Contains("危险"))
            {
                return new DialogueDecision { reply = "我已经进入警觉状态，后续可以把这里映射成小车避障或停止指令。", emotion = "angry", gesture = "explain", carIntent = "none" };
            }

            if (normalized.Contains("思考") || normalized.Contains("方案"))
            {
                return new DialogueDecision { reply = "我会先理解你的意图，再决定表情和动作反馈。", emotion = "fun", gesture = "think", carIntent = "none" };
            }

            return new DialogueDecision { reply = "已经准备好了，可以继续对话。", emotion = "joy", gesture = "explain", carIntent = "none" };
        }
    }
}
