namespace CodeGame.Client;

using System.Net.WebSockets;
using System.Text.Json;
using Websocket.Client;

/// <summary>
/// Represents a debug severity.
/// </summary>
public enum DebugSeverity
{
#pragma warning disable 1591
    Error, Warning, Info, Trace
#pragma warning restore 1591
}

/// <summary>
/// Represents a debug connection to the server.
/// </summary>
public class DebugSocket : IDisposable
{
#pragma warning disable 1591
    public Api Api { get; private set; }
#pragma warning restore 1591

    private Dictionary<string, string> usernameCache = new Dictionary<string, string>();
    private WebsocketClient? wsClient;
    private ManualResetEvent exitEvent = new ManualResetEvent(false);

    private Dictionary<Guid, Func<DebugSeverity, string, string?, Task>> eventListeners = new Dictionary<Guid, Func<DebugSeverity, string, string?, Task>>();

    private bool trace = false, info = true, warning = true, error = true;

    /// <summary>
    /// Creates a new debug socket.
    /// </summary>
    /// <param name="url">The URL of the game server. The protocol should be omitted.</param>
    /// <returns>A new instance of DebugSocket.</returns>
    /// <exception cref="ArgumentException">Thrown when the url does not point to a valid CodeGame game server.</exception>
    public static async Task<DebugSocket> Create(string url)
    {
        try
        {
            var api = await Api.Create(url);
            await api.FetchInfo();
            return new DebugSocket(api);
        }
        catch (Exception e)
        {
            if (e is HttpRequestException || e is JsonException)
                throw new ArgumentException("The provided URL does not point to a valid CodeGame game server.", "url");
            throw;
        }
    }

    /// <summary>
    /// Enables/disables message severities.
    /// </summary>
    /// <param name="trace">Whether to receive trace messages.</param>
    /// <param name="info">Whether to receive info messages.</param>
    /// <param name="warning">Whether to receive warning messages.</param>
    /// <param name="error">Whether to receive error messages.</param>
    /// <exception cref="InvalidOperationException">Thrown when the socket is already connected.</exception>
    public void SetSeverities(bool trace, bool info, bool warning, bool error)
    {
        if (wsClient != null) throw new InvalidOperationException("Cannot call SetSeverities after a connection has already been established.");
        this.trace = trace;
        this.info = info;
        this.warning = warning;
        this.error = error;
    }

    /// <summary>
    /// Registers a callback that is called when a debug message is received.
    /// </summary>
    /// <param name="callback">The function to call.</param>
    /// <returns>The ID of the callback that can be used to remove it.</returns>
    public Guid OnMessage(Action<DebugSeverity, string, string?> callback)
    {
        return OnMessage(async (s, m, d) => await Task.Run(() => callback(s, m, d)));
    }

    /// <summary>
    /// Registers a callback that is called when a debug message is received.
    /// </summary>
    /// <param name="callback">The function to call.</param>
    /// <returns>The ID of the callback that can be used to remove it.</returns>
    public Guid OnMessage(Func<DebugSeverity, string, string?, Task> callback)
    {
        var id = Guid.NewGuid();
        eventListeners.Add(id, callback);
        return id;
    }

    /// <summary>
    /// Removes a callback.
    /// </summary>
    /// <param name="id">The ID of the callback.</param>
    public void RemoveCallback(Guid id)
    {
        eventListeners.Remove(id);
    }

    /// <summary>
    /// Connect to the server debug endpoint.
    /// </summary>
    public async void DebugServer()
    {
        wsClient = await Api.ConnectWebSocket($"/api/debug?trace={trace}&info={info}&warning={warning}&error={error}", OnMessageReceived);
        wsClient.DisconnectionHappened.Subscribe((info) =>
        {
            exitEvent.Set();
        });
    }

    /// <summary>
    /// Connect to the game debug endpoint.
    /// </summary>
    /// <param name="gameId">The ID of the game.</param>
    public async void DebugGame(string gameId)
    {
        wsClient = await Api.ConnectWebSocket($"/api/games/{gameId}/debug?trace={trace}&info={info}&warning={warning}&error={error}", OnMessageReceived);
        wsClient.DisconnectionHappened.Subscribe((info) =>
        {
            exitEvent.Set();
        });
    }

    /// <summary>
    /// Connect to player debug endpoint.
    /// </summary>
    /// <param name="gameId">The ID of the game.</param>
    /// <param name="playerId">The ID of the player.</param>
    /// <param name="playerSecret">The secret of the player.</param>
    public async void DebugPlayer(string gameId, string playerId, string playerSecret)
    {
        wsClient = await Api.ConnectWebSocket($"/api/games/{gameId}/players/{playerId}/debug?trace={trace}&info={info}&warning={warning}&error={error}", OnMessageReceived, playerSecret);
        wsClient.DisconnectionHappened.Subscribe((info) =>
        {
            exitEvent.Set();
        });
    }

    /// <summary>
    /// Wait until the connection is closed.
    /// </summary>
    public void Wait()
    {
        exitEvent.WaitOne();
    }

    /// <summary>
    /// Closes the underlying websocket connection.
    /// </summary>
    public void Dispose()
    {
        if (wsClient == null) return;
        wsClient.Stop(WebSocketCloseStatus.NormalClosure, "Connection closed.").Wait();
        wsClient.Dispose();
    }

    private DebugSocket(Api api)
    {
        this.Api = api;
    }

    private async Task OnMessageReceived(ResponseMessage msg)
    {
        if (msg.MessageType != WebSocketMessageType.Text) return;
        try
        {
            using var doc = JsonDocument.Parse(msg.Text);
            var root = doc.RootElement;
            var severityStr = root.GetProperty("severity").GetString();
            DebugSeverity severity;
            switch (severityStr)
            {
                case "trace": severity = DebugSeverity.Trace; break;
                case "info": severity = DebugSeverity.Info; break;
                case "warning": severity = DebugSeverity.Warning; break;
                case "error": severity = DebugSeverity.Error; break;
                default: throw new JsonException("Unknown severity.");
            }
            var message = root.GetProperty("message").GetString();
            if (message == null) throw new JsonException("Missing message property.");
            JsonElement dataElement;
            var data = root.TryGetProperty("data", out dataElement) ? dataElement.ToString() : null;
            foreach (var cb in eventListeners)
            {
                await cb.Value(severity, message, data);
            }
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
        }
    }
}
