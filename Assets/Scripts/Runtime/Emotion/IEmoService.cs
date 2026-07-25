using System.Threading;
using System.Threading.Tasks;

namespace SrtpAvatar
{
    public interface IEmoService
    {
        Task<UserEmo> GetFaceAsync(byte[] image, CancellationToken cancellationToken);
        Task<UserEmo> GetVoiceAsync(byte[] audio, CancellationToken cancellationToken);
    }
}
