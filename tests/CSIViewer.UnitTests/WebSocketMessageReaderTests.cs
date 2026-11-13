using System.Net.WebSockets;
using CSIViewer.Networking;

namespace CSIViewer.UnitTests;

[TestClass]
public sealed class WebSocketMessageReaderTests
{
    [TestMethod]
    public async Task SmallMessage_IsReadWithSingleAllocation()
    {
        var socket = new FragmentingWebSocket();
        socket.QueueMessage("hello"u8.ToArray(), WebSocketMessageType.Text, fragmentSize: 64);
        var buffer = new byte[64];

        var (type, data) = await WebSocketMessageReader.ReadMessageAsync(
            socket,
            buffer,
            CancellationToken.None);

        Assert.AreEqual(WebSocketMessageType.Text, type);
        CollectionAssert.AreEqual("hello"u8.ToArray(), data);
    }

    [TestMethod]
    public async Task FragmentedOneMegabyteMessage_IsReassembled()
    {
        var socket = new FragmentingWebSocket();
        var payload = new byte[1024 * 1024];
        Random.Shared.NextBytes(payload);
        socket.QueueMessage(payload, WebSocketMessageType.Binary, fragmentSize: 16 * 1024);
        var buffer = new byte[16 * 1024];

        var (type, data) = await WebSocketMessageReader.ReadMessageAsync(
            socket,
            buffer,
            CancellationToken.None);

        Assert.AreEqual(WebSocketMessageType.Binary, type);
        CollectionAssert.AreEqual(payload, data);
    }

    [TestMethod]
    public async Task CloseMessage_ReturnsNullType()
    {
        var socket = new FragmentingWebSocket();
        socket.QueueClose();

        var (type, data) = await WebSocketMessageReader.ReadMessageAsync(
            socket,
            new byte[64],
            CancellationToken.None);

        Assert.IsNull(type);
        Assert.AreEqual(0, data.Length);
    }

    private sealed class FragmentingWebSocket : WebSocket
    {
        private readonly Queue<(byte[] Data, WebSocketMessageType Type, bool End)> _messages = new();
        private byte[]? _current;
        private int _offset;
        private WebSocketMessageType _type;
        private bool _close;

        public void QueueMessage(byte[] payload, WebSocketMessageType type, int fragmentSize)
        {
            for (var offset = 0; offset < payload.Length; offset += fragmentSize)
            {
                var count = Math.Min(fragmentSize, payload.Length - offset);
                var fragment = new byte[count];
                Buffer.BlockCopy(payload, offset, fragment, 0, count);
                _messages.Enqueue((fragment, type, offset + count >= payload.Length));
            }
        }

        public void QueueClose()
        {
            _close = true;
        }

        public override WebSocketCloseStatus? CloseStatus => null;
        public override string? CloseStatusDescription => null;
        public override WebSocketState State => WebSocketState.Open;
        public override string? SubProtocol => null;

        public override Task<WebSocketReceiveResult> ReceiveAsync(
            ArraySegment<byte> buffer,
            CancellationToken cancellationToken)
        {
            if (_close)
            {
                return Task.FromResult(new WebSocketReceiveResult(
                    0,
                    WebSocketMessageType.Close,
                    true,
                    WebSocketCloseStatus.NormalClosure,
                    "closed"));
            }

            if (_current is null || _offset >= _current.Length)
            {
                var next = _messages.Dequeue();
                _current = next.Data;
                _offset = 0;
                _type = next.Type;
                _endPending = next.End;
            }

            var count = Math.Min(buffer.Count, _current.Length - _offset);
            Buffer.BlockCopy(_current, _offset, buffer.Array!, buffer.Offset, count);
            _offset += count;
            var end = _offset >= _current.Length && _endPending;
            return Task.FromResult(new WebSocketReceiveResult(count, _type, end));
        }

        private bool _endPending;

        public override void Abort() => throw new NotSupportedException();
        public override Task CloseAsync(
            WebSocketCloseStatus closeStatus,
            string? statusDescription,
            CancellationToken cancellationToken) => Task.CompletedTask;
        public override Task CloseOutputAsync(
            WebSocketCloseStatus closeStatus,
            string? statusDescription,
            CancellationToken cancellationToken) => Task.CompletedTask;
        public override void Dispose()
        {
        }

        public override Task SendAsync(
            ArraySegment<byte> buffer,
            WebSocketMessageType messageType,
            bool endOfMessage,
            CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
