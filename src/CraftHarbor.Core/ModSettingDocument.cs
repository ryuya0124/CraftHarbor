using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace CraftHarbor.Core;

public sealed record ModSetting(string Id, string Key, string Group, string Kind, string Value, int Start, int Length);

// Edits replace value spans only; unrelated keys, comments and whitespace remain intact.
public sealed class ModSettingDocument
{
    private readonly string text;
    public List<ModSetting> Fields { get; } = [];
    public ModSettingDocument(string text, string extension)
    {
        this.text = text;
        if (extension.Equals(".json", StringComparison.OrdinalIgnoreCase))
        {
            var bytes = Encoding.UTF8.GetBytes(text);
            var reader = new Utf8JsonReader(bytes);
            var path = new List<string>(); string key = "";
            while (reader.Read())
            {
                if (reader.TokenType == JsonTokenType.PropertyName) { key = reader.GetString()!; continue; }
                if (reader.TokenType == JsonTokenType.StartObject) { path.Add(key); key = ""; continue; }
                if (reader.TokenType == JsonTokenType.EndObject) { path.RemoveAt(path.Count - 1); continue; }
                var start = (int)reader.TokenStartIndex; string? value = null; string kind = "text";
                switch (reader.TokenType)
                {
                    case JsonTokenType.String: value = reader.GetString(); break;
                    case JsonTokenType.Number: value = Encoding.UTF8.GetString(reader.ValueSpan); kind = "number"; break;
                    case JsonTokenType.True: case JsonTokenType.False: value = reader.GetBoolean() ? "true" : "false"; kind = "bool"; break;
                    case JsonTokenType.StartArray:
                        using (var array = JsonDocument.ParseValue(ref reader))
                            if (array.RootElement.EnumerateArray().All(x => x.ValueKind == JsonValueKind.String && !(x.GetString() ?? "").Contains('\n')))
                            { value = string.Join("\n", array.RootElement.EnumerateArray().Select(x => x.GetString())); kind = "lines"; }
                        break;
                }
                if (value != null && key.Length > 0 && key != "DO_NOT_CHANGE_IT")
                {
                    var group = string.Join(" / ", path.Where(x => x.Length > 0));
                    int offset = Encoding.UTF8.GetCharCount(bytes.AsSpan(0, start));
                    int length = Encoding.UTF8.GetCharCount(bytes.AsSpan(start, (int)reader.BytesConsumed - start));
                    Fields.Add(new(start.ToString(), key, group, kind, value, offset, length));
                }
                key = "";
            }
        }
        else if (extension.Equals(".toml", StringComparison.OrdinalIgnoreCase) && !text.Contains("\"\"\"") && !text.Contains("'''"))
        {
            string group = "";
            foreach (Match line in Regex.Matches(text, @"[^\r\n]+"))
            {
                var section = Regex.Match(line.Value, @"^\s*\[([A-Za-z0-9_.-]+)\]\s*(?:#.*)?$");
                if (section.Success) { group = section.Groups[1].Value; continue; }
                var scalar = Regex.Match(line.Value, "^\\s*([A-Za-z0-9_-]+)\\s*=\\s*(true|false|[-+]?[0-9]+(?:\\.[0-9]+)?|\"(?:[^\"\\\\]|\\\\.)*\")\\s*(?:#.*)?$");
                if (!scalar.Success) continue;
                string raw = scalar.Groups[2].Value, kind = raw.StartsWith('"') ? "text" : raw is "true" or "false" ? "bool" : "number";
                string value;
                try { value = kind == "text" ? JsonSerializer.Deserialize<string>(raw)! : raw; } catch (JsonException) { continue; }
                var offset = line.Index + scalar.Groups[2].Index;
                Fields.Add(new(offset.ToString(), scalar.Groups[1].Value, group, kind, value, offset, raw.Length));
            }
        }
        if (Fields.Count > 500) throw new IOException("設定が500項目を超えます。テキスト編集を使用してください。");
    }
    public string Apply(IReadOnlyDictionary<string, string> changes)
    {
        if (changes.Keys.Any(id => Fields.All(f => f.Id != id))) throw new IOException("設定項目が見つかりません。");
        var output = new StringBuilder(text);
        foreach (var field in Fields.Where(f => changes.ContainsKey(f.Id)).OrderByDescending(f => f.Start))
        {
            var value = changes[field.Id]; string encoded;
            if (field.Kind == "bool") { if (value is not "true" and not "false") throw new IOException("有効・無効を選んでください。"); encoded = value; }
            else if (field.Kind == "number")
            {
                if (!Regex.IsMatch(value, @"^-?(0|[1-9][0-9]*)(\.[0-9]+)?([eE][+-]?[0-9]+)?$")) throw new IOException("数値を入力してください。");
                encoded = value;
            }
            else if (field.Kind == "lines") encoded = JsonSerializer.Serialize(value.Length == 0 ? Array.Empty<string>() : value.Replace("\r\n", "\n").Split('\n'));
            else encoded = JsonSerializer.Serialize(value);
            output.Remove(field.Start, field.Length).Insert(field.Start, encoded);
        }
        return output.ToString();
    }
    private static readonly Dictionary<string, string> Labels = new(StringComparer.OrdinalIgnoreCase)
    {
        ["modpackName"] = "配布するMOD構成の名前", ["modpackHost"] = "MOD構成の配信先", ["generateModpackOnStart"] = "起動時に同期データを生成",
        ["syncedFiles"] = "クライアントに同期するファイル", ["allowEditsInFiles"] = "クライアント側で編集を許可するファイル",
        ["forceCopyFilesToStandardLocation"] = "標準の保存先へ必ずコピーするファイル", ["nonModpackFilesToDelete"] = "構成に含まれない場合に削除するファイル",
        ["autoExcludeServerSideMods"] = "サーバー専用MODを配布対象から自動除外", ["autoExcludeUnnecessaryFiles"] = "不要なファイルを自動除外",
        ["requireAutoModpackOnClient"] = "クライアントにもAutoModpackを必須にする", ["nagUnModdedClients"] = "未導入のクライアントへ案内を表示",
        ["nagMessage"] = "未導入時の案内文", ["nagClickableMessage"] = "案内リンクの表示名", ["nagClickableLink"] = "案内リンクのアドレス",
        ["bindAddress"] = "配信の待受アドレス", ["bindPort"] = "配信の待受ポート", ["addressToSend"] = "クライアントへ通知するアドレス", ["portToSend"] = "クライアントへ通知するポート",
        ["disableInternalTLS"] = "内蔵の通信暗号化を無効にする", ["requireMagicPackets"] = "専用の接続確認パケットを必須にする",
        ["updateIpsOnEveryStart"] = "起動ごとにIPアドレスを更新", ["bandwidthLimit"] = "配信帯域の上限", ["validateSecrets"] = "接続用の秘密情報を検証", ["secretLifetime"] = "接続用の秘密情報の有効期間",
        ["selfUpdater"] = "MOD自身の自動更新", ["acceptedLoaders"] = "受け入れるローダー", ["enabled"] = "有効にする", ["debug"] = "詳細ログを出力", ["name"] = "名前", ["port"] = "接続ポート", ["password"] = "パスワード",
        ["general"] = "基本", ["server"] = "サーバー", ["client"] = "クライアント", ["network"] = "通信", ["performance"] = "負荷・性能", ["logging"] = "ログ", ["common"] = "共通", ["settings"] = "設定", ["extra"] = "追加設定"
    };
    public static string Label(string key) => Labels.GetValueOrDefault(key, "追加設定（翻訳未登録）");
    public static bool Known(string key) => Labels.ContainsKey(key);
}
