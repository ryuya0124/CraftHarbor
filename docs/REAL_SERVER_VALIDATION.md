# 実サーバー検証 — 2026-09-07

Windows 11 x64、.NET 10.0.400、Temurin JRE 21.0.12.1、Minecraft Java Edition 1.21.1で実施しました。5種類すべてで、アプリと同じ `ServerRuntime` と `SafeFiles` を使う実行・入室・保存・再起動・復元の検証が成功しました。

| 種類 | 構成 | 起動 / 入室 | 保存・再起動 | ZIP復元後の入室・ブロック確認 |
|---|---|---|---|---|
| Vanilla | 1.21.1 | 3 / 3 成功 | 成功 | 成功 |
| Paper | 1.21.1 build 133 | 3 / 3 成功 | 成功 | 成功 |
| Fabric | Loader 0.19.5 + Fabric API 0.116.17+1.21.1 | 3 / 3 成功 | 成功 | 成功 |
| Forge | 1.21.1-52.1.16、追加MODなし | 3 / 3 成功 | 成功 | 成功 |
| NeoForge | 21.1.250、追加MODなし | 3 / 3 成功 | 成功 | 成功 |

Forge・NeoForgeは公式MavenのインストーラーをSHA1照合後に実行し、生成されたサーバーを別の管理フォルダへコピーしました。起動引数は `@user_jvm_args.txt`、`@libraries/…/win_args.txt`、`nogui` の3要素です。アプリの手動取り込み・カスタム引数の経路を検証しています。自動インストーラーの画面はまだありません。

## 各種類で確認した操作

1. 新しいフラットワールドを作成し、`Done` とローカルTCP待受を確認。
2. Minecraftプロトコルでステータスを取得。Mineflayer 4.39.0のクライアント `HarborProbe` がログインし、スポーン・チャンク受信を確認。
3. コンソールから `list` を送り、接続者名とステータスのオンライン人数1を確認。クライアントのチャットとコンソールの `say` が往復することを確認。
4. 座標 `(0,-60,0)` にダイヤモンドブロックを置き、クライアント側でブロック種を確認。`save-all flush` の完了を待ち、`stop` で終了コード0とポート解放を確認。
5. サーバー全体をZIPへバックアップ。再起動・再入室し、保存したダイヤモンドブロックが残っていることを確認。
6. 同じ場所を金ブロックへ変更して保存・停止。先ほどのZIPを復元して再起動・再入室し、ダイヤモンドブロックへ戻っていることを確認。通常停止して終了。

最終成功ランはVanilla/Paper/Fabricが日本時間08:12〜08:13、Forge/NeoForgeが08:16〜08:18。合計15回の実起動・入室・通常停止です。

## 見つかった不具合と修正

最初のFabric検証で、復元ZIPの展開後にWindowsがフォルダ名の切替を一時的に拒否しました。旧データへの巻き戻しは成功しましたが、復元操作自体は失敗しました。

v0.1.2ではWindowsのアクセス拒否・共有違反・ロック違反に限り、各フォルダ移動を最大約3秒再試行します。データを削除して回避する処理はありません。復元と巻き戻しの両方が失敗した場合は、保持しているデータの場所をエラーに含めます。

実サーバーでの再実行に加え、Windowsの実ファイルハンドルを使って、ロック解除後の成功と、ロックが続く場合の時間内の失敗・元データと展開済みデータの保持を確認しました。コアテストは25件成功です。

## 隔離・負荷・検証の限界

- 利用中だったJavaプロセスは各ランの開始前・終了後に同じプロセスが生存していることを確認。既存ワールド・設定・ポート・プロセスをテスト対象にしていません。
- テストは直列、各サーバーのJavaヒープ512〜1536MB、認識CPU数2、優先度BelowNormal。`127.0.0.1` だけにバインドし、空きポートを使用。RCON・queryは無効です。
- テスト用ボットのため隔離サーバーのみ `online-mode=false` / `enforce-secure-profile=false`。インターネット公開や認証付き接続を検証したものではありません。
- Mineflayerによる実プロトコル接続です。公式ゲーム画面の手動操作や長時間の多人数プレイ、Forge/NeoForgeの追加MOD・大型パック、全バージョン、Folia/Quiltの本体実行は未検証です。
- 実サーバーテストはCore経由。別途WPFテストで8種類の選択・保存・再表示、両テーマの全10画面などを確認しています。全操作をマウスで通した検証ではありません。
- 記録中のメモリ値はJavaサーバーの値です。軽量な管理アプリの使用量と混同しないでください。テスト用Node.js依存はアプリに同梱しません。

## 再実行

十分な空き容量を用意してください。Java、各サーバーのライブラリ、ワールド、ZIP、復元前フォルダを保管するため数GBを使用します。下記はリポジトリ直下で実行し、`$testRoot` は毎回新しくします。

```powershell
$testRoot = Join-Path $env:TEMP ('CraftHarbor-real-' + [guid]::NewGuid().ToString('N'))
$clientRoot = Join-Path $env:TEMP ('CraftHarbor-client-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $clientRoot | Out-Null
Copy-Item tests/CraftHarbor.RealTests/client/package*.json $clientRoot
npm ci --prefix $clientRoot --ignore-scripts --no-audit --no-fund
$clientScript = (Resolve-Path tests/CraftHarbor.RealTests/client/verify.cjs).Path
# Minecraft EULAを読み、同意する担当者だけが次を実行
dotnet run --project tests/CraftHarbor.RealTests -c Release -- $testRoot $clientScript "$clientRoot/node_modules" --accept-eula
# 最後に forge,neoforge 等を付けると種類を限定できます
```

`--accept-eula` がない場合は起動しません。通常CIでは本テストを実行しません。`results.json`、種類ごとのサーバーログ、各段階の `*-client.json` をテストフォルダに保存します。成功した場合も再調査用にデータを残します。JAR・ワールド・Java・生ログはGitへコミットしません。

再実行時はVanilla/Paper/Fabric/Javaの配布APIが返す内容が変わる可能性があります。今回使用したバージョンは上表、Forge/NeoForgeの固定値はテストソースに記録しています。

公式情報: [Minecraft EULA](https://www.minecraft.net/en-us/eula)、[Forge配布](https://files.minecraftforge.net/net/minecraftforge/forge/index_1.21.1.html)、[NeoForgeサーバー導入](https://docs.neoforged.net/user/docs/server/)、[Mineflayer](https://github.com/PrismarineJS/mineflayer)。
