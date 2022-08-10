namespace CodeGame;

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

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
    public string BaseURL { get; private set; }

    private static readonly HttpClient http = new HttpClient();
    private static readonly JsonSerializerOptions jsonOptions = new JsonSerializerOptions
    {
        AllowTrailingCommas = true,
        PropertyNamingPolicy = new SnakeCaseNamingPolicy()
    };

    public async Task<GameInfo> FetchInfo()
    {
        using var res = await http.GetAsync(BaseURL+"/api/info");
        res.EnsureSuccessStatusCode();

        var gameInfo = await res.Content.ReadFromJsonAsync<GameInfo>(jsonOptions);
        if (gameInfo == null || gameInfo.Name == "" || gameInfo.CGVersion == "")
        {
            throw new JsonException("Missing name and/or cg_version property.");
        }
        return gameInfo;
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
        this.BaseURL = GetBaseURL("http", tls, trimmedURL);
    }

    internal static string TrimURL(string url)
    {
        url = url.TrimStart('/');
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
