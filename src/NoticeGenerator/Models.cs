// --------------------------------------------------------------------------------------------------------------------
// <copyright file="Models.cs" company="MareMare">
// Copyright © 2026 MareMare.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.
// </copyright>
// --------------------------------------------------------------------------------------------------------------------

namespace NoticeGenerator;

/// <summary>dotnet list package から取得したパッケージ参照。</summary>
internal sealed record PackageRef(string Id, string? Version);

/// <summary>NuGet API から取得したパッケージのメタデータ。</summary>
internal sealed class NoticeEntry
{
    public string Id { get; init; } = string.Empty;
    public string? Version { get; init; }
    public string Authors { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public string ProjectUrl { get; init; } = string.Empty;
    public string RepositoryUrl { get; init; } = string.Empty;
    public string LicenseExpression { get; init; } = string.Empty;
    public string LicenseUrl { get; init; } = string.Empty;
    public string Copyright { get; init; } = string.Empty;

    /// <summary>メタデータ取得に失敗した場合のエラーメッセージ。</summary>
    public string? Error { get; init; }
}
