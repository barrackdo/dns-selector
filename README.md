# DNS Selector

Windows 向けの DNS 切り替えツールです。パブリック DNS をワンタッチで適用したり、DHCP（自動取得）に戻したりできます。

内部的には PowerShell の `Set-DnsClientServerAddress` を呼び出しています。

AI に 100% コーディングしてもらいました。
LTS の .NET 10 ではなく STS の .NET 11 を使っているのもそれが理由です。
「.NET 10」「.NET 11」いずれもサポート終了日は2028年11月14日なのでそのまま開発しました。

## 動作要件

- Windows 10 / 11
- .NET 11 デスクトップランタイム

DNS サーバーの設定を変えるため管理者権限が必要です。`app.manifest` で `requireAdministrator` を指定しているため、起動時に UAC の確認が表示されます。

## インストール
1. [Release](https://github.com/barrackdo/dns-selector/releases) から最新版の ZIP ファイルをダウンロードし展開するだけです。

## .NET 11.0 ランタイムのインストール
.NET 11 デスクトップランタイムがインストールされてない場合はインストールしてください。

1. マニュアル操作でインストール
[.NET 11.0 ランタイムのダウンロード](https://dotnet.microsoft.com/ja-jp/download/dotnet/11.0/runtime) の .NET デスクトップ ランタイムをダウンロードしインストールする。

2. WinGet でインストール
```
winget install --id Microsoft.DotNet.DesktopRuntime.11
```

## 使い方

1. アプリを起動する（UAC を許可）
2. 上部のコンボボックスでネットワークアダプターを選択
   - 物理・接続済みアダプターが優先表示され、仮想アダプター（Hyper-V など）は後ろに並びます
   - 前回選択したアダプターが記憶され、次回起動時に自動選択されます
3. 一覧から使いたい DNS プロバイダーを選択
   - 応答速度（ms）が表示されます（未計測・計測失敗の場合は「未計測」）
   - 詳細情報（説明、Web サイト、場所、測定時刻）はリスト下部に表示されます
4. 「適用する」を押す
5. 「DHCPに戻す」で自動取得に戻す

現在の DNS 設定は上部の「現在のDNS設定」に表示されます。

### 速度テスト

詳細パネルの「速度テスト」ボタンで、選択中のプロバイダーの DNS サーバーに対して UDP クエリを送り、平均レイテンシを計測します。

- 複数のサーバー（IPv4・IPv6）に対して計測し、平均を取得
- 結果は `dns-providers.json` に保存され、リストの「応答速度」列と詳細パネルに反映
- IP アドレスから DNS サーバーの位置情報（国）を取得し、詳細パネルに表示

### ダークモード

- システム / ライト / ダーク の 3 種類から選択可能
- 設定は `dns-selector-settings.json` に保存され、次回起動時に復元

## プロファイルの編集

DNS プロバイダーの一覧は、実行ファイルと同じフォルダにある `dns-providers.json` で管理します。

```json
{
  "profiles": [
    {
      "name": "Cloudflare",
      "description": "高速でプライバシー重視のパブリック DNS。1.1.1.1 で知られる。",
      "website": "https://1.1.1.1/",
      "ipv4": ["1.1.1.1", "1.0.0.1"],
      "ipv6": ["2606:4700:4700::1111", "2606:4700:4700::1001"]
    }
  ]
}
```

- `description`、`website`、`latencyMs`、`location`、`testedAt` は省略可
- 編集後はアプリを再起動すると反映されます
- ファイルが無い場合は既定のプロファイルが自動生成されます

## 位置情報データ

位置情報はローカルのみで解決されます。以下の優先順位で参照されます。

1. **MaxMind DB（`.mmdb`）**：実行ファイルと同じフォルダに `GeoIP.mmdb` があれば使用（都市情報あり／なしの両方に対応）
2. **ipv4.fetus.jp の nginx-geo データ（組み込み）**：`Resources\GeoData\nginx-geo.txt` としてアセンブリに埋め込み。MMDB が無い場合や MMDB で解決できない IPv4 に対して使用（IPv4 のみ）

`GeoIP.mmdb` は本番配布には含まれません。
[DB-IP Lite](https://db-ip.com/db/lite.php) の IP to City Lite でテストを行いました。

## ビルド

```powershell
dotnet build
```

> `dotnet run` は昇格が必要なため UAC の都合で失敗することがあります。
> その場合は `bin\Debug\net11.0-windows\DnsSelector.exe` を直接実行してください。

## 配布用に発行

```powershell
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true
```

出力は `bin\Release\net11.0-windows\win-x64\publish\` に生成されます。

## 技術構成

- **言語**: C# (.NET 11) + WPF
- **設定ファイル**: `dns-providers.json`（DNS プロバイダー）、`dns-selector-settings.json`（UI 設定）
- **テーマ**: `Themes/Light.xaml`, `Themes/Dark.xaml` による DynamicResource ベースの切替
- **速度測定**: `System.Net.Sockets.Socket` による UDP DNS 問い合わせ（raw query）
- **位置情報**: ローカル MaxMind DB（`GeoIP.mmdb` など）と組み込みの ipv4.fetus.jp データ（nginx-geo 形式、IPv4 のみ）のみを使用。外部 API には一切アクセスしない。

## ライセンス
MIT License

### 利用データ・ライブラリ
- MaxMind.GeoIP2 (Apache License 2.0)
  - https://github.com/maxmind/GeoIP2-dotnet

- ipv4.fetus.jp データ (CC0-1.0)
  - https://ipv4.fetus.jp/
  - https://github.com/fetus-hina/ipv4.fetus.jp-exports