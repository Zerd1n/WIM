using System.Threading;
using System.Threading.Tasks;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace WimFluent;

/// <summary>Ссылка на трек в конкретном сервисе.</summary>
public record ServiceLink(string Service, string Url);

/// <summary>Одна песня, найденная в одном или нескольких сервисах.</summary>
public record Track(
    string Title,
    string Artist,
    string Album,
    string Duration,
    string Cover,
    IReadOnlyList<ServiceLink> Links,
    bool Enriched = false);   // true — дополнительные ссылки через Odesli уже получены

/// <summary>Результат одного сервиса до объединения.</summary>
public record RawTrack(
    string Service,
    string Title,
    string Artist,
    string Album,
    string Duration,
    string Cover,
    string Url);

/// <summary>Параллельный поиск трека по нескольким сервисам + дополнительные ссылки через Odesli.</summary>
public static class MusicSearch
{
    private static readonly HttpClient Http = CreateClient();

    private static HttpClient CreateClient()
    {
        var c = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
        c.DefaultRequestHeaders.UserAgent.ParseAdd("WIM-WhereIsMusic/0.1 (beta)");
        return c;
    }

    // ------------------------------------------------------------ ключи Spotify (необязательно)
    // Создайте приложение на https://developer.spotify.com/dashboard и вставьте Client ID / Secret
    // сюда или задайте переменные окружения WIM_SPOTIFY_ID и WIM_SPOTIFY_SECRET.
    // Не публикуйте секрет в открытом репозитории.
    public static string SpotifyClientId =
        Environment.GetEnvironmentVariable("WIM_SPOTIFY_ID") ?? "";
    public static string SpotifyClientSecret =
        Environment.GetEnvironmentVariable("WIM_SPOTIFY_SECRET") ?? "";

    private static bool SpotifyEnabled =>
        SpotifyClientId.Length > 0 && SpotifyClientSecret.Length > 0;

