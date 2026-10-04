using System.Threading;
using System.Threading.Tasks;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace WimFluent;

public partial class MainWindow : Window
{
    // Данные для окна «Об приложении» — меняйте под себя
    private const string AppName = "WIM (Where Is Music)";
    private const string AppVersion = "Release 1.3";
    private const string AppAuthor = "Zerd1n, Claude, VashMarkusha";
    private const string AppDescription =
        "Приложение для поиска треков по названию на нескольких музыкальных сервисах.";

    private const double TitleMaxWidth = 560;

    private List<Track> _results = new();
    private int _index;
    private CancellationTokenSource? _cts;
    private CancellationTokenSource? _enrichCts;
    private Window? _about;

    private enum View { Empty, Message, Track }
    private View _view = View.Empty;
    private string _messageKey = "";
    private string _foundIn = "";          // список сервисов, где найден текущий трек
    private bool _suppressHistory;
    private long _langClosedTick;

    public MainWindow()
    {
        InitializeComponent();

        LangPopup.Closed += (_, _) => _langClosedTick = Environment.TickCount64;
        Loc.Changed += ApplyLanguage;
        Closed += (_, _) => Loc.Changed -= ApplyLanguage;
        ApplyLanguage();
    }

    // ------------------------------------------------------------ язык
    private void ApplyLanguage()
    {
        SearchBox.Tag = Loc.T("searchHint");
        SearchButton.ToolTip = Loc.T("searchTip");
        LinksLabel.Text = Loc.T("openIn");
        AboutButton.Content = Loc.T("about");
        ThemeButton.ToolTip = Loc.T("themeTip");
        LangButton.ToolTip = Loc.T("langTip");
        HistoryTitle.Text = Loc.T("historyTitle");
        HistoryClear.Content = Loc.T("historyClear");

        switch (_view)
        {
            case View.Empty: ShowEmpty(); break;
            case View.Message: ShowMessage(_messageKey); break;
            case View.Track: SourceText.Text = Loc.T("foundIn") + _foundIn; break;
        }

        // окно «Об приложении» пересоздаём на новом языке
        if (_about is { IsLoaded: true })
        {
            var old = _about;
            _about = null;
            old.Close();
            ShowAbout();
        }
    }

    private void Lang_Click(object sender, RoutedEventArgs e)
    {
        // клик по кнопке при открытом меню сначала закрывает попап — заново не открываем
        if (Environment.TickCount64 - _langClosedTick < 250) return;

        LangList.Children.Clear();
        foreach (var (code, name) in Loc.Languages)
        {
            var content = new StackPanel { Orientation = Orientation.Horizontal };
            content.Children.Add(new TextBlock
            {
                Text = code == Loc.Language ? "\uE73E" : "",   // галочка у текущего языка
                FontFamily = (FontFamily)FindResource("IconFont"),
                FontSize = 12, Width = 22, VerticalAlignment = VerticalAlignment.Center
            });
            content.Children.Add(new TextBlock { Text = name, FontSize = 14 });

            var btn = new Button
            {
                Style = (Style)FindResource("SubtleButton"),
                Content = content,
                HorizontalContentAlignment = HorizontalAlignment.Left,
                Padding = new Thickness(10, 8, 16, 8),
                Margin = new Thickness(0, 1, 0, 1)
            };
            var c = code;
            btn.Click += (_, _) => { LangPopup.IsOpen = false; Loc.Set(c); };
            LangList.Children.Add(btn);
        }
        LangPopup.IsOpen = true;
    }

    // ------------------------------------------------------------ история поиска
    private void RefreshHistory()
    {
        var typed = SearchBox.Text.Trim();
        var items = AppSettings.Current.History
            .Where(h => typed.Length == 0 ||
                        (h.Contains(typed, StringComparison.CurrentCultureIgnoreCase)
                         && !string.Equals(h, typed, StringComparison.CurrentCultureIgnoreCase)))
            .Take(8)
            .ToList();

        if (items.Count == 0 || !SearchBox.IsKeyboardFocused)
        {
            HistoryPopup.IsOpen = false;
            return;
        }

        HistoryList.Children.Clear();
        foreach (var q in items) HistoryList.Children.Add(MakeHistoryRow(q));
        HistoryPopup.IsOpen = true;
    }

