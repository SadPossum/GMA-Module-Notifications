[CmdletBinding()]
param([switch] $NoBuild)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repositoryRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
$toolDirectory = Join-Path $repositoryRoot 'artifacts\tools'
$toolName = if ([Environment]::OSVersion.Platform -eq [PlatformID]::Win32NT) { 'dotnet-ef.exe' } else { 'dotnet-ef' }
$toolPath = Join-Path $toolDirectory $toolName
if (-not (Test-Path -LiteralPath $toolPath -PathType Leaf)) {
    New-Item -ItemType Directory -Path $toolDirectory -Force | Out-Null
    & dotnet tool install --tool-path $toolDirectory dotnet-ef --version 10.0.8
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}

$projects = @(
    'src\Gma.Modules.Notifications.Persistence.SqlServerMigrations\Gma.Modules.Notifications.Persistence.SqlServerMigrations.csproj',
    'src\Gma.Modules.Notifications.Persistence.PostgreSqlMigrations\Gma.Modules.Notifications.Persistence.PostgreSqlMigrations.csproj'
)
foreach ($relativeProject in $projects) {
    $project = Join-Path $repositoryRoot $relativeProject
    $arguments = @(
        'migrations', 'has-pending-model-changes',
        '--project', $project,
        '--startup-project', $project
    )
    if ($NoBuild) { $arguments += '--no-build' }
    & $toolPath @arguments
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}

Write-Host 'Notifications migration drift checks passed.'
