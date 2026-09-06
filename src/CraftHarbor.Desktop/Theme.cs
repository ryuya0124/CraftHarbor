using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CraftHarbor.Core;

namespace CraftHarbor.Desktop;

public static class Theme
{
    private sealed record Settings(string Appearance = "Dark");
    private static readonly Dictionary<string, (string Dark, string Light)> Palette = new()
    {
        ["Ink"] = ("#E8EDF5", "#172A3B"),
        ["Background"] = ("#0D141F", "#F0F4F7"),
        ["Sidebar"] = ("#111B29", "#E4ECF1"),
        ["Surface"] = ("#192330", "#FFFFFF"),
        ["Input"] = ("#111A27", "#F7FAFC"),
        ["Button"] = ("#26364A", "#DAE5EE"),
        ["Border"] = ("#465B72", "#889DB0"),
        ["Muted"] = ("#A4B5C9", "#4D6377"),
        ["Label"] = ("#BAC8D8", "#40576D"),
        ["Accent"] = ("#61DBC4", "#087B6D"),
        ["AccentInk"] = ("#102823", "#FFFFFF"),
        ["Selected"] = ("#28514F", "#C7EAE3"),
        ["Selection"] = ("#357E74", "#A1D5CB"),
        ["Console"] = ("#080E17", "#F8FAFC")
    };
    private static readonly Dictionary<string, string> LegacyColors = new()
    {
        ["#0D141F"] = "Background", ["#111B29"] = "Sidebar", ["#192330"] = "Surface",
        ["#61DBC4"] = "Accent", ["#102823"] = "AccentInk", ["#91A3B8"] = "Muted",
        ["#AAB9CA"] = "Label", ["#080E17"] = "Console"
    };
    private static readonly List<(WeakReference<SolidColorBrush> Brush, string Key)> LocalBrushes = [];
    private static string? settingsPath;
    public static string Appearance { get; private set; } = "Dark";
    private static ImageSource? icon;
    public static ImageSource Icon => icon ??= BitmapDecoder.Create(new Uri("pack://application:,,,/CraftHarbor;component/Assets/CraftHarbor.ico"), BitmapCreateOptions.None, BitmapCacheOption.OnLoad).Frames[^1];

    private static Color ColorFor(string key) => (Color)ColorConverter.ConvertFromString(Appearance == "Dark" ? Palette[key].Dark : Palette[key].Light);
    public static SolidColorBrush Brush(string key)
    {
        key = LegacyColors.GetValueOrDefault(key, key);
        var brush = new SolidColorBrush(ColorFor(key)); LocalBrushes.Add((new(brush), key)); return brush;
    }
    public static void Load(string root)
    {
        settingsPath = Path.Combine(root, "settings.json");
        var appearance = "Dark";
        if (File.Exists(settingsPath)) appearance = JsonSerializer.Deserialize<Settings>(File.ReadAllText(settingsPath))?.Appearance ?? "Dark";
        Apply(appearance is "Light" ? "Light" : "Dark");
    }
    public static void Apply(string appearance, bool save = false)
    {
        if (appearance is not ("Dark" or "Light")) throw new ArgumentException("不明なテーマです。");
        if (save && settingsPath != null) SafeFiles.AtomicWrite(settingsPath, JsonSerializer.Serialize(new Settings(appearance), HarborStore.Json));
        Appearance = appearance;
        var resources = Application.Current.Resources;
        foreach (var key in Palette.Keys) resources[key] = new SolidColorBrush(ColorFor(key));
        foreach (var (brush, key) in LocalBrushes.ToArray()) if (brush.TryGetTarget(out var target)) target.Color = ColorFor(key);
        LocalBrushes.RemoveAll(x => !x.Brush.TryGetTarget(out _));
        // WPF's built-in text context menus and scroll-viewer corner use these keys.
        resources[SystemColors.WindowBrushKey] = resources["Input"];
        resources[SystemColors.WindowTextBrushKey] = resources["Ink"];
        resources[SystemColors.ControlBrushKey] = resources["Surface"];
        resources[SystemColors.ControlTextBrushKey] = resources["Ink"];
        resources[SystemColors.HighlightBrushKey] = resources["Selected"];
        resources[SystemColors.HighlightTextBrushKey] = resources["Ink"];
        resources[SystemColors.MenuBrushKey] = resources["Surface"];
        resources[SystemColors.MenuTextBrushKey] = resources["Ink"];
        resources[SystemColors.MenuBarBrushKey] = resources["Surface"];
        resources[SystemColors.MenuHighlightBrushKey] = resources["Selected"];
        resources[SystemColors.GrayTextBrushKey] = resources["Muted"];
        foreach (Window window in Application.Current.Windows) UpdateChrome(window);
    }
    public static void Attach(Window window)
    {
        window.Icon = Icon;
        window.SourceInitialized += (_, _) => UpdateChrome(window);
    }
    private static void UpdateChrome(Window window)
    {
        var handle = new WindowInteropHelper(window).Handle; if (handle == IntPtr.Zero) return;
        int dark = Appearance == "Dark" ? 1 : 0;
        _ = DwmSetWindowAttribute(handle, 20, ref dark, sizeof(int));
        var background = ColorFor("Background"); var ink = ColorFor("Ink");
        int caption = background.R | background.G << 8 | background.B << 16;
        int text = ink.R | ink.G << 8 | ink.B << 16;
        _ = DwmSetWindowAttribute(handle, 35, ref caption, sizeof(int));
        _ = DwmSetWindowAttribute(handle, 36, ref text, sizeof(int));
    }
    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
}