    private UIElement MakeHistoryRow(string query)
    {
        var row = new Grid();
        row.ColumnDefinitions.Add(new ColumnDefinition());
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var glyph = new TextBlock
        {
            Text = "\uE81C", // часы
            FontFamily = (FontFamily)FindResource("IconFont"),
            FontSize = 14, Margin = new Thickness(0, 0, 10, 0),
            VerticalAlignment = VerticalAlignment.Center
        };
        glyph.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondary");

        var text = new TextBlock
        {
            Text = query, FontSize = 14,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(text, 1);

        var content = new Grid();
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        content.ColumnDefinitions.Add(new ColumnDefinition());
        content.Children.Add(glyph);
        content.Children.Add(text);

        // Focusable = false, чтобы поле поиска не теряло фокус при клике
        var pick = new Button
        {
            Style = (Style)FindResource("SubtleButton"),
            Content = content,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Padding = new Thickness(10, 8, 10, 8),
            Margin = new Thickness(0, 1, 0, 1),
            Focusable = false
        };
        pick.Click += async (_, _) =>
        {
            _suppressHistory = true;
            SearchBox.Text = query;
            SearchBox.CaretIndex = query.Length;
            _suppressHistory = false;
            HistoryPopup.IsOpen = false;
            await StartSearchAsync();
        };

        var remove = new Button
        {
            Style = (Style)FindResource("SubtleButton"),
            Content = new TextBlock
            {
                Text = "\uE711", FontSize = 11,
                FontFamily = (FontFamily)FindResource("IconFont")
            },
            Width = 32, Padding = new Thickness(0), Margin = new Thickness(2, 1, 0, 1),
            ToolTip = Loc.T("historyRemove"), Focusable = false
        };
        remove.Click += (_, _) =>
        {
            AppSettings.RemoveHistory(query);
            RefreshHistory();
        };
        Grid.SetColumn(remove, 1);

        row.Children.Add(pick);
        row.Children.Add(remove);
        return row;
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!_suppressHistory && IsLoaded) RefreshHistory();
    }

    private void SearchBox_GotFocus(object sender, KeyboardFocusChangedEventArgs e) => RefreshHistory();

    private void HistoryClear_Click(object sender, RoutedEventArgs e)
    {
        AppSettings.ClearHistory();
        HistoryPopup.IsOpen = false;
    }

    private static bool IsInside(DependencyObject? d, DependencyObject target)
    {
        while (d != null)
        {
            if (ReferenceEquals(d, target)) return true;
            d = d is Visual || d is System.Windows.Media.Media3D.Visual3D
                ? VisualTreeHelper.GetParent(d)
                : LogicalTreeHelper.GetParent(d);
        }
        return false;
    }

