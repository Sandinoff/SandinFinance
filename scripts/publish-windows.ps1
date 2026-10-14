param()
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
$output = Join-Path $projectRoot '.local\publish\win-x64'
$project = Join-Path $projectRoot 'src\SandinFinance.Desktop\SandinFinance.Desktop.csproj'
& dotnet publish $project -c Release -r win-x64 --self-contained true -o $output `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:IncludeAllContentForSelfExtract=true -p:EnableCompressionInSingleFile=true `
    -p:PublishTrimmed=false -p:DebugType=None -p:DebugSymbols=false
if ($LASTEXITCODE -ne 0) { throw 'Publishing SandinFinance failed.' }
$binary = Join-Path $output 'SandinFinance.Desktop.exe'
$target = Join-Path $projectRoot 'SandinFinance.exe'
Copy-Item -LiteralPath $binary -Destination $target -Force
$hash = (Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash.ToLowerInvariant()
Set-Content -LiteralPath ($target + '.sha256') -Value ($hash + '  SandinFinance.exe') -Encoding ASCII
Write-Output "Ready: $target"
Write-Output "SHA256: $hash"
