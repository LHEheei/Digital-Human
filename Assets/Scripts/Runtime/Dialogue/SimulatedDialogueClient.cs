#if UNITY_EDITOR
using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace SrtpAvatar
{
    public enum SimulatedDialogueScenario { Normal, Slow, Interrupted, AuthenticationFailure, Timeout, InvalidJson }

    // Editor-only transport fixture. The production SSE and decision parsers still run.
    public sealed class SimulatedDialogueClient : IDialogueClient
    {
        public bool IsAvailable => true;
        public SimulatedDialogueScenario Scenario { get; set; }
        public string Reply { get; set; } = "你好，很高兴见到你。我们可以从今天想聊的事情开始。";
        public string Emotion { get; set; } = "joy";
        public string Gesture { get; set; } = "explain";
        public string Memory { get; set; } = "";
        public int ChunkDelayMilliseconds { get; set; } = 25;
        public int SlowChunkDelayMilliseconds { get; set; } = 250;
        public string LastRecentContext { get; private set; }
        public string LastLongTermContext { get; private set; }

        public async Task<DialogueDecision> CompleteAsync(TalkInput input, string recentContext, string longTermContext,
            CancellationToken cancellationToken, Action<string> onReplySentence = null)
        {
            LastRecentContext = recentContext;
            LastLongTermContext = longTermContext;
            var scenario = Scenario;
            var delay = Math.Max(1, scenario == SimulatedDialogueScenario.Slow ? SlowChunkDelayMilliseconds : ChunkDelayMilliseconds);
            cancellationToken.ThrowIfCancellationRequested();
            await Task.Delay(delay, cancellationToken);
            if (scenario == SimulatedDialogueScenario.AuthenticationFailure)
                throw new AppException(AppErr.auth, "模拟认证失败（401）。");
            if (scenario == SimulatedDialogueScenario.Timeout)
            {
                await Task.Delay(delay * 4, cancellationToken);
                throw new AppException(AppErr.network, "模拟请求超时。");
            }

            var decision = new DialogueDecision { reply = Reply, emotion = Emotion, gesture = Gesture, carIntent = "none", memory = Memory };
            var json = scenario == SimulatedDialogueScenario.InvalidJson ? "{\"reply\":42}" : JsonUtility.ToJson(decision);
            var emitted = false;
            var decoder = new DialogueStreamDecoder(sentence => { emitted = true; onReplySentence?.Invoke(sentence); });
            for (var offset = 0; offset < json.Length; offset += 8)
            {
                await Task.Delay(delay, cancellationToken);
                var chunk = new Chunk { choices = new[] { new Choice { delta = new Delta { content = json.Substring(offset, Math.Min(8, json.Length - offset)) } } } };
                var bytes = Encoding.UTF8.GetBytes("data: " + JsonUtility.ToJson(chunk) + "\n\n");
                // Deliberately split UTF-8 characters across delivery boundaries.
                for (var i = 0; i < bytes.Length; i++) decoder.Append(new[] { bytes[i] }, 1);
                if (scenario == SimulatedDialogueScenario.Interrupted && emitted)
                    throw new AppException(AppErr.network, "模拟首句之后流连接中断。");
            }
            if (scenario != SimulatedDialogueScenario.Interrupted)
            {
                var done = Encoding.UTF8.GetBytes("data: [DONE]\n\n");
                decoder.Append(done, done.Length);
            }
            cancellationToken.ThrowIfCancellationRequested();
            return decoder.CompleteDecision();
        }

        [Serializable] private sealed class Chunk { public Choice[] choices; }
        [Serializable] private sealed class Choice { public Delta delta; }
        [Serializable] private sealed class Delta { public string content; }
    }
}
#endif
