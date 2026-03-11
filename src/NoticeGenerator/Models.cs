// --------------------------------------------------------------------------------------------------------------------
// <copyright file="Models.cs" company="MareMare">
// Copyright © 2026 MareMare.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.
// </copyright>
// --------------------------------------------------------------------------------------------------------------------

namespace NoticeGenerator;

/// <summary>dotnet list package から取得したパッケージ参照。</summary>
internal sealed record PackageRef(string Id, string? Version);

/// <summary>ライセンス全文の取得元種別。NoticeWriter でのリンク生成判断に使用。</summary>
internal enum LicenseSource
{
    /// <summary>未取得または不明。</summary>
    None,

    /// <summary>.nupkg 内の LICENSE ファイル（パッケージ固有・URL なし）。</summary>
    NupkgFile,

    /// <summary>GitHub リポジトリの LICENSE ファイル（実際の著作権表示を含む）。</summary>
    GitHubRepository,

    /// <summary>SPDX 標準テキスト（type="expression" のフォールバック）。</summary>
    SpdxExpression,

    /// <summary>外部 URL から直接フェッチ。</summary>
    ExternalUrl,
}

/// <summary>NuGet API から取得したパッケージのメタデータ。</summary>
internal sealed class NoticeEntry
{
    public string Id { get; init; } = string.Empty;
    public string? Version { get; init; }
    public string Authors { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public string PackageUrl { get; init; } = string.Empty;

    public string ProjectUrl { get; init; } = string.Empty;

    /// <summary>
    /// .nuspec の &lt;repository url&gt; 属性。未登録の場合は ProjectUrl で代替。
    /// </summary>
    public string RepositoryUrl { get; init; } = string.Empty;

    public string LicenseExpression { get; init; } = string.Empty;
    public string LicenseUrl { get; init; } = string.Empty;
    public string Copyright { get; init; } = string.Empty;

    /// <summary>パッケージ固有のライセンス全文。取得できなかった場合は null。</summary>
    public string? LicenseText { get; init; }

    /// <summary>LicenseText の取得元。NoticeWriter でのリンク・表記生成に使用。</summary>
    public LicenseSource LicenseSource { get; init; } = LicenseSource.None;

    /// <summary>メタデータ取得に失敗した場合のエラーメッセージ。</summary>
    public string? Error { get; init; }
}
