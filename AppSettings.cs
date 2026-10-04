using System.IO;
using System.Text.Json;

namespace WimFluent;

/// <summary>Настройки и история поиска. Хранятся в %AppData%\WIM\settings.json.</summary>
public sealed class AppSettings
{
    public const int MaxHistory = 20;

    public string? Language { get; set; }          // null — язык системы
    public bool? Dark { get; set; }                // null — следовать теме системы
    public List<string> History { get; set; } = new();

    public static AppSettings Current { get; private set; } = new();

    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "WIM", "settings.json");

    public static void Load()
    {
        try
        {
            if (File.Exists(FilePath))
                Current = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath)) ?? new();
        }
        catch { Current = new(); }
        Current.History ??= new();
    }

    public static void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath,
                JsonSerializer.Serialize(Current, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { /* настройки не критичны — тихо игнорируем */ }
    }

    // ------------------------------------------------------------ история
    public static void AddHistory(string query)
    {
        query = query.Trim();
        if (query.Length == 0) return;
        var h = Current.History;
        h.RemoveAll(q => string.Equals(q, query, StringComparison.CurrentCultureIgnoreCase));
        h.Insert(0, query);
        if (h.Count > MaxHistory) h.RemoveRange(MaxHistory, h.Count - MaxHistory);
        Save();
    }

    public static void RemoveHistory(string query)
    {
        Current.History.RemoveAll(q => q == query);
        Save();
    }

    public static void ClearHistory()
    {
        Current.History.Clear();
        Save();
    }
}
