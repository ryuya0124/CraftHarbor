# MOD・設定・プリセットの実機検証

## v0.1.5 設定保持の追加検証（2026-09-07 20:25–20:28 JST）

隔離したFabric / Forge / NeoForge / Paper / Adrenalineを再検証し、14回すべての起動・入室が成功しました。Minecraft 1.21.1、Java 21、各1536MB・2CPU、ループバック限定です。Fabric・Forge・NeoForgeのspark、PaperのChunky、Adrenalineの18 JARは下表と同じバージョンです。

- Fabric / Forge / NeoForge / Paperで、標準のプリセット適用後も変更済み設定を維持。同名設定を復元するモードでも、後から追加したconfig・world/serverconfigのファイルを維持。
- 復元後にMODコマンド、入室、日本語MOTD、難易度、ゲームモード、最大人数を確認。Chunkyの設定復元後は日本語ヘルプも確認。
- Adrenalineでは18 JARのプリセット復元後に再起動・入室を確認。
- Core 40件成功。SNBT/JSON5/JSONC/CFG/TOML/JS/ZS/AutoModpack設定の探索・保持、旧ZIPの既存優先マージ、空構成、途中失敗時のJARと設定のロールバックを含む。
- WPFテスト成功。追加形式を画面で選択・保存し、設定維持が既定でオン、既存のローダー選択・両テーマも確認。
- 利用中の既存Java PID 24260は開始・終了時とも稼働。テスト対象にしていません。

実サーバー試験は共通処理を呼び、確認ダイアログの選択はテストから指定しています。FTB/Create/Mekanism/KubeJS/CraftTweaker本体の網羅的な動作確認やAutoModpackのクライアント同期完了を示す結果ではありません。[設定対応の範囲](MOD_CONFIGURATIONS.md)。

以下はv0.1.3時点の検証記録です。


2026-09-07、Windows 11 x64 / .NET 10.0.400 / Temurin JRE 21.0.12.1 / Minecraft 1.21.1で実施。取得したファイルの確認だけでなく、WPFの画面コントロール、実Javaサーバー、Mineflayer 4.39.0の接続を組み合わせています。

## 構成と確認内容

| 構成 | 実際に導入したもの | 確認 |
|---|---|---|
| Fabric 0.19.5 | Fabric API + spark 1.10.109 | MODコマンド、有効・無効・再有効化、プリセット、設定、入室 |
| Forge 52.1.16 | spark 1.10.109 | 同上 |
| NeoForge 21.1.250 | spark 1.10.124 | 同上 |
| Paper 1.21.1 build 133 | Chunky 1.4.40 | プラグイン切替、プリセット、設定、入室、プラグイン設定の日本語化 |
| Adrenaline 26.4.2+mc1.21.1.fabric | 公開mrpackの18個のJAR、Fabric 0.19.3 | 空modsフォルダがある状態から取り込み、起動・入室、設定、パック構成の保存・復元・再起動 |

元のワールドは前回の隔離テストで作成したものをコピーしています。Adrenalineは新しいワールドです。MOD配布ファイルはModrinth APIから取得しSHA512を検証しました。パックは公開バージョンID `FCPQcVOt` を使用しています。

最終一括実行は日本時間08:52〜08:55に全構成成功。上の4サーバーは各3回、Adrenalineは2回、合計14回の実起動・入室・通常停止を確認しました。UTF-8の起動引数変更後、Vanilla単体でも08:56〜08:57に日本語コマンド・入室・保存・再起動・ZIP復元の3回の起動を再確認し、成功しています。

## 画面から実サーバーまでの確認

- **設定編集**: WPFのファイル選択欄・編集欄・「ファイルを保存」を操作し、保存内容、再表示、旧ファイルの履歴を確認。対象はproperties / JSON / TOML / YML / YAML / TXT / CONF。JSON以外のテスト用設定は保存・再読込を確認するもので、各形式の構文検証機能を意味しません。
- **設定の反映**: 日本語MOTD、max-players=3、gamemode=adventure、force-gamemode=true、difficulty=hardを保存。起動・再起動後にプロトコルのステータスと接続クライアントのゲーム状態で検証。
- **起動・停止・コンソール**: 画面の起動・安全停止・送信ボタンを実行。準備完了、終了コード0、日本語の `say`、チャット往復を確認。コマンドの応答がアプリのコンソール欄へ届くことも確認。
- **MOD・プラグイン**: 検索・依存解決・取得後、画面と共通のJAR追加処理で配置。有効時に `spark tps` / `chunky help` が成功。画面の「有効 / 無効」でJARを無効化して再起動すると、そのコマンドが存在しなくなることを確認。再有効化ボタンも確認。
- **プリセット**: 画面と共通の保存・適用処理を使用。mods / plugins / configの保存後にMODと設定を変更し、全体バックアップを伴うプリセット復元で元へ戻す。復元後のMODコマンド・設定・クライアント入室を再確認。
- **実プラグイン設定**: `plugins/Chunky/config.yml` を画面で `language: ja` に変更してプリセット保存。一度英語へ戻した後にプリセットを復元し、再起動後のヘルプが「Chunkyのコマンド」と日本語になることを確認。
- **接続**: スポーン、チャンク受信、指定座標のダイヤモンドブロック、チャット往復、ゲームモード、難易度、最大人数、日本語MOTDをクライアント側で照合。
- **稼働中の保護**: 設定保存、JAR追加・切替、プリセット保存・適用が、画面と共通の処理で拒否されることを各構成の実稼働中に確認。

