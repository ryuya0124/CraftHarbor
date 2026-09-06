using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CraftHarbor.Core;
using CraftHarbor.Desktop;

internal static class Program
{
    [STAThread]
    static int Main(string[] args)
    {
        var root = Path.Combine(Path.GetTempPath(), "CraftHarbor.UiTests-" + Guid.NewGuid().ToString("N"));
        try
        {
            var app = new App(); app.InitializeComponent();
            var store = new HarborStore(root); var p = store.Add("Harbor Survival"); p.Engine = "fabric"; store.Save();
            var window = new MainWindow(root);
            var navigate = typeof(MainWindow).GetMethod("Navigate", BindingFlags.NonPublic | BindingFlags.Instance)!;
            var content = (FrameworkElement)window.Content;
            void Layout() { content.Measure(new Size(1240, 840)); content.Arrange(new Rect(0, 0, 1240, 840)); content.UpdateLayout(); }
            for (int pass = 0; pass < 2; pass++)
                foreach (var key in new[] { "overview", "console", "launch", "mods", "files", "backups", "java", "system", "help" })
                {
                    navigate.Invoke(window, [key]); Layout();
                    Console.WriteLine($"PASS UI navigation/layout {key} round {pass + 1}");
                }
            navigate.Invoke(window, ["overview"]); Layout();
            typeof(MainWindow).GetMethod("Tick", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(window, null);
            Layout();
            Console.WriteLine($"INFO UI process working set: {System.Diagnostics.Process.GetCurrentProcess().WorkingSet64 / 1048576d:F1} MB (test host)");
            if (args.Length > 0)
            {
                var bitmap = new RenderTargetBitmap(1240, 840, 96, 96, PixelFormats.Pbgra32); bitmap.Render(content);
                var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(args[0]))!); using var stream = File.Create(args[0]); encoder.Save(stream);
            }
            window.Close(); Console.WriteLine("RESULT UI smoke passed; no Minecraft processes launched"); return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
}
