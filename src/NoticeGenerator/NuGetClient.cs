// --------------------------------------------------------------------------------------------------------------------
// <copyright file="NuGetClient.cs" company="MareMare">
// Copyright © 2026 MareMare.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.
// </copyright>
// --------------------------------------------------------------------------------------------------------------------

using System.Net;
using NuGet.Common;
using NuGet.Configuration;
using NuGet.Packaging;
using NuGet.Packaging.Licenses;
using NuGet.Protocol;
using NuGet.Protocol.Core.Types;
using NuGet.Versioning;

namespace NoticeGenerator;

/// <summary>
/// NuGet.Protocol / NuGet.Packaging SDK を使ってパッケージメタデータと
/// ライセンス全文を取得する。
///
/// ライセンス全文の取得戦略（優先順）:
/// 1. nuspec の &lt;license type="file"&gt; で指定されたファイルを .nupkg から取得
/// 2. &lt;license type="file"&gt; がなければ既知パターンで .nupkg を探索
/// 3. &lt;license type="expression"&gt; → SPDX リストから全文取得（複合式は全 ID 分）
/// 4. licenseUrl が外部 URL（licenses.nuget.org 以外）→ URL を直接フェッチ
/// 5. 取得できなければ null
/// </summary>
internal sealed class NuGetClient : IDisposable
{
    // SPDX ライセンス全文テキスト（GitHub raw）
    private const string _spdxRawUrlTemplate =
        "https://raw.githubusercontent.com/spdx/license-list-data/main/text/{0}.txt";

    // .nupkg 内で LICENSE ファイルとして認識する名前パターン（優先順）
    private static readonly string[] _licenseFilePatterns =
    [
        "LICENSE",
        "LICENSE.txt",
        "LICENSE.md",
        "LICENSE.rst",
        "license",
        "license.txt",
        "license.md",
        "LICENCE",
        "LICENCE.txt",
        "LICENCE.md",
    ];

    private readonly SourceCacheContext _cache;
    private readonly PackageMetadataResource _metadataResource;
    private readonly FindPackageByIdResource _findByIdResource;
    private readonly HttpClient _http;

    public NuGetClient()
    {
        this._cache = new SourceCacheContext { NoCache = false, DirectDownload = false, };

        var source = new PackageSource("https://api.nuget.org/v3/index.json");
        var repo = Repository.Factory.GetCoreV3(source);
        this._metadataResource = repo.GetResource<PackageMetadataResource>();
        this._findByIdResource = repo.GetResource<FindPackageByIdResource>();

        this._http = new HttpClient(new HttpClientHandler
        {
            AutomaticDecompression = DecompressionMethods.GZip
                                     | DecompressionMethods.Deflate
                                     | DecompressionMethods.Brotli,
        });
        this._http.DefaultRequestHeaders.UserAgent.ParseAdd($"{AppInfo.ApplicationName}/{AppInfo.Version}");
        this._http.Timeout = TimeSpan.FromSeconds(60);
    }

