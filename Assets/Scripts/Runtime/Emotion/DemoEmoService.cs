using System.Threading;
using System.Threading.Tasks;

namespace SrtpAvatar
{
    public sealed class DemoEmoService : IEmoService
    {
        public Task<UserEmo> GetFaceAsync(byte[] image, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(image == null || image.Length == 0
                ? UserEmo.Failure("face", "没有可识别的图像数据。")
                : new UserEmo { label = "joy", score = 0.75f, source = "face" }.Normalize());
        }

        public Task<UserEmo> GetVoiceAsync(byte[] audio, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(audio == null || audio.Length == 0
                ? UserEmo.Failure("voice", "没有可识别的音频数据。")
                : new UserEmo { label = "neutral", score = 0.65f, source = "voice" }.Normalize());
        }
    }
}
