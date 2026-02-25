// --------------------------------------------------------------------------------------------------------------------
// <copyright file="Program.cs" company="MareMare">
// Copyright © 2026 MareMare.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.
// </copyright>
// --------------------------------------------------------------------------------------------------------------------

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
    config.SetApplicationName("notice-generator");
    config.SetApplicationVersion("1.0.0");
    config.AddExample(["--project", "./src",]);
    config.AddExample(["--project", "./src", "--scope", "top",]);
    config.AddExample(["--project", "./src", "--no-version",]);
    config.AddExample(["--project", "./src", "--scope", "top", "--output", "NOTICE.md",]);
});

return await app.RunAsync(args).ConfigureAwait(false);
