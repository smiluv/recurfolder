using System.Windows;
using RecurFolder.Services;

namespace RecurFolder;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        ThemeManager.LoadAndApply();
        base.OnStartup(e);
    }
}
