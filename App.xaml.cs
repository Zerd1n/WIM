using System.Windows;

namespace WimFluent;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        AppSettings.Load();  // настройки и история
        Loc.Init();          // язык
        ThemeManager.Init(); // тема — до создания окна
        base.OnStartup(e);
    }
}