    public async Task<NoticeEntry> FetchAsync(
        string id,
        string version,
        CancellationToken ct = default)
    {
        if (!NuGetVersion.TryParse(version, out var nugetVersion))
        {
            throw new ArgumentException($"Invalid NuGet version: '{version}'");
        }

        // ---- 1. PackageMetadataResource でパッケージ一覧メタデータを取得 ----
        // （PackageDetailsUrl・Authors・Description・ProjectUrl・LicenseUrl を得る）
        var allMeta = await this._metadataResource.GetMetadataAsync(
                id,
                true,
                false,
                this._cache,
                NullLogger.Instance,
                ct)
            .ConfigureAwait(false);

        var meta = allMeta
                       .Where(m => m is not null)
                       .FirstOrDefault(m => m.Identity.Version == nugetVersion)
                   ?? throw new InvalidOperationException(
                       $"Version '{version}' not found for package '{id}'.");

        var licenseUrl = meta.LicenseUrl?.ToString() ?? string.Empty;

        // ---- 2. .nupkg をダウンロードして PackageArchiveReader で開く ----
        // nuspec パース・LICENSE ファイル抽出を1回のダウンロードで完結させる
        using var packageStream = new MemoryStream();
        var downloaded = await this._findByIdResource.CopyNupkgToStreamAsync(
                id,
                nugetVersion,
                packageStream,
                this._cache,
                NullLogger.Instance,
                ct)
            .ConfigureAwait(false);

        if (!downloaded)
        {
            throw new InvalidOperationException(
                $"Failed to download .nupkg for '{id}' {version}.");
        }

        packageStream.Position = 0;
        using var reader = new PackageArchiveReader(packageStream);
        var nuspec = reader.NuspecReader;

        // ---- 3. NuspecReader で nuspec 情報を取得 ----
        // ③ XLinq 手探りを NuspecReader の専用 API に置き換え
        var copyright = nuspec.GetCopyright() ?? string.Empty;
        var licenseMeta = nuspec.GetLicenseMetadata(); // type="file"|"expression" の情報
        var repoMeta = nuspec.GetRepositoryMetadata(); // <repository url="...">
        var repositoryUrl = !string.IsNullOrEmpty(repoMeta?.Url)
            ? repoMeta.Url
            : meta.ProjectUrl?.ToString() ?? string.Empty;

        // ---- 4. ライセンス式の解決 ----
        // LicenseMetadata は PackageMetadataResource の返値にも含まれているが、
        // nuspec から直接取得することで type="file" / type="expression" の判別が確実になる
        var licenseExpression = licenseMeta?.License ?? string.Empty;

        // 旧来の licenseUrl のみ持つパッケージ向け: licenses.nuget.org URL から SPDX ID を抽出
        if (string.IsNullOrEmpty(licenseExpression) && !string.IsNullOrEmpty(licenseUrl))
        {
            licenseExpression = TryExtractSpdxFromUrl(licenseUrl);
        }

        // ---- 5. ライセンス全文の取得 ----
        var (licenseText, licenseSource) = await this.FetchLicenseTextAsync(
                reader,
                licenseMeta,
                licenseUrl,
                ct)
            .ConfigureAwait(false);

        return new NoticeEntry
        {
            Id = id,
            Version = version,
            Authors = meta.Authors ?? string.Empty,
            Description = meta.Description ?? string.Empty,
            PackageUrl = meta.PackageDetailsUrl?.GetLeftPart(UriPartial.Path) ?? string.Empty,
            ProjectUrl = meta.ProjectUrl?.ToString() ?? string.Empty,
            RepositoryUrl = repositoryUrl,
            LicenseExpression = licenseExpression,
            LicenseUrl = licenseUrl,
            Copyright = copyright,
            LicenseText = licenseText,
            LicenseSource = licenseSource,
        };
    }

    public void Dispose()
    {
        this._cache.Dispose();
        this._http.Dispose();
    }

    /// <summary>
    /// PackageArchiveReader から LICENSE ファイルを読み取る。
    /// ② 独自 ZipArchive 操作を PackageArchiveReader API に置き換え
    ///
    /// 探索順:
    /// 1. nuspec の &lt;license type="file"&gt; で指定されたパス（優先）
    /// 2. 既知パターン（LICENSE, LICENSE.txt, …）
    /// </summary>
    private static async Task<string?> TryReadLicenseFromNupkgAsync(
        PackageArchiveReader reader,
        LicenseMetadata? licenseMeta,
        CancellationToken ct)
    {
        // 候補パスのリスト: nuspec 指定パスを先頭に、既知パターンを後続に並べる
        IEnumerable<string> candidates = licenseMeta?.Type == LicenseType.File
                                         && !string.IsNullOrEmpty(licenseMeta.License)
            ? [NormalizeLicensePath(licenseMeta.License), .. _licenseFilePatterns,]
            : _licenseFilePatterns;

        foreach (var path in candidates)
        {
            try
            {
                // PackageArchiveReader.GetEntry() はパスが存在しない場合 null を返す
                var entry = reader.GetEntry(path);
                if (entry is null)
                {
                    continue;
                }

                await using var stream = await entry.OpenAsync(ct);
                using var textReader = new StreamReader(stream, true);
                return (await textReader.ReadToEndAsync(ct).ConfigureAwait(false)).TrimEnd();
            }
            catch
            {
                // エントリが見つからない・読み取り失敗は次のパターンへ
            }
        }

        return null;
    }

