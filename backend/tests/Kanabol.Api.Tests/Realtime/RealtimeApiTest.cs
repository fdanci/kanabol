using System.Text.Json;
using System.Threading.Channels;
using Kanabol.Api.Realtime;
using Kanabol.Api.Tests.Projects;
using Kanabol.Api.Tests.Support;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;

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

    /// <summary>A message tests send themselves, through the hub's groups.</summary>
    protected const string ProbeEvent = "Probe";

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

    /// <summary>
    /// A started connection, which hears the events named. StartAsync ends with the handshake, before the
    /// server's OnConnectedAsync, and the server takes calls only once that's done: one call, which joins
    /// nothing, makes sure the connection is in its user's group before the test goes on.
    /// </summary>
    protected async Task<Heard> Connect(string accessToken, params string[] events)
    {
        var connection = Hub(accessToken);
        var heard = new Heard(connection, events);
        await connection.StartAsync();
        await connection.InvokeAsync("LeaveProject", Guid.Empty);
        return heard;
    }

    protected Task SendToProject(Guid projectId, string marker) =>
        Api.Factory.Services.GetRequiredService<IHubContext<BoardHub>>()
            .Clients.Group(BoardGroups.Project(projectId)).SendAsync(ProbeEvent, new { marker });

    protected Task SendToUser(Guid userId, string marker) =>
        Api.Factory.Services.GetRequiredService<IHubContext<BoardHub>>()
            .Clients.Group(BoardGroups.User(userId)).SendAsync(ProbeEvent, new { marker });

    protected static async Task<string?> MarkerOf(Heard heard) =>
        (await heard.NextIs(ProbeEvent)).GetProperty("marker").GetString();

    protected static Task<string?> Join(Heard heard, Guid projectId) =>
        heard.Connection.InvokeAsync<string?>("JoinProject", projectId);

    /// <summary>Whether the server closes the connection before the test runs out of patience.</summary>
    protected static async Task<bool> ClosesSoon(Heard heard) =>
        await Task.WhenAny(heard.Closed, Task.Delay(Patience)) == heard.Closed;

    /// <summary>What one connection heard, in the order it came.</summary>
    protected sealed class Heard
    {
        private readonly Channel<(string Event, JsonElement Payload)> _messages = Channel.CreateUnbounded<(string, JsonElement)>();

        private readonly TaskCompletionSource _closed = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Heard(HubConnection connection, IEnumerable<string> events)
        {
            Connection = connection;
            connection.Closed += _ =>
            {
                _closed.TrySetResult();
                return Task.CompletedTask;
            };
            foreach (var name in events)
            {
                connection.On<JsonElement>(name, payload => _messages.Writer.TryWrite((name, payload)));
            }
        }

        public HubConnection Connection { get; }

        /// <summary>Completes when the connection closes, whoever closed it.</summary>
        public Task Closed => _closed.Task;

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
