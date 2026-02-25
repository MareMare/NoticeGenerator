// --------------------------------------------------------------------------------------------------------------------
// <copyright file="NuGetClient.cs" company="MareMare">
// Copyright © 2026 MareMare.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.
// </copyright>
// --------------------------------------------------------------------------------------------------------------------

using System.Net;
using System.Xml.Linq;
using NuGet.Common;
using NuGet.Configuration;
using NuGet.Protocol;
using NuGet.Protocol.Core.Types;
using NuGet.Versioning;

namespace NoticeGenerator;

/// <summary>
/// NuGet.Protocol を使って nuget.org からパッケージメタデータを取得する。
/// Copyright は IPackageSearchMetadata に存在しないため、
/// NuGet flat container API から .nuspec を取得して補完する。
/// </summary>
internal sealed class NuGetClient : IDisposable
{
    // https://api.nuget.org/v3-flatcontainer/{id}/{version}/{id}.nuspec
    private const string _nuspecUrlTemplate =
        "https://api.nuget.org/v3-flatcontainer/{0}/{1}/{0}.nuspec";

    private readonly SourceCacheContext _cache;
    private readonly PackageMetadataResource _metadata;
    private readonly HttpClient _http;

    public NuGetClient()
    {
        this._cache = new SourceCacheContext { NoCache = false, DirectDownload = false, };

        var source = new PackageSource("https://api.nuget.org/v3/index.json");
        var repo = Repository.Factory.GetCoreV3(source);
        this._metadata = repo.GetResource<PackageMetadataResource>();

        // .nuspec フェッチ専用 HttpClient
        // flat container は平文 XML を返すため AutomaticDecompression 不要
        this._http = new HttpClient(new HttpClientHandler
        {
            AutomaticDecompression = DecompressionMethods.GZip
                                     | DecompressionMethods.Deflate
                                     | DecompressionMethods.Brotli,
        });
        this._http.DefaultRequestHeaders.UserAgent.ParseAdd("NoticeGenerator/1.0");
        this._http.Timeout = TimeSpan.FromSeconds(30);
    }

    public async Task<NoticeEntry> FetchAsync(
        string id,
        string version,
        CancellationToken cancellationToken = default)
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
                cancellationToken)
            .ConfigureAwait(false);

        var meta = allMeta
                       .Where(m => m is not null)
                       .FirstOrDefault(m => m.Identity.Version == nugetVersion)
                   ?? throw new InvalidOperationException(
                       $"Version '{version}' not found for package '{id}'.");

        // ---- 2. .nuspec から copyright を取得 ----
        var copyright = await this.FetchCopyrightFromNuspecAsync(id, version, cancellationToken)
            .ConfigureAwait(false);

        // ---- 3. ライセンス式の解決 ----
        var licenseExpression = meta.LicenseMetadata?.License ?? string.Empty;
        var licenseUrl = meta.LicenseUrl?.ToString() ?? string.Empty;

        if (string.IsNullOrEmpty(licenseExpression) && !string.IsNullOrEmpty(licenseUrl))
        {
            licenseExpression = TryExtractSpdxFromUrl(licenseUrl);
        }

        // リポジトリ URL（未登録なら ProjectUrl で代替）
        var repoUrl = meta.ProjectUrl?.ToString() ?? string.Empty;

        return new NoticeEntry
        {
            Id = id,
            Version = version,
            Authors = meta.Authors ?? string.Empty,
            Description = meta.Description ?? string.Empty,
            ProjectUrl = meta.ProjectUrl?.ToString() ?? string.Empty,
            RepositoryUrl = repoUrl,
            LicenseExpression = licenseExpression,
            LicenseUrl = licenseUrl,
            Copyright = copyright,
        };
    }

    public void Dispose()
    {
        this._cache.Dispose();
        this._http.Dispose();
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
    /// NuGet flat container API から .nuspec を取得し、
    /// &lt;copyright&gt; 要素の値を返す。取得できなければ空文字。
    /// </summary>
    private async Task<string> FetchCopyrightFromNuspecAsync(
        string id,
        string version,
        CancellationToken cancellationToken)
    {
        // flat container の ID・バージョンはすべて小文字
        var url = string.Format(
            _nuspecUrlTemplate,
            id.ToLowerInvariant(),
            version.ToLowerInvariant());

        try
        {
            var xml = await this._http.GetStringAsync(url, cancellationToken).ConfigureAwait(false);

            // <copyright> 要素を名前空間に依存せず取得
            var doc = XDocument.Parse(xml);
            var copyright = doc.Descendants()
                .FirstOrDefault(e => e.Name.LocalName == "copyright")
                ?.Value
                .Trim();

            return string.IsNullOrEmpty(copyright) ? string.Empty : copyright;
        }
        catch
        {
            // .nuspec が存在しない・取得失敗した場合は空文字で続行
            return string.Empty;
        }
    }
}
