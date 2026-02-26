param(
    [string]$Project = '.',
    [ValidateSet("all", "top")]
    [string]$Scope = "all"
)
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
[Console]::InputEncoding  = [System.Text.Encoding]::UTF8

# throw "Intentional failure for テスト"

$json = dotnet list $Project package --include-transitive --format json 2>&1

if ($LASTEXITCODE -ne 0) {
    Write-Error "dotnet list package failed (exit code $LASTEXITCODE): $json"
    exit $LASTEXITCODE
}

$json
