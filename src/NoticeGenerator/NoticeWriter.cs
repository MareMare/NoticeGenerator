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
/// 同一 SPDX ライセンスの全文はファイル末尾に1回だけ掲載し、
/// 各パッケージのセクションからアンカーリンクで参照する。
/// </summary>
internal sealed class NoticeWriter
{
    public async Task WriteAsync(
        string outputPath,
        IEnumerable<NoticeEntry> entries,
        SpdxLicenseFetcher licenseFetcher,
        CancellationToken ct = default)
    {
        var entryList = entries
            .OrderBy(e => e.Id, StringComparer.OrdinalIgnoreCase)
            .ToList();

        // ---- 1. 必要な SPDX ID を収集してライセンス全文を一括取得 ----
        var spdxIds = entryList
            .Select(e => e.LicenseExpression)
            .Where(e => !string.IsNullOrEmpty(e))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var licenseTexts = await licenseFetcher.FetchAllAsync(spdxIds, ct)
            .ConfigureAwait(false);

        // ---- 2. Markdown 生成 ----
        var sb = new StringBuilder();

        WriteHeader(sb);

        // パッケージ一覧セクション
        sb.AppendLine("## Packages");
        sb.AppendLine();
        foreach (var e in entryList)
        {
            WritePackageEntry(sb, e, licenseTexts);
        }

        // ライセンス全文セクション（重複なし・SPDX ID 昇順）
        var usedLicenses = licenseTexts
            .Where(kv => kv.Value is not null)
            .OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (usedLicenses.Count > 0)
        {
            sb.AppendLine("---");
            sb.AppendLine();
            sb.AppendLine("## License Texts");
            sb.AppendLine();
            sb.AppendLine("The full text of each license is reproduced below.");
            sb.AppendLine();

            foreach (var (spdxId, text) in usedLicenses)
            {
                // アンカー名は GitHub Markdown の自動生成ルールに合わせる
                sb.AppendLine($"### {spdxId}");
                sb.AppendLine();
                sb.AppendLine("```");
                sb.AppendLine(text);
                sb.AppendLine("```");
                sb.AppendLine();
            }
        }

        // ---- 3. ファイル書き出し ----
        var dir = Path.GetDirectoryName(Path.GetFullPath(outputPath));
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        await File.WriteAllTextAsync(
            outputPath,
            sb.ToString(),
            new UTF8Encoding(false),
            ct).ConfigureAwait(false);
    }

    // -------------------------------------------------------

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

    private static void WritePackageEntry(
        StringBuilder sb,
        NoticeEntry e,
        IReadOnlyDictionary<string, string?> licenseTexts)
    {
        var header = string.IsNullOrEmpty(e.Version)
            ? $"### {e.Id}"
            : $"### {e.Id} {e.Version}";
        sb.AppendLine(header);
        sb.AppendLine();

        if (e.Error is not null)
        {
            sb.AppendLine($"> ⚠️ **Failed to retrieve metadata:** {e.Error}");
            sb.AppendLine();
            return;
        }

        if (!string.IsNullOrEmpty(e.Copyright))
        {
            sb.AppendLine($"**Copyright:** {e.Copyright}  ");
        }

        if (!string.IsNullOrEmpty(e.Authors))
        {
            sb.AppendLine($"**Authors:** {e.Authors}  ");
        }

        // ライセンス表記：SPDX 式があればライセンス全文セクションへのアンカーリンクを付与
        if (!string.IsNullOrEmpty(e.LicenseExpression))
        {
            var licenseLink = BuildLicenseLink(e.LicenseExpression, licenseTexts);
            sb.AppendLine($"**License:** {licenseLink}  ");
        }
        else if (!string.IsNullOrEmpty(e.LicenseUrl))
        {
            sb.AppendLine($"**License URL:** <{e.LicenseUrl}>  ");
        }

        var repoDisplay = !string.IsNullOrEmpty(e.RepositoryUrl) ? e.RepositoryUrl
            : !string.IsNullOrEmpty(e.ProjectUrl) ? e.ProjectUrl
            : null;
        if (repoDisplay is not null)
        {
            sb.AppendLine($"**Repository:** <{repoDisplay}>  ");
        }

        if (!string.IsNullOrEmpty(e.Description))
        {
            sb.AppendLine();
            sb.AppendLine(e.Description);
        }

        sb.AppendLine();
    }

    /// <summary>
    /// SPDX 式中の各IDを、全文が取得できているものはアンカーリンクに変換する。
    /// 例: "Apache-2.0 OR MIT" → "[Apache-2.0](#apache-20) OR [MIT](#mit)"
    /// </summary>
    private static string BuildLicenseLink(
        string expression,
        IReadOnlyDictionary<string, string?> licenseTexts)
    {
        // OR / AND / WITH を区切り文字として保持しつつ置換
        var tokens = expression.Split(' ');
        var result = new List<string>();

        foreach (var token in tokens)
        {
            var id = token.Trim('(', ')');
            var prefix = token.StartsWith('(') ? "(" : string.Empty;
            var suffix = token.EndsWith(')') ? ")" : string.Empty;

            if (licenseTexts.TryGetValue(id, out var text) && text is not null)
            {
                // GitHub Markdown アンカー: 小文字化・記号除去・スペース→ハイフン
                var anchor = id.ToLowerInvariant()
                    .Replace('.', '-')
                    .Replace('+', '-');
                result.Add($"{prefix}[{id}](#{anchor}){suffix}");
            }
            else
            {
                // 全文が取得できなかった ID はプレーンテキストのまま
                result.Add(token);
            }
        }

        return string.Join(' ', result);
    }
}
