using System.Globalization;

namespace WimFluent;

/// <summary>Локализация интерфейса. Чтобы добавить язык: допишите его в Languages
/// и добавьте ещё одну строку-перевод в каждый элемент Strings (в том же порядке).</summary>
public static class Loc
{
    public static readonly (string Code, string Name)[] Languages =
    {
        ("ru", "Русский"),
        ("en", "English"),
        ("ua", "Українська"),
        ("de", "Deutsch"),
        ("fr", "Français"),
        ("da", "Dansk")
    };

    // Принцип: ["понятие"] = new[] { "русское понятие", "английское понятие", "украинское понятие", "немецкое понятие" },
    private static readonly Dictionary<string, string[]> Strings = new()
    {
        ["searchHint"]    = new[] { "Поиск...", "Search...", "Пошук...", "Suche...", "Recherche...", "Søg..." },
        ["searchTip"]     = new[] { "Найти", "Search", "Шукати", "Suchen", "Rechercher", "Find" },
        ["titleEmpty"]    = new[] { "Название", "Title", "Назва", "Titel", "Titre", "Titel" },
        ["artistEmpty"]   = new[] { "Композитор трека", "Track artist", "Виконавець треку", "Komponist des Titels", "Compositeur", "Komponist" },
        ["albumEmpty"]    = new[] { "Альбом", "Album", "Альбом", "Album", "Album", "Album" },
        ["durationEmpty"] = new[] { "Длительность", "Duration", "Тривалість", "Dauer", "Durée", "Varighed" },
        ["searching"]     = new[] { "Поиск...", "Searching...", "Пошук...", "Wird gesucht...", "Recherche...", "Søge..." },
        ["notFound"]      = new[] { "Не найдено", "Not found", "Не знайдено", "Nicht gefunden", "Aucun résultat", "Ikke fundet" },
        ["foundIn"]       = new[] { "Найдено в: ", "Found in: ", "Знайдено в: ", "Zu finden in: ", "Trouvé dans: ", "Fundet i: " },
        ["openIn"]        = new[] { "Открыть в сервисе", "Open in service", "Відкрити в сервісі", "Im Dienstbetrieb", "Ouvrir dans le service", "Åbn i tjeneste" },
        ["about"]         = new[] { "Об приложении", "About this app", "Про застосунок", "Über diese App", "À propos de l'application", "Om appen" },
        ["version"]       = new[] { "Версия: ", "Version: ", "Версія: ", "Version: ", "Version: ", "Version: " },
        ["description"]   = new[]
        {
            "Приложение для поиска треков по названию на нескольких музыкальных сервисах.",
            "An app that searches for tracks by name across several music services.",
            "Застосунок для пошуку треків за назвою у кількох музичних сервісах.",
            "Eine App, die bei verschiedenen Musikdiensten nach Titeln anhand ihres Namens sucht.",
            "Une application qui recherche des morceaux par leur nom sur plusieurs services musicaux.",
            "En app, der søger efter numre efter navn på tværs af flere musiktjenester."
        },
        ["services"]      = new[] { "Сервисы: ", "Services: ", "Сервіси: ", "Dienste: ", "Services: ", "Tjenester: " },
        ["otherLinks"]    = new[]
        {
            "Ссылки на другие платформы: Odesli (song.link)",
            "Links to other platforms: Odesli (song.link)",
            "Посилання на інші платформи: Odesli (song.link)",
            "Links zu anderen Plattformen: Odesli (song.link)",
            "Liens vers d'autres plateformes: Odesli (song.link)",
            "Links til andre platforme: Odesli (song.link)"
        },
        ["author"]        = new[] { "Автор: ", "Author: ", "Автор: ", "Autor: ", "Auteur: ", "Forfatter: " },
        ["close"]         = new[] { "Закрыть", "Close", "Закрити", "Schließen", "Fermer", "Luk" },
        ["themeTip"]      = new[] { "Сменить тему", "Switch theme", "Змінити тему", "Das Thema wechseln", "Changer de thème", "Skift tema" },
        ["langTip"]       = new[] { "Язык", "Language", "Мова", "Sprache", "Langue", "Sprog" },
        ["historyTitle"]  = new[] { "Недавние запросы", "Recent searches", "Нещодавні запити", "Kürzliche Suchanfragen", "Recherches récentes", "Seneste søgninger" },
        ["historyClear"]  = new[] { "Очистить", "Clear", "Очистити", "Löschen", "Effacer", "Ryd" },
        ["historyRemove"] = new[] { "Удалить из истории", "Remove from history", "Видалити з історії", "Aus dem Verlauf entfernen", "Supprimer de l'historique", "Slet fra historik" },
    };

    public static string Language { get; private set; } = "en";
    public static event Action? Changed;

    /// <summary>Сохранённый язык, иначе язык системы, иначе английский.</summary>
    public static void Init()
    {
        var code = AppSettings.Current.Language ?? CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
        Language = Languages.Any(l => l.Code == code) ? code : "en";
    }

    public static void Set(string code)
    {
        if (code == Language || !Languages.Any(l => l.Code == code)) return;
        Language = code;
        AppSettings.Current.Language = code;
        AppSettings.Save();
        Changed?.Invoke();
    }

    public static string T(string key)
    {
        var i = Array.FindIndex(Languages, l => l.Code == Language);
        return Strings.TryGetValue(key, out var v) && i >= 0 && i < v.Length ? v[i] : key;
    }
}
