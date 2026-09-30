using System.Net.Http;
using System.Text.Json;

namespace WimFluent;

public record Track(
    string Service,
    string Title,
    string Artist,
    string Album,
    string Duration,
    string Cover,
    string Url);

/// <summary>Параллельный поиск трека по iTunes, Deezer и MusicBrainz.</summary>
public static class MusicSearch
{
    private static readonly HttpClient Http = CreateClient();

    private static HttpClient CreateClient()
    {
        var c = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
        c.DefaultRequestHeaders.UserAgent.ParseAdd("WIM-WhereIsMusic/1 (release)");
        return c;
    }

    public static readonly string[] ServiceNames = { "iTunes", "Deezer", "MusicBrainz" };

    // ------------------------------------------------------------ helpers
    private static string Str(JsonElement e, string name, string def = "—") =>
        e.ValueKind == JsonValueKind.Object
        && e.TryGetProperty(name, out var v)
        && v.ValueKind == JsonValueKind.String
            ? v.GetString() ?? def
            : def;

    private static double Num(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object
        && e.TryGetProperty(name, out var v)
        && v.ValueKind == JsonValueKind.Number
            ? v.GetDouble()
            : 0;

    private static JsonElement Obj(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) ? v : default;

    private static IEnumerable<JsonElement> Arr(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object
        && e.TryGetProperty(name, out var v)
        && v.ValueKind == JsonValueKind.Array
            ? v.EnumerateArray()
            : Enumerable.Empty<JsonElement>();

    private static string Fmt(double seconds)
    {
        if (seconds <= 0) return "—";
        var s = (int)Math.Round(seconds);
        return $"{s / 60}:{s % 60:00}";
    }

    private static async Task<JsonDocument> GetJson(string url, CancellationToken ct)
    {
        using var resp = await Http.GetAsync(url, ct);
        resp.EnsureSuccessStatusCode();
        await using var stream = await resp.Content.ReadAsStreamAsync(ct);
        return await JsonDocument.ParseAsync(stream, cancellationToken: ct);
    }

    // ------------------------------------------------------------ services
    private static async Task<List<Track>> ITunes(string q, CancellationToken ct)
    {
        var url = $"https://itunes.apple.com/search?term={Uri.EscapeDataString(q)}&entity=song&limit=5";
        using var doc = await GetJson(url, ct);
        var list = new List<Track>();
        foreach (var t in Arr(doc.RootElement, "results"))
        {
            list.Add(new Track(
                "iTunes",
                Str(t, "trackName"),
                Str(t, "artistName"),
                Str(t, "collectionName"),
                Fmt(Num(t, "trackTimeMillis") / 1000),
                Str(t, "artworkUrl100", "").Replace("100x100", "600x600"),
                Str(t, "trackViewUrl", "")));
        }
        return list;
    }

    private static async Task<List<Track>> Deezer(string q, CancellationToken ct)
    {
        var url = $"https://api.deezer.com/search?q={Uri.EscapeDataString(q)}&limit=5";
        using var doc = await GetJson(url, ct);
        var list = new List<Track>();
        foreach (var t in Arr(doc.RootElement, "data"))
        {
            var album = Obj(t, "album");
            list.Add(new Track(
                "Deezer",
                Str(t, "title"),
                Str(Obj(t, "artist"), "name"),
                Str(album, "title"),
                Fmt(Num(t, "duration")),
                Str(album, "cover_big", ""),
                Str(t, "link", "")));
        }
        return list;
    }

    private static async Task<List<Track>> MusicBrainz(string q, CancellationToken ct)
    {
        var url = $"https://musicbrainz.org/ws/2/recording?query={Uri.EscapeDataString(q)}&fmt=json&limit=5";
        using var doc = await GetJson(url, ct);
        var list = new List<Track>();
        foreach (var t in Arr(doc.RootElement, "recordings"))
        {
            var artist = string.Concat(
                Arr(t, "artist-credit").Select(c => Str(c, "name", "") + Str(c, "joinphrase", "")));
            if (artist.Length == 0) artist = "—";

            var rel = Arr(t, "releases").FirstOrDefault();
            var relId = Str(rel, "id", "");
            var id = Str(t, "id", "");

            list.Add(new Track(
                "MusicBrainz",
                Str(t, "title"),
                artist,
                Str(rel, "title"),
                Fmt(Num(t, "length") / 1000),
                relId.Length > 0 ? $"https://coverartarchive.org/release/{relId}/front-500" : "",
                id.Length > 0 ? $"https://musicbrainz.org/recording/{id}" : ""));
        }
        return list;
    }

    // ------------------------------------------------------------ public
    /// <summary>Ошибки отдельных сервисов игнорируются.</summary>
    public static async Task<List<Track>> SearchAllAsync(string query, CancellationToken ct)
    {
        static async Task<List<Track>> Safe(Func<Task<List<Track>>> f)
        {
            try { return await f(); }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"Ошибка сервиса: {ex.Message}"); return new(); }
        }

        var groups = await Task.WhenAll(
            Safe(() => ITunes(query, ct)),
            Safe(() => Deezer(query, ct)),
            Safe(() => MusicBrainz(query, ct)));

        return groups.SelectMany(g => g).ToList();
    }
}
