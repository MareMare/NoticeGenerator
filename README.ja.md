# NoticeGenerator

.NETプロジェクトのNuGetパッケージ依存関係のライセンス情報を含むNOTICE.mdファイルを生成するコマンドラインツールです。

## 概要

NoticeGeneratorは、.NETプロジェクトまたはソリューションを自動的にスキャンし、すべてのNuGetパッケージ依存関係のライセンス情報を取得して、包括的なNOTICE.mdファイルを生成します。これは、帰属表示を要求するオープンソースライセンスへの準拠に特に有用です。

## 機能

- 🔍 **自動パッケージ検出**: `dotnet list package`を使用してすべてのNuGet依存関係を検索
- 📄 **ライセンステキスト取得**: NuGetパッケージと外部ソースから完全なライセンステキストを取得
- ⚡ **並列処理**: より高速な実行のための同時NuGet APIリクエスト
- 🎯 **柔軟なスコープ**: トップレベルと推移的依存関係の両方をサポート
- 📊 **進捗追跡**: 詳細なステータス情報を含むリアルタイム進捗表示
- 🎨 **リッチコンソール出力**: Spectre.Consoleによる美しいコンソールインターフェース
- 🔧 **設定可能な出力**: カスタマイズ可能な出力ファイルパスとフォーマットオプション

## インストール

### 前提条件

- .NET 10.0以降

### ソースからビルド

```bash
git clone https://github.com/MareMare/NoticeGenerator.git
cd NoticeGenerator
dotnet build -c Release
```

## 使用方法

### 基本的な使用方法

現在のディレクトリのNOTICE.mdファイルを生成：

```bash
dotnet run --project src/NoticeGenerator
```

### コマンドラインオプション

```bash
notice-generator [OPTIONS]
```

#### オプション

| オプション | 説明 | デフォルト |
|-----------|------|-----------|
| `-p, --project <PATH>` | 分析するプロジェクトまたはソリューションへのパス | `.` (現在のディレクトリ) |
| `-s, --scope <SCOPE>` | パッケージスコープ: `all` (推移的含む) または `top` (トップレベルのみ) | `all` |
| `--no-version` | パッケージ識別子からバージョンを省略 (グループ化に有用) | `false` |
| `-o, --output <FILE>` | 生成されるNOTICE.mdの出力ファイルパス | `NOTICE.md` |
| `--concurrency <N>` | 同時NuGet APIリクエスト数 (1-16) | `4` |

### 例

#### 特定のプロジェクトディレクトリを分析
```bash
dotnet run --project src/NoticeGenerator -- --project ./src
```

#### トップレベルパッケージのみのNOTICEを生成
```bash
dotnet run --project src/NoticeGenerator -- --project ./src --scope top
```

#### パッケージ名からバージョン情報を省略
```bash
dotnet run --project src/NoticeGenerator -- --project ./src --no-version
```

#### カスタム出力ファイルとスコープ
```bash
dotnet run --project src/NoticeGenerator -- --project ./src --scope top --output THIRD_PARTY_NOTICES.md
```

#### より高速な処理のための高い同時実行数
```bash
dotnet run --project src/NoticeGenerator -- --project ./src --concurrency 8
```

## 出力形式

生成されるNOTICE.mdファイルには以下が含まれます：

- **パッケージ情報**: 名前、バージョン、作者、説明
- **ライセンス詳細**: ライセンス表現、著作権情報
- **完全なライセンステキスト**: 利用可能な場合の完全なライセンステキスト
- **ソースリンク**: パッケージページ、プロジェクトリポジトリ、SPDXライセンスページへのリンク
- **エラー報告**: ライセンス情報を取得できなかったパッケージの明確な表示

## 動作原理

1. **パッケージ検出**: `dotnet list package`を実行してすべてのNuGet依存関係を列挙
2. **メタデータ取得**: NuGet APIに並列でクエリしてパッケージメタデータを取得
3. **ライセンステキスト抽出**: 以下からライセンスファイルをダウンロード：
   - パッケージコンテンツ (.nupkgファイル)
   - パッケージメタデータで指定された外部URL
   - 一般的なライセンス用のSPDX標準ライセンステキスト
4. **レポート生成**: すべての情報を構造化されたNOTICE.mdファイルにコンパイル

## 依存関係

- **Microsoft.Extensions.Http**: HTTPクライアントファクトリと拡張機能
- **NuGet.Protocol**: NuGet APIクライアントライブラリ
- **Spectre.Console.Cli**: リッチコンソールアプリケーションフレームワーク

## ライセンス

このプロジェクトはMITライセンスの下でライセンスされています - 詳細は[LICENSE](LICENSE)ファイルを参照してください。

## 貢献

貢献を歓迎します！プルリクエストをお気軽に提出してください。

## サポート

問題が発生した場合や質問がある場合は、GitHubで[issueを開いて](https://github.com/MareMare/NoticeGenerator/issues)ください。