namespace CodeGame;

using System.Text.Json;
using Directories.Net;

public class Session
{
    private static readonly string gamesPath = Path.Combine(new BaseDirectories().DataDir, "codegame", "games");


    public string GameURL { get; internal set; }
    public string Username { get; internal set; }
    public string GameId { get; internal set; }
    public string PlayerId { get; internal set; }
    public string PlayerSecret { get; internal set; }

    public Session(string gameURL, string username, string gameId, string playerId, string playerSecret)
    {
        this.GameURL = gameURL;
        this.Username = username;
        this.GameId = gameId;
        this.PlayerId = playerId;
        this.PlayerSecret = playerSecret;
    }

    public static Session Load(string gameURL, string username)
    {
        using var file = File.Open(Path.Combine(gamesPath, Uri.EscapeDataString(gameURL), username + ".json"), FileMode.Open);
        var data = JsonSerializer.Deserialize<Dictionary<string, string>>(file);
        if (data == null || !data.ContainsKey("game_id") || !data.ContainsKey("player_id") || !data.ContainsKey("player_secret"))
        {
            throw new JsonException("Invalid session file.");
        }
        return new Session(gameURL, username, data["game_id"], data["player_id"], data["player_secret"]);
    }

    public void Save()
    {
        if (GameURL == "") throw new Exception("Empty game URL.");

        var dir = Path.Combine(gamesPath, Uri.EscapeDataString(this.GameURL));

        if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

        var data = new Dictionary<string, string>(3);
        data.Add("game_id", GameId);
        data.Add("player_id", PlayerId);
        data.Add("player_secret", PlayerSecret);

        using var file = File.Create(Path.Combine(dir, this.Username + ".json"));
        JsonSerializer.Serialize<Dictionary<string, string>>(file, data);
    }

    public void Remove()
    {
        if (GameURL == "") return;

        var dir = Path.Combine(gamesPath, Uri.EscapeDataString(GameURL));
        File.Delete(Path.Combine(dir, Username + ".json"));

        if (Directory.GetFiles(dir).Length == 0) Directory.Delete(dir);
    }
}