    // Клик вне поля и списка закрывает историю
    private void Window_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (!HistoryPopup.IsOpen) return;
        var src = e.OriginalSource as DependencyObject;
        if (!IsInside(src, HistoryCard) && !IsInside(src, SearchBox)) HistoryPopup.IsOpen = false;
    }

    // Потеря активности, перемещение и изменение размера окна тоже закрывают список
    private void Window_Deactivated(object? sender, EventArgs e) => HistoryPopup.IsOpen = false;

    // ------------------------------------------------------------ тема
    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        ThemeManager.Changed += ApplyWindowTheme;
        Closed += (_, _) => ThemeManager.Changed -= ApplyWindowTheme;
        ApplyWindowTheme();
    }

    private void ApplyWindowTheme()
    {
        ThemeManager.ApplyToWindow(this, mica: true);

        // Mica — только Windows 11 22H2+, иначе сплошной фон под тему
        if (ThemeManager.MicaSupported) Background = Brushes.Transparent;
        else SetResourceReference(BackgroundProperty, "SolidBg");

        // значок показывает, на какую тему переключит кнопка
        ThemeIcon.Text = ThemeManager.IsDark ? "\uE706" : "\uE708";
    }

    private void Theme_Click(object sender, RoutedEventArgs e) => ThemeManager.Toggle();

    // ------------------------------------------------------------ отображение
    private void FitTitle(string text)
    {
        var dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var face = new Typeface(TitleText.FontFamily, FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);
        double size = 64;
        while (size > 22)
        {
            var ft = new FormattedText(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
                face, size, Brushes.Black, dpi);
            if (ft.Width <= TitleMaxWidth - 6) break;
            size -= 2;
        }
        TitleText.FontSize = size;
    }

    private void SetTexts(string title, string artist = "", string album = "",
                          string duration = "", string source = "", string counter = "")
    {
        _enrichCts?.Cancel();
        FitTitle(title);
        TitleText.Text = title;
        ArtistText.Text = artist;
        AlbumText.Text = album;
        DurationText.Text = duration;
        SourceText.Text = source;
        CounterText.Text = counter;
        CoverBrush.ImageSource = null;
    }

    private void SetLinks(IReadOnlyList<ServiceLink> links)
    {
        LinksPanel.Children.Clear();
        LinksLabel.Visibility = links.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

        foreach (var link in links)
        {
            var content = new StackPanel { Orientation = Orientation.Horizontal };
            content.Children.Add(new TextBlock { Text = link.Service });
            content.Children.Add(new TextBlock
            {
                Text = "\uE8A7", // значок «открыть в новом окне»
                FontFamily = (FontFamily)FindResource("IconFont"),
                FontSize = 13,
                Margin = new Thickness(10, 2, 0, 0)
            });

            var btn = new Button
            {
                Content = content,
                Tag = link.Url,
                Style = (Style)FindResource("AccentButton"),
                Margin = new Thickness(0, 0, 8, 8),
                ToolTip = link.Url
            };
            btn.Click += Link_Click;
            LinksPanel.Children.Add(btn);
        }
    }

    private void UpdateNav()
    {
        var has = _results.Count > 1;
        PrevButton.IsEnabled = has;
        NextButton.IsEnabled = has;
    }

    private void ShowEmpty()
    {
        _view = View.Empty;
        SetTexts(Loc.T("titleEmpty"), Loc.T("artistEmpty"), Loc.T("albumEmpty"), Loc.T("durationEmpty"));
        SetLinks(Array.Empty<ServiceLink>());
        UpdateNav();
    }

    private void ShowMessage(string key)
    {
        _view = View.Message;
        _messageKey = key;
        SetTexts(Loc.T(key));
        SetLinks(Array.Empty<ServiceLink>());
        UpdateNav();
    }

    private void ShowTrack()
    {
        var t = _results[_index];
        _view = View.Track;
        _foundIn = string.Join(", ", t.Links.Select(l => l.Service));
        SetTexts(t.Title, t.Artist, t.Album, t.Duration,
                 Loc.T("foundIn") + _foundIn,
                 $"{_index + 1}/{_results.Count}");
        SetLinks(t.Links);
        UpdateNav();
        LoadCover(t.Cover);
        StartEnrich();
    }

    // Дополнительные ссылки (Spotify, YouTube Music, Tidal, Яндекс Музыка …) через Odesli.
    // Запрос уходит с небольшой задержкой, чтобы не расходовать лимит при быстром листании.
    private async void StartEnrich()
    {
        var idx = _index;
        if (idx >= _results.Count) return;
        var track = _results[idx];
        if (track.Enriched) return;

        _enrichCts?.Cancel();
        var cts = _enrichCts = new CancellationTokenSource();

        try
        {
            await Task.Delay(400, cts.Token);
            var more = await MusicSearch.FindMoreLinksAsync(track, cts.Token);
            if (more == null || cts.IsCancellationRequested) return;

            // результаты могли смениться новым поиском
            if (idx >= _results.Count || !ReferenceEquals(_results[idx], track)) return;

            var merged = track with { Links = track.Links.Concat(more).ToList(), Enriched = true };
            _results[idx] = merged;
            if (_index == idx) SetLinks(merged.Links);
        }
        catch (OperationCanceledException) { }
    }

    private void LoadCover(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return;

        var bmp = new BitmapImage();
        bmp.BeginInit();
        bmp.UriSource = uri;
        bmp.DecodePixelWidth = 600;
        bmp.CacheOption = BitmapCacheOption.OnLoad;
        bmp.EndInit();

        // Обложка последнего выбранного трека побеждает: присваиваем сразу,
        // а при ошибке загрузки — очищаем (если она всё ещё актуальна).
        bmp.DownloadFailed += (_, _) =>
        {
            if (ReferenceEquals(CoverBrush.ImageSource, bmp)) CoverBrush.ImageSource = null;
        };
        CoverBrush.ImageSource = bmp;
    }

    // ------------------------------------------------------------ действия
    private async Task StartSearchAsync()
    {
        var query = SearchBox.Text.Trim();
        if (query.Length == 0) return;

        HistoryPopup.IsOpen = false;
        _cts?.Cancel();
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;

        Busy.Visibility = Visibility.Visible;
        ShowMessage("searching");

        var results = await MusicSearch.SearchAllAsync(query, ct);
        if (ct.IsCancellationRequested) return; // запущен более новый поиск

        Busy.Visibility = Visibility.Hidden;
        _results = results;
        _index = 0;

        if (results.Count > 0)
        {
            AppSettings.AddHistory(query);   // в историю попадают только удачные запросы
            ShowTrack();
        }
        else ShowMessage("notFound");
    }

    private async void Search_Click(object sender, RoutedEventArgs e) => await StartSearchAsync();

    private async void SearchBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) await StartSearchAsync();
        else if (e.Key == Key.Escape) HistoryPopup.IsOpen = false;
    }

    private void Next_Click(object sender, RoutedEventArgs e)
    {
        if (_results.Count == 0) return;
        _index = (_index + 1) % _results.Count;
        ShowTrack();
    }

    private void Prev_Click(object sender, RoutedEventArgs e)
    {
        if (_results.Count == 0) return;
        _index = (_index - 1 + _results.Count) % _results.Count;
        ShowTrack();
    }

    private void Link_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string url }
            && Uri.TryCreate(url, UriKind.Absolute, out var uri)
            && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
        {
            Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
        }
    }

    // ------------------------------------------------------------ «Об приложении»
    private void About_Click(object sender, RoutedEventArgs e) => ShowAbout();

    private void ShowAbout()
    {
        if (_about is { IsLoaded: true })
        {
            _about.Activate();
            return;
        }

        TextBlock Line(string text, double size, string fgKey, FontWeight? weight = null, Thickness? margin = null)
        {
            var tb = new TextBlock
            {
                Text = text, FontSize = size,
                FontWeight = weight ?? FontWeights.Normal,
                TextWrapping = TextWrapping.Wrap,
                TextAlignment = TextAlignment.Center,
                Margin = margin ?? new Thickness(0, 4, 0, 0)
            };
            tb.SetResourceReference(TextBlock.ForegroundProperty, fgKey); // обновляется при смене темы
            return tb;
        }

        const string primary = "TextPrimary";
        const string secondary = "TextSecondary";
        var services = string.Join(", ", MusicSearch.ServiceNames);

        var close = new Button
        {
            Content = Loc.T("close"),
            Style = (Style)FindResource("AccentButton"),
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 24, 0, 0),
            IsDefault = true,
            IsCancel = true
        };

        var panel = new StackPanel { Margin = new Thickness(28), VerticalAlignment = VerticalAlignment.Center };
        panel.Children.Add(Line(AppName, 22, primary, FontWeights.SemiBold));
        panel.Children.Add(Line(Loc.T("version") + AppVersion, 13, secondary));
        panel.Children.Add(Line(Loc.T("description"), 14, primary, margin: new Thickness(0, 16, 0, 0)));
        panel.Children.Add(Line(Loc.T("services") + services, 14, primary, margin: new Thickness(0, 12, 0, 0)));
        panel.Children.Add(Line(Loc.T("otherLinks"), 13, secondary));
        panel.Children.Add(Line(Loc.T("author") + AppAuthor, 14, primary));
        panel.Children.Add(close);

        _about = new Window
        {
            Title = Loc.T("about"),
            Width = 440, Height = 360,
            ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = this,
            ShowInTaskbar = false,
            FontFamily = (FontFamily)FindResource("UiFont"),
            Content = panel
        };
        _about.SetResourceReference(BackgroundProperty, "SolidBg");
        close.Click += (_, _) => _about.Close();
        _about.Show();
        ThemeManager.ApplyToWindow(_about, mica: false); // тёмный заголовок в тёмной теме
    }
}
