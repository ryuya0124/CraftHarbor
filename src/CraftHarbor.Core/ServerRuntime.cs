using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;

namespace CraftHarbor.Core;

public sealed class ServerRuntime : IDisposable
{
    private Process? process;
    private readonly ConcurrentQueue<string> lines = new();
    private readonly Queue<string> history = new();
    private readonly object sync = new();
    private StreamWriter? log;
    private Task? drain;
    public bool Running => process is { HasExited: false };
    public bool Busy => drain is { IsCompleted: false };
    public bool Ready { get; private set; }
    public int? ExitCode { get; private set; }
    public int? Pid => Running ? process!.Id : null;
    public long WorkingSet => Running ? ReadMemory() : 0;
    private long ReadMemory() { try { process!.Refresh(); return process.WorkingSet64; } catch { return 0; } }
    public string[] History { get { lock (sync) return history.ToArray(); } }
    public void Append(string line)
    {
        if (line.Length > 8192) line = line[..8192];
        if (line.Contains("Done (") && line.Contains("For help")) Ready = true;
        var item = $"[{DateTime.Now:HH:mm:ss}] {line}";
        lock (sync)
        {
            history.Enqueue(item); while (history.Count > 2000) history.Dequeue();
            try { log?.WriteLine(item); } catch (IOException) { /* Keep draining the child even if disk is full. */ }
        }
        lines.Enqueue(item); while (lines.Count > 2000) lines.TryDequeue(out _);
    }
    public List<string> Drain()
    {
        var result = new List<string>(); while (result.Count < 250 && lines.TryDequeue(out var line)) result.Add(line); return result;
    }
    public static ProcessStartInfo BuildStart(ServerProfile p, string directory)
    {
        p.Validate();
        var info = new ProcessStartInfo(p.JavaPath) { WorkingDirectory = directory, UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true, StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8, StandardInputEncoding = new UTF8Encoding(false) };
        info.ArgumentList.Add($"-Xms{p.MinMemoryMb}M"); info.ArgumentList.Add($"-Xmx{p.MaxMemoryMb}M");
        // Match the UTF-8 redirected streams on Windows, including legacy JREs.
        foreach (var property in new[] { "file.encoding", "stdout.encoding", "stderr.encoding", "sun.stdout.encoding", "sun.stderr.encoding" }) info.ArgumentList.Add("-D" + property + "=UTF-8");
        foreach (var arg in p.JvmArgs) info.ArgumentList.Add(arg);
        if (p.LaunchArgs.Length > 0) foreach (var arg in p.LaunchArgs) info.ArgumentList.Add(arg);
        else { info.ArgumentList.Add("-jar"); info.ArgumentList.Add(SafeFiles.Inside(directory, p.Jar)); info.ArgumentList.Add("nogui"); }
        return info;
    }
    public void Start(ServerProfile p, string directory, string logDirectory)
    {
        if (Busy || Running) throw new InvalidOperationException("サーバーは稼働中です。");
        if (!p.EulaAccepted) throw new InvalidOperationException("Minecraft EULAへの同意が必要です。");
        if (p.LaunchArgs.Length == 0 && !File.Exists(SafeFiles.Inside(directory, p.Jar))) throw new FileNotFoundException("サーバーJARを導入してください。");
        if (IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpListeners().Any(x => x.Port == p.Port)) throw new IOException($"ポート {p.Port} は使用中です。");
        var info = BuildStart(p, directory);
        SafeFiles.AtomicWrite(Path.Combine(directory, "eula.txt"), "eula=true\n");
        var propsPath = Path.Combine(directory, "server.properties");
        var props = File.Exists(propsPath) ? File.ReadAllText(propsPath) : "online-mode=true\n";
        SafeFiles.AtomicWrite(propsPath, SafeFiles.SetProperty(props, "server-port", p.Port.ToString()));
        Directory.CreateDirectory(logDirectory);
        process?.Dispose();
        log = new StreamWriter(Path.Combine(logDirectory, $"{DateTime.Now:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.log")) { AutoFlush = true };
        Ready = false; ExitCode = null;
        process = new Process { StartInfo = info };
        try { process.Start(); }
        catch { log.Dispose(); log = null; throw; }
        Append($"起動: PID {process.Id} / {p.Name}");
        drain = WatchAsync(process);
    }
    private async Task Pump(StreamReader reader)
    {
        // Bound individual lines, including pathological output without a newline.
        var buffer = new char[2048]; var pending = new StringBuilder(); int n;
        while ((n = await reader.ReadAsync(buffer)) > 0)
            for (var i = 0; i < n; i++)
            {
                if (buffer[i] == '\n') { Append(pending.ToString().TrimEnd('\r')); pending.Clear(); }
                else { pending.Append(buffer[i]); if (pending.Length >= 8192) { Append(pending.ToString()); pending.Clear(); } }
            }
        if (pending.Length > 0) Append(pending.ToString());
    }
    private async Task WatchAsync(Process child)
    {
        try { await Task.WhenAll(Pump(child.StandardOutput), Pump(child.StandardError), child.WaitForExitAsync()); ExitCode = child.ExitCode; Append($"終了コード: {ExitCode}"); }
        catch (Exception ex) { Append("監視エラー: " + ex.Message); }
        finally { Ready = false; lock (sync) { log?.Dispose(); log = null; } }
    }
    public async Task SendAsync(string command)
    {
        if (!Running) throw new InvalidOperationException("サーバーは停止中です。");
        if (string.IsNullOrWhiteSpace(command) || command.IndexOfAny(['\r','\n']) >= 0) throw new ArgumentException("コマンドは1行で入力してください。");
        await process!.StandardInput.WriteLineAsync(command.TrimStart('/')); await process.StandardInput.FlushAsync();
    }
    public async Task StopAsync()
    {
        if (!Running) { if (drain != null) await drain; return; }
        await SendAsync("stop");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        try { await process!.WaitForExitAsync(timeout.Token); if (drain != null) await drain; }
        catch (OperationCanceledException) { throw new TimeoutException("60秒以内に停止しませんでした。ログを確認し、必要なら強制終了してください。"); }
    }
    public void Kill() { if (Running) process!.Kill(true); }
    public void Dispose() { if (Busy || Running) throw new InvalidOperationException("停止してから破棄してください。"); process?.Dispose(); log?.Dispose(); }
}

