# 開発ガイド

## 必要環境

Windows x64 / .NET 10 SDK / Git。GitHub公開にはGitHub CLIを使用できます。VS Code、Visual Studio、Riderなど好みのエディターを使用してください。npm・Rust・Electronは不要です。

## 構造

```text
src/CraftHarbor.Core/       永続化、プロセス、取得、ファイル保護、診断
src/CraftHarbor.Desktop/    WPFアプリ、日本語UI
tests/CraftHarbor.Tests/    依存パッケージ不要の自動テスト
tests/CraftHarbor.UiTests/  WPF画面ナビゲーション・描画テスト
tests/CraftHarbor.LiveTests/ 実配布サービスへの取得テスト
tests/CraftHarbor.RealTests/ 明示的オプトインの実サーバー・接続検証
tests/CraftHarbor.FeatureTests/ WPF操作と実MOD・設定・パックの結合検証
scripts/package.ps1        portable / standalone ZIP生成
scripts/generate-icon.ps1  SVGパスから7解像度ICOを再生成
.github/workflows/ci.yml   Windowsビルド・テスト・成果物保存
docs/                      利用・運用・開発文書
```

UIに依存しないロジックはCoreへ置きます。プロセスの所有範囲、停止中だけのデータ変更、ダウンロードとZIPの境界検証を変更する場合は失敗系テストを追加してください。

## 日常のコマンド

```powershell
dotnet build src/CraftHarbor.Desktop -c Release
dotnet run --project tests/CraftHarbor.Tests -c Release
dotnet run --project tests/CraftHarbor.UiTests -c Release -- artifacts/preview.png
dotnet run --project src/CraftHarbor.Desktop
```

UIテストは独自の一時データで画面を構成し、OS上の別アプリを操作しません。コアテストはテストEXE自身を模擬サーバーとして起動します。マイクラの既存プロセスを探して停止しません。

実サービスのテストはネットワークと約200MBの一時ディスクを使うため、通常CIには含めません。

```powershell
dotnet run --project tests/CraftHarbor.LiveTests -c Release
```

このテストはサーバーJAR・MOD・Javaを取得し、Javaの `-version` を実行します。Minecraft本体を稼働させたりEULAへ同意したりはしません。

本体の実起動・入室・復元は別の `RealTests` で検証します。こちらだけはテスト用Node.jsとMineflayerが必要です。通常CIには含めず、[実サーバー検証の手順](REAL_SERVER_VALIDATION.md) に従って、新しい隔離フォルダと明示的なEULAオプトインを使用します。アプリのビルド・実行にNode.jsは不要です。

画面操作・MOD・設定は [FeatureTestsの手順](FEATURE_VALIDATION.md) を参照してください。こちらもNode.jsを使用し、通常CIはコンパイルのみです。テストは表示しないWPFコントロールを操作し、他アプリのウィンドウやキーボードフォーカスを操作しません。

## 配布

```powershell
./scripts/package.ps1
```

`artifacts/packages` にバージョン付きZIPと `SHA256SUMS.txt` を生成します。portableは.NETを外部ランタイムとして使用、standaloneは.NET込みsingle-fileです。WPFのトリミングは無効で、互換性を優先します。standaloneは初回にネイティブライブラリを.NETの一時キャッシュへ展開する場合があります。

バージョンは `Directory.Build.props` とpackage.ps1の引数、変更履歴を合わせます。署名証明書は未設定です。正式一般公開前にはコード署名とクリーンなWindows環境での動作検証を推奨します。

## GitHubの運用

mainを基準とし、変更はブランチ→PR→CIで管理します。CIはpush / PR / 手動起動に対応し、Windowsでビルド・コア/UIテスト・ZIP作成を実行します。GitHub Actionsの成果物保持は14日です。Releaseへのアップロードは維持管理者が検証後に行います。

ワールド、ダウンロードしたJAR、Java、ユーザー設定、ログ、認証情報はコミットしません。`.gitignore` はbin / obj / data / artifacts等を除外します。

## 外部API

- [Mojang version manifest](https://piston-meta.mojang.com/mc/game/version_manifest_v2.json)：バージョン、取得URL、Java要件、SHA1
- [Paper Downloads Service](https://docs.papermc.io/misc/downloads-service/)：安定ビルドの取得
- [Fabric Meta](https://meta.fabricmc.net/)：ローダー、インストーラー、サーバーランチャー
- [Modrinth API](https://docs.modrinth.com/api/)：検索、リリース、依存関係、ハッシュ
- [Adoptium API](https://api.adoptium.net/q/swagger-ui/)：Temurinパッケージ、SHA256

User-Agentは製品名・バージョン・GitHub URLを設定しています。API変更時はLiveTestsを使って確認します。
