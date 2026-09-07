using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using CraftHarbor.Core;

if (args.Length < 4 || !args.Contains("--accept-eula"))
{
    Console.WriteLine("Explicit opt-in real-server test. Usage: RealTests <new-work-directory> <client-script> <node_modules-directory> --accept-eula [comma-separated-engines]");
    Console.WriteLine("Read https://www.minecraft.net/eula before opting in. Never runs in normal CI.");
    return;
}
var root = Path.GetFullPath(args[0]);
var clientScript = Path.GetFullPath(args[1]);
var nodeModules = Path.GetFullPath(args[2]);
if (Directory.Exists(root) && Directory.EnumerateFileSystemEntries(root).Any()) throw new IOException("Use a NEW, empty test directory.");
Directory.CreateDirectory(root);
var before = Process.GetProcessesByName("java").Concat(Process.GetProcessesByName("javaw")).ToArray();
using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(20));
Console.CancelKeyPress += (_, e) => { e.Cancel = true; deadline.Cancel(); };
using var downloads = new Downloads();
var events = new List<object>();
var progress = new Progress<string>();
void Record(string engine, string step, object? details = null)
{
    var entry = new { time = DateTimeOffset.UtcNow, engine, step, details }; events.Add(entry);
    Console.WriteLine(JsonSerializer.Serialize(entry));
    SafeFiles.AtomicWrite(Path.Combine(root, "results.json"), JsonSerializer.Serialize(events, HarborStore.Json));
}
async Task WaitFor(Func<bool> condition, ServerRuntime runtime, string label, int seconds = 120)
{
    var end = DateTime.UtcNow.AddSeconds(seconds);
    while (!condition())
    {
        deadline.Token.ThrowIfCancellationRequested();
        if (!runtime.Running && !runtime.Busy) throw new IOException(label + ": server exited\n" + string.Join("\n", runtime.History.TakeLast(20)));
        if (DateTime.UtcNow > end) throw new TimeoutException(label);
        await Task.Delay(100, deadline.Token);
    }
}
async Task Command(ServerRuntime runtime, string command, string expected)
{
    // Bound the assertion to output emitted after this command, not a prior startup.
    var prior = runtime.History.LastOrDefault();
    await runtime.SendAsync(command);
    await WaitFor(() => runtime.History.SkipWhile(line => line != prior).Skip(prior == null ? 0 : 1).Any(line => line.Contains(expected, StringComparison.OrdinalIgnoreCase)), runtime, "Command did not respond: " + command, 30);
}
async Task VerifyClient(ServerProfile profile, ServerRuntime runtime, string phase)
{
    var outputPath = Path.Combine(root, profile.Engine + "-" + phase + "-client.json");
    using var client = new Process { StartInfo = new ProcessStartInfo("node") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true } };
    foreach (var value in new[] { clientScript, profile.Port.ToString(), "diamond_block", outputPath }) client.StartInfo.ArgumentList.Add(value);
    client.StartInfo.Environment["NODE_PATH"] = nodeModules;
    var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    client.Start();
    var output = new StringBuilder();
    async Task ReadOutput()
    {
        while (await client.StandardOutput.ReadLineAsync() is { } line) { output.AppendLine(line); if (line == "CLIENT_READY") ready.TrySetResult(); }
    }
    var reading = ReadOutput(); var error = client.StandardError.ReadToEndAsync();
    try
    {
        var winner = await Task.WhenAny(ready.Task, client.WaitForExitAsync(deadline.Token), Task.Delay(50000, deadline.Token));
        if (winner != ready.Task) throw new IOException("Client could not spawn: " + (client.HasExited ? await error : "timed out") + output);
        await Command(runtime, "list", "HarborProbe");
        var status = await ServerStatus(profile.Port, deadline.Token);
        if (status.GetProperty("players").GetProperty("online").GetInt32() != 1) throw new IOException("Server status player count mismatch");
        await runtime.SendAsync("say CraftHarbor_server_reply");
        using var clientDeadline = CancellationTokenSource.CreateLinkedTokenSource(deadline.Token); clientDeadline.CancelAfter(TimeSpan.FromSeconds(45));
        await client.WaitForExitAsync(clientDeadline.Token); await reading;
        if (client.ExitCode != 0 || !File.Exists(outputPath)) throw new IOException("Client failed: " + await error + output);
        Record(profile.Engine, phase + "-client-pass", JsonSerializer.Deserialize<JsonElement>(File.ReadAllText(outputPath)));
    }
    finally { if (!client.HasExited) { client.Kill(true); await client.WaitForExitAsync(); } }
}

