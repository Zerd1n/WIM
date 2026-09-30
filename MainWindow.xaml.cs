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
    private const string AppVersion = "Release 1.1";
    private const string AppAuthor = "Zerd1n, Claude";
    private const string AppDescription =
        "Приложение для поиска треков по названию на нескольких музыкальных сервисах.";

    private const double TitleMaxWidth = 560;

    private List<Track> _results = new();
    private int _index;
    private string _currentUrl = "";
    private CancellationTokenSource? _cts;
    private Window? _about;

    public MainWindow()
    {
        InitializeComponent();
        ShowEmpty();
    }

    // ------------------------------------------------------------ Mica
    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        // Mica доступен в Windows 11 22H2+ (build 22621). Иначе — сплошной фон.
        if (Environment.OSVersion.Version.Build >= 22621)
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            int light = 0, mica = 2;
            DwmSetWindowAttribute(hwnd, 20, ref light, sizeof(int)); // DWMWA_USE_IMMERSIVE_DARK_MODE
            DwmSetWindowAttribute(hwnd, 38, ref mica, sizeof(int));  // DWMWA_SYSTEMBACKDROP_TYPE
        }
        else
        {
            Background = new SolidColorBrush(Color.FromRgb(0xF3, 0xF3, 0xF3));
        }
    }

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
        FitTitle(title);
        TitleText.Text = title;
        ArtistText.Text = artist;
        AlbumText.Text = album;
        DurationText.Text = duration;
        SourceText.Text = source;
        CounterText.Text = counter;
        CoverBrush.ImageSource = null;
    }

    private void SetUrl(string url)
    {
        _currentUrl = url;
        OpenButton.IsEnabled = !string.IsNullOrEmpty(url);
    }

    private void UpdateNav()
    {
        var has = _results.Count > 1;
        PrevButton.IsEnabled = has;
        NextButton.IsEnabled = has;
    }

    private void ShowEmpty()
    {
        SetTexts("Название", "Композитор трека", "Альбом", "Длительность");
        SetUrl("");
        UpdateNav();
    }

    private void ShowMessage(string text)
    {
        SetTexts(text);
        SetUrl("");
        UpdateNav();
    }

    private void ShowTrack()
    {
        var t = _results[_index];
        SetTexts(t.Title, t.Artist, t.Album, t.Duration,
                 $"Источник: {t.Service}", $"{_index + 1}/{_results.Count}");
        SetUrl(t.Url);
        UpdateNav();
        LoadCover(t.Cover);
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

        _cts?.Cancel();
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;

        Busy.Visibility = Visibility.Visible;
        ShowMessage("Поиск…");

        var results = await MusicSearch.SearchAllAsync(query, ct);
        if (ct.IsCancellationRequested) return; // запущен более новый поиск

        Busy.Visibility = Visibility.Hidden;
        _results = results;
        _index = 0;

        if (results.Count > 0) ShowTrack();
        else ShowMessage("Не найдено");
    }

    private async void Search_Click(object sender, RoutedEventArgs e) => await StartSearchAsync();

    private async void SearchBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) await StartSearchAsync();
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

    private void Open_Click(object sender, RoutedEventArgs e)
    {
        if (Uri.TryCreate(_currentUrl, UriKind.Absolute, out var uri)
            && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
        {
            Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
        }
    }

    // ------------------------------------------------------------ «Об приложении»
    private void About_Click(object sender, RoutedEventArgs e)
    {
        if (_about is { IsLoaded: true })
        {
            _about.Activate();
            return;
        }

        TextBlock Line(string text, double size, Brush fg, FontWeight? weight = null, Thickness? margin = null) =>
            new()
            {
                Text = text, FontSize = size, Foreground = fg,
                FontWeight = weight ?? FontWeights.Normal,
                TextWrapping = TextWrapping.Wrap,
                TextAlignment = TextAlignment.Center,
                Margin = margin ?? new Thickness(0, 4, 0, 0)
            };

        var primary = (Brush)FindResource("TextPrimary");
        var secondary = (Brush)FindResource("TextSecondary");
        var services = string.Join(", ", MusicSearch.ServiceNames);

        var close = new Button
        {
            Content = "Закрыть",
            Style = (Style)FindResource("AccentButton"),
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 24, 0, 0),
            IsDefault = true,
            IsCancel = true
        };

        var panel = new StackPanel { Margin = new Thickness(28), VerticalAlignment = VerticalAlignment.Center };
        panel.Children.Add(Line(AppName, 22, primary, FontWeights.SemiBold));
        panel.Children.Add(Line($"Версия: {AppVersion}", 13, secondary));
        panel.Children.Add(Line(AppDescription, 14, primary, margin: new Thickness(0, 16, 0, 0)));
        panel.Children.Add(Line($"Сервисы: {services}", 14, primary, margin: new Thickness(0, 12, 0, 0)));
        panel.Children.Add(Line($"Автор: {AppAuthor}", 14, primary));
        panel.Children.Add(close);

        _about = new Window
        {
            Title = "Об приложении",
            Width = 440, Height = 330,
            ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = this,
            ShowInTaskbar = false,
            Background = new SolidColorBrush(Color.FromRgb(0xF3, 0xF3, 0xF3)),
            FontFamily = (FontFamily)FindResource("UiFont"),
            Content = panel
        };
        close.Click += (_, _) => _about.Close();
        _about.Show();
    }
}
