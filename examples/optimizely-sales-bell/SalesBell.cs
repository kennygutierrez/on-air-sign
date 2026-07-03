using System.Threading.Channels;

namespace SalesBellSample;

/// <summary>Rings the "sales bell" (a quick blink of the plug). Fire-and-forget, never throws.</summary>
public interface ISalesBell
{
    void Ring();
}

/// <summary>
/// Drops a token into a bounded channel. The actual (slow) plug blink happens on
/// <see cref="SalesBellWorker"/>, so this never blocks or fails the calling thread.
/// If the queue is full during a rush, extra rings are simply dropped.
/// </summary>
public sealed class SalesBell : ISalesBell
{
    private readonly ChannelWriter<byte> _writer;

    public SalesBell(Channel<byte> channel) => _writer = channel.Writer;

    public void Ring() => _writer.TryWrite(0);
}
