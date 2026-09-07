# AutoModpackとの併用

AutoModpackはサーバー側の構成をクライアントへ配布するMODです。同期・証明書の確認・クライアント側の適用はAutoModpackが担当し、CraftHarborはサーバー側の導入・設定編集・コンソール・バックアップを担当します。

## CraftHarborでの操作

1. 対応するMinecraft・ローダーのAutoModpackを、サーバーと各プレイヤーのクライアントへ導入します。サーバー側はMOD画面のModrinth検索またはJAR追加を利用できます。
2. サーバーを起動して設定を生成し、停止します。「設定ファイル」で `automodpack/automodpack-server.json` を編集します。JSON検査と保存前の履歴が適用されます。
3. 再起動し、「コンソール」から `automodpack` / `automodpack host` で状態を確認します。設定ファイルがあるサーバーには専用ボタンが表示されます。
4. MODや同期する設定を変更したら、停止中に保存して再起動するか、対象を確認して `automodpack generate` で配布データを再生成します。`automodpack config reload` も送信できます。
5. AutoModpack入りクライアントから接続し、配布元の証明書・構成を確認してインストールを進めます。必要な再起動はAutoModpackの表示に従ってください。

ゲームサーバーが起動中でも、CraftHarborのコンソール欄からコマンドを送れます。`list`、`say こんにちは`、`save-all` などを入力してEnterまたは「送信」を押します。対象は左側で選択した、**CraftHarborが起動したサーバーだけ**です。別アプリから起動した既存Javaの標準入力へ後から接続する機能はありません。

## 同期範囲とプリセット

`syncedFiles` はクライアントへ配布するファイルの指定です。必要なMODとクライアント向け設定を選びます。サーバー全体、ワールド、バックアップ、認証情報を指定しないでください。クライアント専用の配布物は、対応バージョンの公式説明に従って `automodpack/host-modpack/main/` へ配置できます。サーバーのmodsフォルダへクライアント専用MODを入れる必要はありません。

CraftHarborの通常プリセットはmods / plugins / configのみです。AutoModpack独自の設定、配布専用ファイル、生成済み配布情報は含みません。プリセット変更後はAutoModpackの同期範囲を確認して配布情報を再生成してください。**サーバー全体バックアップ・復元にはautomodpackフォルダも含まれます。** 証明書なども含むため、全体バックアップをクライアント配布用パックにしないでください。

設定画面が追加表示するのは既知のサーバー設定ファイルだけです。automodpack配下のクライアント情報や秘密情報をまとめて設定候補に列挙しません。

## ネットワークとバージョン

ゲーム接続先とMODダウンロード先は同じとは限りません。別ポートを使う構成では、そちらへの経路も必要です。CraftHarborはFirewallやルーターを自動変更しません。`bindAddress` はPC側の待受、クライアントへ伝えるアドレスは到達先の設定であり、用途が違います。

今回参照したリリース4.0.6には `bindPort`、`addressToSend`、`portToSend`、`requireMagicPackets` があります。GitHubのmainブランチの説明には `connectionMode` や `advertisedEndpointHost` など、異なる設定名があります。導入版が生成したJSONと、その版の説明に従ってください。CraftHarborは独自の固定スキーマでこれらを上書きしません。

## 検証範囲

WPFテストでサーバー設定の検出・日本語編集・保存履歴、クライアント設定を候補から除外すること、専用コマンドの表示を確認しています。クライアントへの自動ダウンロード・証明書確認・MOD適用を最後まで検証したものではありません。

2026-09-07、Minecraft 1.21.1 / Fabric / AutoModpack 4.0.6 / Java 21の隔離環境で、3回の実起動・通常停止、`automodpack` の応答、`automodpack generate` の完了、同期対象JSONを含む配布情報の生成、全体バックアップ・復元に成功しました。結果は `SERVER_ONLY_PASS` と記録しています。利用中の既存Javaプロセスは操作せず継続稼働を確認しました。

自作のMinecraftステータス検査はAutoModpack入り構成で接続が切断されました。同一ポート・別ポート双方で再現し、原因は未特定です。そのため通常クライアント接続・クライアント同期は成功扱いにせず、上記の最終ランはサーバー管理部分に限定しました。実際のAutoModpack入りクライアントでの同期確認は残っています。

テスト用 `RealTests` は通常の3つのパス引数に続けて `--accept-eula fabric --automodpack` を指定するとAutoModpack 4.0.6を追加できます。専用のローカル待受ポートを使い、テスト環境だけ `requireAutoModpackOnClient=false` にします。このモードは接続検査を成功扱いせず、サーバー側の配布情報生成・起動停止・復元に限定します。利用中のサーバーは対象にしません。

公式情報: [導入手順](https://github.com/Skidamek/AutoModpack/blob/main/docs/quick-start.mdx)、[設定](https://github.com/Skidamek/AutoModpack/blob/main/docs/configuration/server-config.mdx)、[コマンド](https://github.com/Skidamek/AutoModpack/blob/main/docs/commands/commands.mdx)、[配布ファイルの構成](https://github.com/Skidamek/AutoModpack/blob/main/docs/technicals/modpack-creation.mdx)。mainの内容は導入済みリリースと異なる場合があります。
