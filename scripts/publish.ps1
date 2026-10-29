param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$projectFile = Join-Path $projectRoot 'src\CleanC.App\CleanC.App.csproj'
$outputDirectory = Join-Path $projectRoot 'artifacts\CleanC-win-x64'

dotnet restore $projectFile --ignore-failed-sources
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

dotnet publish $projectFile `
    --configuration $Configuration `
    --self-contained false `
    --no-restore `
    --output $outputDirectory

exit $LASTEXITCODE
