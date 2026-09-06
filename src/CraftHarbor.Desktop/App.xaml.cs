using System.IO;
using System.Windows;

namespace CraftHarbor.Desktop;
public partial class App : Application
{
    private Mutex? instance;
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        instance = new Mutex(true, "Local\\CraftHarbor.Desktop", out var created);
        if (!created) { MessageBox.Show("CraftHarborはすでに開いています。"); Shutdown(); return; }
        try
        {
            var root = Environment.GetEnvironmentVariable("CRAFTHARBOR_DATA") ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "CraftHarbor", "data");
            new MainWindow(root).Show();
        }
        catch (Exception ex) { MessageBox.Show("起動できませんでした。既存データはそのままです。\n" + ex.Message, "CraftHarbor"); Shutdown(1); }
    }
    protected override void OnExit(ExitEventArgs e) { instance?.Dispose(); base.OnExit(e); }
}
