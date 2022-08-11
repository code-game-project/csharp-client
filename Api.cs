namespace CodeGame.Client;

using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Numerics;
using System.Reactive.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Websocket.Client;

public class SnakeCaseNamingPolicy : JsonNamingPolicy
{
    public override string ConvertName(string name)
    {
        StringBuilder sbuilder = new StringBuilder();
        for (var i = 0; i < name.Length; i++)
        {
            if (char.IsLower(name[i]) && i < name.Length - 1 && char.IsUpper(name[i + 1]))
            {
                sbuilder.Append(name[i]);
                sbuilder.Append('_');
                continue;
            }
            if (char.IsUpper(name[i]) && i > 0 && i < name.Length - 1 && char.IsUpper(name[i - 1]) && char.IsLower(name[i + 1]))
            {
                sbuilder.Append('_');
                sbuilder.Append(name[i]);
                continue;
            }
            sbuilder.Append(name[i]);
        }
        return sbuilder.ToString().ToLower();
    }
}

public class BigIntegerConverter : JsonConverter<BigInteger>
{
    public override BigInteger Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.String) throw new JsonException($"Found token {reader.TokenType} but expected token {JsonTokenType.String}.");
        var token = reader.Read();
        using var doc = JsonDocument.ParseValue(ref reader);
        BigInteger result;
        var success = BigInteger.TryParse(doc.RootElement.GetString()!, NumberStyles.AllowLeadingSign, NumberFormatInfo.InvariantInfo, out result);
        if (!success) throw new JsonException($"Could not convert {token.ToString()} to BigInteger.");
        return result;
    }

    public override void Write(Utf8JsonWriter writer, BigInteger value, JsonSerializerOptions options)
    {
        var str = value.ToString(NumberFormatInfo.InvariantInfo);
        using var doc = JsonDocument.Parse(str);
        doc.WriteTo(writer);
    }
}

public class Api
{
    public class GameInfo
    {
        public string Name { get; set; } = "";
        public string CGVersion { get; set; } = "";
        public string? DisplayName { get; set; }
        public string? Description { get; set; }
        public string? Version { get; set; }
        public string? RepositoryURL { get; set; }
    }

    /// <summary>
    /// The URL of the game server without any protocol or trailing slashes.
    /// </summary>
    public string URL { get; private set; }
    /// <summary>
    /// Whether the game server supports TLS.
    /// </summary>
    public bool TLS { get; private set; }
    /// <summary>
    /// The URL of the game server including the protocol and without a trailing slash.
    /// </summary>
    public string BaseURL { get; private set; }

    internal static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
    {
        AllowTrailingCommas = true,
        PropertyNamingPolicy = new SnakeCaseNamingPolicy(),
        DictionaryKeyPolicy = new SnakeCaseNamingPolicy(),
        Converters = {
            new JsonStringEnumConverter(new SnakeCaseNamingPolicy(), false),
            new BigIntegerConverter(),
        }
    };

    private static readonly HttpClient http = new HttpClient();

    /// <summary>
    /// Fetches the game info from the /api/info endpoint.
    /// </summary>
    /// <returns></returns>
    /// <exception cref="HttpRequestException">Thrown when the request fails.</exception>
    /// <exception cref="JsonException">Thrown when the decoding of the response body fails.</exception>
    public async Task<GameInfo> FetchInfo()
    {
        var gameInfo = await http.GetFromJsonAsync<GameInfo>(BaseURL + "/api/info", JsonOptions);
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
        var result = await http.GetFromJsonAsync<GameConfigResponse<T>>(BaseURL + "/api/games/" + gameId, JsonOptions);
        if (result == null || result.Config == null)
        {
            throw new JsonException("Invalid server response.");
        }
        return result.Config;
    }

    internal async Task<WebsocketClient> ConnectWebSocket(string endpoint, Action<ResponseMessage> onMessage)
    {
        var client = new WebsocketClient(new Uri(GetBaseURL("ws", TLS, URL) + endpoint));
        client.ReconnectTimeout = null;
        client.ErrorReconnectTimeout = null;
        client.MessageReceived.Subscribe(onMessage);
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

        var res = await http.PostAsJsonAsync(BaseURL + "/api/games", requestData, JsonOptions);
        await ensureSuccessful(res);

        var result = await res.Content.ReadFromJsonAsync<Dictionary<string, string>>(JsonOptions);
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

        var res = await http.PostAsJsonAsync(BaseURL + "/api/games/" + gameId + "/players", requestData, JsonOptions);
        await ensureSuccessful(res);

        var result = await res.Content.ReadFromJsonAsync<Dictionary<string, string>>(JsonOptions);
        if (result == null || !result.ContainsKey("player_id") || !result.ContainsKey("player_secret"))
        {
            throw new JsonException("Invaild server response.");
        }

        return (result["player_id"], result["player_secret"]);
    }

    internal async Task<string> FetchUsername(string gameId, string playerId)
    {
        var res = await http.GetAsync(BaseURL + "/api/games/" + gameId + "/players");
        if (res.StatusCode == HttpStatusCode.NotFound) throw new CodeGameException("The player does not exist in the game.");
        await ensureSuccessful(res);
        var result = await res.Content.ReadFromJsonAsync<Dictionary<string, string>>(JsonOptions);
        if (result == null || !result.ContainsKey("username"))
        {
            throw new JsonException("Invalid server response.");
        }
        return result["username"];
    }

    internal async Task<Dictionary<string, string>> FetchPlayers(string gameId)
    {
        var result = await http.GetFromJsonAsync<Dictionary<string, string>>(BaseURL + "/api/games/" + gameId + "/players", JsonOptions);
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

    private async static Task ensureSuccessful(HttpResponseMessage? res)
    {
        if (res == null) throw new HttpRequestException("Received no response from the server.");
        try
        {
            res.EnsureSuccessStatusCode();
        }
        catch (HttpRequestException e)
        {
            try
            {
                var msg = await res.Content.ReadAsStringAsync();
                if (msg != null && msg != "")
                    throw new CodeGameException(msg, e);
            }
            catch (Exception ex)
            {
                if (ex is CodeGameException) throw;
            }
            throw;
        }
    }
}
