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
                foreach (var key in new[] { "overview", "console", "launch", "mods", "files", "backups", "java", "system", "help", "appearance" })
                {
                    navigate.Invoke(window, [key]); Layout();
                    Console.WriteLine($"PASS UI navigation/layout {key} round {pass + 1}");
                }
            navigate.Invoke(window, ["launch"]); Layout();
            var engine = Descendants(content).OfType<ComboBox>().Single(x => x.Name == "ServerEngine");
            var fabric = Descendants(content).OfType<StackPanel>().Single(x => x.Name == "FabricSettings");
            var versionButton = Descendants(content).OfType<Button>().Single(x => x.Name == "ServerVersions");
            var saveButton = Descendants(content).OfType<Button>().Single(x => x.Content?.ToString() == "設定を保存");
            foreach (var kind in new[] { "vanilla", "paper", "folia", "forge", "neoforge", "quilt", "custom", "fabric" })
            {
                engine.SelectedItem = kind; Layout();
                if ((fabric.Visibility == Visibility.Visible) != (kind == "fabric")) throw new Exception("Incorrect Fabric fields for " + kind);
                if (versionButton.IsEnabled != (kind is "vanilla" or "paper" or "folia" or "fabric")) throw new Exception("Incorrect version selector for " + kind);
                Descendants(fabric).OfType<TextBox>().Single().Text = "0.16.0";
                saveButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                var saved = new HarborStore(root).Profiles.Single();
                if (saved.Engine != kind || saved.LoaderVersion != (kind == "fabric" ? "0.16.0" : "")) throw new Exception("Incorrect persisted engine/loader for " + kind);
                navigate.Invoke(window, ["launch"]); Layout();
                engine = Descendants(content).OfType<ComboBox>().Single(x => x.Name == "ServerEngine");
                fabric = Descendants(content).OfType<StackPanel>().Single(x => x.Name == "FabricSettings");
                versionButton = Descendants(content).OfType<Button>().Single(x => x.Name == "ServerVersions");
                saveButton = Descendants(content).OfType<Button>().Single(x => x.Content?.ToString() == "設定を保存");
                if (engine.SelectedItem?.ToString() != kind || (fabric.Visibility == Visibility.Visible) != (kind == "fabric")) throw new Exception("Incorrect reopened engine for " + kind);
                Console.WriteLine("PASS UI engine switch/save/reopen " + kind);
            }
            foreach (var appearance in new[] { "Light", "Dark" })
            {
                Theme.Apply(appearance, true);
                Theme.Load(root);
                if (Theme.Appearance != appearance) throw new Exception("Theme preference did not persist");
                foreach (var key in new[] { "overview", "console", "launch", "mods", "files", "backups", "java", "system", "help", "appearance" })
                {
                    navigate.Invoke(window, [key]); Layout();
                    var expected = ((SolidColorBrush)app.Resources["Input"]).Color;
                    foreach (var box in Descendants(content).OfType<ComboBox>())
                    {
                        if (((SolidColorBrush)box.Background).Color != expected) throw new Exception("ComboBox has incorrect theme");
                        if (box.Template.FindName("PART_Popup", box) is not System.Windows.Controls.Primitives.Popup popup || popup.Child is not Border popupBorder || ((SolidColorBrush)popupBorder.Background).Color != expected) throw new Exception("Dropdown has incorrect theme");
                    }
                    if (((SolidColorBrush)((Grid)content).Background).Color != ((SolidColorBrush)app.Resources["Background"]).Color) throw new Exception("Local background did not update");
                    if (args.Length > 0 && key is "launch" or "appearance") SaveImage(content, Path.Combine(Path.GetDirectoryName(args[0])!, $"{appearance.ToLowerInvariant()}-{key}.png"));
                }
                var dialog = HarborDialog.Create(null, "テーマの確認ダイアログ", "確認", MessageBoxButton.YesNo, out var result);
                dialog.ApplyTemplate();
                if (((SolidColorBrush)dialog.Background).Color != ((SolidColorBrush)app.Resources["Background"]).Color || result() != MessageBoxResult.No) throw new Exception("Dialog theme or safe default is incorrect");
                dialog.Close(); Console.WriteLine("PASS theme apply/persist/all pages/dropdown/dialog " + appearance);
            }
            if (window.Icon == null || Theme.Icon.Width != 256) throw new Exception("App icon is missing or low resolution");
            Console.WriteLine("PASS multi-resolution app icon");
            var serverDir = store.ServerDir(p); var autoDir = Path.Combine(serverDir, "automodpack"); Directory.CreateDirectory(autoDir);
            File.WriteAllText(Path.Combine(autoDir, "automodpack-server.json"), "{\"modpackName\":\"before\"}");
            File.WriteAllText(Path.Combine(autoDir, "automodpack-client.json"), "{\"testPrivateData\":true}");
            navigate.Invoke(window, ["files"]); Layout();
            var configs = Descendants(content).OfType<ComboBox>().Single();
            if (!configs.Items.Cast<string>().Contains(Path.Combine("automodpack", "automodpack-server.json")) || configs.Items.Cast<string>().Any(x => x.EndsWith("automodpack-client.json"))) throw new Exception("AutoModpack configuration scope incorrect");
            configs.SelectedItem = Path.Combine("automodpack", "automodpack-server.json");
            Descendants(content).OfType<TextBox>().Single().Text = "{\"modpackName\":\"日本語同期テスト\"}";
            Descendants(content).OfType<Button>().Single(b => b.Content?.ToString() == "ファイルを保存").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            if (!File.ReadAllText(Path.Combine(autoDir, "automodpack-server.json")).Contains("日本語同期テスト") || !SafeFiles.Files(Path.Combine(root, "file-history")).Any()) throw new Exception("AutoModpack save/history failed");
            var extraConfigs = new[] { "config/ftb.snbt", "config/mod.json5", "defaultconfigs/create.toml", "Adventure/serverconfig/mod.toml", "kubejs/server_scripts/test.js", "scripts/test.zs" };
            foreach (var relative in extraConfigs) { var file = Path.Combine(serverDir, relative); Directory.CreateDirectory(Path.GetDirectoryName(file)!); File.WriteAllText(file, "original"); }
            navigate.Invoke(window, ["files"]); Layout(); configs = Descendants(content).OfType<ComboBox>().Single();
            foreach (var relative in extraConfigs)
            {
                var item = relative.Replace('/', Path.DirectorySeparatorChar); if (!configs.Items.Contains(item)) throw new Exception("Missing config " + relative);
                configs.SelectedItem = item; Descendants(content).OfType<TextBox>().Single().Text = "edited 日本語";
                Descendants(content).OfType<Button>().Single(b => b.Content?.ToString() == "ファイルを保存").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                if (File.ReadAllText(Path.Combine(serverDir, relative)) != "edited 日本語") throw new Exception("Save failed " + relative);
            }
            navigate.Invoke(window, ["mods"]); Layout();
            if (Descendants(content).OfType<CheckBox>().Single(b => b.Content?.ToString()?.StartsWith("現在の設定を維持") == true).IsChecked != true) throw new Exception("Preserve settings must be default");
            Console.WriteLine("PASS extended MOD config UI edits and preservation default");
            navigate.Invoke(window, ["console"]); Layout();
            foreach (var command in new[] { "automodpack", "automodpack host", "automodpack generate", "automodpack config reload" })
                if (!Descendants(content).OfType<Button>().Any(b => b.Content?.ToString() == command)) throw new Exception("Missing AutoModpack command");
            Console.WriteLine("PASS AutoModpack config discovery/save/history and console commands");
            navigate.Invoke(window, ["overview"]); Layout();
            typeof(MainWindow).GetMethod("Tick", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(window, null);
            Layout();
            Console.WriteLine($"INFO UI process working set: {System.Diagnostics.Process.GetCurrentProcess().WorkingSet64 / 1048576d:F1} MB (test host)");
            if (args.Length > 0)
            {
                SaveImage(content, args[0]);
            }
            window.Close(); Console.WriteLine("RESULT UI smoke passed; no Minecraft processes launched"); return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
    private static void SaveImage(FrameworkElement content, string path)
    {
        var bitmap = new RenderTargetBitmap(1240, 840, 96, 96, PixelFormats.Pbgra32); bitmap.Render(content);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!); using var stream = File.Create(path); encoder.Save(stream);
    }
    private static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i); yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }
}