try
{
    Record("all", "baseline", new { existingJavaProcessIds = before.Select(p => p.Id).ToArray(), cpuLimit = 2, heapMb = 1536, host = "127.0.0.1" });
    var java = await downloads.InstallJava(21, Path.Combine(root, "java"), progress, deadline.Token);
    Record("all", "java-installed", new { path = java });
    var engines = args.Length > 4 ? args[4].Split(',') : new[] { "vanilla", "paper", "fabric", "forge", "neoforge" };
    foreach (var engine in engines)
    {
        if (engine is not ("vanilla" or "paper" or "fabric" or "forge" or "neoforge")) throw new ArgumentException("Unsupported real-test engine");
        var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start(); var port = ((IPEndPoint)listener.LocalEndpoint).Port; listener.Stop();
        var directory = Path.Combine(root, "servers", engine); Directory.CreateDirectory(directory);
        var profile = new ServerProfile { Name = "Isolated " + engine, Engine = engine, Version = "1.21.1", JavaPath = java, Port = port, MinMemoryMb = 512, MaxMemoryMb = 1536, EulaAccepted = true, JvmArgs = ["-XX:ActiveProcessorCount=2"] };
        if (engine is "forge" or "neoforge")
        {
            var loader = engine == "forge" ? "1.21.1-52.1.16" : "21.1.250";
            var baseUrl = engine == "forge" ? "https://maven.minecraftforge.net/net/minecraftforge/forge" : "https://maven.neoforged.net/releases/net/neoforged/neoforge";
            var url = $"{baseUrl}/{loader}/{engine}-{loader}-installer.jar";
            using var metadata = new HttpClient();
            metadata.DefaultRequestHeaders.UserAgent.ParseAdd("CraftHarbor-RealTests/0.1.2 (https://github.com/ryuya0124/CraftHarbor)");
            var hash = (await metadata.GetStringAsync(url + ".sha1", deadline.Token)).Trim().Split(' ')[0];
            if (hash.Length != 40) throw new IOException("Invalid installer SHA1");
            var source = Path.Combine(root, "import-source", engine); Directory.CreateDirectory(source);
            var installer = Path.Combine(source, "installer.jar");
            await downloads.FileAsync(url, installer, hash, "SHA1", progress, deadline.Token);
            Record(engine, "installer-started", new { loader, url, sha1 = hash });
            using (var installing = new Process { StartInfo = new ProcessStartInfo(java) { WorkingDirectory = source, UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true } })
            {
                foreach (var arg in new[] { "-Xmx1536M", "-XX:ActiveProcessorCount=2", "-jar", installer, "--installServer" }) installing.StartInfo.ArgumentList.Add(arg);
                installing.Start(); installing.PriorityClass = ProcessPriorityClass.BelowNormal;
                var stdout = installing.StandardOutput.ReadToEndAsync(); var stderr = installing.StandardError.ReadToEndAsync();
                try { await installing.WaitForExitAsync(deadline.Token); }
                finally { if (!installing.HasExited) { installing.Kill(true); await installing.WaitForExitAsync(); } }
                await File.WriteAllTextAsync(Path.Combine(root, engine + "-installer.log"), await stdout + await stderr);
                if (installing.ExitCode != 0) throw new IOException(engine + " installer failed; see installer log");
            }
            var argFile = SafeFiles.Files(source).Single(path => Path.GetFileName(path) == "win_args.txt");
            profile.LaunchArgs = ["@user_jvm_args.txt", "@" + Path.GetRelativePath(source, argFile).Replace('\\', '/'), "nogui"];
            SafeFiles.CopyTree(source, directory);
            Record(engine, "installed-and-imported", new { loader, launchArgs = profile.LaunchArgs, source });
        }
        else await downloads.InstallServer(profile, directory, progress, deadline.Token);
        File.WriteAllText(Path.Combine(directory, "server.properties"), $"server-ip=127.0.0.1\nserver-port={port}\nonline-mode=false\nenforce-secure-profile=false\nmotd=CraftHarbor isolated {engine} test\nlevel-type=minecraft:flat\ngenerate-structures=false\nview-distance=2\nsimulation-distance=2\nmax-players=2\ndifficulty=peaceful\ngamemode=creative\nspawn-protection=0\nenable-rcon=false\nenable-query=false\n");
        if (engine == "fabric")
        {
            var mods = await downloads.ResolveMods("fabric-api", profile.Version, "fabric", deadline.Token);
            await downloads.InstallMods(mods, Path.Combine(directory, "mods"), progress, deadline.Token);
        }
        using var runtime = new ServerRuntime();
        var autoModpack = args.Contains("--automodpack");
        if (autoModpack)
        {
            if (engine != "fabric") throw new ArgumentException("AutoModpack test fixture uses Fabric");
            var release = await downloads.Json("https://api.modrinth.com/v2/version/ig9vuxA6", deadline.Token);
            await downloads.InstallMods([release], Path.Combine(directory, "mods"), progress, deadline.Token);
            SafeFiles.AtomicWrite(Path.Combine(directory, "automodpack", "automodpack-server.json"), """{"modpackName":"CraftHarbor isolated sync","bindAddress":"127.0.0.1","bindPort":0,"requireAutoModpackOnClient":false,"nagUnModdedClients":false,"selfUpdater":false,"syncedFiles":["/mods/*.jar","/config/harbor-sync.json"]}""");
            SafeFiles.AtomicWrite(Path.Combine(directory, "config", "harbor-sync.json"), "{\"message\":\"同期対象テスト\"}");
            var hostProbe = new TcpListener(IPAddress.Loopback, 0); hostProbe.Start(); var hostPort = ((IPEndPoint)hostProbe.LocalEndpoint).Port; hostProbe.Stop();
            var configPath = Path.Combine(directory, "automodpack", "automodpack-server.json");
            var config = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(configPath))!; config["bindPort"] = hostPort; SafeFiles.AtomicWrite(configPath, config.ToJsonString());
            Record(engine, "automodpack-installed", new { version = "4.0.6", hostPort, clientSynchronizationTested = false });
        }
        async Task Boot(string phase)
        {
            var start = Stopwatch.StartNew(); runtime.Start(profile, directory, Path.Combine(root, "logs", engine));
            using (var process = Process.GetProcessById(runtime.Pid!.Value)) process.PriorityClass = ProcessPriorityClass.BelowNormal;
            Record(engine, phase + "-started", new { pid = runtime.Pid, port });
            await WaitFor(() => runtime.Ready, runtime, engine + " startup", 240);
            if (!IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpListeners().Any(e => e.Port == port && IPAddress.IsLoopback(e.Address))) throw new IOException("Expected loopback listener missing");
            if (IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpListeners().Any(e => e.Port == port && !IPAddress.IsLoopback(e.Address))) throw new IOException("Unexpected public test listener");
            Record(engine, phase + "-ready", new { elapsedSeconds = start.Elapsed.TotalSeconds, memoryMb = runtime.WorkingSet / 1048576d, status = autoModpack ? (JsonElement?)null : await ServerStatus(port, deadline.Token) });
        }
        async Task Stop(string phase)
        {
            await runtime.StopAsync();
            if (runtime.Running || runtime.Busy || runtime.ExitCode != 0) throw new IOException("Server did not exit cleanly");
            if (IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpListeners().Any(e => e.Port == port)) throw new IOException("Test listener still open");
            Record(engine, phase + "-stopped", new { exitCode = runtime.ExitCode });
        }
        try
        {
            await Boot("initial");
            if (autoModpack)
            {
                await Command(runtime, "automodpack", "AutoModpack");
                await Command(runtime, "automodpack generate", "Modpack generated");
                var manifest = Path.Combine(directory, "automodpack", "host-modpack", "automodpack-content.json");
                await WaitFor(() => File.Exists(manifest) && File.ReadAllText(manifest).Contains("harbor-sync.json"), runtime, "AutoModpack manifest generation", 120);
                Record(engine, "automodpack-server-manifest-pass", new { configurationIncluded = true, clientSynchronizationTested = false });
            }
            await Command(runtime, "say 日本語コンソール確認", "日本語コンソール確認");
            if (engine == "fabric" && !runtime.History.Any(s => s.Contains("fabric-api"))) throw new IOException("Fabric API was not loaded");
            await Command(runtime, "gamerule spawnRadius 0", "spawnRadius");
            await Command(runtime, "setworldspawn 2 -60 2", "world spawn");
            await Command(runtime, "setblock 0 -60 0 minecraft:diamond_block", "Changed the block");
            if (!autoModpack) await VerifyClient(profile, runtime, "initial");
            else Record(engine, "client-checks-not-passed", "The raw status probe disconnected in earlier runs; this run verifies server management only, not client synchronization.");
            await Command(runtime, "save-all flush", "Saved the game");
            await Stop("initial");
            var levelFile = Path.Combine(directory, "world", "level.dat");
            if (!File.Exists(levelFile) || new FileInfo(levelFile).Length == 0) throw new IOException("World was not saved");
            var archive = SafeFiles.Snapshot(directory, Path.Combine(root, "backups", engine));
            Record(engine, "backup-created", new { file = archive, bytes = new FileInfo(archive).Length });
            await Boot("restart");
            if (!autoModpack) await VerifyClient(profile, runtime, "restart");
            await Command(runtime, "setblock 0 -60 0 minecraft:gold_block", "Changed the block");
            await Command(runtime, "save-all flush", "Saved the game");
            await Stop("restart");
            var previous = SafeFiles.Restore(archive, directory);
            Record(engine, "backup-restored", new { previous });
            await Boot("restored");
            if (!autoModpack) await VerifyClient(profile, runtime, "restored");
            await Stop("restored");
            Record(engine, autoModpack ? "SERVER_ONLY_PASS" : "PASS");
        }
        finally
        {
            if (runtime.Running) { try { await runtime.StopAsync(); } catch { runtime.Kill(); } }
            while (runtime.Busy) await Task.Delay(100);
        }
    }
    Record("all", args.Contains("--automodpack") ? "SERVER_ONLY_PASS" : "PASS");
}
catch (Exception ex) { Record("all", "FAIL", ex.ToString()); Environment.ExitCode = 1; }
finally
{
    var existing = before.Select(p => new { pid = p.Id, stillRunning = !p.HasExited }).ToArray();
    Record("all", "existing-process-check", existing);
    foreach (var p in before) p.Dispose();
}

