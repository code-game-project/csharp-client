namespace CodeGame;

using System.Net.WebSockets;
using System.Reactive.Linq;
using System.Text.Json;
using Websocket.Client;

public class GameSocket : IDisposable
{
    private static readonly string CGVersion = "0.7";
    public Api Api { get; private set; }
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
    /// <exception cref="Exception">Thrown when the url does not point to a valid CodeGame game server.</exception>
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
            {
                throw new Exception("The provided URL does not point to a valid CodeGame game server.");
            }
            throw;
        }
    }

    /// <summary>
    /// Creates a new game on the server.
    /// </summary>
    /// <param name="makePublic">Whether to make the created game public.</param>
    /// <param name="config">The game config.</param>
    /// <returns>The ID of the created game.</returns>
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
    /// <param name="joinSecret">The secret for joining the game.</param>
    /// <param name="config">The game config.</param>
    /// <returns>A named tuple of the game ID and the join secret.</returns>
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
    /// <exception cref="Exception">Thrown when the socket is already connected to a game.</exception>
    /// <exception cref="HttpRequestException">Thrown when the http request fails.</exception>
    /// <exception cref="JsonException">Thrown when the server response is invalid.</exception>
    public async Task Join(string gameId, string username, string joinSecret = "")
    {
        if (Session.GameURL != "") throw new Exception("This socket is already connected to a game.");
        var (playerId, playerSecret) = await Api.CreatePlayer(gameId, username, joinSecret);
        await Connect(gameId, playerId, playerSecret);
    }

    /// <summary>
    /// Loads the session from disk and reconnects to the game.
    /// </summary>
    /// <param name="username">The username of the session.</param>
    /// <exception cref="Exception">Thrown when the socket is already connected to a game.</exception>
    public async Task RestoreSession(string username)
    {
        if (Session.GameURL != "") throw new Exception("This socket is already connected to a game.");
        var session = Session.Load(Api.URL, username);
        try
        {
            await Connect(session.GameId, session.PlayerId, session.PlayerSecret);
        }
        catch
        {
            session.Remove();
        }
    }

    /// <summary>
    /// Connects to a player on the server.
    /// </summary>
    /// <param name="gameId">The ID of the game.</param>
    /// <param name="playerId">The ID of the player.</param>
    /// <param name="playerSecret">The secret of the player.</param>
    /// <exception cref="Exception">Thrown when the socket is already connected to a game.</exception>
    public async Task Connect(string gameId, string playerId, string playerSecret)
    {
        if (Session.GameURL != "") throw new Exception("This socket is already connected to a game.");

        wsClient = await Api.Connect(gameId, playerId, playerSecret, OnMessageReceived);
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
    /// Blocks until the connection is closed.
    /// </summary>
    public void Wait()
    {
        exitEvent.WaitOne();
    }

    public void Dispose()
    {
        if (wsClient == null) return;
        wsClient.Stop(WebSocketCloseStatus.NormalClosure, "Connection closed.").Wait();
        wsClient.Dispose();
    }

    public Guid On<T>(string eventName, Action<T> callback, bool once = false) where T : EventData
    {
        if (!eventListeners.ContainsKey(eventName)) eventListeners.Add(eventName, new EventCallbacks<T>());
        var callbacks = (EventCallbacks<T>)eventListeners[eventName];
        return callbacks.AddCallback(callback, once);
    }

    public Guid On<T>(string eventName, Func<T, Task> callback, bool once = false) where T : EventData
    {
        if (!eventListeners.ContainsKey(eventName)) eventListeners.Add(eventName, new EventCallbacks<T>());
        var callbacks = (EventCallbacks<T>)eventListeners[eventName];
        return callbacks.AddCallback(callback, once);
    }

    public void RemoveCallback(string eventName, Guid id)
    {
        if (!eventListeners.ContainsKey(eventName)) return;
        eventListeners[eventName].RemoveCallback(id);
    }

    public void Send<T>(string commandName, T data) where T : CommandData
    {
        Command<T> e = new Command<T>(commandName, data);
        var json = JsonSerializer.Serialize<Command<T>>(e, Api.JsonOptions);
        if (json == null) throw new JsonException("Failed to serialize command.");
        wsClient?.Send(json);
    }

    public async Task<string> Username(string playerId)
    {
        string? username;
        if (usernameCache.TryGetValue(playerId, out username)) return username;
        username = await Api.FetchUsername(Session.GameId, playerId);
        usernameCache.Add(playerId, username);
        return username;
    }

    private void TriggerEventListeners(string eventName, string eventJson)
    {
        if (!eventListeners.ContainsKey(eventName)) return;
        eventListeners[eventName].Call(eventJson);
    }

    private struct EventNameObj
    {
        public string Name { get; set; }
    }
    private void OnMessageReceived(ResponseMessage msg)
    {
        if (msg.MessageType != WebSocketMessageType.Text) return;
        try
        {
            var e = JsonSerializer.Deserialize<EventNameObj>(msg.Text, Api.JsonOptions);
            TriggerEventListeners(e.Name, msg.Text); ;
        }
        catch (JsonException) { }
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
