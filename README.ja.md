# NoticeGenerator

.NETプロジェクトのNuGetパッケージ依存関係のライセンス情報を含むNOTICE.mdファイルを生成するコマンドラインツールです。

## 概要

NoticeGeneratorは、.NETプロジェクトまたはソリューションを自動的にスキャンし、すべてのNuGetパッケージ依存関係のライセンス情報を取得して、包括的なNOTICE.mdファイルを生成します。これは、帰属表示を要求するオープンソースライセンスへの準拠に特に有用です。

## 機能

- 🔍 **自動パッケージ検出**: `dotnet list package`を使用してすべてのNuGet依存関係を検索
- 📄 **ライセンステキスト取得**: NuGetパッケージ、GitHubリポジトリ、外部ソースから完全なライセンステキストを取得
- ⚡ **並列処理**: より高速な実行のための並行NuGet APIリクエスト
- 🎯 **柔軟なスコープ**: トップレベルと推移的依存関係の両方をサポート
- 📊 **進捗追跡**: 詳細なステータス情報を含むリアルタイム進捗表示
- 🎨 **リッチコンソール出力**: Spectre.Consoleによる美しいコンソールインターフェース
- 🔧 **設定可能な出力**: カスタマイズ可能な出力ファイルパスとフォーマットオプション

## インストール

### 前提条件

- .NET 10.0以降

### .NET Toolとしてインストール

#### GitHub Packagesから

```bash
# GitHub Packagesソースを追加（初回のみ）
dotnet nuget add source "https://nuget.pkg.github.com/MareMare/index.json" \
  --name "MareMare GitHub Packages" \
  --username "token" \
  --password "YOUR_GITHUB_TOKEN" \
  --store-password-in-clear-text

# ツールをインストール
dotnet tool install MareMare.NoticeGenerator --local --prerelease
```

#### PowerShellスクリプトの使用（開発時推奨）

`sandbox/run-tools.ps1`スクリプトは、ツールのインストールと実行を自動化する方法を提供します：

```powershell
# sandboxディレクトリに移動
cd sandbox

# デフォルト設定で実行
.\run-tools.ps1

# カスタム引数で実行
.\run-tools.ps1 -- --project ../src --scope top --output THIRD_PARTY_NOTICES.md
```

### ソースからビルド

```bash
git clone https://github.com/MareMare/NoticeGenerator.git
cd NoticeGenerator
dotnet build -c Release
```

### スタンドアロン実行ファイルの発行

Windows x64用の自己完結型単一ファイル実行ファイルを作成するには：

```bash
dotnet publish .\src\NoticeGenerator -c Release -r win-x64 -p:PublishSingleFile=true -p:SelfContained=true -p:PublishReadyToRun=false -o artifacts
```

このコマンドは以下を実行します：
- Releaseコンフィギュレーションでプロジェクトをビルド
- Windows x64ランタイム（`win-x64`）をターゲット
- 単一実行ファイルを作成（`PublishSingleFile=true`）
- すべての依存関係を含める（`SelfContained=true`）
- `artifacts`ディレクトリに実行ファイルを出力

生成された実行ファイルは、.NETがインストールされていないWindows x64システムでも配布・実行できます。

## 使用方法

### 基本的な使用方法

#### スタンドアロン実行ファイルを使用

```powershell
.\NoticeGenerator.exe --help
```

#### インストール済み.NETツールとして使用

```bash
dotnet tool run NoticeGenerator -- --help
```

#### ソースから使用

```bash
dotnet run --project src/NoticeGenerator -- --help
```

#### PowerShellスクリプトを使用

```powershell
cd sandbox
.\run-tools.ps1 -- --help
```

### コマンドラインオプション

```powershell
.\NoticeGenerator.exe [OPTIONS]
```

#### オプション

| オプション | 説明 | デフォルト |
|-----------|------|-----------|
| `-p, --project <PATH>` | 分析するプロジェクトまたはソリューションのパス | `.`（現在のディレクトリ） |
| `-s, --scope <SCOPE>` | パッケージスコープ：`all`（推移的含む）または`top`（トップレベルのみ） | `all` |
| `--no-version` | パッケージ識別子からバージョンを省略（グループ化に有用） | `false` |
| `-o, --output <FILE>` | 生成されるNOTICE.mdの出力ファイルパス | `NOTICE.md` |
| `--concurrency <N>` | 並行NuGet APIリクエスト数（1-16） | `4` |

### 例

#### スタンドアロン実行ファイルを使用

```powershell
# 特定のプロジェクトディレクトリを分析
.\NoticeGenerator.exe --project ./src

# トップレベルパッケージのみのNOTICEを生成
.\NoticeGenerator.exe --project ./src --scope top

# パッケージ名からバージョン情報を省略
.\NoticeGenerator.exe --project ./src --no-version

# カスタム出力ファイルとスコープ
.\NoticeGenerator.exe --project ./src --scope top --output THIRD_PARTY_NOTICES.md

# より高速な処理のための高い並行性
.\NoticeGenerator.exe --project ./src --concurrency 8
```

#### PowerShellスクリプトを使用

```powershell
# 基本的な使用
.\run-tools.ps1

# カスタム引数付き
.\run-tools.ps1 -- --project ../src --scope top --output THIRD_PARTY_NOTICES.md
```

#### ソースから使用

```bash
# 特定のプロジェクトディレクトリを分析
dotnet run --project src/NoticeGenerator -- --project ./src

# トップレベルパッケージのみのNOTICEを生成
dotnet run --project src/NoticeGenerator -- --project ./src --scope top

# カスタム出力ファイルとスコープ
dotnet run --project src/NoticeGenerator -- --project ./src --scope top --output THIRD_PARTY_NOTICES.md
```

## 出力形式

生成されるNOTICE.mdファイルには以下が含まれます：

- **パッケージ情報**: 名前、バージョン、作者、説明
- **ライセンス詳細**: ライセンス式、著作権情報
- **完全なライセンステキスト**: 利用可能な場合の完全なライセンステキスト
- **ソースリンク**: パッケージページ、プロジェクトリポジトリ、SPDXライセンスページへのリンク
- **エラー報告**: ライセンス情報を取得できなかったパッケージの明確な表示

## 動作原理

1. **パッケージ検出**: `dotnet list package`を実行してすべてのNuGet依存関係を列挙
2. **メタデータ取得**: NuGet APIに並列でクエリしてパッケージメタデータを取得
3. **ライセンステキスト抽出**: 以下からライセンスファイルをダウンロード：
   - パッケージ内容（.nupkgファイル）
   - GitHubリポジトリのLICENSEファイル（実際の著作権表示を含む）
   - 一般的なライセンスのSPDX標準ライセンステキスト
   - パッケージメタデータで指定された外部URL
4. **レポート生成**: すべての情報を構造化されたNOTICE.mdファイルにコンパイル

## 依存関係

- **Microsoft.Extensions.Http**: HTTPクライアントファクトリと拡張機能
- **NuGet.Protocol**: NuGet APIクライアントライブラリ
- **Spectre.Console.Cli**: リッチコンソールアプリケーションフレームワーク

## ライセンス

このプロジェクトはMITライセンスの下でライセンスされています - 詳細は[LICENSE](LICENSE)ファイルを参照してください。