static async Task<JsonElement> ServerStatus(int port, CancellationToken ct)
{
    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct); timeout.CancelAfter(TimeSpan.FromSeconds(10));
    using var client = new TcpClient(); await client.ConnectAsync(IPAddress.Loopback, port, timeout.Token);
    var stream = client.GetStream();
    using var packet = new MemoryStream(); WriteVarInt(packet, 0); WriteVarInt(packet, 767);
    var host = Encoding.UTF8.GetBytes("127.0.0.1"); WriteVarInt(packet, host.Length); packet.Write(host); packet.WriteByte((byte)(port >> 8)); packet.WriteByte((byte)port); WriteVarInt(packet, 1);
    using var framed = new MemoryStream(); WriteVarInt(framed, (int)packet.Length); packet.Position = 0; packet.CopyTo(framed); framed.Write([1, 0]);
    await stream.WriteAsync(framed.ToArray(), timeout.Token);
    var size = await ReadVarInt(stream, timeout.Token); if (size is < 1 or > 1048576) throw new IOException("Invalid status packet length");
    var data = new byte[size]; await stream.ReadExactlyAsync(data, timeout.Token); using var payload = new MemoryStream(data);
    if (await ReadVarInt(payload, timeout.Token) != 0) throw new IOException("Invalid status packet");
    var length = await ReadVarInt(payload, timeout.Token); if (length < 0 || length > size) throw new IOException("Invalid status JSON length");
    var json = new byte[length]; await payload.ReadExactlyAsync(json, timeout.Token); return JsonSerializer.Deserialize<JsonElement>(json);
}
static void WriteVarInt(Stream stream, int value)
{
    do { var part = value & 0x7f; value >>= 7; if (value != 0) part |= 0x80; stream.WriteByte((byte)part); } while (value != 0);
}
static async Task<int> ReadVarInt(Stream stream, CancellationToken ct)
{
    int result = 0; var b = new byte[1];
    for (int i = 0; i < 5; i++) { await stream.ReadExactlyAsync(b, ct); result |= (b[0] & 0x7f) << (7 * i); if ((b[0] & 0x80) == 0) return result; }
    throw new IOException("Invalid VarInt");
}