    /// <summary>
    /// SPDX 式から SPDX ライセンス識別子の一覧を抽出する。
    /// <see cref="NuGetLicenseExpression.Parse" /> で構文検証（無効な式は例外）を行い、
    /// 検証済みの式文字列を再トークン化して識別子を収集する。
    /// これにより SDK の内部サブクラス型（非公開 API）への依存を避けつつ、
    /// 構文バリデーションの恩恵は確実に得られる。
    /// 
    /// WITH 演算子の exception 部分（例: Classpath-exception-2.0）は
    /// SPDX ライセンス ID ではないため、SPDX テキスト取得対象から除外する。
    /// </summary>
    private static IReadOnlyList<string> CollectLicenseIdentifiers(string spdxExpression)
    {
        string validatedExpression;
        try
        {
            // Parse() で構文検証（無効な式は例外を投げる）
            // ToString() は正規化された式文字列を返す
            var parsed = NuGetLicenseExpression.Parse(spdxExpression);
            validatedExpression = parsed.ToString() ?? spdxExpression;
        }
        catch
        {
            // 非標準識別子等でパースに失敗した場合はそのままフォールバック
            validatedExpression = spdxExpression;
        }

        // トークン分割: 演算子キーワード・括弧を除いて識別子のみ収集
        // WITH が含まれる式: "GPL-2.0-only WITH Classpath-exception-2.0"
        // → "GPL-2.0-only" だけを license ID として扱い、exception 識別子は除外する
        var tokens = validatedExpression
            .Split([' ', '(', ')',], StringSplitOptions.RemoveEmptyEntries);

        var ids = new List<string>();
        var skipNext = false;
        foreach (var token in tokens)
        {
            if (token is "OR" or "AND")
            {
                skipNext = false;
                continue;
            }

            if (token is "WITH")
            {
                // WITH の直後のトークンは exception 識別子 → スキップ
                skipNext = true;
                continue;
            }

            if (skipNext)
            {
                skipNext = false;
                continue;
            }

            if (!ids.Contains(token, StringComparer.Ordinal))
            {
                ids.Add(token);
            }
        }

        return ids;
    }

    // -------------------------------------------------------
    // ユーティリティ
    // -------------------------------------------------------

    /// <summary>
    /// nuspec の &lt;license type="file"&gt; に記述されたパスを
    /// PackageArchiveReader.GetEntry() が受け付ける形式に正規化する。
    /// （先頭のディレクトリ区切り文字・バックスラッシュを除去）
    /// </summary>
    private static string NormalizeLicensePath(string path) =>
        path.TrimStart('/', '\\').Replace('\\', '/');

    /// <summary>
    /// licenses.nuget.org URL から SPDX ID を抽出する。
    /// 例: https://licenses.nuget.org/MIT → "MIT"
    /// </summary>
    private static string TryExtractSpdxFromUrl(string licenseUrl)
    {
        const string prefix = "https://licenses.nuget.org/";
        if (licenseUrl.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return Uri.UnescapeDataString(licenseUrl[prefix.Length..].TrimEnd('/'));
        }

        return string.Empty;
    }

    // -------------------------------------------------------
    // ライセンス全文の取得
    // -------------------------------------------------------

