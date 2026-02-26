// --------------------------------------------------------------------------------------------------------------------
// <copyright file="DotnetListRunner.cs" company="MareMare">
// Copyright © 2026 MareMare.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.
// </copyright>
// --------------------------------------------------------------------------------------------------------------------

using System.Diagnostics;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace NoticeGenerator;

/// <summary>
/// 埋め込みの get-packages.ps1 を pwsh 経由で呼び出し、
/// dotnet list package の JSON 出力をパースしてパッケージ一覧を返す。
/// </summary>
internal sealed class DotnetListRunner : IDisposable
{
    // 埋め込みスクリプトを実行時に一時ファイルへ展開したパス
    private readonly string _scriptPath = ExtractEmbeddedScript();

    public async Task<List<PackageRef>> GetPackagesAsync(
        string project,
        string scope, // "all" | "top"
        bool noVersion)
    {
        var json = await this.RunPwshAsync(project, scope).ConfigureAwait(false);
        return ParseJson(json, scope, noVersion);
    }

    // -------------------------------------------------------
    // IDisposable：一時ファイルの後片付け
    // -------------------------------------------------------

    public void Dispose()
    {
        try { File.Delete(this._scriptPath); }
        catch
        {
            /* best effort */
        }
    }

    // -------------------------------------------------------
    // 埋め込みリソースの展開
    // -------------------------------------------------------

    /// <summary>
    /// アセンブリに埋め込まれた get-packages.ps1 を
    /// 一時ディレクトリに書き出してそのパスを返す。
    /// </summary>
    private static string ExtractEmbeddedScript()
    {
        const string resourceSuffix = "get-packages.ps1";

        var assembly = Assembly.GetExecutingAssembly();
        var resourceName = assembly.GetManifestResourceNames()
                               .FirstOrDefault(name =>
                                   name.EndsWith(resourceSuffix, StringComparison.OrdinalIgnoreCase))
                           ?? throw new InvalidOperationException(
                               $"Embedded resource '{resourceSuffix}' not found. " +
                               $"Confirm <EmbeddedResource Include=\"{resourceSuffix}\" /> in .csproj.");

        var tempPath = Path.Combine(
            Path.GetTempPath(),
            $"NoticeGenerator_{resourceSuffix}");

        using var stream = assembly.GetManifestResourceStream(resourceName)!;
        using var file = File.Create(tempPath);
        stream.CopyTo(file);

        return tempPath;
    }

    // -------------------------------------------------------
    // pwsh 実行可能ファイルの解決
    // -------------------------------------------------------

    private static string ResolvePwshExecutable()
    {
        // pwsh (PowerShell 7+) を優先し、なければ powershell (Windows PS) を試みる
        foreach (var candidate in (string[])["pwsh", "powershell",])
        {
            try
            {
                using var probe = Process.Start(new ProcessStartInfo
                {
                    FileName = candidate,
                    ArgumentList = { "-NoProfile", "-Command", "exit 0", },
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                });
                probe?.WaitForExit();
                if (probe?.ExitCode == 0)
                {
                    return candidate;
                }
            }
            catch
            {
                /* not found, try next */
            }
        }

        throw new InvalidOperationException(
            """
            Neither 'pwsh' (PowerShell 7+) nor 'powershell' was found on PATH.
            Please install PowerShell: https://aka.ms/powershell
            """);
    }

    // -------------------------------------------------------
    // JSON パース (dotnet list package --format json)
    // -------------------------------------------------------

    private static List<PackageRef> ParseJson(string json, string scope, bool noVersion)
    {
        // dotnet list は JSON 以外の警告行を stdout に混在させることがある
        // → 最初の '{' 以降のみを切り出す
        var jsonStart = json.IndexOf('{');
        if (jsonStart > 0)
        {
            json = json[jsonStart..];
        }

        var root = JsonSerializer.Deserialize<DotnetListOutput>(json)
                   ?? throw new InvalidOperationException(
                       "dotnet list package returned empty or invalid JSON.");

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var packages = new List<PackageRef>();

        foreach (var proj in root.Projects ?? [])
        {
            foreach (var framework in proj.Frameworks ?? [])
            {
                IEnumerable<DotnetPackage> candidates = scope == "top"
                    ? framework.TopLevelPackages ?? []
                    :
                    [
                        .. framework.TopLevelPackages ?? [],
                        .. framework.TransitivePackages ?? [],
                    ];

                foreach (var pkg in candidates)
                {
                    if (string.IsNullOrWhiteSpace(pkg.Id))
                    {
                        continue;
                    }

                    var key = noVersion ? pkg.Id : $"{pkg.Id}/{pkg.ResolvedVersion}";
                    if (!seen.Add(key))
                    {
                        continue;
                    }

                    packages.Add(new PackageRef(
                        pkg.Id,
                        noVersion ? null : pkg.ResolvedVersion));
                }
            }
        }

        return [.. packages.OrderBy(p => p.Id, StringComparer.OrdinalIgnoreCase),];
    }

    // -------------------------------------------------------
    // pwsh 呼び出し
    // -------------------------------------------------------

    private async Task<string> RunPwshAsync(string project, string scope)
    {
        // pwsh が見つからない環境では powershell にフォールバック
        var pwsh = ResolvePwshExecutable();
        var psi = new ProcessStartInfo
        {
            FileName = pwsh,
            ArgumentList =
            {
                "-NoProfile",
                "-NonInteractive",
                "-ExecutionPolicy",
                "Bypass",
                "-File",
                this._scriptPath,
                "-Project",
                project,
                "-Scope",
                scope,
            },
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };

        using var process = new Process();
        process.StartInfo = psi;
        process.Start();

        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        await Task.WhenAll(stdoutTask, stderrTask).ConfigureAwait(false);
        await process.WaitForExitAsync().ConfigureAwait(false);

        var stdout = await stdoutTask.ConfigureAwait(false);
        var stderr = await stderrTask.ConfigureAwait(false);
        var strippedStdErr = StripAnsi(stderr);

        if (process.ExitCode != 0)
        {
            var detail = string.IsNullOrWhiteSpace(strippedStdErr) ? stdout : strippedStdErr;
            throw new InvalidOperationException(
                $"get-packages.ps1 exited with code {process.ExitCode}.\n{detail.Trim()}");
        }

        return stdout;

        static string StripAnsi(string s)
        {
            return Regex.Replace(s, @"\x1B\[[0-9;]*[A-Za-z]", "");
        }
    }
}

// ---- JSON モデル (dotnet list package --format json) ----

file sealed class DotnetListOutput
{
    [JsonPropertyName("projects")]
    public List<DotnetProject>? Projects { get; init; }
}

file sealed class DotnetProject
{
    [JsonPropertyName("frameworks")]
    public List<DotnetFramework>? Frameworks { get; init; }
}

file sealed class DotnetFramework
{
    [JsonPropertyName("topLevelPackages")]
    public List<DotnetPackage>? TopLevelPackages { get; init; }

    [JsonPropertyName("transitivePackages")]
    public List<DotnetPackage>? TransitivePackages { get; init; }
}

file sealed class DotnetPackage
{
    [JsonPropertyName("id")]
    public string? Id { get; init; }

    [JsonPropertyName("resolvedVersion")]
    public string? ResolvedVersion { get; init; }
}
