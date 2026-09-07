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
    public string SaveServerProperties(string? expectedText, IReadOnlyDictionary<string, string> changes)
    {
        Stopped();
        var path = SafeFiles.Inside(Root, "server.properties");
        var current = File.Exists(path) ? File.ReadAllText(path) : null;
        if (current != expectedText) throw new IOException("設定ファイルが別の操作で変更されました。画面を開き直してください。");
        foreach (var (key, value) in changes)
        {
            if (string.IsNullOrWhiteSpace(key)) throw new IOException("設定名が空です。");
            PropertyFields.For(key, value).Validate(value);
            if (key == "level-name") _ = SafeFiles.Inside(Root, value);
        }
        var updated = new ServerProperties(current ?? "").Apply(changes);
        if (changes.Count == 0) return current ?? "";
        var oldPort = profile.Port;
        SaveConfiguration("server.properties", updated);
        try
        {
            if (changes.TryGetValue("server-port", out var port)) { profile.Port = int.Parse(port); store.Save(); }
        }
        catch (Exception saveError)
        {
            profile.Port = oldPort;
            try { if (current == null) File.Delete(path); else SafeFiles.AtomicWrite(path, current); }
            catch (Exception rollbackError) { throw new AggregateException("ポート設定の保存と復旧に失敗しました。file-historyを確認してください。", saveError, rollbackError); }
            throw;
        }
        return updated;
    }
    public string SavePreset(string name)
    {
        Stopped(); var dest = SafeFiles.Inside(store.PresetDir(profile), name + ".zip");
        if (File.Exists(dest)) throw new IOException("同名プリセットがあります。");
        var stage = Path.Combine(store.Root, "staging", Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(stage);
            // Only JARs belong to the binary selection. Plugin data remains on the server.
            var jars = new[] { "mods", "plugins" }.SelectMany(dir => Directory.Exists(Path.Combine(Root, dir)) ? Directory.EnumerateFiles(SafeFiles.Inside(Root, dir)) : [])
                .Where(f => ModConfigurations.IsManagedJar(Path.GetRelativePath(Root, f)));
            var pluginConfigs = SafeFiles.Files(SafeFiles.Inside(Root, "plugins"))
                .Where(f => ModConfigurations.IsTextConfiguration(f));
            foreach (var source in ModConfigurations.PresetFiles(Root).Concat(jars).Concat(pluginConfigs).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                var relative = Path.GetRelativePath(Root, source); _ = SafeFiles.Inside(Root, relative);
                var target = SafeFiles.Inside(stage, relative); Directory.CreateDirectory(Path.GetDirectoryName(target)!); File.Copy(source, target);
            }
            var zip = SafeFiles.Snapshot(stage, store.PresetDir(profile)); File.Move(zip, dest); return dest;
        }
        finally { if (Directory.Exists(stage)) Directory.Delete(stage, true); }
    }
    public string ApplyPreset(string name, bool keepExistingSettings = true)
    {
        Stopped(); var stage = Root + ".preset-" + Guid.NewGuid().ToString("N");
        try
        {
            Directory.CreateDirectory(stage);
            SafeFiles.ExtractZip(SafeFiles.Inside(store.PresetDir(profile), name), stage);
            var entries = SafeFiles.Files(stage).Select(f => Path.GetRelativePath(stage, f)).ToArray();
            foreach (var relative in entries)
                if (!ModConfigurations.AllowedPresetFile(relative)) throw new IOException("プリセットの内容が不正です。");
            var backup = SafeFiles.Snapshot(Root, store.BackupDir(profile));
            try
            {
                foreach (var dir in new[] { "mods", "plugins" })
                {
                    var target = SafeFiles.Inside(Root, dir);
                    if (Directory.Exists(target))
                        foreach (var file in Directory.EnumerateFiles(target).Where(f => ModConfigurations.IsManagedJar(Path.GetRelativePath(Root, f))))
                            File.Delete(SafeFiles.Inside(Root, Path.GetRelativePath(Root, file)));
                }
                foreach (var relative in entries)
                {
                    var target = SafeFiles.Inside(Root, relative);
                    if (!ModConfigurations.IsManagedJar(relative) && keepExistingSettings && File.Exists(target)) continue;
                    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                    File.Copy(SafeFiles.Inside(stage, relative), target, true);
                }
            }
            catch { SafeFiles.Restore(backup, Root); throw; }
            return backup;
        }
        finally { if (Directory.Exists(stage)) Directory.Delete(stage, true); }
    }
}