public static class Diagnostics
{
    public static string Describe()
    {
        var text = new StringBuilder();
        using var self = Process.GetCurrentProcess();
        text.AppendLine($"OS: {System.Runtime.InteropServices.RuntimeInformation.OSDescription}");
        text.AppendLine($"CPU論理コア: {Environment.ProcessorCount} / {System.Runtime.InteropServices.RuntimeInformation.OSArchitecture}");
        text.AppendLine($"アプリ使用メモリ: {self.WorkingSet64 / 1048576d:F1} MB");
        text.AppendLine($"GC利用可能メモリ上限: {GC.GetGCMemoryInfo().TotalAvailableMemoryBytes / 1073741824d:F1} GB");
        foreach (var drive in DriveInfo.GetDrives().Where(d => d.IsReady)) text.AppendLine($"ドライブ {drive.Name} 空き {drive.AvailableFreeSpace / 1073741824d:F1} / {drive.TotalSize / 1073741824d:F1} GB");
        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces().Where(n => n.OperationalStatus == OperationalStatus.Up))
            foreach (var ip in nic.GetIPProperties().UnicastAddresses.Where(a => a.Address.AddressFamily == AddressFamily.InterNetwork)) text.AppendLine($"LAN: {nic.Name} → {ip.Address}");
        text.AppendLine("\nTCP待受: " + string.Join(", ", IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpListeners().Select(x => x.Port).Distinct().Order()));
        text.AppendLine("\nLANの接続先は LAN IP:ポート。外部公開はルーターのTCP転送とWindows Firewallの許可が必要です。\nこのアプリはルーター・Firewallを自動変更しません。ローカル疎通の成功はインターネットからの到達を保証しません。");
        return text.ToString();
    }
    public static async Task<string> Probe(string host, int port)
    {
        using var client = new TcpClient(); using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(4));
        var sw = Stopwatch.StartNew();
        try { await client.ConnectAsync(host, port, timeout.Token); return $"{host}:{port} TCP接続成功 ({sw.ElapsedMilliseconds}ms)"; }
        catch (Exception ex) when (ex is SocketException or OperationCanceledException) { return $"{host}:{port} 接続失敗: {ex.Message}"; }
    }
}
