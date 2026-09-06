using System.Diagnostics;
using System.IO.Compression;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using CraftHarbor.Core;

if (args.Contains("--fake-server"))
{
    Console.WriteLine("Done (0.1s)! For help, type help"); Console.Error.WriteLine("stderr-is-captured");
    string? command;
    while ((command = Console.ReadLine()) != null) { Console.WriteLine("echo:" + command); if (command == "stop") return; }
    return;
}

var root = Path.Combine(Path.GetTempPath(), "CraftHarbor.Tests-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
int passed = 0, failed = 0;
async Task Test(string name, Func<Task> body) { try { await body(); Console.WriteLine("PASS " + name); passed++; } catch (Exception ex) { Console.WriteLine("FAIL " + name + ": " + ex); failed++; } }
void Check(bool condition, string reason = "Assertion failed") { if (!condition) throw new Exception(reason); }
void Throws<T>(Action body) where T : Exception { try { body(); } catch (T) { return; } throw new Exception("Expected " + typeof(T).Name); }
async Task ThrowsAsync<T>(Func<Task> body) where T : Exception { try { await body(); } catch (T) { return; } throw new Exception("Expected " + typeof(T).Name); }
Task Sync(Action action) { action(); return Task.CompletedTask; }
string Temp(string name) => Path.Combine(root, name);

await Test("Storage roundtrip, Unicode and atomic writes", () => Sync(() => { var store = new HarborStore(Temp("store")); var p = store.Add("日本語のワールド"); p.JvmArgs = ["-Dname=a b"]; store.Save(); var again = new HarborStore(Temp("store")); Check(again.Profiles[0].Name == p.Name && again.Profiles[0].JvmArgs[0] == "-Dname=a b"); Check(!Directory.EnumerateFiles(Temp("store"), "*.partial").Any()); }));
await Test("Corrupt configuration is never silently replaced", () => Sync(() => { Directory.CreateDirectory(Temp("corrupt")); File.WriteAllText(Temp("corrupt/profiles.json"), "invalid"); Throws<System.Text.Json.JsonException>(() => new HarborStore(Temp("corrupt"))); Check(File.ReadAllText(Temp("corrupt/profiles.json")) == "invalid"); }));
await Test("Reject traversal, absolute paths and NTFS streams", () => Sync(() => { foreach (var bad in new[] { "../outside", "..\\outside", "C:\\outside", "file:stream", "" }) Throws<IOException>(() => SafeFiles.Inside(root, bad)); }));
await Test("Memory / port validation", () => Sync(() => { Throws<InvalidOperationException>(() => new ServerProfile { MinMemoryMb = 2048, MaxMemoryMb = 512 }.Validate()); Throws<InvalidOperationException>(() => new ServerProfile { Port = 65536 }.Validate()); }));
await Test("Arguments preserve spaces without shell interpretation", () => Sync(() => { var p = new ServerProfile { Jar = "folder with spaces/server.jar", JvmArgs = ["-Dname=a b"], JavaPath = "C:\\Java Home\\bin\\java.exe" }; var info = ServerRuntime.BuildStart(p, root); Check(!info.UseShellExecute); Check(info.ArgumentList.Contains("-Dname=a b")); Check(info.ArgumentList.Contains(SafeFiles.Inside(root, p.Jar))); }));
await Test("Forge argfiles are separate native arguments", () => Sync(() => { var info = ServerRuntime.BuildStart(new ServerProfile { LaunchArgs = ["@libraries/forge/win_args.txt", "nogui"] }, root); Check(info.ArgumentList.Contains("@libraries/forge/win_args.txt") && !info.ArgumentList.Contains("-jar")); }));
await Test("Property update preserves comments and removes duplicate key", () => Sync(() => { var text = SafeFiles.SetProperty("# comment\nserver-port=1\nonline-mode=true\nserver-port=2", "server-port", "25570"); Check(text.Contains("# comment") && text.Contains("online-mode=true") && text.Split("server-port=").Length == 2); Throws<ArgumentException>(() => SafeFiles.SetProperty(text, "motd", "a\nb")); }));
await Test("Backup restores full tree and preserves pre-restore data", () => Sync(() => { var source = Temp("world"); Directory.CreateDirectory(Path.Combine(source, "world")); File.WriteAllText(Path.Combine(source, "world/level.dat"), "before"); var zip = SafeFiles.Snapshot(source, Temp("backups")); File.WriteAllText(Path.Combine(source, "world/level.dat"), "after"); File.WriteAllText(Path.Combine(source, "extra.txt"), "extra"); var old = SafeFiles.Restore(zip, source); Check(File.ReadAllText(Path.Combine(source, "world/level.dat")) == "before"); Check(!File.Exists(Path.Combine(source, "extra.txt"))); Check(File.ReadAllText(Path.Combine(old, "world/level.dat")) == "after"); }));
await Test("ZIP slip rejected with original tree untouched", () => Sync(() => { var zip = Temp("evil.zip"); using (var z = ZipFile.Open(zip, ZipArchiveMode.Create)) { using var w = new StreamWriter(z.CreateEntry("../escape.txt").Open()); w.Write("bad"); } var target = Temp("safe"); Directory.CreateDirectory(target); File.WriteAllText(Path.Combine(target, "keep"), "original"); Throws<IOException>(() => SafeFiles.Restore(zip, target)); Check(File.ReadAllText(Path.Combine(target, "keep")) == "original"); Check(!File.Exists(Temp("escape.txt"))); }));
await Test("ZIP expanded size limit", () => Sync(() => { var zip = Temp("large.zip"); using (var z = ZipFile.Open(zip, ZipArchiveMode.Create)) { using var w = new StreamWriter(z.CreateEntry("large.txt").Open()); w.Write(new string('x', 1000)); } Throws<IOException>(() => SafeFiles.ExtractZip(zip, Temp("large"), 10)); }));
await Test("ZIP duplicate paths rejected", () => Sync(() => { var zip = Temp("duplicate.zip"); using (var z = ZipFile.Open(zip, ZipArchiveMode.Create)) { z.CreateEntry("same"); z.CreateEntry("same"); } Throws<IOException>(() => SafeFiles.ExtractZip(zip, Temp("duplicate"))); }));
await Test("Reject unencrypted downloads", () => Sync(() => { Throws<IOException>(() => Downloads.ValidateUrl("http://example.com/a")); Throws<IOException>(() => Downloads.ValidateUrl("https://localhost/a")); }));
var payload = Encoding.UTF8.GetBytes("verified bytes"); var hash = Convert.ToHexString(SHA256.HashData(payload));
await Test("Verified download promotes completed file", async () => { using var d = new Downloads(new FakeHttp(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(payload) })); await d.FileAsync("https://example.com/a", Temp("download.bin"), hash); Check(File.ReadAllBytes(Temp("download.bin")).SequenceEqual(payload)); });
await Test("Hash mismatch preserves existing destination", async () => { using var d = new Downloads(new FakeHttp(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(payload) })); File.WriteAllText(Temp("existing.bin"), "original"); await ThrowsAsync<IOException>(() => d.FileAsync("https://example.com/a", Temp("existing.bin"), new string('0', 64))); Check(File.ReadAllText(Temp("existing.bin")) == "original"); Check(!Directory.EnumerateFiles(root, "*.partial").Any()); });
await Test("HTTP failure does not create destination", async () => { using var d = new Downloads(new FakeHttp(_ => new HttpResponseMessage(HttpStatusCode.NotFound))); await ThrowsAsync<HttpRequestException>(() => d.FileAsync("https://example.com/a", Temp("missing.bin"))); Check(!File.Exists(Temp("missing.bin"))); });
await Test("Download cancellation does not promote file", async () => { using var d = new Downloads(new FakeHttp(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(payload) })); using var cancel = new CancellationTokenSource(); cancel.Cancel(); await ThrowsAsync<OperationCanceledException>(() => d.FileAsync("https://example.com/a", Temp("cancel.bin"), ct: cancel.Token)); Check(!File.Exists(Temp("cancel.bin"))); });
await Test("Runtime logs are bounded", () => Sync(() => { using var runtime = new ServerRuntime(); for (var i = 0; i < 5000; i++) runtime.Append("line " + i); Check(runtime.History.Length == 2000 && runtime.History[^1].Contains("4999")); Check(runtime.Drain().Count == 250); }));
await Test("Runtime refuses missing EULA and missing JAR", () => Sync(() => { using var runtime = new ServerRuntime(); Throws<InvalidOperationException>(() => runtime.Start(new ServerProfile(), root, Temp("logs"))); Throws<FileNotFoundException>(() => runtime.Start(new ServerProfile { EulaAccepted = true }, root, Temp("logs"))); }));
await Test("Port collision never touches existing listener", async () => { var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start(); try { using var runtime = new ServerRuntime(); var port = ((IPEndPoint)listener.LocalEndpoint).Port; Throws<IOException>(() => runtime.Start(new ServerProfile { Port = port, EulaAccepted = true, LaunchArgs = ["--fake-server"] }, root, Temp("logs"))); Check((await Diagnostics.Probe("127.0.0.1", port)).Contains("成功")); } finally { listener.Stop(); } });
await Test("Managed child: stdout, stderr, command, graceful stop, restart", async () =>
{
    var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start(); var port = ((IPEndPoint)listener.LocalEndpoint).Port; listener.Stop();
    var dir = Temp("process"); Directory.CreateDirectory(dir); using var runtime = new ServerRuntime();
    var profile = new ServerProfile { JavaPath = Environment.ProcessPath!, LaunchArgs = ["--fake-server"], EulaAccepted = true, Port = port };
    try
    {
        runtime.Start(profile, dir, Temp("process-logs"));
        var deadline = DateTime.UtcNow.AddSeconds(10); while (!runtime.Ready && DateTime.UtcNow < deadline) await Task.Delay(25);
        Check(runtime.Ready && runtime.Running); Throws<InvalidOperationException>(() => runtime.Start(profile, dir, Temp("process-logs")));
        await runtime.SendAsync("list"); await ThrowsAsync<ArgumentException>(() => runtime.SendAsync("list\nstop")); await runtime.StopAsync();
        Check(runtime.ExitCode == 0 && runtime.History.Any(x => x.Contains("echo:list")) && runtime.History.Any(x => x.Contains("stderr-is-captured")));
        runtime.Start(profile, dir, Temp("process-logs")); await runtime.StopAsync(); Check(runtime.ExitCode == 0);
    }
    finally { if (runtime.Running) runtime.Kill(); while (runtime.Busy) await Task.Delay(25); }
});
await Test("Mrpack skips client files and applies server overrides", async () =>
{
    var zip = Temp("sample.mrpack");
    using (var z = ZipFile.Open(zip, ZipArchiveMode.Create))
    {
        using (var w = new StreamWriter(z.CreateEntry("modrinth.index.json").Open())) w.Write("""{"formatVersion":1,"game":"minecraft","dependencies":{"minecraft":"1.21.1","fabric-loader":"0.16.0"},"files":[{"path":"mods/client.jar","env":{"server":"unsupported"}},{"path":"mods/test.jar","downloads":["https://cdn.modrinth.com/test"],"hashes":{"sha512":"HASH"}}]}""".Replace("HASH", Convert.ToHexString(SHA512.HashData(payload))));
        using (var w = new StreamWriter(z.CreateEntry("overrides/config/test.txt").Open())) w.Write("common");
        using (var w = new StreamWriter(z.CreateEntry("server-overrides/config/test.txt").Open())) w.Write("server");
    }
    using var d = new Downloads(new FakeHttp(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(payload) }));
    var target = Temp("pack"); var deps = await d.ImportMrpack(zip, target, new Progress<string>(), default);
    Check(!File.Exists(Path.Combine(target, "mods/client.jar")) && File.Exists(Path.Combine(target, "mods/test.jar"))); Check(File.ReadAllText(Path.Combine(target, "config/test.txt")) == "server" && deps.Contains("fabric-loader"));
});
await Test("Mrpack refuses occupied server", async () => { using var d = new Downloads(); await ThrowsAsync<IOException>(() => d.ImportMrpack(Temp("sample.mrpack"), Temp("pack"), new Progress<string>(), default)); });