    /// <summary>
    /// ライセンス全文と取得元種別を返す。取得できなければ (null, None)。
    ///
    /// 優先順:
    /// 1. type="file"       → .nupkg 内の指定ファイル            → NupkgFile
    /// 2. type="file" 失敗  → .nupkg 内を既知パターンで探索       → NupkgFile
    /// 3. type="expression" → SPDX リストから全 ID の全文を結合   → SpdxExpression
    /// 4. 外部 licenseUrl   → URL を直接フェッチ                  → ExternalUrl
    /// </summary>
    private async Task<(string? Text, LicenseSource Source)> FetchLicenseTextAsync(
        PackageArchiveReader reader,
        LicenseMetadata? licenseMeta,
        string licenseUrl,
        CancellationToken ct)
    {
        // 戦略1 & 2: .nupkg から LICENSE ファイルを取得
        // type="expression" でもファイルが同封されているケースがあるため常に試みる
        var fromNupkg = await TryReadLicenseFromNupkgAsync(reader, licenseMeta, ct)
            .ConfigureAwait(false);
        if (fromNupkg is not null)
        {
            return (fromNupkg, LicenseSource.NupkgFile);
        }

        // 戦略3: type="expression" → 全 SPDX ID のテキストを取得
        // ③ 独自スプリットを NuGetLicenseExpression.Parse() に置き換え
        // 複合式（"Apache-2.0 OR MIT" 等）も全 ID 分取得して結合する
        if (licenseMeta?.Type == LicenseType.Expression
            && !string.IsNullOrEmpty(licenseMeta.License))
        {
            var fromSpdx = await this.TryFetchAllSpdxTextsAsync(licenseMeta.License, ct)
                .ConfigureAwait(false);
            if (fromSpdx is not null)
            {
                return (fromSpdx, LicenseSource.SpdxExpression);
            }
        }

        // 戦略4: 外部 licenseUrl の場合はフェッチ
        // （licenses.nuget.org・www.nuget.org は HTML ページなのでスキップ）
        if (!string.IsNullOrEmpty(licenseUrl)
            && !licenseUrl.StartsWith("https://licenses.nuget.org/", StringComparison.OrdinalIgnoreCase)
            && !licenseUrl.StartsWith("https://www.nuget.org/packages/", StringComparison.OrdinalIgnoreCase))
        {
            var fromUrl = await this.TryFetchUrlAsync(licenseUrl, ct).ConfigureAwait(false);
            if (fromUrl is not null)
            {
                return (fromUrl, LicenseSource.ExternalUrl);
            }
        }

        return (null, LicenseSource.None);
    }

    /// <summary>
    /// SPDX 式を <see cref="NuGetLicenseExpression.Parse" /> で構文検証したうえで、
    /// ③ 含まれる全 SPDX ライセンス ID を抽出してテキストを取得・結合して返す。
    /// 複合式（Apache-2.0 OR MIT 等）では各 ID のテキストを "---" で区切って結合する。
    /// いずれか1つでも取得できなければ null を返す。
    /// </summary>
    private async Task<string?> TryFetchAllSpdxTextsAsync(string spdxExpression, CancellationToken ct)
    {
        var ids = CollectLicenseIdentifiers(spdxExpression);

        if (ids.Count == 0)
        {
            return null;
        }

        var texts = new List<string>(ids.Count);
        foreach (var id in ids)
        {
            var text = await this.TryFetchSpdxTextAsync(id, ct).ConfigureAwait(false);
            if (text is null)
            {
                return null; // 1つでも欠けたら全体を null に
            }

            texts.Add(ids.Count == 1 ? text : $"--- {id} ---\n\n{text}");
        }

        return string.Join("\n\n", texts);
    }

    /// <summary>
    /// SPDX ライセンスリストから標準テキストを取得する。
    /// 例: "MIT" → https://raw.githubusercontent.com/spdx/license-list-data/main/text/MIT.txt
    /// </summary>
    private async Task<string?> TryFetchSpdxTextAsync(string spdxId, CancellationToken ct)
    {
        var url = string.Format(_spdxRawUrlTemplate, spdxId);
        try
        {
            return (await this._http.GetStringAsync(url, ct).ConfigureAwait(false)).TrimEnd();
        }
        catch
        {
            return null;
        }
    }

    private async Task<string?> TryFetchUrlAsync(string url, CancellationToken ct)
    {
        try
        {
            return (await this._http.GetStringAsync(url, ct).ConfigureAwait(false)).TrimEnd();
        }
        catch
        {
            return null;
        }
    }
}
