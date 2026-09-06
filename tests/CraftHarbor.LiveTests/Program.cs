using System.Diagnostics;
using CraftHarbor.Core;

var root = Path.Combine(Path.GetTempPath(), "CraftHarbor.LiveTests-" + Guid.NewGuid().ToString("N"));
using var downloads = new Downloads(); using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(8));
var progress = new Progress<string>(s => { });
try
{
    var versions = await downloads.MinecraftVersions(deadline.Token); Console.WriteLine($"PASS Mojang releases: {versions.Length}, latest {versions[0]}");
    foreach (var engine in new[] { "vanilla", "paper", "fabric" })
    {
        var p = new ServerProfile { Engine = engine, Version = "1.21.1" }; var dir = Path.Combine(root, engine); Directory.CreateDirectory(dir);
        var java = await downloads.InstallServer(p, dir, progress, deadline.Token);
        Console.WriteLine($"PASS {engine} 1.21.1 download: {new FileInfo(Path.Combine(dir, "server.jar")).Length} bytes, Java {java}");
    }
    var hits = await downloads.SearchMods("ferritecore", "1.21.1", "fabric", deadline.Token); if (hits.Count == 0) throw new Exception("Search returned no results"); Console.WriteLine($"PASS Modrinth search: {hits.Count}");
    var releases = await downloads.ResolveMods("fabric-api", "1.21.1", "fabric", deadline.Token); await downloads.InstallMods(releases, Path.Combine(root, "mods"), progress, deadline.Token); Console.WriteLine($"PASS Modrinth resolve + SHA512 download: {releases.Count}");
    var javaPath = await downloads.InstallJava(21, Path.Combine(root, "java"), progress, deadline.Token);
    using var child = new Process { StartInfo = new ProcessStartInfo(javaPath) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true } }; child.StartInfo.ArgumentList.Add("-version"); child.Start(); var output = await child.StandardError.ReadToEndAsync(deadline.Token); await child.WaitForExitAsync(deadline.Token); if (child.ExitCode != 0) throw new Exception(output);
    Console.WriteLine("PASS Temurin 21 SHA256 download, extract, launch -version: " + output.Split('\n')[0]);
    Console.WriteLine("RESULT live integration passed; no Minecraft servers started or stopped");
}
finally
{
    // Windows may briefly retain an executable mapping after process exit.
    for (var attempt = 0; Directory.Exists(root); attempt++)
    {
        try { Directory.Delete(root, true); }
        catch (Exception ex) when (attempt < 8 && ex is IOException or UnauthorizedAccessException) { await Task.Delay(500); }
    }
}
