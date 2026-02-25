// --------------------------------------------------------------------------------------------------------------------
// <copyright file="SpdxLicenseFetcher.cs" company="MareMare">
// Copyright © 2026 MareMare.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.
// </copyright>
// --------------------------------------------------------------------------------------------------------------------

using System.Collections.Concurrent;
using System.Net;

namespace NoticeGenerator;

/// <summary>
/// SPDX ライセンス全文を GitHub の spdx/license-list-data から取得してキャッシュする。
/// 同一ライセンス（例: MIT）を複数パッケージが共有していても HTTP リクエストは1回だけ。
/// </summary>
internal sealed class SpdxLicenseFetcher : IDisposable
{
    // SPDX 公式テキスト配布リポジトリ（改行正規化済みの .txt）
    private const string _urlTemplate =
        "https://raw.githubusercontent.com/spdx/license-list-data/main/text/{0}.txt";

    private readonly HttpClient _http;

    // SPDX 式 → 全文 のメモリキャッシュ（null = 取得失敗）
    private readonly ConcurrentDictionary<string, string?> _cache = new(StringComparer.OrdinalIgnoreCase);

    public SpdxLicenseFetcher()
    {
        this._http = new HttpClient(new HttpClientHandler
        {
            AutomaticDecompression = DecompressionMethods.GZip
                                     | DecompressionMethods.Deflate
                                     | DecompressionMethods.Brotli,
        });
        this._http.DefaultRequestHeaders.UserAgent.ParseAdd("NoticeGenerator/1.0");
        this._http.Timeout = TimeSpan.FromSeconds(30);
    }

    /// <summary>
    /// SPDX 式を受け取り、ライセンス全文を返す。
    /// 複合式（例: Apache-2.0 OR MIT）は "OR" / "AND" / "WITH" で分割して個別取得する。
    /// 取得できなかった式は null を返す。
    /// </summary>
    public async Task<IReadOnlyDictionary<string, string?>> FetchAllAsync(
        IEnumerable<string> spdxExpressions,
        CancellationToken ct = default)
    {
        // 式を個別の SPDX ID に分解して重複排除
        var ids = spdxExpressions
            .SelectMany(SplitExpression)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        await Task.WhenAll(ids.Select(id => this.FetchOneAsync(id, ct)));

        return ids.ToDictionary(
            id => id,
            id => _cache.GetValueOrDefault(id),
            StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>単一 SPDX ID のライセンス全文を取得してキャッシュする。</summary>
    public async Task<string?> FetchOneAsync(string spdxId, CancellationToken ct = default)
    {
        if (_cache.TryGetValue(spdxId, out var cached))
        {
            return cached;
        }

        try
        {
            var url = string.Format(_urlTemplate, spdxId);
            var text = await this._http.GetStringAsync(url, ct).ConfigureAwait(false);
            _cache[spdxId] = text.TrimEnd();
            return _cache[spdxId];
        }
        catch
        {
            // 存在しない SPDX ID・ネットワークエラーは null でキャッシュして再試行しない
            _cache[spdxId] = null;
            return null;
        }
    }

    public void Dispose() => this._http.Dispose();

    /// <summary>
    /// "Apache-2.0 OR MIT", "GPL-2.0-only WITH Classpath-exception-2.0" などを
    /// 個別の SPDX ID に分解する。
    /// </summary>
    private static IEnumerable<string> SplitExpression(string expression) =>
        expression
            .Split([" OR ", " AND ", " WITH ",], StringSplitOptions.RemoveEmptyEntries)
            .Select(t => t.Trim().Trim('(', ')'))
            .Where(t => !string.IsNullOrEmpty(t));
}
