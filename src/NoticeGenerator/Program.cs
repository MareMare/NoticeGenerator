// --------------------------------------------------------------------------------------------------------------------
// <copyright file="Program.cs" company="MareMare">
// Copyright © 2026 MareMare.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.
// </copyright>
// --------------------------------------------------------------------------------------------------------------------

using System.Diagnostics;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using NoticeGenerator;
using Spectre.Console.Cli;

var services = new ServiceCollection();

services.AddSingleton<NuGetClient>();
services.AddSingleton<NoticeWriter>();
services.AddSingleton<DotnetListRunner>();

var registrar = new TypeRegistrar(services);
var app = new CommandApp<GenerateCommand>(registrar);

app.Configure(config =>
{
    config.SetApplicationName(VersionInfo.ApplicationName);
    config.SetApplicationVersion(VersionInfo.Version);

    config.AddExample(["--project", "./src",]);
    config.AddExample(["--project", "./src", "--scope", "top",]);
    config.AddExample(["--project", "./src", "--no-version",]);
    config.AddExample(["--project", "./src", "--scope", "top", "--output", "NOTICE.md",]);
});

return await app.RunAsync(args).ConfigureAwait(false);

/// <summary>
/// アプリケーションのバージョンと名前に関する情報を提供します。
/// </summary>
file static class VersionInfo
{
    /// <summary>
    /// アプリケーション名を取得します。
    /// </summary>
    /// <value>
    /// 値を表す <see cref="string" /> 型。
    /// <para>アプリケーション名。</para>
    /// </value>
    public static string ApplicationName { get; } = Path.GetFileNameWithoutExtension(Process.GetCurrentProcess().MainModule?.ModuleName) ?? string.Empty;

    /// <summary>
    /// バージョン名を取得します。
    /// </summary>
    /// <value>
    /// 値を表す <see cref="string" /> 型。
    /// <para>バージョン名。</para>
    /// </value>
    public static string Version { get; } = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? string.Empty;
}
