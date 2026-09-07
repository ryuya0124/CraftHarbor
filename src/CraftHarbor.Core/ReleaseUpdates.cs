using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace CraftHarbor.Core;

public sealed record ReleaseUpdate(string Version, string FileName, long InstallerId, long ChecksumsId, long Size);

public sealed class ReleaseUpdates : IDisposable
{
    public const string Repository = "ryuya0124/CraftHelm";
    private readonly HttpClient http = new() { Timeout = TimeSpan.FromMinutes(5) };
    public ReleaseUpdates() { http.DefaultRequestHeaders.UserAgent.ParseAdd("CraftHelm-Updater/1.0"); }
    public static ReleaseUpdate? Select(string json, Version current)
    {
        var candidates = new List<ReleaseUpdate>();
        foreach (var release in JsonNode.Parse(json)?.AsArray() ?? throw new IOException("更新情報が不正です。"))
        {
            if (release?["draft"]?.GetValue<bool>() != false) continue;
            var tag = release["tag_name"]?.ToString() ?? "";
            if (!Regex.IsMatch(tag, @"^v\d+\.\d+\.\d+$") || !Version.TryParse(tag[1..], out var version) || version <= new Version(current.Major, current.Minor, Math.Max(0, current.Build))) continue;
            var fileName = $"CraftHelm-{version}-win-x64-setup.exe";
            var assets = release["assets"]?.AsArray();
            var setup = assets?.SingleOrDefault(a => a?["name"]?.ToString() == fileName);
            if (setup == null) { fileName = $"CraftHarbor-{version}-win-x64-setup.exe"; setup = assets?.SingleOrDefault(a => a?["name"]?.ToString() == fileName); }
            var sums = assets?.SingleOrDefault(a => a?["name"]?.ToString() == "SHA256SUMS.txt");
            if (setup == null || sums == null) continue;
            long size = setup["size"]!.GetValue<long>(), installerId = setup["id"]!.GetValue<long>(), checksumId = sums["id"]!.GetValue<long>();
            if (size is < 1024 or > 536870912 || installerId <= 0 || checksumId <= 0) continue;
            candidates.Add(new(version.ToString(), fileName, installerId, checksumId, size));
        }
        return candidates.OrderByDescending(c => Version.Parse(c.Version)).FirstOrDefault();
    }
    public static string ExpectedHash(string checksums, string fileName)
    {
        var matches = checksums.Split('\n').Select(l => Regex.Match(l.Trim(), @"^([0-9a-fA-F]{64})\s+\*?(.+)$")).Where(m => m.Success && m.Groups[2].Value == fileName).ToArray();
        if (matches.Length != 1) throw new IOException("更新ファイルのSHA-256情報がありません。");
        return matches[0].Groups[1].Value.ToLowerInvariant();
    }
    public static string Verify(string path, string expected, long expectedSize)
    {
        using var input = File.OpenRead(path);
        if (input.Length != expectedSize) throw new IOException("更新ファイルのサイズが一致しません。");
        var actual = Convert.ToHexString(SHA256.HashData(input)).ToLowerInvariant();
        if (!actual.Equals(expected, StringComparison.OrdinalIgnoreCase)) throw new IOException("更新ファイルのSHA-256が一致しません。");
        return actual;
    }
    public async Task<ReleaseUpdate?> CheckAsync(Version current, CancellationToken ct)
    {
        using var data = new MemoryStream();
        await FetchAsync($"repos/{Repository}/releases?per_page=20", data, 4 * 1024 * 1024, false, ct);
        return Select(System.Text.Encoding.UTF8.GetString(data.ToArray()), current);
    }
    public async Task<(string Path, string Hash)> DownloadAsync(ReleaseUpdate update, string directory, CancellationToken ct)
    {
        Directory.CreateDirectory(directory);
        using var sums = new MemoryStream();
        await FetchAsync($"repos/{Repository}/releases/assets/{update.ChecksumsId}", sums, 65536, true, ct);
        var hash = ExpectedHash(System.Text.Encoding.UTF8.GetString(sums.ToArray()).TrimStart('\uFEFF'), update.FileName);
        var target = SafeFiles.Inside(directory, update.FileName); var partial = target + "." + Guid.NewGuid().ToString("N") + ".partial";
        try
        {
            await using (var output = new FileStream(partial, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                await FetchAsync($"repos/{Repository}/releases/assets/{update.InstallerId}", output, update.Size, true, ct);
            Verify(partial, hash, update.Size); File.Move(partial, target, true); return (target, hash);
        }
        finally { if (File.Exists(partial)) File.Delete(partial); }
    }
    private static async Task CopyBounded(Stream source, Stream destination, long limit, CancellationToken ct)
    {
        var buffer = new byte[81920]; long total = 0; int read;
        while ((read = await source.ReadAsync(buffer, ct)) > 0)
        {
            total += read; if (total > limit) throw new IOException("更新データの上限を超えました。");
            await destination.WriteAsync(buffer.AsMemory(0, read), ct);
        }
    }
    private async Task FetchAsync(string path, Stream output, long limit, bool asset, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://api.github.com/" + path);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(asset ? "application/octet-stream" : "application/vnd.github+json"));
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        if (response.IsSuccessStatusCode) { await using var input = await response.Content.ReadAsStreamAsync(ct); await CopyBounded(input, output, limit, ct); return; }
        if (response.StatusCode is not (HttpStatusCode.NotFound or HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)) response.EnsureSuccessStatusCode();
        // Private repositories use the user's existing CLI authentication; never export tokens.
        var info = new ProcessStartInfo("gh") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var arg in new[] { "api", "--hostname", "github.com", path, "-H", "Accept: " + (asset ? "application/octet-stream" : "application/vnd.github+json") }) info.ArgumentList.Add(arg);
        using var process = new Process { StartInfo = info };
        try { process.Start(); } catch (System.ComponentModel.Win32Exception) { throw new IOException("非公開リポジトリの更新にはGitHub CLIで gh auth login を行ってください。"); }
        var error = process.StandardError.ReadToEndAsync(ct);
        try
        {
            await CopyBounded(process.StandardOutput.BaseStream, output, limit, ct);
            await process.WaitForExitAsync(ct); _ = await error;
            if (process.ExitCode != 0) throw new IOException("GitHubから更新を取得できませんでした。gh auth login とリポジトリへのアクセス権を確認してください。");
        }
        finally { if (!process.HasExited) process.Kill(true); }
    }
    public void Dispose() => http.Dispose();
}
