// --------------------------------------------------------------------------------------------------------------------
// <copyright file="GenerateCommand.cs" company="MareMare">
// Copyright © 2026 MareMare.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.
// </copyright>
// --------------------------------------------------------------------------------------------------------------------

using System.ComponentModel;
using Spectre.Console;
using Spectre.Console.Cli;

namespace NoticeGenerator;

internal sealed class GenerateSettings : CommandSettings
{
    [CommandOption("-p|--project <PATH>")]
    [Description("Path to the project or solution to analyze. Defaults to current directory.")]
    [DefaultValue(".")]
    public string Project { get; init; } = ".";

    [CommandOption("-s|--scope <SCOPE>")]
    [Description("Package scope: 'all' includes transitive packages, 'top' includes top-level only.")]
    [DefaultValue("all")]
    public string Scope { get; init; } = "all";

    [CommandOption("--no-version")]
    [Description("Omit version from package identifiers. Useful for grouping.")]
    [DefaultValue(false)]
    public bool NoVersion { get; init; }

    [CommandOption("-o|--output <FILE>")]
    [Description("Output file path for the generated NOTICE.md.")]
    [DefaultValue("NOTICE.md")]
    public string Output { get; init; } = "NOTICE.md";

    [CommandOption("--concurrency <N>")]
    [Description("Number of concurrent NuGet API requests. Defaults to 4.")]
    [DefaultValue(4)]
    public int Concurrency { get; init; } = 4;

    public override ValidationResult Validate()
    {
        if (this.Scope is not ("all" or "top"))
        {
            return ValidationResult.Error("--scope must be 'all' or 'top'.");
        }

        if (this.Concurrency is < 1 or > 16)
        {
            return ValidationResult.Error("--concurrency must be between 1 and 16.");
        }

        return ValidationResult.Success();
    }
}

internal sealed class GenerateCommand(
    DotnetListRunner dotnetRunner,
    NuGetClient nugetClient,
    NoticeWriter noticeWriter) : AsyncCommand<GenerateSettings>
{
    protected override async Task<int> ExecuteAsync(CommandContext context, GenerateSettings settings,
        CancellationToken cancellationToken)
    {
        //AnsiConsole.Write(new FigletText("Notice Generator").Color(Color.SteelBlue1));
        AnsiConsole.Write(new Text("Notice Generator", new Style(foreground: Color.SteelBlue1)));
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine($"[bold]Project:[/] {settings.Project}");
        AnsiConsole.MarkupLine($"[bold]Scope:[/]   {settings.Scope}");
        AnsiConsole.MarkupLine($"[bold]Output:[/]  {settings.Output}");
        AnsiConsole.WriteLine();

        // 1. dotnet list package でパッケージ一覧を取得
        List<PackageRef> packages;
        try
        {
            packages = await AnsiConsole.Status()
                .Spinner(Spinner.Known.Dots)
                .StartAsync(
                    "Running [cyan]dotnet list package[/]...",
                    async _ =>
                        await dotnetRunner.GetPackagesAsync(
                            settings.Project,
                            settings.Scope,
                            settings.NoVersion))
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]Error:[/] Failed to run dotnet list package: {ex.Message}");
            return 1;
        }

        if (packages.Count == 0)
        {
            AnsiConsole.MarkupLine("[yellow]Warning:[/] No packages found.");
            return 0;
        }

        AnsiConsole.MarkupLine($"Found [green]{packages.Count}[/] package(s). Fetching metadata from NuGet...");
        AnsiConsole.WriteLine();

        // 2. NuGet API からメタデータ・ライセンス全文を並列取得
        var entries = new List<NoticeEntry>();
        var semaphore = new SemaphoreSlim(settings.Concurrency);
        var lockObj = new object();

        await AnsiConsole.Progress()
            .AutoClear(false)
            .Columns(
                new TaskDescriptionColumn(),
                new ProgressBarColumn(),
                new PercentageColumn(),
                new ElapsedTimeColumn(),
                new SpinnerColumn(Spinner.Known.Dots))
            .StartAsync(async ctx =>
            {
                var task = ctx.AddTask("Fetching NuGet metadata", maxValue: packages.Count);

#if !DEBUG
                var tasks = packages.Select(async pkg =>
                {
                    await semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
                    try
                    {
                        NoticeEntry entry;
                        if (pkg.Version is null)
                        {
                            entry = new NoticeEntry { Id = pkg.Id, };
                        }
                        else
                        {
                            try
                            {
                                entry = await nugetClient.FetchAsync(pkg.Id, pkg.Version, cancellationToken)
                                    .ConfigureAwait(false);
                            }
                            catch (Exception ex)
                            {
                                entry = new NoticeEntry { Id = pkg.Id, Version = pkg.Version, Error = ex.Message, };
                            }
                        }

                        lock (lockObj)
                        {
                            entries.Add(entry);
                        }
                    }
                    finally
                    {
                        semaphore.Release();
                        task.Increment(1);
                    }
                });

                await Task.WhenAll(tasks).ConfigureAwait(false);
#else
                foreach (var pkg in packages)
                {
                    NoticeEntry entry;
                    try
                    {
                        entry = await nugetClient.FetchAsync(pkg.Id, pkg.Version, cancellationToken)
                            .ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        entry = new NoticeEntry { Id = pkg.Id, Version = pkg.Version, Error = ex.Message, };
                    }

                    entries.Add(entry);
                    task.Increment(1);
                }
#endif
            })
            .ConfigureAwait(false);

        AnsiConsole.WriteLine();

        // 3. 結果サマリー表示
        var succeeded = entries.Count(e => e.Error is null);
        var failed = entries.Count(e => e.Error is not null);
        var noLicenseText = entries.Count(e => e.Error is null && e.LicenseText is null);

        var table = new Table()
            .Border(TableBorder.Rounded)
            .AddColumn("Status")
            .AddColumn("Count");
        table.AddRow("[green]Success[/]", succeeded.ToString());
        if (noLicenseText > 0)
        {
            table.AddRow("[yellow]No license text[/]", noLicenseText.ToString());
        }

        if (failed > 0)
        {
            table.AddRow("[red]Failed[/]", failed.ToString());
        }

        AnsiConsole.Write(table);
        AnsiConsole.WriteLine();

        if (failed > 0)
        {
            AnsiConsole.MarkupLine("[yellow]Packages with errors:[/]");
            foreach (var e in entries.Where(e => e.Error is not null))
            {
                AnsiConsole.MarkupLine($"  [red]✗[/] {e.Id} {e.Version}: {e.Error}");
            }

            AnsiConsole.WriteLine();
        }

        if (noLicenseText > 0)
        {
            AnsiConsole.MarkupLine("[yellow]Packages without license text (manual review recommended):[/]");
            foreach (var e in entries.Where(e => e.Error is null && e.LicenseText is null))
            {
                AnsiConsole.MarkupLine($"  [yellow]⚠[/] {e.Id} {e.Version}");
            }

            AnsiConsole.WriteLine();
        }

        // 4. NOTICE.md 書き出し
        await noticeWriter.WriteAsync(settings.Output, entries, settings.NoVersion, cancellationToken);
        AnsiConsole.MarkupLine($"[green]✓[/] Generated: [bold]{settings.Output}[/]");

        return failed > 0 ? 2 : 0; // 2 = partial failure
    }
}
