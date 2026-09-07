namespace CraftHarbor.Core;

// Preserve files without interpreting version-dependent MOD schemas.
public static class ModConfigurations
{
    public static readonly string[] Roots = ["config", "defaultconfigs", "kubejs", "scripts"];
    public const string AutoModpack = "automodpack/automodpack-server.json";
    private static readonly HashSet<string> Extensions = new(StringComparer.OrdinalIgnoreCase)
        { ".properties", ".json", ".json5", ".jsonc", ".toml", ".yml", ".yaml", ".txt", ".conf", ".cfg", ".snbt", ".hocon", ".js", ".zs" };
    public static bool IsTextConfiguration(string path) => Extensions.Contains(Path.GetExtension(path));

    public static IEnumerable<string> WorldConfigRoots(string root)
    {
        // Only inspect immediate world directories; never walk region/player data.
        foreach (var dir in Directory.EnumerateDirectories(root))
        {
            var relative = Path.GetFileName(dir) + "/serverconfig";
            var path = SafeFiles.Inside(root, relative);
            if (Directory.Exists(path)) yield return relative;
        }
    }

    public static IEnumerable<string> PresetFiles(string root)
    {
        foreach (var relative in Roots.Concat(WorldConfigRoots(root)))
            foreach (var file in SafeFiles.Files(SafeFiles.Inside(root, relative))) yield return file;
        var auto = SafeFiles.Inside(root, AutoModpack);
        if (File.Exists(auto)) yield return auto;
    }

    public static IEnumerable<string> EditableFiles(string root) =>
        Directory.EnumerateFiles(root).Concat(PresetFiles(root)).Concat(SafeFiles.Files(SafeFiles.Inside(root, "plugins")))
            .Where(f => Extensions.Contains(Path.GetExtension(f)) && new FileInfo(f).Length < 1024 * 1024)
            .Select(f => Path.GetRelativePath(root, SafeFiles.Inside(root, Path.GetRelativePath(root, f))))
            .Distinct(StringComparer.OrdinalIgnoreCase).Take(500);

    public static bool AllowedPresetFile(string relative)
    {
        var parts = relative.Replace('\\', '/').Split('/');
        return parts.Length >= 2 && (Roots.Contains(parts[0], StringComparer.OrdinalIgnoreCase)
            || parts[0].Equals("mods", StringComparison.OrdinalIgnoreCase)
            || parts[0].Equals("plugins", StringComparison.OrdinalIgnoreCase)
            || (parts.Length >= 3 && parts[1].Equals("serverconfig", StringComparison.OrdinalIgnoreCase)))
            || relative.Replace('\\', '/').Equals(AutoModpack, StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsManagedJar(string relative)
    {
        var parts = relative.Replace('\\', '/').Split('/');
        return parts.Length == 2 && (parts[0].Equals("mods", StringComparison.OrdinalIgnoreCase) || parts[0].Equals("plugins", StringComparison.OrdinalIgnoreCase))
            && (parts[1].EndsWith(".jar", StringComparison.OrdinalIgnoreCase) || parts[1].EndsWith(".jar.disabled", StringComparison.OrdinalIgnoreCase));
    }
}
