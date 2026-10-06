$ErrorActionPreference = 'Stop'
$projectFile = Join-Path $PSScriptRoot '../src/SandinFinance.Desktop/SandinFinance.Desktop.csproj'
if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw 'Install .NET 10 SDK before starting SandinFinance.'
}
& dotnet run --project $projectFile
if ($LASTEXITCODE -ne 0) { throw "SandinFinance exited with code $LASTEXITCODE." }
