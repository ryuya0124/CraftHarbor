# CraftHelm 名称の予備調査

調査日: 2026-09-07。採用名は **CraftHelm（クラフトヘルム）**、説明名は **Minecraft Server Manager**。Minecraftの非公式サーバー管理ツールです。

## 調査結果

| 対象 | 条件 | 結果 |
|---|---|---|
| 一般Web | CraftHelm の引用符付き検索 | Diablo Wikiのアイテム用テンプレート名に使用例あり。同名のサーバー管理アプリ・企業は今回の検索では確認できず |
| GitHub公開リポジトリ | CraftHelm in:name | 改名前に0件 |
| J-PlatPat 出願・登録情報 | 商標（検索用）= CraftHelm クラフトヘルム、区分など他条件なし（OR検索） | 0件 |
| J-PlatPat 出願・登録情報 | 称呼（類似検索）= クラフトヘルム、他条件なし | 5件。以下参照 |

[J-PlatPat商標検索](https://www.j-platpat.inpit.go.jp/t0100)をブラウザで実施しました。一般Webの既存使用例は[Diablo Wikiのテンプレート一覧](https://diablo.fandom.com/wiki/Category%3AItem_Templates)です。検索結果がないことは、未登録の既存使用がないことの証明ではありません。

### 称呼の類似検索で表示された商標

| 番号 | 表記 | 一覧で確認した区分・状態 |
|---|---|---|
| 登録6206909 | CRAFTALE／クラフタル | 35、43。登録継続 |
| 商願2026-001614 | CRAFTALE（クラフテイル） | 35、36、40ほか。一覧では全区分を確認できず。審査中 |
| 登録4396632 | KRAFTWERK | 08。登録継続 |
| 国際登録0642441 | Kraftwerk | 09、16、18ほか。登録継続 |
| 国際登録1811422 | KRAFTWERK | 12、21、25。登録継続 |

[国際登録0642441の国内照会ページ](https://www.j-platpat.inpit.go.jp/c1801/TR/JP-0642441-20060308/49/ja)も確認しました。国内の指定商品・役務の全文は今回のブラウザでは詳細画面が開かず、未確認です。したがって第9類を含む結果について「当アプリと無関係」「問題なし」とは判定していません。

上記は検索システムが返した候補であり、法的に類似・非類似と判定された一覧ではありません。特許庁は、外観・称呼・観念と商品・役務の類似性を総合して判断すると説明しています。[商標制度概要](https://www.jpo.go.jp/system/trademark/gaiyo/seidogaiyo/chizai08.html)。

## 採用判断と限界

ユーザーが選んだCraftHelmを採用します。同一表記の国内商標は今回の検索条件で見つかりませんでしたが、**類似商標との抵触、登録可能性、非侵害を保証する調査ではありません**。指定商品・役務の精査、海外登録、未登録商標、図形商標、データ更新の遅れは未解決です。商用展開・出願の判断には対象国と商品・役務を定めた専門家の確認が必要です。[特許庁の調査案内](https://www.jpo.go.jp/system/basic/trademark/index.html)、[USPTOの包括的調査案内](https://www.uspto.gov/trademarks/search/comprehensive-clearance-search-similar-trademarks)。

旧名CraftHarborには衣料向けECプラットフォームの使用例があり、混同を避けるため変更しました。会社名の一致だけで侵害と断定したものではありません。[JST J-GLOBAL](https://jglobal.jst.go.jp/en/detail?JGLOBAL_ID=202502233993018142)。

Minecraftは説明用の副題とし、当アプリ固有の名前を主表示にします。Mojang/Microsoftの公式製品・公認製品ではありません。[Minecraft Usage Guidelines](https://www.minecraft.net/usage-guidelines)。

## 更新互換性

画面、Windowsのアプリ表示名、スタートメニュー、新しい配布物とリポジトリはCraftHelmへ変更します。既存データを移動せず、Documents/CraftHarbor/data、CRAFTHARBOR_DATA、内部実行ファイルCraftHarbor.exe、名前空間、インストール識別子を維持します。既存のインストール先も引き継ぎます。

旧更新プログラム向けに同じインストーラーを旧ファイル名でも配布し、SHA256SUMSへ両方を記録します。新しい更新プログラムは新名を優先し、旧名の配布物も読めます。改名のために稼働中のサーバーを終了しません。
