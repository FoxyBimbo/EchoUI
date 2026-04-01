using PrismPane_Widgets.Models;
using PrismPane_Widgets.Services;

namespace PrismPane_Widgets
{
    public partial class App : System.Windows.Application
    {
        protected override void OnStartup(System.Windows.StartupEventArgs e)
        {
            base.OnStartup(e);

            var settings = AppSettings.Load();
            var colors = ThemeHelper.ResolveColors(settings);
            ThemeHelper.ApplyToApp(colors);

            SessionEnding += (_, _) => PrismPane_Widgets.MainWindow.DockManager.RestoreAll();
            Exit += (_, _) => PrismPane_Widgets.MainWindow.DockManager.RestoreAll();
            AppDomain.CurrentDomain.ProcessExit += (_, _) => PrismPane_Widgets.MainWindow.DockManager.RestoreAll();
        }
    }
}
