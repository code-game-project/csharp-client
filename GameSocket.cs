namespace CodeGame;

using System.Text.Json;

public class GameSocket
{
    private static readonly string CGVersion = "0.7";
    public Api Api { get; private set; }

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

    private GameSocket(Api api)
    {
        this.Api = api;
    }

    private static bool IsVersionCompatible(string serverVersion) {
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
        } catch {
            return false;
        }
    }
}
