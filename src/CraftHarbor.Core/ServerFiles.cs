using System.Text.Json;

namespace CraftHarbor.Core;

// Shared by the desktop controls and opt-in integration tests.
public sealed class ServerFiles(HarborStore store, ServerProfile profile, ServerRuntime runtime)
{
    private string Root => store.ServerDir(profile);
    private void Stopped() { if (runtime.Running || runtime.Busy) throw new InvalidOperationException("この操作はサーバー停止中に行ってください。"); }
    private string JarFolder(string folder) => folder is "mods" or "plugins" ? SafeFiles.Inside(Root, folder) : throw new IOException("MODの配置先が不正です。");
    public void AddJars(string folder, IEnumerable<string> files)
    {
        Stopped(); var target = JarFolder(folder); var sources = files.ToArray();
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in sources)
        {
            var name = Path.GetFileName(file);
            if (!name.EndsWith(".jar", StringComparison.OrdinalIgnoreCase) || !File.Exists(file)) throw new IOException("JARを選択してください。");
            if (!names.Add(name) || File.Exists(SafeFiles.Inside(target, name))) throw new IOException("同名JARが存在します。");
        }
        Directory.CreateDirectory(target);
        foreach (var file in sources) File.Copy(file, SafeFiles.Inside(target, Path.GetFileName(file)), false);
    }
    public void ToggleJar(string folder, string name)
    {
        Stopped();
        if (!name.EndsWith(".jar", StringComparison.OrdinalIgnoreCase) && !name.EndsWith(".jar.disabled", StringComparison.OrdinalIgnoreCase)) throw new IOException("JARを選択してください。");
        var path = SafeFiles.Inside(JarFolder(folder), name);
        File.Move(path, name.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase) ? path[..^9] : path + ".disabled");
    }
    public void SaveConfiguration(string relative, string content)
    {
        Stopped(); var path = SafeFiles.Inside(Root, relative);
        if (relative.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) { using var json = JsonDocument.Parse(content); }
        if (File.Exists(path))
        {
            var archive = SafeFiles.Inside(Path.Combine(store.Root, "file-history", profile.Id, DateTime.Now.ToString("yyyyMMddHHmmss") + "-" + Guid.NewGuid().ToString("N")), relative);
            Directory.CreateDirectory(Path.GetDirectoryName(archive)!); File.Copy(path, archive);
        }
        SafeFiles.AtomicWrite(path, content);
    }
    public string SavePreset(string name)
    {
        Stopped(); var dest = SafeFiles.Inside(store.PresetDir(profile), name + ".zip");
        if (File.Exists(dest)) throw new IOException("同名プリセットがあります。");
        var stage = Path.Combine(store.Root, "staging", Guid.NewGuid().ToString("N"));
        try
        {
            foreach (var dir in new[] { "mods", "plugins", "config" }) SafeFiles.CopyTree(Path.Combine(Root, dir), Path.Combine(stage, dir));
            var zip = SafeFiles.Snapshot(stage, store.PresetDir(profile)); File.Move(zip, dest); return dest;
        }
        finally { if (Directory.Exists(stage)) Directory.Delete(stage, true); }
    }
    public string ApplyPreset(string name)
    {
        Stopped(); var stage = Root + ".preset-" + Guid.NewGuid().ToString("N");
        try
        {
            Directory.CreateDirectory(stage);
            SafeFiles.ExtractZip(SafeFiles.Inside(store.PresetDir(profile), name), stage);
            foreach (var entry in Directory.EnumerateFileSystemEntries(stage))
                if (!new[] { "mods", "plugins", "config" }.Contains(Path.GetFileName(entry)) || !Directory.Exists(entry)) throw new IOException("プリセットの内容が不正です。");
            var backup = SafeFiles.Snapshot(Root, store.BackupDir(profile));
            try
            {
                foreach (var dir in new[] { "mods", "plugins", "config" })
                {
                    var target = SafeFiles.Inside(Root, dir); if (Directory.Exists(target)) Directory.Delete(target, true);
                    if (Directory.Exists(Path.Combine(stage, dir))) Directory.Move(Path.Combine(stage, dir), target);
                }
            }
            catch { SafeFiles.Restore(backup, Root); throw; }
            return backup;
        }
        finally { if (Directory.Exists(stage)) Directory.Delete(stage, true); }
    }
}
