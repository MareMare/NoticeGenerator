// --------------------------------------------------------------------------------------------------------------------
// <copyright file="NuGetClient.cs" company="MareMare">
// Copyright © 2026 MareMare.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.
// </copyright>
// --------------------------------------------------------------------------------------------------------------------

using System.IO.Compression;
using System.Net;
using System.Xml.Linq;
using NuGet.Common;
using NuGet.Configuration;
using NuGet.Protocol;
using NuGet.Protocol.Core.Types;
using NuGet.Versioning;

namespace NoticeGenerator;

/// <summary>
/// NuGet.Protocol + flat container API を使ってパッケージメタデータと
/// ライセンス全文を取得する。
/// 
/// ライセンス全文の取得戦略（優先順）:
/// 1. .nuspec の &lt;license type="file"&gt; で指定されたファイルを .nupkg から取得
/// 2. .nuspec の &lt;license type="file"&gt; がなければ既知パターンで .nupkg を探索
/// 3. .nuspec の &lt;license type="expression"&gt; → SPDX リストから全文取得
/// 4. licenseUrl が外部 URL（licenses.nuget.org 以外）→ URL を直接フェッチ
/// 5. 取得できなければ null
/// </summary>
internal sealed class NuGetClient : IDisposable
{
    private const string _nuspecUrlTemplate =
        "https://api.nuget.org/v3-flatcontainer/{0}/{1}/{0}.nuspec";

    private const string _nupkgUrlTemplate =
        "https://api.nuget.org/v3-flatcontainer/{0}/{1}/{0}.{1}.nupkg";

    // SPDX ライセンス全文テキスト（Copyright 抜きの標準文）
    private const string _spdxRawUrlTemplate =
        "https://raw.githubusercontent.com/spdx/license-list-data/main/text/{0}.txt";

    // SPDX ライセンスページ（Markdown Link 用）
    private const string _spdxLicensePageTemplate =
        "https://spdx.org/licenses/{0}.html";

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
    private readonly PackageMetadataResource _metadata;
    private readonly HttpClient _http;

