using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace CraftHarbor.Desktop;

// Translate presentation only. SelectedItem, file names and command strings stay unchanged.
public sealed class JapaneseDisplay : IValueConverter
{
    public static string Label(string value) => value switch
    {
        "vanilla" => "バニラ（標準サーバー）", "custom" => "独自サーバー（手動設定）",
        "paper" => "Paper", "fabric" => "Fabric", "folia" => "Folia", "forge" => "Forge", "neoforge" => "NeoForge", "quilt" => "Quilt",
        "mods" => "MOD", "plugins" => "プラグイン", "Dark" => "ダーク", "Light" => "ライト",
        "list" => "参加者を確認", "save-all" => "ワールドを保存", "whitelist list" => "参加許可リストを確認",
        "automodpack" => "同期機能のヘルプ", "automodpack host" => "同期配信の管理",
        "automodpack generate" => "同期データを再生成", "automodpack config reload" => "同期設定を再読み込み",
        "deflate" => "標準圧縮", "lz4" => "高速圧縮", "none" => "圧縮しない",
        _ => value
    };
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var text = value?.ToString() ?? "";
        if (text == "server.properties") return "サーバーの基本設定";
        var parts = text.Replace('\\', '/').Split('/');
        if (parts.Length > 1)
        {
            parts[0] = parts[0] switch { "config" => "共通MOD設定", "defaultconfigs" => "新規ワールドの初期設定", "automodpack" => "クライアント同期", "plugins" => "プラグイン設定", "kubejs" => "KubeJSスクリプト", "scripts" => "追加スクリプト", _ => parts[0] };
            return string.Join(" › ", parts);
        }
        return Label(text);
    }
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
    public static void Apply(ItemsControl control)
    {
        var text = new FrameworkElementFactory(typeof(TextBlock));
        text.SetBinding(TextBlock.TextProperty, new Binding { Converter = new JapaneseDisplay() });
        control.ItemTemplate = new DataTemplate { VisualTree = text };
    }
}