    /// <summary>Сервисы, по которым сейчас идёт поиск.</summary>
    public static IEnumerable<string> ServiceNames =>
        new[] { "iTunes", "Deezer", "MusicBrainz", "Audius" }
            .Concat(SpotifyEnabled ? new[] { "Spotify" } : Array.Empty<string>());

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
    private static async Task<List<RawTrack>> ITunes(string q, CancellationToken ct)
    {
        var url = $"https://itunes.apple.com/search?term={Uri.EscapeDataString(q)}&entity=song&limit=5";
        using var doc = await GetJson(url, ct);
        var list = new List<RawTrack>();
        foreach (var t in Arr(doc.RootElement, "results"))
        {
            list.Add(new RawTrack(
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

    private static async Task<List<RawTrack>> Deezer(string q, CancellationToken ct)
    {
        var url = $"https://api.deezer.com/search?q={Uri.EscapeDataString(q)}&limit=5";
        using var doc = await GetJson(url, ct);
        var list = new List<RawTrack>();
        foreach (var t in Arr(doc.RootElement, "data"))
        {
            var album = Obj(t, "album");
            list.Add(new RawTrack(
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

    private static async Task<List<RawTrack>> MusicBrainz(string q, CancellationToken ct)
    {
        var url = $"https://musicbrainz.org/ws/2/recording?query={Uri.EscapeDataString(q)}&fmt=json&limit=5";
        using var doc = await GetJson(url, ct);
        var list = new List<RawTrack>();
        foreach (var t in Arr(doc.RootElement, "recordings"))
        {
            var artist = string.Concat(
                Arr(t, "artist-credit").Select(c => Str(c, "name", "") + Str(c, "joinphrase", "")));
            if (artist.Length == 0) artist = "—";

            var rel = Arr(t, "releases").FirstOrDefault();
            var relId = Str(rel, "id", "");
            var id = Str(t, "id", "");

            list.Add(new RawTrack(
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

    private static async Task<List<RawTrack>> Audius(string q, CancellationToken ct)
    {
        var url = $"https://api.audius.co/v1/tracks/search?query={Uri.EscapeDataString(q)}&limit=5&app_name=WIM";
        using var doc = await GetJson(url, ct);
        var list = new List<RawTrack>();
        foreach (var t in Arr(doc.RootElement, "data"))
        {
            var art = Obj(t, "artwork");
            var cover = Str(art, "480x480", "");
            if (cover.Length == 0) cover = Str(art, "1000x1000", "");
            var permalink = Str(t, "permalink", "");
            list.Add(new RawTrack(
                "Audius",
                Str(t, "title"),
                Str(Obj(t, "user"), "name"),
                "—",
                Fmt(Num(t, "duration")),
                cover,
                permalink.Length > 0 ? "https://audius.co" + permalink : ""));
        }
        return list;
    }

    private static string? _spToken;
    private static DateTime _spExpires;

    private static async Task<string?> SpotifyToken(CancellationToken ct)
    {
        if (_spToken != null && DateTime.UtcNow < _spExpires) return _spToken;

        using var req = new HttpRequestMessage(HttpMethod.Post, "https://accounts.spotify.com/api/token");
        var basic = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{SpotifyClientId}:{SpotifyClientSecret}"));
        req.Headers.Authorization = new AuthenticationHeaderValue("Basic", basic);
        req.Content = new FormUrlEncodedContent(new[] { new KeyValuePair<string, string>("grant_type", "client_credentials") });

        using var resp = await Http.SendAsync(req, ct);
        resp.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
        _spToken = Str(doc.RootElement, "access_token", "");
        _spExpires = DateTime.UtcNow.AddSeconds(Math.Max(60, Num(doc.RootElement, "expires_in") - 60));
        return _spToken.Length > 0 ? _spToken : null;
    }

    private static async Task<List<RawTrack>> Spotify(string q, CancellationToken ct)
    {
        var list = new List<RawTrack>();
        if (!SpotifyEnabled) return list;
        var token = await SpotifyToken(ct);
        if (token == null) return list;

        using var req = new HttpRequestMessage(HttpMethod.Get,
            $"https://api.spotify.com/v1/search?q={Uri.EscapeDataString(q)}&type=track&limit=5");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var resp = await Http.SendAsync(req, ct);
        resp.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));

        foreach (var t in Arr(Obj(doc.RootElement, "tracks"), "items"))
        {
            var album = Obj(t, "album");
            var cover = Arr(album, "images").Select(i => Str(i, "url", "")).FirstOrDefault(u => u.Length > 0) ?? "";
            list.Add(new RawTrack(
                "Spotify",
                Str(t, "name"),
                string.Join(", ", Arr(t, "artists").Select(a => Str(a, "name", "")).Where(n => n.Length > 0)),
                Str(album, "name"),
                Fmt(Num(t, "duration_ms") / 1000),
                cover,
                Str(Obj(t, "external_urls"), "spotify", "")));
        }
        return list;
    }

    // ------------------------------------------------------------ Odesli (song.link)
    // Не поиск, а агрегатор ссылок: по ссылке на трек возвращает ссылки на другие платформы
    // (Spotify, YouTube Music, Tidal, Яндекс Музыка, Amazon Music …). Ключ не нужен, лимит ~10 запросов/мин.
    private static readonly (string Key, string Name)[] OdesliPlatforms =
    {
        ("spotify", "Spotify"),
        ("appleMusic", "Apple Music"),
        ("yandex", "Яндекс Музыка"),
        ("youtubeMusic", "YouTube Music"),
        ("youtube", "YouTube"),
        ("tidal", "Tidal"),
        ("amazonMusic", "Amazon Music"),
        ("soundcloud", "SoundCloud"),
        ("deezer", "Deezer"),
        ("pandora", "Pandora"),
        ("napster", "Napster"),
        ("anghami", "Anghami"),
        ("audiomack", "Audiomack"),
        ("boomplay", "Boomplay"),
    };

    /// <summary>Возвращает только НОВЫЕ ссылки (которых ещё нет у трека) или null при ошибке.</summary>
    public static async Task<List<ServiceLink>?> FindMoreLinksAsync(Track track, CancellationToken ct)
    {
        var source = track.Links.FirstOrDefault(l => l.Service is "iTunes" or "Deezer" or "Spotify");
        if (source == null) return new();      // Odesli не знает Audius и MusicBrainz

        try
        {
            using var doc = await GetJson(
                $"https://api.song.link/v1-alpha.1/links?url={Uri.EscapeDataString(source.Url)}", ct);
            var platforms = Obj(doc.RootElement, "linksByPlatform");
            var have = track.Links.Select(l => l.Service).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var result = new List<ServiceLink>();

            foreach (var (key, name) in OdesliPlatforms)
            {
                if (have.Contains(name)) continue;
                if (name == "Apple Music" && have.Contains("iTunes")) continue; // это одна и та же ссылка
                var url = Str(Obj(platforms, key), "url", "");
                if (url.Length > 0) result.Add(new ServiceLink(name, url));
            }
            return result;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Odesli: {ex.Message}");
            return null;
        }
    }

    // ------------------------------------------------------------ группировка
    // Приоритет сервиса при выборе названия, альбома и обложки.
    private static readonly string[] Priority = { "iTunes", "Deezer", "Spotify", "MusicBrainz", "Audius" };

    /// <summary>Ключ для сравнения: без регистра, скобок, «feat.», «Remastered» и пунктуации.</summary>
    private static string Norm(string s)
    {
        s = s.ToLowerInvariant();
        s = System.Text.RegularExpressions.Regex.Replace(s, @"[\(\[][^\)\]]*[\)\]]", " ");          // (feat. X), [Live]
        s = System.Text.RegularExpressions.Regex.Replace(s, @"\s[-–—]\s.*$", " ");                    // - Remastered 2011
        s = System.Text.RegularExpressions.Regex.Replace(s, @"\b(feat|ft|featuring)\b.*$", " ");
        s = System.Text.RegularExpressions.Regex.Replace(s, @"[^\p{L}\p{N}]+", "");
        return s;
    }

    /// <summary>Главный (первый) исполнитель: до «,», «&amp;», «feat.» и т.п.</summary>
    private static string MainArtist(string a) =>
        Norm(System.Text.RegularExpressions.Regex.Split(a, @",|&|;|\bfeat\b|\bft\b|\bfeaturing\b",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase)[0]);

    private static bool Has(string v) => !string.IsNullOrWhiteSpace(v) && v != "—";

    private static List<Track> Group(IEnumerable<RawTrack> raw)
    {
        // Сохраняем порядок первого появления, чтобы сортировка была стабильной.
        var buckets = new Dictionary<string, List<RawTrack>>();
        foreach (var t in raw)
        {
            var key = Norm(t.Title) + "|" + MainArtist(t.Artist);
            if (!buckets.TryGetValue(key, out var list)) buckets[key] = list = new();
            list.Add(t);
        }

        return buckets.Values
            .Select(list =>
            {
                var ordered = list.OrderBy(t => Array.IndexOf(Priority, t.Service)).ToList();
                var best = ordered[0];

                string First(Func<RawTrack, string> f, bool dash = true) =>
                    ordered.Select(f).FirstOrDefault(v => dash ? Has(v) : !string.IsNullOrEmpty(v))
                    ?? (dash ? "—" : "");

                var links = ordered
                    .Where(t => !string.IsNullOrEmpty(t.Url))
                    .GroupBy(t => t.Service)                       // одна ссылка на сервис
                    .Select(g => new ServiceLink(g.Key, g.First().Url))
                    .ToList();

                return new Track(
                    best.Title,
                    best.Artist,
                    First(t => t.Album),
                    First(t => t.Duration),
                    First(t => t.Cover, dash: false),
                    links);
            })
            .OrderByDescending(t => t.Links.Count)                 // сначала песни, найденные в нескольких сервисах
            .ToList();
    }

    // ------------------------------------------------------------ public
    /// <summary>Ошибки отдельных сервисов игнорируются.</summary>
    public static async Task<List<Track>> SearchAllAsync(string query, CancellationToken ct)
    {
        static async Task<List<RawTrack>> Safe(Func<Task<List<RawTrack>>> f)
        {
            try { return await f(); }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"Ошибка сервиса: {ex.Message}"); return new(); }
        }

        var groups = await Task.WhenAll(
            Safe(() => ITunes(query, ct)),
            Safe(() => Deezer(query, ct)),
            Safe(() => MusicBrainz(query, ct)),
            Safe(() => Audius(query, ct)),
            Safe(() => Spotify(query, ct)));

        return Group(groups.SelectMany(g => g));
    }
}
