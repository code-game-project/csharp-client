namespace CodeGame;

using System.Text.Json;

public class GameSocket
{
    private static readonly string CGVersion = "0.7";
    public Api Api { get; private set; }

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
    /// <exception cref="Exception">Thrown when the server refuses to create the game.</exception>
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
    /// <exception cref="Exception">Thrown when the server refuses to create the game.</exception>
    /// <exception cref="HttpRequestException">Thrown when the http request fails.</exception>
    /// <exception cref="JsonException">Thrown when server response is invalid.</exception>
    public async Task<(string gameId, string joinSecret)> CreateProtectedGame(bool makePublic, object? config = null)
    {
        return await Api.CreateGame(makePublic, true, config);
    }

    private GameSocket(Api api)
    {
        this.Api = api;
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
