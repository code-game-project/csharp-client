namespace CodeGame;

public class GameSocket {
	public string URL {get; private set;}

	private bool tls;

	public static async Task<GameSocket> Create(string url) {
		var tls = await Url.IsTLS(url);
        return new GameSocket(url, tls);
	}

	private GameSocket(string url, bool tls) {
		this.URL = url;
		this.tls = tls;
	}
}
