using System.Text.Json;
using System.Threading.Channels;
using Kanabol.Api.Tests.Projects;
using Kanabol.Api.Tests.Support;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.Client;

namespace Kanabol.Api.Tests.Realtime;

/// <summary>
/// Connects to the board hub as the browser does (SPEC sections 4.5 and 9): a negotiation, then a WebSocket,
/// both with the access token only in the query string, since a browser can't put a header on a WebSocket.
/// </summary>
public abstract class RealtimeApiTest(ApiFixture api) : ProjectApiTest(api)
{
    protected const string BoardHub = "/hubs/board";

    /// <summary>How long a test waits for a message, or for a connection to close.</summary>
    protected static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);

    private readonly List<HubConnection> _connections = [];

    public override async Task DisposeAsync()
    {
        foreach (var connection in _connections)
        {
            await connection.DisposeAsync();
        }

        await base.DisposeAsync();
    }

    /// <summary>A connection not started yet, with the token in its address, or none.</summary>
    protected HubConnection Hub(string? accessToken)
    {
        var server = Api.Factory.Server;
        var query = accessToken is null ? string.Empty : $"?access_token={Uri.EscapeDataString(accessToken)}";
        var connection = new HubConnectionBuilder()
            .WithUrl(new Uri(server.BaseAddress, $"{BoardHub.TrimStart('/')}{query}"), options =>
            {
                options.Transports = HttpTransportType.WebSockets;
                options.HttpMessageHandlerFactory = _ => server.CreateHandler();
                options.WebSocketFactory = async (context, ct) =>
                    await server.CreateWebSocketClient().ConnectAsync(context.Uri, ct);
            })
            .Build();
        _connections.Add(connection);
        return connection;
    }

    /// <summary>A started connection, which hears the events named.</summary>
    protected async Task<Heard> Connect(string accessToken, params string[] events)
    {
        var connection = Hub(accessToken);
        var heard = new Heard(connection, events);
        await connection.StartAsync();
        return heard;
    }

    /// <summary>What one connection heard, in the order it came.</summary>
    protected sealed class Heard
    {
        private readonly Channel<(string Event, JsonElement Payload)> _messages = Channel.CreateUnbounded<(string, JsonElement)>();

        public Heard(HubConnection connection, IEnumerable<string> events)
        {
            Connection = connection;
            foreach (var name in events)
            {
                connection.On<JsonElement>(name, payload => _messages.Writer.TryWrite((name, payload)));
            }
        }

        public HubConnection Connection { get; }

        /// <summary>The next message, whatever it is.</summary>
        public async Task<(string Event, JsonElement Payload)> Next()
        {
            using var timeout = new CancellationTokenSource(Patience);
            return await _messages.Reader.ReadAsync(timeout.Token);
        }

        /// <summary>Nothing came before the message that ends it, which was sent after whatever shouldn't arrive.</summary>
        public async Task<JsonElement> NextIs(string eventName)
        {
            var (name, payload) = await Next();
            name.Should().Be(eventName);
            return payload;
        }
    }
}
