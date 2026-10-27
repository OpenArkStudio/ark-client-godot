namespace Ark.Client.Core;

/// <summary>Owner-thread preparation must not consume proposal/heartbeat state while the connection is busy.</summary>
public static class SendPreparation
{
    public static bool TryCreate(bool connected, bool sending, Func<byte[]> createFrame, out byte[]? frame)
    {
        frame = null;
        if (!connected || sending)
        {
            return false;
        }
        frame = createFrame();
        return true;
    }
}
