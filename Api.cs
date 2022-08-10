namespace CodeGame;

using System.Net;
using System.Net.Http.Json;
using System.Reactive.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using Websocket.Client;

public class SnakeCaseNamingPolicy : JsonNamingPolicy
{
    public override string ConvertName(string name)
    {
        return string.Concat(name.Select((c, i) => i > 0 && char.IsUpper(c) ? "_" + c : c.ToString())).ToLower();
    }
}

public class Api
{
    public class GameInfo
    {
        public string Name { get; set; } = "";
        [JsonPropertyName("cg_version")]
        public string CGVersion { get; set; } = "";
        public string? DisplayName { get; set; }
        public string? Description { get; set; }
        public string? Version { get; set; }
        [JsonPropertyName("repository_url")]
        public string? RepositoryURL { get; set; }
    }

    public string URL { get; private set; }
    public bool TLS { get; private set; }
    public string BaseURL { get; private set; }

    private static readonly HttpClient http = new HttpClient();
    private static readonly JsonSerializerOptions jsonOptions = new JsonSerializerOptions
    {
        AllowTrailingCommas = true,
        PropertyNamingPolicy = new SnakeCaseNamingPolicy(),
        DictionaryKeyPolicy = new SnakeCaseNamingPolicy(),
    };

    /// <summary>
    /// Fetches the game info from the /api/info endpoint.
    /// </summary>
    /// <returns></returns>
    /// <exception cref="HttpRequestException">Thrown when the request fails.</exception>
    /// <exception cref="JsonException">Thrown when the decoding of the response body fails.</exception>
    public async Task<GameInfo> FetchInfo()
    {
        var gameInfo = await http.GetFromJsonAsync<GameInfo>(BaseURL + "/api/info", jsonOptions);
        if (gameInfo == null || gameInfo.Name == "" || gameInfo.CGVersion == "")
        {
            throw new JsonException("Invalid server response.");
        }
        return gameInfo;
    }

    private class GameConfigResponse<T>
    {
        public T? Config { get; set; }
    }
    /// <summary>
    /// Fetches the game config from the server.
    /// </summary>
    /// <typeparam name="T">The type of the game config.</typeparam>
    /// <param name="gameId">The ID of the game.</param>
    /// <returns>The config of the game.</returns>
    /// <exception cref="JsonException">Thrown when the response of the server is invalid.</exception>
    public async Task<T> FetchGameConfig<T>(string gameId)
    {
        var result = await http.GetFromJsonAsync<GameConfigResponse<T>>(BaseURL + "/api/games/" + gameId, jsonOptions);
        if (result == null || result.Config == null)
        {
            throw new JsonException("Invalid server response.");
        }
        return result.Config;
    }

    internal async Task<WebsocketClient> Connect(string gameId, string playerId, string playerSecret, Func<ResponseMessage, Task> onMessage)
    {
        var client = new WebsocketClient(new Uri(GetBaseURL("ws", TLS, URL)+"/api/games/"+gameId+"/connect?player_id="+playerId+"&player_secret="+playerSecret));
        client.ReconnectTimeout = null;
        client.ErrorReconnectTimeout = null;
        client.MessageReceived.Select(msg => Observable.FromAsync(async () => await onMessage(msg))).Concat().Subscribe();
        await client.StartOrFail();
        return client;
    }

    internal async Task<(string gameId, string joinSecret)> CreateGame(bool makePublic, bool protect, object? config = null)
    {
        var requestData = new
        {
            Public = makePublic,
            Protected = protect,
            Config = config
        };

        var res = await http.PostAsJsonAsync(BaseURL + "/api/games", requestData, jsonOptions);
        res.EnsureSuccessStatusCode();

        var result = await res.Content.ReadFromJsonAsync<Dictionary<string, string>>(jsonOptions);
        if (result == null || !result.ContainsKey("game_id") || (protect && !result.ContainsKey("join_secret")))
        {
            throw new JsonException("Invaild server response.");
        }
        return (result["game_id"], protect ? result["join_secret"] : "");
    }

    internal async Task<(string playerId, string playerSecret)> CreatePlayer(string gameId, string username, string joinSecret = "")
    {
        var requestData = new
        {
            Username = username,
            JoinSecret = joinSecret
        };

        var res = await http.PostAsJsonAsync(BaseURL + "/api/games/" + gameId + "/players", requestData, jsonOptions);
        res.EnsureSuccessStatusCode();

        var result = await res.Content.ReadFromJsonAsync<Dictionary<string, string>>(jsonOptions);
        if (result == null || !result.ContainsKey("player_id") || !result.ContainsKey("player_secret"))
        {
            throw new JsonException("Invaild server response.");
        }

        return (result["player_id"], result["player_secret"]);
    }

    internal async Task<string> FetchUsername(string gameId, string playerId)
    {
        var result = await http.GetFromJsonAsync<Dictionary<string, string>>(BaseURL + "/api/games/" + gameId + "/players/" + playerId, jsonOptions);
        if (result == null || !result.ContainsKey("username"))
        {
            throw new JsonException("Invalid server response.");
        }
        return result["username"];
    }

    internal async Task<Dictionary<string, string>> FetchPlayers(string gameId)
    {
        var result = await http.GetFromJsonAsync<Dictionary<string, string>>(BaseURL + "/api/games/" + gameId + "/players", jsonOptions);
        if (result == null)
        {
            throw new JsonException("Invalid server response.");
        }
        return result;
    }

    internal static async Task<Api> Create(string url)
    {
        url = TrimURL(url);
        var tls = await IsTLS(url);
        return new Api(url, tls);
    }

    private Api(string trimmedURL, bool tls)
    {
        this.URL = trimmedURL;
        this.TLS = tls;
        this.BaseURL = GetBaseURL("http", tls, trimmedURL);
    }

    internal static string TrimURL(string url)
    {
        url = url.TrimEnd('/');
        string[] parts = url.Split("://");
        if (parts.Length < 2)
        {
            return url;
        }
        return string.Join("://", parts);
    }

    internal static string GetBaseURL(string protocol, bool tls, string trimmedURL)
    {
        if (tls)
        {
            return protocol + "s://" + trimmedURL;
        }
        return protocol + "://" + trimmedURL;
    }

    internal static async Task<bool> IsTLS(string trimmedURL)
    {
        try
        {
            var res = await http.GetAsync("https://" + trimmedURL);
            return res.IsSuccessStatusCode || res.StatusCode == HttpStatusCode.NotFound;
        }
        catch (HttpRequestException)
        {
            return false;
        }
    }
}
