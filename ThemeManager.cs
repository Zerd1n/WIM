using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using Microsoft.Win32;

namespace WimFluent;

/// <summary>Светлая/тёмная тема и акцентный цвет системы Windows.</summary>
public static class ThemeManager
{
    private static bool? _override; // null — следовать системной теме

    public static bool IsDark { get; private set; }
    public static bool MicaSupported => Environment.OSVersion.Version.Build >= 22621;
    public static event Action? Changed;

    public static void Init()
    {
        _override = AppSettings.Current.Dark; // сохранённый выбор пользователя
        SystemEvents.UserPreferenceChanged += (_, e) =>
        {
            if (e.Category is UserPreferenceCategory.General
                or UserPreferenceCategory.VisualStyle
                or UserPreferenceCategory.Color)
            {
                Application.Current?.Dispatcher.InvokeAsync(() => Refresh());
            }
        };
        Refresh();
    }

    /// <summary>Ручное переключение светлая ↔ тёмная.</summary>
    public static void Toggle()
    {
        _override = !IsDark;
        AppSettings.Current.Dark = _override;
        AppSettings.Save();
        Refresh();
    }

    private static void Refresh()
    {
        IsDark = _override ?? SystemUsesDark();
        Apply(IsDark, ReadAccent());
        Changed?.Invoke();
    }

    // ------------------------------------------------------------ система
    private static bool SystemUsesDark()
    {
        try
        {
            return Registry.GetValue(
                @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize",
                "AppsUseLightTheme", 1) is int v && v == 0;
        }
        catch { return false; }
    }

    private static Color ReadAccent()
    {
        try
        {
            // DWM хранит цвет в формате ABGR
            if (Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\DWM",
                    "AccentColor", null) is int v)
            {
                return Color.FromRgb((byte)(v & 0xFF), (byte)((v >> 8) & 0xFF), (byte)((v >> 16) & 0xFF));
            }
        }
        catch { /* используем запасной вариант */ }

        var g = SystemParameters.WindowGlassColor;
        return Color.FromRgb(g.R, g.G, g.B);
    }

    // ------------------------------------------------------------ палитра
    private static double Luma(Color c) => (0.2126 * c.R + 0.7152 * c.G + 0.0722 * c.B) / 255.0;

    private static Color Mix(Color a, Color b, double t) => Color.FromRgb(
        (byte)Math.Round(a.R + (b.R - a.R) * t),
        (byte)Math.Round(a.G + (b.G - a.G) * t),
        (byte)Math.Round(a.B + (b.B - a.B) * t));

    private static Color H(string hex) => (Color)ColorConverter.ConvertFromString(hex);

    private static void Apply(bool dark, Color accent)
    {
        var res = Application.Current.Resources;
        void Set(string key, Color c)
        {
            var b = new SolidColorBrush(c);
            b.Freeze();
            res[key] = b;
        }

        // Акцент: в тёмной теме светлее, в светлой слишком светлый — затемняем
        var ui = dark
            ? Mix(accent, Colors.White, 0.25)
            : Luma(accent) > 0.45 ? Mix(accent, Colors.Black, 0.30) : accent;

        Set("Accent", ui);
        Set("AccentHover", Mix(ui, Colors.Black, 0.12));
        Set("AccentPressed", Mix(ui, Colors.Black, 0.22));
        Set("OnAccent", Luma(ui) > 0.6 ? H("#1A1A1A") : Colors.White);

        if (dark)
        {
            Set("TextPrimary", Colors.White);
            Set("TextSecondary", H("#C8C8C8"));
            Set("CardBrush", H("#14FFFFFF"));
            Set("FieldBrush", H("#0FFFFFFF"));
            Set("StrokeBrush", H("#1AFFFFFF"));
            Set("SubtleHover", H("#0FFFFFFF"));
            Set("SubtlePressed", H("#1AFFFFFF"));
            Set("DisabledBg", H("#28FFFFFF"));
            Set("DisabledFg", H("#5DFFFFFF"));
            Set("PlaceholderGlyph", H("#26FFFFFF"));
            Set("SolidBg", H("#202020"));
        }
        else
        {
            Set("TextPrimary", H("#1A1A1A"));
            Set("TextSecondary", H("#616161"));
            Set("CardBrush", H("#B3FFFFFF"));
            Set("FieldBrush", H("#B3FFFFFF"));
            Set("StrokeBrush", H("#1A000000"));
            Set("SubtleHover", H("#0F000000"));
            Set("SubtlePressed", H("#1A000000"));
            Set("DisabledBg", H("#14000000"));
            Set("DisabledFg", H("#5C000000"));
            Set("PlaceholderGlyph", H("#26000000"));
            Set("SolidBg", H("#F3F3F3"));
        }
    }

    // ------------------------------------------------------------ окно
    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    /// <summary>Тёмный заголовок окна и (опционально) Mica под текущую тему.</summary>
    public static void ApplyToWindow(Window w, bool mica)
    {
        var hwnd = new WindowInteropHelper(w).EnsureHandle();
        int dark = IsDark ? 1 : 0;
        DwmSetWindowAttribute(hwnd, 20, ref dark, sizeof(int)); // DWMWA_USE_IMMERSIVE_DARK_MODE
        if (mica && MicaSupported)
        {
            int type = 2;                                       // Mica
            DwmSetWindowAttribute(hwnd, 38, ref type, sizeof(int)); // DWMWA_SYSTEMBACKDROP_TYPE
        }
    }
}
