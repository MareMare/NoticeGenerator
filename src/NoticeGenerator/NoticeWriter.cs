// --------------------------------------------------------------------------------------------------------------------
// <copyright file="NoticeWriter.cs" company="MareMare">
// Copyright © 2026 MareMare.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.
// </copyright>
// --------------------------------------------------------------------------------------------------------------------

using System.Text;

namespace NoticeGenerator;

/// <summary>
/// NoticeEntry のリストを NOTICE.md 形式で書き出す。
/// </summary>
internal sealed class NoticeWriter
{
    // SPDX ライセンスページ URL テンプレート
    private const string _spdxLicensePageTemplate =
        "https://spdx.org/licenses/{0}.html";

    public async Task WriteAsync(
        string outputPath,
        IEnumerable<NoticeEntry> entries,
        CancellationToken ct = default)
    {
        var entryList = entries
            .OrderBy(e => e.Id, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var sb = new StringBuilder();
        NoticeWriter.WriteHeader(sb);

        foreach (var entry in entryList)
        {
            NoticeWriter.WritePackageEntry(sb, entry);
        }

        var dir = Path.GetDirectoryName(Path.GetFullPath(outputPath));
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        await File.WriteAllTextAsync(
                outputPath,
                sb.ToString(),
                new UTF8Encoding(false),
                ct)
            .ConfigureAwait(false);
    }

    private static void WriteHeader(StringBuilder sb)
    {
        sb.AppendLine("# Notices");
        sb.AppendLine();
        sb.AppendLine("This repository incorporates material from the projects listed below.");
        sb.AppendLine("The original copyright notices and the licenses under which they were");
        sb.AppendLine("received are set out below.");
        sb.AppendLine();
        sb.AppendLine("---");
        sb.AppendLine();
    }

    private static void WritePackageEntry(StringBuilder sb, NoticeEntry e)
    {
        var header = string.IsNullOrEmpty(e.Version)
            ? $"## [{e.Id}]({e.PackageUrl})"
            : $"## [{e.Id} {e.Version}]({e.PackageUrl})";
        sb.AppendLine(header);
        sb.AppendLine();

        if (e.Error is not null)
        {
            sb.AppendLine($"> ⚠️ **Failed to retrieve metadata:** {e.Error}");
            sb.AppendLine();
            sb.AppendLine("---");
            sb.AppendLine();
            return;
        }

        // ---- メタデータ ----
        if (!string.IsNullOrEmpty(e.Copyright))
        {
            sb.AppendLine($"**Copyright:** {e.Copyright}  ");
        }

        if (!string.IsNullOrEmpty(e.Authors))
        {
            sb.AppendLine($"**Authors:** {e.Authors}  ");
        }

        // ライセンス表記：取得元に応じてリンク形式を変える
        if (!string.IsNullOrEmpty(e.LicenseExpression))
        {
            var licenseDisplay = BuildLicenseDisplay(e);
            sb.AppendLine($"**License:** {licenseDisplay}  ");
        }
        else if (!string.IsNullOrEmpty(e.LicenseUrl))
        {
            sb.AppendLine($"**License URL:** <{e.LicenseUrl}>  ");
        }

        // リポジトリ URL（.nuspec の <repository url> 優先、なければ ProjectUrl）
        if (!string.IsNullOrEmpty(e.RepositoryUrl))
        {
            sb.AppendLine($"**Repository:** <{e.RepositoryUrl}>  ");
        }
        else if (!string.IsNullOrEmpty(e.ProjectUrl))
        {
            sb.AppendLine($"**Repository:** <{e.ProjectUrl}>  ");
        }

        if (!string.IsNullOrEmpty(e.Description))
        {
            sb.AppendLine();
            sb.AppendLine(e.Description);
        }

        // ---- ライセンス全文 ----
        if (!string.IsNullOrEmpty(e.LicenseText))
        {
            sb.AppendLine();
            sb.AppendLine("<details>");
            sb.AppendLine("<summary>License Text</summary>");
            sb.AppendLine();
            sb.AppendLine("```");
            sb.AppendLine(e.LicenseText);
            sb.AppendLine("```");
            sb.AppendLine();
            sb.AppendLine("</details>");
        }

        sb.AppendLine();
        sb.AppendLine("---");
        sb.AppendLine();
    }

    /// <summary>
    /// LicenseSource に応じてライセンス表記文字列を生成する。
    /// 
    /// NupkgFile      → プレーンテキスト（.nupkg 由来でURL根拠なし）
    /// 例: MIT
    /// SpdxExpression → SPDX ページへの Markdown リンク
    /// 例: [MIT](https://spdx.org/licenses/MIT.html)
    /// 複合式は各 ID を個別リンクに変換
    /// 例: [Apache-2.0](https://...) OR [MIT](https://...)
    /// ExternalUrl    → licenseUrl への Markdown リンク
    /// 例: [Apache-2.0](https://www.apache.org/licenses/LICENSE-2.0)
    /// None           → プレーンテキスト
    /// </summary>
    private static string BuildLicenseDisplay(NoticeEntry e) =>
        e.LicenseSource switch
        {
            LicenseSource.NupkgFile =>
                // .nupkg のファイルから取得 → URL 根拠がないのでプレーンテキスト
                e.LicenseExpression,

            LicenseSource.SpdxExpression =>
                // SPDX 式の各 ID を spdx.org ページへのリンクに変換
                BuildSpdxExpressionLinks(e.LicenseExpression),

            LicenseSource.ExternalUrl when !string.IsNullOrEmpty(e.LicenseUrl) =>
                // 外部 URL → licenseUrl へのリンク
                $"[{e.LicenseExpression}]({e.LicenseUrl})",

            _ =>
                // フォールバック: SPDX 式があれば spdx.org リンク、なければプレーン
                string.IsNullOrEmpty(e.LicenseExpression)
                    ? e.LicenseExpression
                    : BuildSpdxExpressionLinks(e.LicenseExpression),
        };

    /// <summary>
    /// SPDX 式（単純・複合どちらも）の各 SPDX ID を spdx.org ページへの
    /// Markdown リンクに変換する。
    /// 例: "Apache-2.0 OR MIT"
    /// → "[Apache-2.0](https://spdx.org/licenses/Apache-2.0.html) OR [MIT](https://spdx.org/licenses/MIT.html)"
    /// </summary>
    private static string BuildSpdxExpressionLinks(string expression)
    {
        // スペース区切りでトークン化し、SPDX ID のみリンクに変換
        // 演算子（OR / AND / WITH）と括弧はそのまま保持
        var tokens = expression.Split(' ');
        var result = new List<string>(tokens.Length);

        foreach (var token in tokens)
        {
            var inner = token.Trim('(', ')');
            var prefix = token.StartsWith('(') ? "(" : string.Empty;
            var suffix = token.EndsWith(')') ? ")" : string.Empty;

            if (inner is "OR" or "AND" or "WITH" || string.IsNullOrEmpty(inner))
            {
                result.Add(token);
            }
            else
            {
                var url = string.Format(_spdxLicensePageTemplate, inner);
                result.Add($"{prefix}[{inner}]({url}){suffix}");
            }
        }

        return string.Join(' ', result);
    }
}
