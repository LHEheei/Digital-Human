namespace SrtpAvatar
{
    public enum CarCommunicationMode
    {
        Disabled,
        LogOnly,
        HttpPost,
        AndroidBluetoothSpp
    }

    public enum CarIntent
    {
        none,
        forward,
        backward,
        left,
        right,
        stop,
        speedUp,
        slowDown
    }
}