    public NuGetClient()
    {
        this._cache = new SourceCacheContext { NoCache = false, DirectDownload = false, };

        var source = new PackageSource("https://api.nuget.org/v3/index.json");
        var repo = Repository.Factory.GetCoreV3(source);
        this._metadata = repo.GetResource<PackageMetadataResource>();

        this._http = new HttpClient(new HttpClientHandler
        {
            AutomaticDecompression = DecompressionMethods.GZip
                                     | DecompressionMethods.Deflate
                                     | DecompressionMethods.Brotli,
        });
        this._http.DefaultRequestHeaders.UserAgent.ParseAdd("NoticeGenerator/1.0");
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

        // ---- 1. PackageMetadataResource でメタデータ取得 ----
        var allMeta = await this._metadata.GetMetadataAsync(
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

        // ---- 2. .nuspec から copyright・ライセンス種別・リポジトリ URL を取得 ----
        var nuspecInfo = await this.FetchNuspecInfoAsync(id, version, ct).ConfigureAwait(false);

        // ---- 3. ライセンス式・URL の解決 ----
        var licenseExpression = meta.LicenseMetadata?.License ?? string.Empty;
        var licenseUrl = meta.LicenseUrl?.ToString() ?? string.Empty;

        if (string.IsNullOrEmpty(licenseExpression) && !string.IsNullOrEmpty(licenseUrl))
        {
            licenseExpression = TryExtractSpdxFromUrl(licenseUrl);
        }

        // リポジトリ URL: .nuspec の <repository url> 優先、なければ ProjectUrl で代替
        var repositoryUrl = !string.IsNullOrEmpty(nuspecInfo.RepositoryUrl)
            ? nuspecInfo.RepositoryUrl
            : meta.ProjectUrl?.ToString() ?? string.Empty;

        // ---- 4. ライセンス全文の取得 ----
        var (licenseText, licenseSource) = await this.FetchLicenseTextAsync(
                id,
                version,
                nuspecInfo,
                licenseUrl,
                ct)
            .ConfigureAwait(false);

        return new NoticeEntry
        {
            Id = id,
            Version = version,
            Authors = meta.Authors ?? string.Empty,
            Description = meta.Description ?? string.Empty,
            PackageUrl = meta.PackageDetailsUrl.GetLeftPart(UriPartial.Path),
            ProjectUrl = meta.ProjectUrl?.ToString() ?? string.Empty,
            RepositoryUrl = repositoryUrl,
            LicenseExpression = licenseExpression,
            LicenseUrl = licenseUrl,
            Copyright = nuspecInfo.Copyright,
            LicenseText = licenseText,
            LicenseSource = licenseSource,
        };
    }

    public void Dispose()
    {
        this._cache.Dispose();
        this._http.Dispose();
    }

    private static ZipArchiveEntry? FindEntry(ZipArchive zip, string path)
    {
        var normalizedPath = path.Replace('\\', '/');
        return zip.Entries.FirstOrDefault(e =>
            string.Equals(
                e.FullName.Replace('\\', '/'),
                normalizedPath,
                StringComparison.OrdinalIgnoreCase));
    }

    private static async Task<string?> ReadEntryAsync(ZipArchiveEntry entry, CancellationToken ct)
    {
        try
        {
            await using var stream = await entry.OpenAsync(ct);
            using var reader = new StreamReader(stream, true);
            return (await reader.ReadToEndAsync(ct).ConfigureAwait(false)).TrimEnd();
        }
        catch
        {
            return null;
        }
    }

    private static string TryExtractSpdxFromUrl(string licenseUrl)
    {
        const string prefix = "https://licenses.nuget.org/";
        if (licenseUrl.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return Uri.UnescapeDataString(licenseUrl[prefix.Length..].TrimEnd('/'));
        }

        return string.Empty;
    }

    /// <summary>
    /// .nuspec を取得して copyright・ライセンス情報・リポジトリ URL を返す。
    /// &lt;license type="file"&gt;       → LicenseFile に .nupkg 内パス
    /// &lt;license type="expression"&gt; → SpdxExpression に SPDX 式
    /// &lt;repository url="..."&gt;      → RepositoryUrl に URL
    /// </summary>
    private async Task<NuspecInfo> FetchNuspecInfoAsync(string id, string version, CancellationToken ct)
    {
        var url = string.Format(
            _nuspecUrlTemplate,
            id.ToLowerInvariant(),
            version.ToLowerInvariant());

        try
        {
            var xml = await this._http.GetStringAsync(url, ct).ConfigureAwait(false);
            var doc = XDocument.Parse(xml);

            var copyright = doc.Descendants()
                                .FirstOrDefault(e => e.Name.LocalName == "copyright")
                                ?.Value.Trim()
                            ?? string.Empty;

            // <license type="file"|"expression">
            var licenseEl = doc.Descendants()
                .FirstOrDefault(e => e.Name.LocalName == "license");
            var licenseType = licenseEl?.Attribute("type")?.Value;

            string? licenseFile = null;
            string? spdxExpression = null;

            if (licenseType == "file")
            {
                licenseFile = licenseEl!.Value.Trim();
            }
            else if (licenseType == "expression")
            {
                spdxExpression = licenseEl!.Value.Trim();
            }

            // <repository url="https://github.com/..." type="git" branch="..." commit="...">
            var repositoryUrl = doc.Descendants()
                                    .FirstOrDefault(e => e.Name.LocalName == "repository")
                                    ?.Attribute("url")?.Value.Trim()
                                ?? string.Empty;

            return new NuspecInfo(copyright, licenseFile, spdxExpression, repositoryUrl);
        }
        catch
        {
            return new NuspecInfo(string.Empty, null, null, string.Empty);
        }
    }

    // -------------------------------------------------------
    // ライセンス全文の取得
    // -------------------------------------------------------

    /// <summary>
    /// ライセンス全文と取得元種別を返す。取得できなければ (null, None)。
    /// 
    /// 優先順:
    /// 1. type="file"       → .nupkg 内の指定ファイル  → NupkgFile
    /// 2. type="file" 失敗  → .nupkg 内を既知パターンで探索 → NupkgFile
    /// 3. type="expression" → SPDX リストから標準全文  → SpdxExpression
    /// 4. 外部 licenseUrl   → URL を直接フェッチ        → ExternalUrl
    /// </summary>
    private async Task<(string? Text, LicenseSource Source)> FetchLicenseTextAsync(
        string id,
        string version,
        NuspecInfo nuspecInfo,
        string licenseUrl,
        CancellationToken ct)
    {
        // 戦略1 & 2: .nupkg から LICENSE ファイルを取得
        // type="expression" でもファイルが同封されているケースがあるため常に試みる
        var fromNupkg = await this.TryFetchFromNupkgAsync(id, version, nuspecInfo.LicenseFile, ct)
            .ConfigureAwait(false);
        if (fromNupkg is not null)
        {
            return (fromNupkg, LicenseSource.NupkgFile);
        }

        // 戦略3: type="expression" → SPDX 標準テキストを取得
        // 複合式（"Apache-2.0 OR MIT" 等）は先頭の主要 SPDX ID を使用
        if (!string.IsNullOrEmpty(nuspecInfo.SpdxExpression))
        {
            var primaryId = nuspecInfo.SpdxExpression
                .Split([' ', '(', ')',], StringSplitOptions.RemoveEmptyEntries)
                .FirstOrDefault(t => t is not ("OR" or "AND" or "WITH"));

            if (primaryId is not null)
            {
                var fromSpdx = await this.TryFetchSpdxTextAsync(primaryId, ct).ConfigureAwait(false);
                if (fromSpdx is not null)
                {
                    return (fromSpdx, LicenseSource.SpdxExpression);
                }
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
    /// .nupkg をダウンロードして ZIP 展開し、LICENSE ファイルのテキストを返す。
    /// </summary>
    private async Task<string?> TryFetchFromNupkgAsync(
        string id,
        string version,
        string? preferredPath,
        CancellationToken ct)
    {
        var url = string.Format(
            _nupkgUrlTemplate,
            id.ToLowerInvariant(),
            version.ToLowerInvariant());

        try
        {
            var bytes = await this._http.GetByteArrayAsync(url, ct).ConfigureAwait(false);
            await using var zip = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read);

            // nuspec で <license type="file"> が指定されていればそのパスを優先
            if (preferredPath is not null)
            {
                var entry = FindEntry(zip, preferredPath);
                if (entry is not null)
                {
                    return await ReadEntryAsync(entry, ct).ConfigureAwait(false);
                }
            }

            // 既知パターンで探索
            foreach (var pattern in _licenseFilePatterns)
            {
                var entry = FindEntry(zip, pattern);
                if (entry is not null)
                {
                    return await ReadEntryAsync(entry, ct).ConfigureAwait(false);
                }
            }

            return null;
        }
        catch
        {
            return null;
        }
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

    // -------------------------------------------------------
    // .nuspec の取得
    // -------------------------------------------------------

    private record NuspecInfo(
        string Copyright,
        string? LicenseFile, // type="file"  のファイルパス
        string? SpdxExpression, // type="expression" の SPDX 式
        string RepositoryUrl // <repository url="..."> 属性
    );
}
