using System.Threading.Channels;

namespace Kanabol.Api.Realtime;

/// <summary>
/// What the board hub has yet to send, in the order the changes made it (SPEC section 9): HubBoardNotifier
/// writes, BoardSender alone reads. Unbounded, so a request never waits here; the messages are small, and
/// the one consumer only falls behind while a socket takes its time, at most the hub's send timeout.
/// </summary>
public sealed class BoardOutbox
{
    private readonly Channel<BoardMessage> _channel = Channel.CreateUnbounded<BoardMessage>(
        new UnboundedChannelOptions { SingleReader = true, SingleWriter = false });

    public ChannelReader<BoardMessage> Reader => _channel.Reader;

    /// <summary>False once the queue is closed, when the API stops.</summary>
    public bool TryAdd(BoardMessage message) => _channel.Writer.TryWrite(message);

    /// <summary>Takes nothing more: what's in it still goes out.</summary>
    public void Close() => _channel.Writer.TryComplete();
}