await Test("Version candidates use selected engine and exclude snapshots", async () =>
{
    var requests = new List<string>();
    using var d = new Downloads(new FakeHttp(request =>
    {
        var url = request.RequestUri!.AbsoluteUri; requests.Add(url);
        var json = url.Contains("version_manifest") ? """{"versions":[{"id":"1.21.1","type":"release"},{"id":"24w01a","type":"snapshot"},{"id":"1.20.1","type":"release"},{"id":"1.12.2","type":"release"}]}"""
            : url.Contains("fabricmc") ? """[{"version":"1.21.1","stable":true},{"version":"24w01a","stable":false}]"""
            : url.EndsWith("/paper") ? """{"versions":{"1.21":["1.21.1","1.21.1"],"1.20":["1.20.1"]}}"""
            : """{"versions":{"1.20":["1.20.1"]}}""";
        return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json) };
    }));
    Check((await d.ServerVersions("vanilla", default)).SequenceEqual(new[] { "1.21.1", "1.20.1", "1.12.2" }));
    Check((await d.ServerVersions("fabric", default)).SequenceEqual(new[] { "1.21.1" }));
    Check((await d.ServerVersions("paper", default)).SequenceEqual(new[] { "1.21.1", "1.20.1" }));
    Check((await d.ServerVersions("folia", default)).SequenceEqual(new[] { "1.20.1" }));
    var count = requests.Count;
    foreach (var kind in new[] { "forge", "neoforge", "quilt", "custom" }) await ThrowsAsync<NotSupportedException>(() => d.ServerVersions(kind, default));
    Check(requests.Count == count, "Manual engines must not fetch Vanilla candidates");
});
Console.WriteLine($"RESULT {passed} passed, {failed} failed");
Directory.Delete(root, true); Environment.ExitCode = failed == 0 ? 0 : 1;

sealed class FakeHttp(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested(); var response = respond(request); response.RequestMessage = request; return Task.FromResult(response);
    }
}
