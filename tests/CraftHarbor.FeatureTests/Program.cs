using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using CraftHarbor.Core;
using CraftHarbor.Desktop;

internal static class Program
{
    static readonly BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    static readonly List<object> Results = [];
    static string root = "";
    static T Field<T>(object instance, string name) => (T)instance.GetType().GetField(name, Private)!.GetValue(instance)!;
    static IEnumerable<DependencyObject> Tree(DependencyObject item)
    {
        yield return item;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(item); i++) foreach (var child in Tree(VisualTreeHelper.GetChild(item, i))) yield return child;
    }
    static void Check(bool ok, string message) { if (!ok) throw new IOException(message); }
    static void Record(string engine, string step, object? data = null)
    {
        Results.Add(new { time = DateTimeOffset.UtcNow, engine, step, data });
        Console.WriteLine(JsonSerializer.Serialize(Results[^1]));
        SafeFiles.AtomicWrite(Path.Combine(root, "results.json"), JsonSerializer.Serialize(Results, HarborStore.Json));
    }
    [STAThread]
    static int Main(string[] args)
    {
        if (args.Length < 5 || args[4] != "--accept-eula") { Console.WriteLine("FeatureTests <new-root> <previous-test-work-parent> <client-script> <node_modules> --accept-eula [engines]"); return 2; }
        root = Path.GetFullPath(args[0]);
        if (Directory.Exists(root) && Directory.EnumerateFileSystemEntries(root).Any()) throw new IOException("Use a NEW empty test root");
        Directory.CreateDirectory(root);
        var app = new App(); app.InitializeComponent(); // Never App.Run/OnStartup or the user's data directory.
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
        var frame = new DispatcherFrame();
        var task = Run(args);
        _ = task.ContinueWith(_ => app.Dispatcher.BeginInvoke(() => frame.Continue = false));
        Dispatcher.PushFrame(frame);
        try { task.GetAwaiter().GetResult(); return 0; } catch (Exception ex) { Record("all", "FAIL", ex.ToString()); return 1; }
    }
    static async Task Run(string[] args)
    {
        var original = Process.GetProcessesByName("java").Concat(Process.GetProcessesByName("javaw")).ToArray();
        Record("all", "baseline", original.Select(p => p.Id).ToArray());
        using var downloads = new Downloads();
        using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(25)); var ct = deadline.Token;
        var progress = new Progress<string>();
        var previous = Path.GetFullPath(args[1]);
        var java = Directory.GetFiles(Path.Combine(previous, "real-servers-run2", "java"), "java.exe", SearchOption.AllDirectories).Single();
        try
        {
            foreach (var engine in (args.Length > 5 ? args[5].Split(',') : new[] { "fabric", "forge", "neoforge", "paper", "adrenaline" }))
            {
                Check(new[] { "fabric", "forge", "neoforge", "paper", "adrenaline" }.Contains(engine), "Invalid test engine");
                var store = new HarborStore(Path.Combine(root, engine)); var p = store.Add("Isolated feature " + engine);
                var dir = store.ServerDir(p); p.Engine = engine == "adrenaline" ? "fabric" : engine; p.JavaPath = java; p.EulaAccepted = true;
                p.MinMemoryMb = 512; p.MaxMemoryMb = 1536; p.JvmArgs = ["-XX:ActiveProcessorCount=2"];
                var socket = new TcpListener(IPAddress.Loopback, 0); socket.Start(); p.Port = ((IPEndPoint)socket.LocalEndpoint).Port; socket.Stop();
                if (engine == "adrenaline")
                {
                    var release = await downloads.Json("https://api.modrinth.com/v2/version/FCPQcVOt", ct);
                    var file = release["files"]![0]!; var archive = Path.Combine(root, "adrenaline.mrpack");
                    await downloads.FileAsync(file["url"]!.ToString(), archive, file["hashes"]!["sha512"]!.ToString(), "SHA512", progress, ct);
                    Directory.CreateDirectory(Path.Combine(dir, "mods")); // The MOD screen creates this empty directory.
                    var deps = JsonNode.Parse(await downloads.ImportMrpack(archive, dir, progress, ct))!;
                    p.LoaderVersion = deps["fabric-loader"]!.ToString(); p.Version = deps["minecraft"]!.ToString();
                    await downloads.InstallServer(p, dir, progress, ct);
                    Record(engine, "public-mrpack-imported", new { version = release["version_number"]!.ToString(), dependencies = deps, jars = Directory.GetFiles(Path.Combine(dir, "mods"), "*.jar").Select(Path.GetFileName).ToArray() });
                }
                else
                {
                    var run = engine is "forge" or "neoforge" ? "real-servers-run3" : "real-servers-run2";
                    await Task.Run(() => SafeFiles.CopyTree(Path.Combine(previous, run, "servers", engine), dir), ct);
                    if (engine is "forge" or "neoforge")
                    {
                        var argFile = SafeFiles.Files(dir).Single(x => Path.GetFileName(x) == "win_args.txt");
                        p.LaunchArgs = ["@user_jvm_args.txt", "@" + Path.GetRelativePath(dir, argFile).Replace('\\', '/'), "nogui"];
                    }
                }
                store.Save();
                // Isolated fixtures only; external servers and their folders are never candidates.
                SafeFiles.AtomicWrite(Path.Combine(dir, "server.properties"), $"server-ip=127.0.0.1\nserver-port={p.Port}\nonline-mode=false\nenforce-secure-profile=false\nlevel-type=minecraft:flat\nview-distance=2\nsimulation-distance=2\nenable-rcon=false\nenable-query=false\nspawn-protection=0\n");
                var window = new MainWindow(store.Root); // In-process controls; no visible window or focus change.
                var content = (FrameworkElement)window.Content;
                var actualProfile = Field<HarborStore>(window, "store").Profiles.Single();
                var runtime = (ServerRuntime)typeof(MainWindow).GetMethod("Runtime", Private)!.Invoke(window, [actualProfile])!;
                var files = new ServerFiles(store, p, runtime);
                void Layout() { content.Measure(new Size(1240, 840)); content.Arrange(new Rect(0, 0, 1240, 840)); content.UpdateLayout(); }
                void Navigate(string page) { typeof(MainWindow).GetMethod("Navigate", Private)!.Invoke(window, [page]); Layout(); }
                async Task Until(Func<bool> test, string label, int seconds = 240)
                {
                    var end = DateTime.UtcNow.AddSeconds(seconds);
                    while (!test()) { ct.ThrowIfCancellationRequested(); Check(DateTime.UtcNow < end, label + " timed out: " + string.Join("\n", runtime.History.TakeLast(12))); await Task.Delay(100, ct); }
                }
                async Task Click(string label)
                {
                    Tree(content).OfType<Button>().Single(b => b.Content?.ToString() == label).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    await Until(() => !Field<bool>(window, "busy"), label); Check(!Field<TextBlock>(window, "status").Text.StartsWith("エラー"), label + " failed");
                }
                async Task Command(string command, string expected)
                {
                    Navigate("console"); var prior = runtime.History.LastOrDefault();
                    Tree(content).OfType<TextBox>().Single(b => !b.IsReadOnly).Text = command; await Click("送信 ↵");
                    await Until(() => runtime.History.SkipWhile(s => s != prior).Skip(1).Any(s => s.Contains(expected, StringComparison.OrdinalIgnoreCase)), command, 30);
                    await Until(() => Field<TextBox>(window, "console").Text.Contains(expected, StringComparison.OrdinalIgnoreCase), "UI console display: " + command, 10);
                }
                async Task Edit(string relative, string text)
                {
                    relative = relative.Replace('/', Path.DirectorySeparatorChar);
                    Navigate("files"); Tree(content).OfType<ComboBox>().Single().SelectedItem = relative;
                    Check(Tree(content).OfType<ComboBox>().Single().SelectedItem?.ToString() == relative, "Setting not in editor list: " + relative);
                    Tree(content).OfType<TextBox>().Single().Text = text; await Click("ファイルを保存");
                    Check(File.ReadAllText(Path.Combine(dir, relative)).Replace("\r\n", "\n") == text.Replace("\r\n", "\n"), "UI file save mismatch: " + relative);
                    Navigate("files"); Tree(content).OfType<ComboBox>().Single().SelectedItem = relative;
                    Check(Tree(content).OfType<TextBox>().Single().Text.Replace("\r\n", "\n") == text.Replace("\r\n", "\n"), "UI file reopen mismatch: " + relative);
                }
                async Task Boot(string phase, bool modEnabled)
                {
                    Navigate("overview"); await Click("▶ 起動");
                    using (var child = Process.GetProcessById(runtime.Pid!.Value)) child.PriorityClass = ProcessPriorityClass.BelowNormal;
                    await Until(() => runtime.Ready || (!runtime.Running && !runtime.Busy), "server startup"); Check(runtime.Ready, "Server exited: " + string.Join("\n", runtime.History.TakeLast(25)));
                    Record(engine, phase + "-ready", new { runtime.Pid, p.Port });
                    await Command("say 日本語コマンド確認", "日本語コマンド確認");
                    if (engine == "paper" && phase == "enabled")
                    {
                        await Command("difficulty", "Peaceful");
                        await Command("difficulty hard", "Hard");
                        Record(engine, "existing-world-difficulty-changed-through-console");
                    }
                    if (engine != "adrenaline") await Command(engine == "paper" ? "chunky help" : "spark tps", modEnabled ? engine == "paper" ? "Chunky" : "TPS" : "Unknown");
                    if (engine == "paper" && phase == "preset-restored")
                    {
                        await Command("chunky help", "Chunkyのコマンド");
                        Record(engine, "plugin-config-language-applied-after-restart");
                    }
                    await Command("gamerule spawnRadius 0", "spawnRadius"); await Command("setworldspawn 2 -60 2", "world spawn");
                    // A restored fixture may already contain the marker; clear it first.
                    await runtime.SendAsync("setblock 0 -60 0 minecraft:air"); await Task.Delay(200, ct);
                    await Command("setblock 0 -60 0 minecraft:diamond_block", "Changed the block");
                    var output = Path.Combine(root, engine + "-" + phase + "-client.json");
                    using var client = new Process { StartInfo = new ProcessStartInfo("node") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true } };
                    foreach (var a in new[] { args[2], p.Port.ToString(), "diamond_block", output, JsonSerializer.Serialize(new { gameMode = "adventure", difficulty = "hard", maxPlayers = 3, motd = "設定反映テスト" }) }) client.StartInfo.ArgumentList.Add(a);
                    client.StartInfo.Environment["NODE_PATH"] = args[3]; client.Start(); var error = client.StandardError.ReadToEndAsync();
                    try
                    {
                        while (await client.StandardOutput.ReadLineAsync(ct) is { } line) if (line == "CLIENT_READY") await Command("say CraftHarbor_server_reply", "CraftHarbor_server_reply");
                        await client.WaitForExitAsync(ct); Check(client.ExitCode == 0, "Client: " + await error);
                        Record(engine, phase + "-client-pass", JsonSerializer.Deserialize<JsonElement>(File.ReadAllText(output)));
                    }
                    finally { if (!client.HasExited) { client.Kill(true); await client.WaitForExitAsync(); } }
                }
                async Task Stop() { Navigate("overview"); await Click("■ 安全に停止"); Check(!runtime.Running && runtime.ExitCode == 0, "Clean stop failed"); }
                try
                {
                    var settings = File.ReadAllText(Path.Combine(dir, "server.properties"));
                    foreach (var (key, value) in new[] { ("motd", "設定反映テスト"), ("difficulty", "hard"), ("gamemode", "adventure"), ("force-gamemode", "true"), ("max-players", "3"), ("spawn-monsters", "false") }) settings = SafeFiles.SetProperty(settings, key, value);
                    await Edit("server.properties", settings);
                    Check(SafeFiles.Files(Path.Combine(store.Root, "file-history")).Any(), "No file history");
                    foreach (var extension in new[] { "json", "toml", "yml", "yaml", "txt", "conf" })
                    {
                        var relative = "config/probe." + extension; files.SaveConfiguration(relative, extension == "json" ? "{}" : "# initial");
                        await Edit(relative, extension == "json" ? "{\"名前\":\"保存確認\"}" : "# 日本語設定\nvalue=changed\n");
                    }
                    Record(engine, "UI-settings-save-reopen-history-pass");
                    string? jarName = null; var folder = engine == "paper" ? "plugins" : "mods";
                    if (engine != "adrenaline")
                    {
                        var project = engine == "paper" ? "chunky" : "spark";
                        var hits = await downloads.SearchMods(project, p.Version, p.Engine, ct); Check(hits.Count > 0, "Search empty");
                        var releases = await downloads.ResolveMods(project, p.Version, p.Engine, ct);
                        var fetched = Path.Combine(root, engine + "-downloaded"); await downloads.InstallMods(releases, fetched, progress, ct);
                        files.AddJars(folder, Directory.GetFiles(fetched, "*.jar")); jarName = Path.GetFileName(Directory.GetFiles(fetched, "*.jar").Single());
                        Record(engine, "real-mod-download-add-pass", releases.Select(r => new { id = r["id"]!.ToString(), version = r["version_number"]!.ToString() }).ToArray());
                    }
                    await Boot("enabled", true);
                    foreach (var operation in new Action[] { () => files.SaveConfiguration("server.properties", "bad"), () => files.SavePreset("running"), () => files.ApplyPreset("missing.zip"), () => files.ToggleJar(folder, jarName ?? "probe.jar"), () => files.AddJars(folder, []) })
                    { try { operation(); throw new IOException("Running mutation allowed"); } catch (InvalidOperationException) { } }
                    Record(engine, "running-mutations-refused"); await Stop();
                    if (jarName != null)
                    {
                        if (engine == "paper") await Edit("plugins/Chunky/config.yml", File.ReadAllText(Path.Combine(dir, "plugins/Chunky/config.yml")).Replace("language: en", "language: ja"));
                        var configBefore = File.ReadAllText(Path.Combine(dir, "config/probe.json"));
                        await Task.Run(() => files.SavePreset("enabled"), ct);
                        Navigate("mods"); Tree(content).OfType<ComboBox>().Single().SelectedItem = folder;
                        Tree(content).OfType<ListBox>().Single(b => b.Items.Contains(jarName)).SelectedItem = jarName; await Click("有効 / 無効");
                        Check(File.Exists(Path.Combine(dir, folder, jarName + ".disabled")), "UI disable failed");
                        await Edit("config/probe.json", "{\"changed\":true}"); await Task.Run(() => files.SavePreset("disabled"), ct);
                        await Boot("disabled", false); await Stop();
                        Navigate("mods"); Tree(content).OfType<ComboBox>().Single().SelectedItem = folder;
                        Tree(content).OfType<ListBox>().Single(b => b.Items.Contains(jarName + ".disabled")).SelectedItem = jarName + ".disabled"; await Click("有効 / 無効");
                        Check(File.Exists(Path.Combine(dir, folder, jarName)), "UI re-enable failed");
                        if (engine == "paper") await Edit("plugins/Chunky/config.yml", File.ReadAllText(Path.Combine(dir, "plugins/Chunky/config.yml")).Replace("language: ja", "language: en"));
                        files.SaveConfiguration("config/added-after-preset.toml", "keep = true");
                        files.SaveConfiguration("world/serverconfig/harbor-preserve.toml", "keep = true");
                        await Task.Run(() => files.ApplyPreset("enabled.zip"), ct);
                        Check(File.ReadAllText(Path.Combine(dir, "config/probe.json")).Contains("changed"), "Default preset erased existing settings");
                        var backup = await Task.Run(() => files.ApplyPreset("enabled.zip", false), ct);
                        Check(File.ReadAllText(Path.Combine(dir, "config/added-after-preset.toml")) == "keep = true" && File.Exists(Path.Combine(dir, "world/serverconfig/harbor-preserve.toml")), "Preset erased added settings");
                        Record(engine, "preset-preserves-current-and-additional-settings-pass");
                        Check(File.Exists(backup) && File.Exists(Path.Combine(dir, folder, jarName)) && !File.Exists(Path.Combine(dir, folder, jarName + ".disabled")), "Preset JAR restore failed");
                        Check(File.ReadAllText(Path.Combine(dir, "config/probe.json")) == configBefore, "Preset config restore failed");
                        await Boot("preset-restored", true); await Stop();
                        Record(engine, "preset-config-and-mod-restored-pass", new { backup });
                    }
                    else
                    {
                        await Task.Run(() => files.SavePreset("public-pack"), ct);
                        var jarCount = Directory.GetFiles(Path.Combine(dir, "mods"), "*.jar").Length;
                        await Edit("config/probe.json", "{\"temporary\":true}");
                        await Task.Run(() => files.ApplyPreset("public-pack.zip", false), ct);
                        Check(Directory.GetFiles(Path.Combine(dir, "mods"), "*.jar").Length == jarCount && !File.ReadAllText(Path.Combine(dir, "config/probe.json")).Contains("temporary"), "Public pack preset failed");
                        await Boot("pack-restored", true); await Stop();
                        Record(engine, "public-pack-preset-restart-pass");
                    }
                    Record(engine, "PASS");
                }
                finally
                {
                    if (runtime.Running) { try { await runtime.StopAsync(); } catch { runtime.Kill(); } }
                    while (runtime.Busy) await Task.Delay(100);
                    Field<DispatcherTimer>(window, "timer").Stop(); runtime.Dispose(); Field<Downloads>(window, "downloads").Dispose();
                }
            }
            Record("all", "PASS");
        }
        finally { Record("all", "existing-process-check", original.Select(p => new { p.Id, stillRunning = !p.HasExited }).ToArray()); foreach (var p in original) p.Dispose(); }
    }
}
