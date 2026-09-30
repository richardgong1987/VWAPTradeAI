using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using cAlgo.Robots;
using Xunit;

namespace VWAPTradeAI.Tests.Notifications {
    // The cBot's link to the relay, against a real WebSocket server on a free local port.
    public class TradeNotificationClientTests {
        private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);
        private static readonly TimeSpan FastRetry = TimeSpan.FromMilliseconds(50);

        private readonly BlockingCollection<string> _received = new();
        private readonly ConcurrentQueue<string> _logs = new();

        private TradeNotificationClient Link(Uri url, string accessKey = "") =>
            new(new RelayEndpointModel(url, accessKey), _received.Add, _logs.Enqueue, FastRetry);

        [Fact]
        public async Task text_goes_both_ways() {
            using var relay = new LocalRelay(FreePort());
            using TradeNotificationClient link = Link(relay.Url);
            WebSocket server = await relay.AcceptAsync().WaitAsync(Timeout);

            link.Send("from the cBot");
            Assert.Equal("from the cBot", await ReceiveTextAsync(server).WaitAsync(Timeout));

            await SendTextAsync(server, "from the app");
            Assert.True(_received.TryTake(out string text, Timeout));
            Assert.Equal("from the app", text);
        }

        [Fact]
        public async Task a_message_sent_while_the_relay_is_down_goes_out_once_it_is_up() {
            int port = FreePort();
            using TradeNotificationClient link = Link(LocalRelay.UrlFor(port));

            link.Send("queued");
            await Task.Delay(200);
            using var relay = new LocalRelay(port);
            WebSocket server = await relay.AcceptAsync().WaitAsync(Timeout);

            Assert.Equal("queued", await ReceiveTextAsync(server).WaitAsync(Timeout));
        }

        [Fact]
        public async Task an_unreachable_relay_is_reported_once_not_on_every_retry() {
            using TradeNotificationClient link = Link(LocalRelay.UrlFor(FreePort()));

            await Task.Delay(600);

            Assert.Single(_logs, line => line.Contains("unavailable"));
        }

        [Fact]
        public async Task after_the_relay_drops_the_connection_the_link_reconnects() {
            using var relay = new LocalRelay(FreePort());
            using TradeNotificationClient link = Link(relay.Url);
            WebSocket first = await relay.AcceptAsync().WaitAsync(Timeout);

            await first.CloseAsync(WebSocketCloseStatus.EndpointUnavailable, "relay restarting", CancellationToken.None)
                .WaitAsync(Timeout);
            WebSocket second = await relay.AcceptAsync().WaitAsync(Timeout);
            link.Send("after reconnecting");

            Assert.Equal("after reconnecting", await ReceiveTextAsync(second).WaitAsync(Timeout));
            Assert.Contains(_logs, line => line.Contains("connection lost"));
        }

        [Fact]
        public async Task a_failing_message_handler_does_not_break_the_link() {
            using var relay = new LocalRelay(FreePort());
            int calls = 0;
            using var link = new TradeNotificationClient(new RelayEndpointModel(relay.Url, ""), text => {
                if (Interlocked.Increment(ref calls) == 1)
                    throw new InvalidOperationException("handler bug");
                _received.Add(text);
            }, _logs.Enqueue, FastRetry);
            WebSocket server = await relay.AcceptAsync().WaitAsync(Timeout);

            await SendTextAsync(server, "first");
            await SendTextAsync(server, "second");

            Assert.True(_received.TryTake(out string text, Timeout));
            Assert.Equal("second", text);
            Assert.Contains(_logs, line => line.Contains("handler bug"));
        }

        [Fact]
        public async Task the_access_key_is_sent_as_a_bearer_token() {
            using var relay = new LocalRelay(FreePort());
            using TradeNotificationClient link = Link(relay.Url, "myaccesscode");

            await relay.AcceptAsync().WaitAsync(Timeout);

            Assert.Equal("Bearer myaccesscode", relay.LastAuthorization);
        }

        [Fact]
        public async Task without_an_access_key_no_authorization_header_is_sent() {
            using var relay = new LocalRelay(FreePort());
            using TradeNotificationClient link = Link(relay.Url);

            await relay.AcceptAsync().WaitAsync(Timeout);

            Assert.Null(relay.LastAuthorization);
        }

        private static int FreePort() {
            var probe = new TcpListener(IPAddress.Loopback, 0);
            probe.Start();
            int port = ((IPEndPoint)probe.LocalEndpoint).Port;
            probe.Stop();
            return port;
        }

        private static Task SendTextAsync(WebSocket socket, string text) =>
            socket.SendAsync(Encoding.UTF8.GetBytes(text), WebSocketMessageType.Text, true, CancellationToken.None);

        private static async Task<string> ReceiveTextAsync(WebSocket socket) {
            var buffer = new byte[4096];
            WebSocketReceiveResult result = await socket.ReceiveAsync(buffer, CancellationToken.None);
            return Encoding.UTF8.GetString(buffer, 0, result.Count);
        }

        // A bare WebSocket server: accepts connections, nothing more.
        private sealed class LocalRelay : IDisposable {
            private readonly HttpListener _listener = new();

            public LocalRelay(int port) {
                Url = UrlFor(port);
                _listener.Prefixes.Add($"http://127.0.0.1:{port}/");
                _listener.Start();
            }

            public Uri Url { get; }

            public static Uri UrlFor(int port) => new($"ws://127.0.0.1:{port}/ws");

            // The Authorization header of the last connection accepted.
            public string LastAuthorization { get; private set; }

            public async Task<WebSocket> AcceptAsync() {
                HttpListenerContext context = await _listener.GetContextAsync();
                LastAuthorization = context.Request.Headers["Authorization"];
                HttpListenerWebSocketContext webSocket = await context.AcceptWebSocketAsync(subProtocol: null);
                return webSocket.WebSocket;
            }

            public void Dispose() {
                _listener.Close();
            }
        }
    }
}
