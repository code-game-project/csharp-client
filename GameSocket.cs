namespace CodeGame.Client;

using System.Net.WebSockets;
using System.Reactive.Linq;
using System.Text.Json;
using Websocket.Client;

/// <summary>
/// Represents a connection to a game server.
/// </summary>
public class GameSocket : IDisposable
{
    private static readonly string CGVersion = "0.7";
#pragma warning disable 1591
    public Api Api { get; private set; }
#pragma warning restore 1591
    /// <summary>
    /// The current session.
    /// </summary>
    public Session Session { get; private set; }

    private Dictionary<string, string> usernameCache = new Dictionary<string, string>();
    private WebsocketClient? wsClient;
    private ManualResetEvent exitEvent = new ManualResetEvent(false);
    private Dictionary<string, IEventCallbacks> eventListeners = new Dictionary<string, IEventCallbacks>();

    /// <summary>
    /// Creates a new game socket.
    /// </summary>
    /// <param name="url">The URL of the game server. The protocol should be omitted.</param>
    /// <returns>A new instance of GameSocket.</returns>
    /// <exception cref="ArgumentException">Thrown when the url does not point to a valid CodeGame game server.</exception>
    public static async Task<GameSocket> Create(string url)
    {
        try
        {
            var api = await Api.Create(url);
            var info = await api.FetchInfo();
            if (!IsVersionCompatible(info.CGVersion))
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine($"WARNING: CodeGame version mismatch. Server: v{info.CGVersion}, client: v{CGVersion}");
                Console.ResetColor();
            }

            return new GameSocket(api);
        }
        catch (Exception e)
        {
            if (e is HttpRequestException || e is JsonException)
                throw new ArgumentException("The provided URL does not point to a valid CodeGame game server.", "url");
            throw;
        }
    }

    /// <summary>
    /// Creates a new game on the server.
    /// </summary>
    /// <param name="makePublic">Whether to make the created game public.</param>
    /// <param name="config">The game config.</param>
    /// <returns>The ID of the created game.</returns>
    /// <exception cref="CodeGameException">Thrown when the server refuses to create a new game.</exception>
    /// <exception cref="HttpRequestException">Thrown when the http request fails.</exception>
    /// <exception cref="JsonException">Thrown when the server response is invalid.</exception>
    public async Task<string> CreateGame(bool makePublic, object? config = null)
    {
        return (await Api.CreateGame(makePublic, false, config)).gameId;
    }

    /// <summary>
    /// Creates a new protected game on the server.
    /// </summary>
    /// <param name="makePublic">Whether to make the created game public.</param>
    /// <param name="config">The game config.</param>
    /// <returns>A named tuple of the game ID and the join secret.</returns>
    /// <exception cref="CodeGameException">Thrown when the server refuses to create a new game.</exception>
    /// <exception cref="HttpRequestException">Thrown when the http request fails.</exception>
    /// <exception cref="JsonException">Thrown when server response is invalid.</exception>
    public async Task<(string gameId, string joinSecret)> CreateProtectedGame(bool makePublic, object? config = null)
    {
        return await Api.CreateGame(makePublic, true, config);
    }

    /// <summary>
    /// Creates a new player in the game and connects to it.
    /// </summary>
    /// <param name="gameId">The ID of the game.</param>
    /// <param name="username">The desired username.</param>
    /// <param name="joinSecret">The join secret of the game. (only needed when the game is protected)</param>
    /// <exception cref="CodeGameException">Thrown when the server refuses to create a new player in the game.</exception>
    /// <exception cref="WebSocketException">Thrown when the websocket connection could not be established.</exception>
    /// <exception cref="InvalidOperationException">Thrown when the socket is already connected to a game.</exception>
    /// <exception cref="HttpRequestException">Thrown when the http request fails.</exception>
    /// <exception cref="JsonException">Thrown when the server response is invalid.</exception>
    public async Task Join(string gameId, string username, string joinSecret = "")
    {
        if (Session.GameURL != "") throw new InvalidOperationException("This socket is already connected to a game.");
        var (playerId, playerSecret) = await Api.CreatePlayer(gameId, username, joinSecret);
        await Connect(gameId, playerId, playerSecret);
    }

    /// <summary>
    /// Loads the session from disk and reconnects to the game.
    /// </summary>
    /// <param name="username">The username of the session.</param>
    /// <exception cref="InvalidOperationException">Thrown when the socket is already connected to a game.</exception>
    /// <exception cref="WebSocketException">Thrown when the websocket connection could not be established.</exception>
    /// <exception cref="IOException">Thrown when the session does not exist.</exception>
    /// <exception cref="JsonException">Thrown when the session file is corrupted.</exception>
    public async Task RestoreSession(string username)
    {
        if (Session.GameURL != "") throw new InvalidOperationException("This socket is already connected to a game.");
        var session = Session.Load(Api.URL, username);
        try
        {
            await Connect(session.GameId, session.PlayerId, session.PlayerSecret);
        }
        catch
        {
            session.Remove();
            throw;
        }
    }

    /// <summary>
    /// Connects to a player on the server.
    /// </summary>
    /// <param name="gameId">The ID of the game.</param>
    /// <param name="playerId">The ID of the player.</param>
    /// <param name="playerSecret">The secret of the player.</param>
    /// <exception cref="InvalidOperationException">Thrown when the socket is already connected to a game.</exception>
    /// <exception cref="WebSocketException">Thrown when the websocket connection could not be established.</exception>
    public async Task Connect(string gameId, string playerId, string playerSecret)
    {
        if (Session.GameURL != "") throw new InvalidOperationException("This socket is already connected to a game.");

        wsClient = await Api.ConnectWebSocket($"/api/games/{gameId}/connect?player_id={playerId}&player_secret={playerSecret}", OnMessageReceived);
        wsClient.DisconnectionHappened.Subscribe((info) =>
        {
            exitEvent.Set();
        });

        Session = new Session(Api.URL, "", gameId, playerId, playerSecret);

        usernameCache = await Api.FetchPlayers(gameId);
        Session.Username = usernameCache[playerId];
        try
        {
            Session.Save();
        }
        catch (Exception e)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("ERROR: Failed to save session: " + e.Message);
            Console.ResetColor();
        }
    }

    /// <summary>
    /// Connects to the game as a spectator.
    /// </summary>
    /// <param name="gameId">The ID of the game.</param>
    /// <exception cref="InvalidOperationException">Thrown when the socket is already connected to a game.</exception>
    /// <exception cref="WebSocketException">Thrown when the websocket connection could not be established.</exception>
    public async Task Spectate(string gameId)
    {
        if (Session.GameURL != "") throw new InvalidOperationException("This socket is already connected to a game.");

        wsClient = await Api.ConnectWebSocket($"/api/games/{gameId}/spectate", OnMessageReceived);
        wsClient.DisconnectionHappened.Subscribe((info) =>
        {
            exitEvent.Set();
        });

        Session = new Session(Api.URL, "", gameId, "", "");

        usernameCache = await Api.FetchPlayers(gameId);
    }

    /// <summary>
    /// Blocks until the connection is closed.
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

    /// <summary>
    /// Registers a callback that is triggered every time the event is received.
    /// </summary>
    /// <typeparam name="T">The type of the event data.</typeparam>
    /// <param name="eventName">The name of the event.</param>
    /// <param name="callback">The callback function.</param>
    /// <param name="once">Only trigger the the callback the first time the event is received.</param>
    /// <returns>An ID that can be used to remove the callback.</returns>
    public Guid On<T>(string eventName, Action<T> callback, bool once = false) where T : EventData
    {
        if (!eventListeners.ContainsKey(eventName)) eventListeners.Add(eventName, new EventCallbacks<T>());
        var callbacks = (EventCallbacks<T>)eventListeners[eventName];
        return callbacks.AddCallback(callback, once);
    }

    /// <summary>
    /// Registers a callback that is triggered every time the event is received.
    /// </summary>
    /// <typeparam name="T">The type of the event data.</typeparam>
    /// <param name="eventName">The name of the event.</param>
    /// <param name="callback">The callback function.</param>
    /// <param name="once">Only trigger the the callback the first time the event is received.</param>
    /// <returns>An ID that can be used to remove the callback.</returns>
    public Guid On<T>(string eventName, Func<T, Task> callback, bool once = false) where T : EventData
    {
        if (!eventListeners.ContainsKey(eventName)) eventListeners.Add(eventName, new EventCallbacks<T>());
        var callbacks = (EventCallbacks<T>)eventListeners[eventName];
        return callbacks.AddCallback(callback, once);
    }

    /// <summary>
    /// Removes an event callback.
    /// </summary>
    /// <param name="eventName">The name of the event.</param>
    /// <param name="id">The ID of the callback.</param>
    public void RemoveCallback(string eventName, Guid id)
    {
        if (!eventListeners.ContainsKey(eventName)) return;
        eventListeners[eventName].RemoveCallback(id);
    }

    /// <summary>
    /// Sends the command to the server.
    /// </summary>
    /// <typeparam name="T">The type of the event data.</typeparam>
    /// <param name="commandName">The name of the command.</param>
    /// <param name="data">The command data.</param>
    /// <exception cref="InvalidOperationException">Thrown when socket is not connected to a player.</exception>
    /// <exception cref="JsonException">Thrown when the command could not be serialized.</exception>
    public void Send<T>(string commandName, T data) where T : CommandData
    {
        if (wsClient == null || Session.PlayerId == "") throw new InvalidOperationException("The socket is not connected to a player.");
        Command<T> e = new Command<T>(commandName, data);
        var json = JsonSerializer.Serialize<Command<T>>(e, Api.JsonOptions);
        if (json == null) throw new JsonException("Failed to serialize command.");
        wsClient?.Send(json);
    }

    /// <summary>
    /// Retrieves the username of the player from the player cache and fetches it from the server if it is not already in there.
    /// </summary>
    /// <param name="playerId">The ID of the player.</param>
    /// <returns>The username of the player.</returns>
    /// <exception cref="CodeGameException">Thrown when the player does not exist in the game.</exception>
    /// <exception cref="HttpRequestException">Thrown when the http request failed.</exception>
    /// <exception cref="JsonException">Thrown when the server response could not be deserialized.</exception>
    public async Task<string> Username(string playerId)
    {
        string? username;
        if (usernameCache.TryGetValue(playerId, out username)) return username;
        username = await Api.FetchUsername(Session.GameId, playerId);
        usernameCache.Add(playerId, username);
        return username;
    }

    private async Task TriggerEventListeners(string eventName, string eventJson)
    {
        if (!eventListeners.ContainsKey(eventName)) return;
        await eventListeners[eventName].Call(eventJson);
    }

    private struct EventNameObj
    {
        public string Name { get; set; }
    }
    private async Task OnMessageReceived(ResponseMessage msg)
    {
        if (msg.MessageType != WebSocketMessageType.Text) return;
        try
        {
            var e = JsonSerializer.Deserialize<EventNameObj>(msg.Text, Api.JsonOptions);
            await TriggerEventListeners(e.Name, msg.Text); ;
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
            if (e is not JsonException) Environment.Exit(134);
        }
    }

    private GameSocket(Api api)
    {
        this.Api = api;
        this.Session = new Session("", "", "", "", "");
    }

    private static bool IsVersionCompatible(string serverVersion)
    {
        var serverParts = serverVersion.Split('.');
        if (serverParts.Length == 1) serverParts.Append("0");
        var clientParts = CGVersion.Split('.');
        if (clientParts.Length == 1) clientParts.Append("0");

        if (serverParts[0] != clientParts[0]) return false;

        if (clientParts[0] == "0") return serverParts[1] == clientParts[1];

        try
        {
            var serverMinor = int.Parse(serverParts[1]);
            var clientMinor = int.Parse(clientParts[1]);
            return clientMinor <= serverMinor;
        }
        catch
        {
            return false;
        }
    }
}
