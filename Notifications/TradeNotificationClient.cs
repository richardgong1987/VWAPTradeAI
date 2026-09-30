using System;
using System.IO;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace cAlgo.Robots;

// The cBot's link to the WebSocket relay (rustwebsocket). Networking is auxiliary: nothing here
// decides anything about trading, throws into the strategy, or blocks the cBot thread.
//
// Built on .NET's ClientWebSocket, not cAlgo.API.WebSocketClient: that one can only be created on
// the cBot thread and connects synchronously, so every reconnect would stall the bars. Here
// everything runs on the link's own tasks: connect, reconnect every few seconds while the relay is unreachable, write
// queued messages, read incoming ones. Send only queues. Received text is handed to onText on the
// link's thread: switch to the cBot thread (BeginInvokeOnMainThread) before touching the strategy.
//
// Pattern: Adapter — wraps ClientWebSocket's connect/receive/send loop behind Send(text) and an
// onText callback. Its outbox is a bounded Producer–Consumer queue (the cBot thread produces, the
// link's writer consumes), which is why Send never blocks.
//
// No cAlgo dependency, so it is tested against a real local WebSocket server.
public sealed class TradeNotificationClient : IDisposable {
    private static readonly TimeSpan DefaultReconnectDelay = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan CloseTimeout = TimeSpan.FromSeconds(2);

    // While the relay is down messages wait here; beyond this many, new ones are dropped.
    private const int OutboxCapacity = 100;

    private readonly RelayEndpointModel _endpoint;
    private readonly Action<string> _onText;
    private readonly Action<string> _log;
    private readonly TimeSpan _reconnectDelay;
    private readonly Channel<string> _outbox = Channel.CreateBounded<string>(OutboxCapacity);
    private readonly CancellationTokenSource _stop = new();
    private readonly Task _running;

    public TradeNotificationClient(RelayEndpointModel endpoint, Action<string> onText, Action<string> log) : this(endpoint, onText, log,
        DefaultReconnectDelay) { }

    // The delay is only shortened by tests.
    public TradeNotificationClient(RelayEndpointModel endpoint, Action<string> onText, Action<string> log, TimeSpan reconnectDelay) {
        _endpoint = endpoint;
        _onText = onText;
        _log = log;
        _reconnectDelay = reconnectDelay;
        _running = Task.Run(RunAsync);
    }

    // Never blocks: the message is written once the relay is reachable.
    public void Send(string text) {
        if (!_stop.IsCancellationRequested && !_outbox.Writer.TryWrite(text))
            _log($"Relay outbox full; message dropped: {text}");
    }

    public void Dispose() {
        _stop.Cancel();

        try {
            // Every await watches _stop, so this ends quickly; the timeout is only a safety net.
            _running.Wait(TimeSpan.FromSeconds(3));
        } catch (AggregateException) {
            // The link is going away; how its last attempt ended no longer matters.
        }
    }

    private async Task RunAsync() {
        bool hasReportedUnavailable = false;

        while (!_stop.IsCancellationRequested) {
            using var socket = new ClientWebSocket();

            try {
                await ConnectAsync(socket);
            } catch (Exception) when (_stop.IsCancellationRequested) {
                return;
            } catch (Exception exception) {
                // Once per outage, not on every retry.
                if (!hasReportedUnavailable)
                    _log($"Relay {_endpoint.Url} unavailable ({exception.Message}); retrying every {_reconnectDelay.TotalSeconds:0.#} s");

                hasReportedUnavailable = true;
                await PauseAsync(_reconnectDelay);
                continue;
            }

            hasReportedUnavailable = false;
            _log($"Connected to relay {_endpoint.Url}");

            string ending = await ServeAsync(socket);

            if (!_stop.IsCancellationRequested) {
                _log($"Relay connection lost ({ending}); reconnecting");
                await PauseAsync(_reconnectDelay);
            }
        }
    }

    private async Task ConnectAsync(ClientWebSocket socket) {
        // In a header rather than the URL, so it never shows up in the proxy's logs. A relay
        // refusing it answers 401, which is reported like any other failed connect.
        if (_endpoint.AccessKey != "")
            socket.Options.SetRequestHeader("Authorization", $"Bearer {_endpoint.AccessKey}");

        using var connectTimeout = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token);
        connectTimeout.CancelAfter(ConnectTimeout);

        try {
            await socket.ConnectAsync(_endpoint.Url, connectTimeout.Token);
        } catch (OperationCanceledException) when (!_stop.IsCancellationRequested) {
            throw new TimeoutException($"no answer within {ConnectTimeout.TotalSeconds:0} s");
        }
    }

    // Reads and writes at the same time until either side stops; returns why the connection ended.
    private async Task<string> ServeAsync(ClientWebSocket socket) {
        using var connection = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token);
        Task reading = ReadAsync(socket, connection.Token);
        Task writing = WriteAsync(socket, connection.Token);

        Task first = await Task.WhenAny(reading, writing);
        connection.Cancel();
        string ending = first.Exception?.GetBaseException().Message ?? "closed by the relay";

        try {
            await Task.WhenAll(reading, writing);
        } catch (Exception) {
            // The other side was cancelled or failed along with the connection; `ending` says why.
        }

        await CloseAsync(socket);
        return ending;
    }

    private async Task ReadAsync(ClientWebSocket socket, CancellationToken token) {
        var buffer = new byte[8192];
        using var message = new MemoryStream();

        while (true) {
            WebSocketReceiveResult frame = await socket.ReceiveAsync(buffer, token);

            if (frame.MessageType == WebSocketMessageType.Close)
                return;

            message.Write(buffer, 0, frame.Count);

            if (!frame.EndOfMessage)
                continue;

            if (frame.MessageType == WebSocketMessageType.Text)
                Deliver(Encoding.UTF8.GetString(message.GetBuffer(), 0, (int)message.Length));

            message.SetLength(0);
        }
    }

    private async Task WriteAsync(ClientWebSocket socket, CancellationToken token) {
        while (true) {
            string text = await _outbox.Reader.ReadAsync(token);

            try {
                await socket.SendAsync(Encoding.UTF8.GetBytes(text), WebSocketMessageType.Text, true, token);
            } catch (Exception exception) when (exception is not OperationCanceledException) {
                _log($"Relay message not sent ({exception.Message}): {text}");
                throw;
            }
        }
    }

    private void Deliver(string text) {
        try {
            _onText(text);
        } catch (Exception exception) {
            _log($"Relay message handling failed ({exception.Message}): {text}");
        }
    }

    private static async Task CloseAsync(ClientWebSocket socket) {
        if (socket.State != WebSocketState.Open && socket.State != WebSocketState.CloseReceived)
            return;

        using var closeTimeout = new CancellationTokenSource(CloseTimeout);

        try {
            await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "cBot closing the connection", closeTimeout.Token);
        } catch (Exception) {
            // Already gone; nothing left to close politely.
        }
    }

    private async Task PauseAsync(TimeSpan delay) {
        try {
            await Task.Delay(delay, _stop.Token);
        } catch (OperationCanceledException) {
            // Stopping: the loop condition ends the link.
        }
    }
}