ファイル選択ダイアログやプリセット名入力・確認ダイアログは自動操作していません。それらの入力値はテストから渡し、実際に画面が呼び出す共通処理を実行しています。WPFウィンドウは表示せず、ユーザーのゲームのフォーカスを奪いません。

## 実際に見つかった問題

| 問題 | v0.1.3の対応 |
|---|---|
| MOD画面で作られた空modsフォルダがあるとmrpackの最後の配置で失敗 | 準備済みフォルダとの切替に変更。配置直前にも既存ファイルを再検査し、元フォルダは `.empty-*` として保持 |
| Minecraft依存情報がないパックが配置後に失敗 | 依存情報を先に検証し、不正パックを配置前に拒否 |
| 空プリセットの展開先が作られず切替失敗 | 展開先を先に作成。空構成への切替でもワールドと全体バックアップを保持 |
| Paperの検索条件がMOD用で検索結果が空 | Paper/Folia用にplugin条件へ変更。オンライン導入先と一覧の初期選択もpluginsへ修正 |
| Javaの日本語出力がコンソールで文字化け | UTF-8のJavaシステムプロパティを設定し、アプリのリダイレクト文字コードと統一 |

Paperの既存ワールドでは、設定ファイルのdifficultyを変更しても元の難易度が残る挙動も再現しました。これはワールド側の設定との関係によるものです。本検証では画面のコンソールから `difficulty hard` を送って変更し、以後の再起動でもhardを確認しました。設定画面と操作ガイドにこの手順を追記しています。Paperの難易度については「設定ファイルだけで反映成功」とは扱いません。

## 異常系・回帰テスト

Coreテスト37件。既存のパス保護、ZIP slip、展開上限、ダウンロード検証、ポート競合、停止制御に加え、以下を確認しています。

- 不正JSONやフォルダ外パスを拒否し、元の設定と履歴を保護
- バッチ追加に同名JARがある場合、コピー開始前に拒否。切替先が既存の場合も両方を保持
- 不正なプリセットの内容を置換前に拒否。空プリセットでもワールドを保持
- mrpackの空フォルダ、依存情報欠落、キャンセル、配置先へのファイル追加
- 必須の固定バージョン依存を親より先に解決、任意依存の除外、ローダー不一致拒否
- 複数MODの途中でハッシュが不一致でも導入先を変更しない
- Paper / Folia / Fabricの検索条件を分離
- 日本語を含む子プロセスの入出力、Windowsの一時・継続ファイルロック

別途、8種類のローダー選択・保存・再表示、10画面のダーク/ライト表示、設定永続化、アイコンのWPFテストも実行しています。

## 隔離と未検証範囲

既存の利用中Javaプロセスは開始前・終了後に同じプロセスの生存を確認し、操作していません。テストは専用フォルダ、127.0.0.1、空きポート、Javaヒープ上限1536MB、認識CPU数2、BelowNormal優先度で直列実行。テスト用にだけonline-mode=falseを使用しています。

全MODの全組み合わせ、全バージョン、長期間・多人数負荷、認証付きの公開サーバー、Folia/QuiltのMOD構成、Java 8/11/17/25での実ゲーム起動、公式ゲームクライアントの手動操作は未検証です。Adrenalineの各最適化機能の性能を測定したわけでもありません。今回の結果は、表に記載した具体的な構成と操作の成功を示します。

## 再実行の手順

まず [RealTests](REAL_SERVER_VALIDATION.md) の手順でテスト用Node.js依存と隔離サーバーを準備します。FeatureTestsは、指定した作業用親フォルダの以下の**テスト生成物だけ**をコピーします。

```text
<fixture-parent>/real-servers-run2/java/           RealTestsのJava 21
<fixture-parent>/real-servers-run2/servers/       fabric / paper
<fixture-parent>/real-servers-run3/servers/       forge / neoforge
```

作成する場合はRealTestsの出力先を上記名にし、run2に `vanilla,paper,fabric`、run3に `forge,neoforge` を指定してください。普段遊ぶサーバーのフォルダを代用しないでください。

```powershell
# $fixtureParent / $clientScript / $nodeModules は上記のテスト環境を指定
$newRoot = Join-Path $fixtureParent ('feature-' + [guid]::NewGuid().ToString('N'))
dotnet run --project tests/CraftHarbor.FeatureTests -c Release -- $newRoot $fixtureParent $clientScript $nodeModules --accept-eula
# 最後に paper,adrenaline 等を付けると構成を限定できます
```

EULAを確認した担当者の明示的な `--accept-eula` が必要です。通常CIはこのテストをビルドするだけで実行しません。結果は `results.json`、`*-client.json`、各構成のlogsに残します。JAR・ワールド・Java・生ログはGitにコミットせず、テスト依存は配布アプリに同梱しません。

配布情報: [spark](https://modrinth.com/plugin/spark)、[Chunky](https://modrinth.com/plugin/chunky)、[Adrenaline](https://modrinth.com/modpack/adrenaline)。
