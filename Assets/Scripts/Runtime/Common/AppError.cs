using System;

namespace SrtpAvatar
{
    public enum AppErr
    {
        none,
        micPerm,
        speech,
        tts,
        network,
        auth,
        aiUnavailable,
        badJson,
        config,
        car,
        canceled,
        unknown
    }

    public sealed class AppException : Exception
    {
        public AppException(AppErr error, string message, Exception innerException = null)
            : base(message, innerException)
        {
            Error = error;
        }

        public AppErr Error { get; }
    }
}
