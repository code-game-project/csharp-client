namespace CodeGame;

using System.Net;

internal static class Url {
	private static readonly HttpClient http = new HttpClient();

	internal static string TrimURL(string url) {
		url = url.TrimStart('/');
		string[] parts = url.Split("://");
		if (parts.Length < 2) {
			return url;
		}
		return string.Join("://", parts);
	}

	internal static string BaseURL(string protocol, bool tls, string trimmedURL) {
		if (tls) {
			return protocol + "s://" + trimmedURL;
		}
		return protocol + "://" + trimmedURL;
	}

	internal static async Task<bool> IsTLS(string trimmedURL) {
		try {
			var res = await http.GetAsync("https://" + trimmedURL);
            return res.IsSuccessStatusCode || res.StatusCode == HttpStatusCode.NotFound;
		} catch (HttpRequestException) {
			return false;
		}
	}
}
