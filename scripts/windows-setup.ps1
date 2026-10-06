param(
    [string]$ProjectsDirectory = (Join-Path ([Environment]::GetFolderPath('MyDocuments')) 'Projects')
)

$ErrorActionPreference = 'Stop'

if (-not (Get-Command git -ErrorAction SilentlyContinue)) {
    throw 'Install Git for Windows before running this script.'
}

$projectDirectory = Join-Path $ProjectsDirectory 'SandinFinance'
if (Test-Path -LiteralPath $projectDirectory) {
    throw "The project path already exists. Inspect it before proceeding: $projectDirectory"
}

New-Item -ItemType Directory -Force -Path $ProjectsDirectory | Out-Null
& git clone 'https://github.com/Sandinoff/SandinFinance.git' $projectDirectory
if ($LASTEXITCODE -ne 0) {
    throw 'Git clone failed. Check GitHub access and any partially created folder.'
}

Write-Host "Project folder prepared: $projectDirectory"
