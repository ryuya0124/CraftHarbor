using System.IO;
using System.Windows;
using CraftHarbor.Core;
using MessageBox = CraftHarbor.Desktop.HarborDialog;

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
            var loading = new StartupWindow();
            MainWindow = loading;
            loading.Closed += (_, _) => { if (ReferenceEquals(MainWindow, loading)) Shutdown(); };
            EventHandler? rendered = null;
            rendered = async (_, _) =>
            {
                loading.ContentRendered -= rendered;
                await loading.LoadAsync(() => Task.Run(() => new HarborStore(root)));
                if (MainWindow is MainWindow main) main.BeginUpdateChecks();
            };
            loading.ContentRendered += rendered;
            loading.Show();
        }
        catch (Exception ex) { MessageBox.Show("起動できませんでした。既存データはそのままです。\n" + ex.Message, "CraftHarbor"); Shutdown(1); }
    }
    protected override void OnExit(ExitEventArgs e) { instance?.Dispose(); base.OnExit(e); }
}
