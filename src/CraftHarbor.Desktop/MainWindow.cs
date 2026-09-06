using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using CraftHarbor.Core;
using Microsoft.Win32;

namespace CraftHarbor.Desktop;

public sealed class MainWindow : Window
{
    private readonly HarborStore store;
    private readonly Downloads downloads = new();
    private readonly Dictionary<string, ServerRuntime> runtimes = [];
    private readonly ListBox servers = new();
    private readonly StackPanel page = new();
    private readonly TextBlock title = new() { FontSize = 28, FontWeight = FontWeights.Bold };
    private readonly TextBlock subtitle = new() { Foreground = Brush("#91A3B8"), Margin = new Thickness(0, 8, 0, 20) };
    private readonly TextBlock status = new() { Text = "準備完了  •  データはこのPCに保存", Foreground = Brush("#61DBC4"), VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock metrics = new() { Foreground = Brush("#91A3B8"), Margin = new Thickness(0, 12, 0, 0) };
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(500) };
    private TextBox? console;
    private bool busy;
    private CancellationTokenSource? operation;
    private string currentPage = "overview";
    private Func<bool>? mayLeave;
    private bool restoringSelection;
    private ServerProfile? Selected => servers.SelectedItem as ServerProfile;
    private ServerRuntime Runtime(ServerProfile p)
    {
        if (!runtimes.TryGetValue(p.Id, out var runtime)) runtimes[p.Id] = runtime = new ServerRuntime(); return runtime;
    }
    private static SolidColorBrush Brush(string color) => new((Color)ColorConverter.ConvertFromString(color));
    public MainWindow(string root)
    {
        store = new HarborStore(root); Style = (Style)FindResource(typeof(Window));
        Title = "CraftHarbor — Minecraft Server Control"; Width = 1240; Height = 840; MinWidth = 980; MinHeight = 700; WindowStartupLocation = WindowStartupLocation.CenterScreen;
        var layout = new Grid { Background = Brush("#0D141F") }; layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(240) }); layout.ColumnDefinitions.Add(new ColumnDefinition()); Content = layout;
        var sidebar = new DockPanel { Background = Brush("#111B29"), Margin = new Thickness(0) }; layout.Children.Add(sidebar);
        var brand = new StackPanel { Margin = new Thickness(22, 28, 18, 20) };
        brand.Children.Add(new TextBlock { Text = "◈  CraftHarbor", FontSize = 23, FontWeight = FontWeights.Bold, Foreground = Brush("#61DBC4") });
        brand.Children.Add(new TextBlock { Text = "YOUR WORLDS. YOUR CONTROL.", FontSize = 10, Foreground = Brush("#91A3B8"), Margin = new Thickness(0, 8, 0, 24) });
        brand.Children.Add(Btn("＋ サーバーを追加", AddServer, true)); brand.Children.Add(new TextBlock { Text = "サーバー", Foreground = Brush("#91A3B8"), Margin = new Thickness(0, 12, 0, 8) });
        DockPanel.SetDock(brand, Dock.Top); sidebar.Children.Add(brand);
        var footer = new StackPanel { Margin = new Thickness(20) };
        footer.Children.Add(Btn("Java ランタイム", () => Navigate("java"))); footer.Children.Add(Btn("ネットワーク・システム", () => Navigate("system"))); footer.Children.Add(Btn("ガイド / 保存場所", () => Navigate("help")));
        footer.Children.Add(new TextBlock { Text = "v0.1.0  •  Windows native", FontSize = 11, Foreground = Brush("#91A3B8") });
        DockPanel.SetDock(footer, Dock.Bottom); sidebar.Children.Add(footer); servers.Margin = new Thickness(12, 0, 12, 8); sidebar.Children.Add(servers);
        servers.SelectionChanged += (_, e) =>
        {
            if (busy || restoringSelection) return;
            if (mayLeave != null && !mayLeave())
            {
                restoringSelection = true;
                servers.SelectedItem = e.RemovedItems.Count > 0 ? e.RemovedItems[0] : null;
                restoringSelection = false; return;
            }
            mayLeave = null; Navigate(currentPage);
        };
        var main = new DockPanel { Margin = new Thickness(30, 26, 30, 16) }; Grid.SetColumn(main, 1); layout.Children.Add(main);
        var header = new StackPanel(); header.Children.Add(title); header.Children.Add(subtitle); DockPanel.SetDock(header, Dock.Top); main.Children.Add(header);
        var nav = new WrapPanel();
        foreach (var (label, key) in new[] { ("概要", "overview"), ("コンソール", "console"), ("起動設定", "launch"), ("MOD / パック", "mods"), ("設定ファイル", "files"), ("バックアップ", "backups") }) nav.Children.Add(Btn(label, () => Navigate(key)));
        DockPanel.SetDock(nav, Dock.Top); main.Children.Add(nav);
        var bottom = new DockPanel { Margin = new Thickness(0, 12, 0, 0) };
        var cancel = Btn("処理をキャンセル", () => operation?.Cancel()); DockPanel.SetDock(cancel, Dock.Right); bottom.Children.Add(cancel); bottom.Children.Add(status);
        DockPanel.SetDock(bottom, Dock.Bottom); main.Children.Add(bottom);
        main.Children.Add(new ScrollViewer { Content = page, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled });
        RefreshServers(); Navigate("overview");
        timer.Tick += (_, _) => Tick(); timer.Start(); Closing += OnClosing;
    }
    private void RefreshServers(ServerProfile? select = null)
    {
        var selection = select ?? Selected; servers.ItemsSource = null; servers.ItemsSource = store.Profiles; servers.SelectedItem = selection ?? store.Profiles.FirstOrDefault();
    }
    private Button Btn(string text, Action action, bool accent = false)
    {
        var button = new Button { Content = text }; if (accent) { button.Background = Brush("#61DBC4"); button.Foreground = Brush("#102823"); }
        button.Click += (_, _) => { if (busy && text != "処理をキャンセル") { status.Text = "処理中です。完了を待つかキャンセルしてください。"; return; } try { action(); } catch (Exception ex) { Error(ex); } }; return button;
    }
    private Button AsyncBtn(string text, Func<CancellationToken, Task> action, bool accent = false) => Btn(text, () => _ = Run(action), accent);
    private async Task Run(Func<CancellationToken, Task> action)
    {
        if (busy) return; busy = true; servers.IsEnabled = false; operation = new CancellationTokenSource(); status.Text = "処理中…";
        try { await action(operation.Token); status.Text = "完了"; }
        catch (OperationCanceledException) { status.Text = "キャンセルしました"; }
        catch (Exception ex) { Error(ex); }
        finally { busy = false; servers.IsEnabled = true; operation.Dispose(); operation = null; }
    }
    private void Error(Exception ex) { status.Text = "エラー: " + ex.Message; MessageBox.Show(this, ex.Message, "CraftHarbor", MessageBoxButton.OK, MessageBoxImage.Warning); }
    private static void Open(string path) => Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
    private static TextBlock Text(string value, int size = 14) => new() { Text = value, FontSize = size, Margin = new Thickness(0, 0, 0, 12) };
    private TextBox Field(Panel parent, string label, string value, bool multiline = false)
    {
        parent.Children.Add(new TextBlock { Text = label, Foreground = Brush("#AAB9CA") });
        var box = new TextBox { Text = value, AcceptsReturn = multiline, MinHeight = multiline ? 88 : 0, VerticalScrollBarVisibility = multiline ? ScrollBarVisibility.Auto : ScrollBarVisibility.Hidden }; parent.Children.Add(box); return box;
    }
    private static StackPanel Card(Panel parent, string heading)
    {
        var panel = new StackPanel(); panel.Children.Add(Text(heading, 18));
        parent.Children.Add(new Border { Background = Brush("#192330"), CornerRadius = new CornerRadius(10), Padding = new Thickness(20), Margin = new Thickness(0, 12, 0, 0), Child = panel }); return panel;
    }
    private IProgress<string> Progress() => new Progress<string>(s => status.Text = s);
    private void Stopped(ServerProfile p) { if (Runtime(p).Running || Runtime(p).Busy) throw new InvalidOperationException("この操作はサーバー停止中に行ってください。"); }
    private bool Confirm(string message) => MessageBox.Show(this, message, "CraftHarbor", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;
    private void Navigate(string key)
    {
        if (busy || (mayLeave != null && !mayLeave())) return; mayLeave = null; currentPage = key; page.Children.Clear(); console = null;
        title.Text = key switch { "java" => "Java ランタイム", "system" => "ネットワーク・システム", "help" => "CraftHarbor ガイド", _ => Selected?.Name ?? "サーバーのための、小さな港。" };
        subtitle.Text = Selected is { } p ? $"{p.Engine.ToUpperInvariant()}  /  Minecraft {p.Version}  /  localhost:{p.Port}" : "サーバー、Java、MOD、ワールドをひとつの場所に。";
        if (key == "java") { JavaPage(); return; } if (key == "system") { SystemPage(); return; } if (key == "help") { HelpPage(); return; }
        if (Selected == null) { Welcome(); return; }
        switch (key) { case "console": ConsolePage(); break; case "launch": LaunchPage(); break; case "mods": ModsPage(); break; case "files": FilesPage(); break; case "backups": BackupsPage(); break; default: Overview(); break; }
    }
    private void Welcome()
    {
        var card = Card(page, "最初のワールドを迎えましょう");
        card.Children.Add(Text("1  サーバーを追加して種類とバージョンを選択\n2  Javaを選び、サーバー本体をダウンロード\n3  EULAを確認して起動")); card.Children.Add(Btn("＋ 最初のサーバーを追加", AddServer, true));
        var features = Card(page, "軽く動いて、しっかり管理");
        features.Children.Add(Text("リアルタイムコンソール  •  複数サーバー  •  Java 8 / 11 / 17 / 21 / 25\nMOD・プラグインの切替  •  構成プリセット  •  Modrinth検索 / mrpack\n設定ファイル編集  •  ZIPバックアップ / 復元  •  TCP疎通確認"));
        features.Children.Add(Text("すでに別のアプリで動いているサーバーは操作しません。既存サーバーの取り込みは、元サーバーを停止できる時にコピーで行います。"));
    }
    private void AddServer()
    {
        var name = Ask("サーバー名", "My Minecraft Server"); if (string.IsNullOrWhiteSpace(name)) return;
        var p = store.Add(name); RefreshServers(p); Navigate("launch");
    }
    private string? Ask(string label, string initial)
    {
        var dialog = new Window { Owner = this, Title = label, Width = 520, Height = 210, WindowStartupLocation = WindowStartupLocation.CenterOwner, ResizeMode = ResizeMode.NoResize };
        var body = new StackPanel { Margin = new Thickness(24) }; var box = Field(body, label, initial);
        var ok = new Button { Content = "決定", IsDefault = true }; ok.Click += (_, _) => dialog.DialogResult = true; body.Children.Add(ok); dialog.Content = body; box.Focus(); return dialog.ShowDialog() == true ? box.Text.Trim() : null;
    }
    private void Overview()
    {
        var p = Selected!; var runtime = Runtime(p);
        var card = Card(page, "サーバーコントロール");
        var row = new WrapPanel(); row.Children.Add(Btn("▶ 起動", () => Start(p), true));
        row.Children.Add(AsyncBtn("■ 安全に停止", async _ => await runtime.StopAsync()));
        row.Children.Add(AsyncBtn("↻ 再起動", async _ => { await runtime.StopAsync(); Start(p); }));
        row.Children.Add(Btn("フォルダを開く", () => Open(store.ServerDir(p)))); card.Children.Add(row); if (metrics.Parent is Panel previous) previous.Children.Remove(metrics); card.Children.Add(metrics);
        var info = Card(page, "このサーバーの構成"); info.Children.Add(Text($"種類: {p.Engine}   Minecraft: {p.Version}\nメモリ: {p.MinMemoryMb} – {p.MaxMemoryMb} MB\nJava: {p.JavaPath}\n起動: {(p.LaunchArgs.Length == 0 ? p.Jar : string.Join(" ", p.LaunchArgs))}\n保存先: {store.ServerDir(p)}"));
        info.Children.Add(Text("起動前にJavaとMODの対応バージョンを確認してください。Java要件はサーバーのダウンロード後に表示します。"));
        var tools = Card(page, "クイック操作");
        var actions = new WrapPanel(); actions.Children.Add(AsyncBtn("停止中バックアップ", async ct => { Stopped(p); var path = await Task.Run(() => SafeFiles.Snapshot(store.ServerDir(p), store.BackupDir(p)), ct); MessageBox.Show(this, path, "バックアップ保存"); }));
        actions.Children.Add(Btn("コンソールへ", () => Navigate("console"))); actions.Children.Add(Btn("MOD構成へ", () => Navigate("mods"))); tools.Children.Add(actions);
    }
    private void Start(ServerProfile p)
    {
        Runtime(p).Start(p, store.ServerDir(p), Path.Combine(store.Root, "logs", p.Id)); status.Text = "サーバーを起動しました。コンソールで準備状況を確認できます。";
    }
    private void ConsolePage()
    {
        var p = Selected!; var runtime = Runtime(p);
        var row = new WrapPanel(); row.Children.Add(Btn("▶ 起動", () => Start(p), true)); row.Children.Add(AsyncBtn("■ 安全に停止", async _ => await runtime.StopAsync()));
        row.Children.Add(Btn("強制終了", () => { if (Confirm("このアプリから起動した選択サーバーを強制終了します。ワールドが破損する場合があります。実行しますか？")) runtime.Kill(); }));
        row.Children.Add(Btn("ログを開く", () => { var dir = Path.Combine(store.Root, "logs", p.Id); Directory.CreateDirectory(dir); Open(dir); })); page.Children.Add(row);
        console = new TextBox { Text = string.Join(Environment.NewLine, runtime.History), IsReadOnly = true, AcceptsReturn = true, FontFamily = new FontFamily("Cascadia Mono, Consolas"), FontSize = 12, Height = 350, TextWrapping = TextWrapping.NoWrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, Background = Brush("#080E17") }; page.Children.Add(console);
        var command = Field(page, "サーバーコマンド（先頭の / は不要）", "");
        async Task Send(CancellationToken _) { await runtime.SendAsync(command.Text); command.Clear(); }
        command.KeyDown += (_, e) => { if (e.Key == Key.Enter && !busy) { e.Handled = true; _ = Run(Send); } };
        var controls = new WrapPanel(); controls.Children.Add(AsyncBtn("送信 ↵", Send, true));
        foreach (var cmd in new[] { "list", "save-all", "whitelist list" }) controls.Children.Add(AsyncBtn(cmd, async _ => await runtime.SendAsync(cmd)));
        page.Children.Add(controls); page.Children.Add(Text("画面は最大2,000行。完全な出力は logs に保存します。停止操作はCraftHarborが起動したプロセスだけが対象です。", 12));
    }
    private void Tick()
    {
        foreach (var (id, runtime) in runtimes)
        {
            var fresh = runtime.Drain();
            if (console != null && Selected?.Id == id && fresh.Count > 0)
            {
                var follow = console.VerticalOffset >= console.ExtentHeight - console.ViewportHeight - 30;
                console.AppendText(string.Join(Environment.NewLine, fresh) + Environment.NewLine);
                if (console.LineCount > 2200 || console.Text.Length > 1000000) console.Text = string.Join(Environment.NewLine, runtime.History);
                if (follow) console.ScrollToEnd();
            }
        }
        if (Selected is { } p)
        {
            var r = Runtime(p); metrics.Text = $"{(r.Running ? r.Ready ? "● 稼働中" : "● 起動中 / ログを確認" : "○ 停止中")}   PID {r.Pid?.ToString() ?? "—"}   RAM {r.WorkingSet / 1048576d:F0} MB";
        }
    }
    private void LaunchPage()
    {
        var p = Selected!; var card = Card(page, "起動プロファイル");
        var name = Field(card, "名前", p.Name);
        card.Children.Add(Text("種類（Forge / NeoForge等は、導入済みフォルダのコピー＋カスタム引数で起動）", 12));
        var engine = new ComboBox { ItemsSource = new[] { "vanilla", "paper", "fabric", "folia", "forge", "neoforge", "quilt", "custom" }, SelectedItem = p.Engine }; card.Children.Add(engine);
        var version = Field(card, "Minecraft バージョン", p.Version);
        var loaderVersion = Field(card, "Fabricローダーバージョン（空欄で最新安定版・パック指定版も入力可能）", p.LoaderVersion);
        card.Children.Add(AsyncBtn("リリース一覧を取得", async ct => { var versions = await downloads.MinecraftVersions(ct); var chosen = Choose("Minecraft バージョン", versions); if (chosen != null) version.Text = chosen; }));
        var java = Field(card, "java.exe のパス（Java画面からインストールできます）", p.JavaPath);
        card.Children.Add(Btn("java.exe を選択", () => { var file = Pick("Java|java.exe"); if (file != null) java.Text = file; }));
        var min = Field(card, "最小メモリ MB", p.MinMemoryMb.ToString()); var max = Field(card, "最大メモリ MB", p.MaxMemoryMb.ToString()); var port = Field(card, "サーバーポート", p.Port.ToString());
        var jar = Field(card, "JAR 相対パス", p.Jar);
        var jvm = Field(card, "追加JVM引数（1行に1引数・引用符は不要）", string.Join(Environment.NewLine, p.JvmArgs), true);
        var args = Field(card, "カスタム起動引数（1行に1引数・空欄なら -jar / nogui）", string.Join(Environment.NewLine, p.LaunchArgs), true);
        card.Children.Add(Text("Forge / NeoForge例: @libraries/net/neoforged/neoforge/…/win_args.txt と nogui を別々の行に指定。シェル / BATは実行しません。", 12));
        var eula = new CheckBox { Content = "Minecraft EULAを読み、このサーバーでの利用に同意する", IsChecked = p.EulaAccepted }; card.Children.Add(eula); card.Children.Add(Btn("Minecraft EULAを開く", () => Open("https://www.minecraft.net/eula")));
        void Save()
        {
            Stopped(p);
            var draft = new ServerProfile { Name = name.Text.Trim(), MinMemoryMb = int.Parse(min.Text), MaxMemoryMb = int.Parse(max.Text), Port = int.Parse(port.Text), JavaPath = java.Text.Trim() }; draft.Validate();
            _ = SafeFiles.Inside(store.ServerDir(p), jar.Text.Trim());
            p.Name = draft.Name; p.MinMemoryMb = draft.MinMemoryMb; p.MaxMemoryMb = draft.MaxMemoryMb; p.Port = draft.Port; p.JavaPath = draft.JavaPath;
            p.Engine = engine.SelectedItem?.ToString() ?? "custom"; p.Version = version.Text.Trim(); p.LoaderVersion = loaderVersion.Text.Trim(); p.Jar = jar.Text.Trim(); p.JvmArgs = Lines(jvm.Text); p.LaunchArgs = Lines(args.Text); p.EulaAccepted = eula.IsChecked == true; store.Save(); status.Text = "起動設定を保存しました";
        }
        var row = new WrapPanel(); row.Children.Add(Btn("設定を保存", () => { Save(); RefreshServers(p); }, true));
        row.Children.Add(AsyncBtn("保存してサーバー本体を導入", async ct =>
        {
            Save();
            if (File.Exists(Path.Combine(store.ServerDir(p), "server.jar")))
            {
                if (!Confirm("server.jarを更新します。先にサーバー全体のバックアップを作成します。続行しますか？")) return;
                await Task.Run(() => SafeFiles.Snapshot(store.ServerDir(p), store.BackupDir(p)), ct);
            }
            var required = await downloads.InstallServer(p, store.ServerDir(p), Progress(), ct); store.Save();
            MessageBox.Show(this, $"導入完了。Minecraftのメタデータ上のJava要件: {required}\nPaper等は独自の要件も確認してください。Java画面で使用Javaを選択できます。", "サーバーを導入しました");
        })); card.Children.Add(row);
        var imports = Card(page, "既存サーバーをコピーして取り込む");
        imports.Children.Add(Text("元フォルダは変更しません。稼働中のワールドはコピーしないでください。取り込み先は空の新規サーバーのみです。"));
        imports.Children.Add(AsyncBtn("停止済みフォルダを選んでコピー", async ct =>
        {
            Stopped(p); var dialog = new OpenFolderDialog { Title = "停止済みサーバーフォルダ" }; if (dialog.ShowDialog(this) != true) return;
            var source = Path.GetFullPath(dialog.FolderName); var target = store.ServerDir(p);
            if (target.StartsWith(source.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) || source.Equals(target, StringComparison.OrdinalIgnoreCase)) throw new IOException("取り込み元と先が重なっています。");
            if (SafeFiles.Files(target).Any()) throw new IOException("空の新規サーバーを使用してください。");
            if (!Confirm("元サーバーが停止していることを確認しましたか？稼働中の場合は「いいえ」を選んでください。")) return;
            var stage = target + ".import-" + Guid.NewGuid().ToString("N");
            try { await Task.Run(() => SafeFiles.CopyTree(source, stage), ct); ct.ThrowIfCancellationRequested(); Directory.Delete(target); Directory.Move(stage, target); }
            finally { if (Directory.Exists(stage)) Directory.Delete(stage, true); }
            MessageBox.Show(this, "コピーしました。JARパスまたはカスタム起動引数とJavaを設定してください。");
        }));
    }
    private static string[] Lines(string value) => value.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    private string? Pick(string filter) { var dialog = new OpenFileDialog { Filter = filter }; return dialog.ShowDialog(this) == true ? dialog.FileName : null; }
    private string? Choose(string heading, IEnumerable<string> values)
    {
        var dialog = new Window { Owner = this, Title = heading, Width = 620, Height = 480, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var body = new DockPanel { Margin = new Thickness(20) }; var list = new ListBox { ItemsSource = values.ToArray() }; var button = new Button { Content = "選択" }; button.Click += (_, _) => { if (list.SelectedItem != null) dialog.DialogResult = true; }; DockPanel.SetDock(button, Dock.Bottom); body.Children.Add(button); body.Children.Add(list); dialog.Content = body; return dialog.ShowDialog() == true ? list.SelectedItem?.ToString() : null;
    }
    private void JavaPage()
    {
        var card = Card(page, "サーバーごとにJavaを使い分ける"); card.Children.Add(Text("Temurin JREを専用フォルダへ導入します。システムのJava / PATHは変更しません。"));
        var versions = new ComboBox { ItemsSource = new[] { 8, 11, 17, 21, 25 }, SelectedItem = 21 }; card.Children.Add(versions);
        var list = new ListBox { Height = 170 }; void Refresh() { list.ItemsSource = Downloads.DiscoverJava(Path.Combine(store.Root, "java")).ToArray(); } Refresh();
        card.Children.Add(AsyncBtn("選択したJavaをダウンロード", async ct =>
        {
            var path = await downloads.InstallJava((int)versions.SelectedItem, Path.Combine(store.Root, "java"), Progress(), ct); Refresh(); list.SelectedItem = path;
        }, true)); card.Children.Add(list);
        var row = new WrapPanel(); row.Children.Add(Btn("再検出", Refresh)); row.Children.Add(Btn("選択サーバーに割り当て", () =>
        {
            var p = Selected ?? throw new InvalidOperationException("左側でサーバーを選んでください。"); Stopped(p); p.JavaPath = list.SelectedItem?.ToString() ?? throw new IOException("Javaを選択してください。"); store.Save(); status.Text = $"{p.Name} にJavaを割り当てました";
        }));
        row.Children.Add(AsyncBtn("Java バージョン確認", async ct =>
        {
            var path = list.SelectedItem?.ToString() ?? throw new IOException("Javaを選択してください。");
            using var child = new Process { StartInfo = new ProcessStartInfo(path) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true, RedirectStandardOutput = true } }; child.StartInfo.ArgumentList.Add("-version"); child.Start();
            var output = child.StandardOutput.ReadToEndAsync(ct); var error = child.StandardError.ReadToEndAsync(ct);
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct); deadline.CancelAfter(TimeSpan.FromSeconds(15));
            try { await child.WaitForExitAsync(deadline.Token); } catch { child.Kill(true); throw; }
            MessageBox.Show(this, await output + await error, "Java");
        })); card.Children.Add(row);
    }
    private void ModsPage()
    {
        var p = Selected!; var root = store.ServerDir(p);
        var local = Card(page, "MOD / プラグイン"); var folder = new ComboBox { ItemsSource = new[] { "mods", "plugins" }, SelectedIndex = 0 }; local.Children.Add(folder);
        var list = new ListBox { Height = 150 }; local.Children.Add(list);
        string Target() => Path.Combine(root, folder.SelectedItem.ToString()!);
        void Refresh() { Directory.CreateDirectory(Target()); list.ItemsSource = Directory.EnumerateFiles(Target()).Where(f => f.EndsWith(".jar", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".jar.disabled", StringComparison.OrdinalIgnoreCase)).Select(Path.GetFileName).Order().ToArray(); }
        folder.SelectionChanged += (_, _) => Refresh(); Refresh();
        var row = new WrapPanel(); row.Children.Add(Btn("JARを追加", () => { Stopped(p); var dialog = new OpenFileDialog { Filter = "MOD / Plugin|*.jar", Multiselect = true }; if (dialog.ShowDialog(this) != true) return; foreach (var file in dialog.FileNames) File.Copy(file, SafeFiles.Inside(Target(), Path.GetFileName(file)), false); Refresh(); }));
        row.Children.Add(Btn("有効 / 無効", () => { Stopped(p); var name = list.SelectedItem?.ToString() ?? throw new IOException("ファイルを選択してください。"); var path = SafeFiles.Inside(Target(), name); File.Move(path, name.EndsWith(".disabled") ? path[..^9] : path + ".disabled"); Refresh(); }));
        row.Children.Add(Btn("フォルダ", () => Open(Target()))); local.Children.Add(row);
        var preset = Card(page, "構成プリセット"); preset.Children.Add(Text("mods・plugins・configをひとまとまりで保存。切り替え前のサーバー全体もバックアップします。"));
        var presetRow = new WrapPanel(); presetRow.Children.Add(AsyncBtn("現在の構成を保存", async ct =>
        {
            Stopped(p); var name = Ask("プリセット名（英数字・日本語可）", "構成-" + DateTime.Now.ToString("MMdd-HHmm")); if (name == null) return;
            var dest = SafeFiles.Inside(store.PresetDir(p), name + ".zip"); if (File.Exists(dest)) throw new IOException("同名プリセットがあります。");
            var stage = Path.Combine(store.Root, "staging", Guid.NewGuid().ToString("N"));
            try { await Task.Run(() => { foreach (var dir in new[] { "mods", "plugins", "config" }) SafeFiles.CopyTree(Path.Combine(root, dir), Path.Combine(stage, dir)); var zip = SafeFiles.Snapshot(stage, store.PresetDir(p)); File.Move(zip, dest); }, ct); }
            finally { if (Directory.Exists(stage)) Directory.Delete(stage, true); }
        }));
        presetRow.Children.Add(AsyncBtn("プリセットへ切り替え", async ct =>
        {
            Stopped(p); Directory.CreateDirectory(store.PresetDir(p)); var chosen = Choose("プリセット", Directory.EnumerateFiles(store.PresetDir(p), "*.zip").Select(f => Path.GetFileName(f)!)); if (chosen == null) return;
            if (!Confirm("mods / plugins / configを選択構成に置き換えます。現在の全体バックアップを作って続行しますか？")) return;
            await Task.Run(() =>
            {
                var backup = SafeFiles.Snapshot(root, store.BackupDir(p)); var stage = root + ".preset-" + Guid.NewGuid().ToString("N");
                try
                {
                    SafeFiles.ExtractZip(SafeFiles.Inside(store.PresetDir(p), chosen), stage);
                    foreach (var entry in Directory.EnumerateFileSystemEntries(stage)) if (!new[] { "mods", "plugins", "config" }.Contains(Path.GetFileName(entry))) throw new IOException("プリセットの内容が不正です。");
                    try { foreach (var dir in new[] { "mods", "plugins", "config" }) { var target = SafeFiles.Inside(root, dir); if (Directory.Exists(target)) Directory.Delete(target, true); if (Directory.Exists(Path.Combine(stage, dir))) Directory.Move(Path.Combine(stage, dir), target); } }
                    catch { SafeFiles.Restore(backup, root); throw; }
                }
                finally { if (Directory.Exists(stage)) Directory.Delete(stage, true); }
            }, ct); Refresh();
        })); preset.Children.Add(presetRow);
        var search = Card(page, "Modrinthから検索・導入"); var query = Field(search, "MOD名（選択サーバーのMCバージョン・ローダーで検索）", ""); var hits = new ListBox { Height = 150 }; List<JsonNode> results = [];
        search.Children.Add(AsyncBtn("検索", async ct => { var nodes = await downloads.SearchMods(query.Text, p.Version, p.Engine, ct); results = nodes.Select(n => n!).ToList(); hits.ItemsSource = results.Select(n => n["title"]!.ToString() + "  —  " + n["description"]!.ToString()).ToArray(); })); search.Children.Add(hits);
        search.Children.Add(AsyncBtn("選択MODと必須依存を導入", async ct =>
        {
            Stopped(p); if (hits.SelectedIndex < 0) throw new IOException("検索結果を選択してください。");
            var releases = await downloads.ResolveMods(results[hits.SelectedIndex]["project_id"]!.ToString(), p.Version, p.Engine, ct);
            if (!Confirm("導入予定:\n" + string.Join("\n", releases.Select(n => n["name"]!.ToString())) + "\n\n既存MODとの互換性は作者の説明も確認してください。導入しますか？")) return;
            await downloads.InstallMods(releases, Path.Combine(root, "mods"), Progress(), ct); Refresh();
        }, true));
        var packs = Card(page, "Modrinth MODパック (.mrpack)"); packs.Children.Add(Text("空の新規サーバーへサーバー必須ファイルを取り込みます。クライアント専用・任意ファイルは除外。ローダー本体は別途導入します。"));
        packs.Children.Add(AsyncBtn("mrpackを取り込む", async ct =>
        {
            Stopped(p); var file = Pick("Modrinth pack|*.mrpack"); if (file == null) return;
            var deps = await downloads.ImportMrpack(file, root, Progress(), ct);
            var dependencies = JsonNode.Parse(deps)!; p.Version = dependencies["minecraft"]?.ToString() ?? p.Version;
            p.Engine = dependencies["fabric-loader"] != null ? "fabric" : dependencies["neoforge"] != null ? "neoforge" : dependencies["forge"] != null ? "forge" : dependencies["quilt-loader"] != null ? "quilt" : "custom"; store.Save();
            p.LoaderVersion = dependencies["fabric-loader"]?.ToString() ?? ""; store.Save();
            MessageBox.Show(this, "取り込み完了。必要ローダーのバージョン:\n" + deps + "\n起動設定からローダー本体を導入してください。Fabricはパック指定版を保存しました。", "MODパック"); Refresh();
        }));
    }
    private void FilesPage()
    {
        var p = Selected!; var root = store.ServerDir(p); var card = Card(page, "設定ファイルを編集"); card.Children.Add(Text("停止中に保存できます。保存前のファイルは履歴に退避します。server-portは起動設定のポートが優先されます。"));
        var candidates = Directory.EnumerateFiles(root).Concat(SafeFiles.Files(Path.Combine(root, "config"))).Concat(SafeFiles.Files(Path.Combine(root, "plugins")));
        var paths = candidates.Where(f => new[] { ".properties", ".json", ".toml", ".yml", ".yaml", ".txt", ".conf" }.Contains(Path.GetExtension(f).ToLowerInvariant()) && new FileInfo(f).Length < 1024 * 1024).Take(500).Select(f => Path.GetRelativePath(root, f)).ToList(); if (!paths.Contains("server.properties")) paths.Insert(0, "server.properties");
        var list = new ComboBox { ItemsSource = paths, SelectedIndex = 0 }; card.Children.Add(list);
        var editor = new TextBox { AcceptsReturn = true, AcceptsTab = true, Height = 340, FontFamily = new FontFamily("Consolas"), FontSize = 13, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto };
        string current = ""; bool dirty = false; bool loading = false;
        void Load() { loading = true; current = list.SelectedItem?.ToString() ?? "server.properties"; var path = SafeFiles.Inside(root, current); editor.Text = File.Exists(path) ? File.ReadAllText(path) : "online-mode=true\nserver-port=" + p.Port; dirty = false; loading = false; }
        list.SelectionChanged += (_, _) => { if (loading) return; if (dirty && !Confirm("未保存の編集を破棄して切り替えますか？")) { loading = true; list.SelectedItem = current; loading = false; return; } Load(); };
        editor.TextChanged += (_, _) => { if (!loading) dirty = true; }; Load(); card.Children.Add(editor);
        mayLeave = () => !dirty || Confirm("設定ファイルの未保存の編集を破棄して移動しますか？");
        card.Children.Add(Btn("ファイルを保存", () =>
        {
            Stopped(p); var path = SafeFiles.Inside(root, current);
            if (current.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) _ = System.Text.Json.JsonDocument.Parse(editor.Text);
            if (File.Exists(path)) { var archive = Path.Combine(store.Root, "file-history", p.Id, DateTime.Now.ToString("yyyyMMddHHmmss") + "-" + Guid.NewGuid().ToString("N"), current); Directory.CreateDirectory(Path.GetDirectoryName(archive)!); File.Copy(path, archive); }
            SafeFiles.AtomicWrite(path, editor.Text); dirty = false; status.Text = "保存しました";
        }, true));
    }
    private void BackupsPage()
    {
        var p = Selected!; var card = Card(page, "ワールドを、戻せる安心と一緒に"); card.Children.Add(Text("サーバー全体をZIPで保存します。整合性のため停止中のみ実行できます。復元前のフォルダは .previous-* として残ります。"));
        Directory.CreateDirectory(store.BackupDir(p)); var list = new ListBox { Height = 240 }; void Refresh() => list.ItemsSource = Directory.EnumerateFiles(store.BackupDir(p), "*.zip").OrderDescending().Select(f => Path.GetFileName(f)!).ToArray(); Refresh(); card.Children.Add(list);
        var row = new WrapPanel(); row.Children.Add(AsyncBtn("バックアップを作成", async ct => { Stopped(p); await Task.Run(() => SafeFiles.Snapshot(store.ServerDir(p), store.BackupDir(p)), ct); Refresh(); }, true));
        row.Children.Add(AsyncBtn("選択したバックアップを復元", async ct =>
        {
            Stopped(p); var file = list.SelectedItem?.ToString() ?? throw new IOException("バックアップを選択してください。"); if (!Confirm("選択したバックアップにサーバー全体を戻します。続行しますか？")) return;
            var old = await Task.Run(() => SafeFiles.Restore(SafeFiles.Inside(store.BackupDir(p), file), store.ServerDir(p)), ct); MessageBox.Show(this, "復元完了。直前のデータ:\n" + old + "\n起動プロファイルは変更されないため、Java・バージョン設定も確認してください。");
        })); row.Children.Add(Btn("保存先を開く", () => Open(store.BackupDir(p)))); card.Children.Add(row);
    }
    private void SystemPage()
    {
        var card = Card(page, "このPCの状態"); var info = new TextBox { Text = Diagnostics.Describe(), IsReadOnly = true, AcceptsReturn = true, Height = 260, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }; card.Children.Add(info); card.Children.Add(Btn("更新", () => info.Text = Diagnostics.Describe()));
        var network = Card(page, "TCP疎通チェック"); var host = Field(network, "ホスト名 / IP", "127.0.0.1"); var port = Field(network, "ポート", (Selected?.Port ?? 25565).ToString()); var result = Text(""); network.Children.Add(AsyncBtn("接続をテスト", async _ => { var number = int.Parse(port.Text); if (number is < 1 or > 65535) throw new IOException("ポートが不正です。"); result.Text = await Diagnostics.Probe(host.Text.Trim(), number); })); network.Children.Add(result);
        network.Children.Add(Btn("Windows Firewall設定を開く", () => Open("windowsdefender://network/")));
    }
    private void HelpPage()
    {
        var card = Card(page, "はじめに"); card.Children.Add(Text("サーバー追加 → 起動設定 → 本体導入 → Javaの導入・割り当て → EULA同意 → 起動。\nコンソールに Done が出たら接続できます。サーバーはアプリ終了前に停止してください。\n複数サーバーは異なるポートで起動してください。"));
        card.Children.Add(Text("Forge / NeoForge / Quilt / 独自JARは、事前導入したサーバーフォルダをコピーし、Javaと起動引数を設定します。CurseForge形式の自動解決、Bedrock専用サーバー、UPnP、自動スケジュール、遠隔操作は本版の対応外です。"));
        card.Children.Add(Text("MODは実行コードです。作者と対応環境を確認して導入してください。Modrinthの必須依存は解決しますが、既存MODとの全互換性は保証しません。\nバックアップ・ログの自動削除はしません。空き容量はシステム画面で確認できます。"));
        card.Children.Add(Btn("日本語ドキュメント（GitHub）", () => Open("https://github.com/ryuya0124/CraftHarbor/tree/main/docs")));
        card.Children.Add(Btn("データフォルダ", () => Open(store.Root))); card.Children.Add(Text("保存先: " + store.Root, 12));
    }
    private void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (busy || runtimes.Values.Any(r => r.Busy || r.Running)) { e.Cancel = true; MessageBox.Show(this, "処理の完了と、CraftHarborから起動したサーバーの停止を確認してから閉じてください。", "CraftHarbor"); return; }
        if (mayLeave != null && !mayLeave()) { e.Cancel = true; return; }
        timer.Stop(); foreach (var runtime in runtimes.Values) runtime.Dispose(); downloads.Dispose();
    }
}
