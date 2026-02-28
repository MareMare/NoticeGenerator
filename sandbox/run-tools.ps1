# PowerShell

[CmdletBinding(PositionalBinding = $false)]
param(
    # dotnet tool run のコマンド名（通常はパッケージ側で定義されたコマンド）
    [Parameter(Mandatory = $false)]
    [string] $ToolCommand = "NoticeGenerator",

    # NuGet の設定ファイル
    [Parameter(Mandatory = $false)]
    [string] $NuGetConfigPath = "nuget.config",

    # プレリリース許可
    [Parameter(Mandatory = $false)]
    [switch] $PreRelease,

    # -- 以降に渡された追加引数（自動で収集）
    [Parameter(ValueFromRemainingArguments = $true)]
    [string[]] $ToolArgs
)

# --- Color helpers -----------------------------------------------------------
function Write-Info    { param([string]$Message) Write-Host $Message -ForegroundColor Cyan }
function Write-Success { param([string]$Message) Write-Host $Message -ForegroundColor Green }
function Write-Warn    { param([string]$Message) Write-Host $Message -ForegroundColor Yellow }
function Write-ErrLine { param([string]$Message) Write-Host $Message -ForegroundColor Red }
function Write-Header  { param([string]$Message)
    Write-Host ("`n=== " + $Message + " ===") -ForegroundColor Magenta
}

# --- WorkDir: この ps1 があるディレクトリをカレントに ---
$scriptDir =
    if ($PSScriptRoot) { $PSScriptRoot }
    elseif ($MyInvocation.MyCommand.Path) { Split-Path -Path $MyInvocation.MyCommand.Path -Parent }
    else { $PWD.Path }

Set-Location -Path $scriptDir
Write-Info "Working directory: $scriptDir"

function Invoke-ToolUpdateInstallAndRun {
    param(
        [Parameter(Mandatory = $true)]
        [string] $ToolCommand,

        [Parameter(Mandatory = $false)]
        [string] $NuGetConfigPath = "nuget.config",

        [Parameter(Mandatory = $false)]
        [switch] $PreRelease,

        [Parameter(Mandatory = $false)]
        [string[]] $ToolArgs = @()
    )

    # PackageId は規約で生成
    $owner     = "MareMare"
    $source    = "https://nuget.pkg.github.com/$owner/index.json"
    $PackageId = "$owner.$ToolCommand"

    if (-not (Test-Path -Path "dotnet-tools.json")) {
        Write-Warn "NuGet config file not found: dotnet-tools.json"
        dotnet new tool-manifest --force
    }

    if (-not (Test-Path -Path $NuGetConfigPath)) {
        Write-Warn "NuGet config file not found: $NuGetConfigPath"
        dotnet new nugetconfig --force
        dotnet nuget add source $source `
            --name "MareMare GitHub Packages" `
            --username "token" `
            --password "%GPR_API_KEY%" `
            --store-password-in-clear-text `
            --configfile "$NuGetConfigPath"
        dotnet tool install $PackageId --local --prerelease --configfile "$NuGetConfigPath"
    }

    Write-Header "Tool Update Start"
    Write-Info   "Package: $PackageId"
    Write-Info   "Config : $NuGetConfigPath"
    if ($PreRelease) { Write-Info "PreRelease: enabled" }

    $commonArgs = @("--local", "--configfile", $NuGetConfigPath)
    if ($PreRelease) { $commonArgs += "--prerelease" }

    # update → 失敗時は install
    dotnet tool update $PackageId @commonArgs
    if ($LASTEXITCODE -ne 0) {
        Write-Warn "Update failed for $PackageId. Attempting install..."
        dotnet tool install $PackageId @commonArgs

        if ($LASTEXITCODE -ne 0) {
            Write-ErrLine "Install failed for $PackageId (exit code: $LASTEXITCODE)"
            throw "Install failed for $PackageId (exit code: $LASTEXITCODE)"
        } else {
            Write-Success "Installed: $PackageId"
        }
    } else {
        Write-Success "Updated: $PackageId"
    }

    Write-Header "Tool Execution Start"
    Write-Info "Command : $ToolCommand"
    if ($ToolArgs -and $ToolArgs.Count -gt 0) {
        Write-Info "Args    : $($ToolArgs -join ' ')"
        # --% で PowerShell の解釈を止める
        dotnet tool run $ToolCommand -- $ToolArgs
    } else {
        dotnet tool run $ToolCommand
    }

    if ($LASTEXITCODE -ne 0) {
        Write-ErrLine "Tool execution failed: $ToolCommand (exit code: $LASTEXITCODE)"
        throw "Tool execution failed: $ToolCommand (exit code: $LASTEXITCODE)"
    } else {
        Write-Success "Tool execution completed: $ToolCommand"
    }
}

try {
    Invoke-ToolUpdateInstallAndRun -ToolCommand $ToolCommand -NuGetConfigPath $NuGetConfigPath -PreRelease:$PreRelease -ToolArgs $ToolArgs
    exit 0
}
catch {
    Write-Error $_
    exit 1
}
