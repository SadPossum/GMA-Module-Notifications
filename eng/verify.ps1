[CmdletBinding()]
param([switch] $SkipDocker)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repositoryRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
$solution = Join-Path $repositoryRoot 'Gma.Modules.Notifications.slnx'
$solutionSync = @(
    (Join-Path $repositoryRoot '..\..\framework\eng\sync-solution.ps1')
    (Join-Path $repositoryRoot '..\gma-framework\eng\sync-solution.ps1')
) | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
if ($null -eq $solutionSync) {
    throw 'Cannot locate the GMA Framework solution synchronization script.'
}

& $solutionSync -RepositoryRoot $repositoryRoot -Solution 'Gma.Modules.Notifications.slnx' -Check
& (Join-Path $PSScriptRoot 'check-boundaries.ps1')
& dotnet restore $solution
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
& dotnet build $solution --no-restore -m:1
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
& (Join-Path $PSScriptRoot 'check-migrations.ps1') -NoBuild
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
& dotnet test $solution --no-build --filter 'Category!=Docker' --logger 'console;verbosity=minimal'
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
& dotnet list $solution package --vulnerable --include-transitive
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

if (-not $SkipDocker) {
    $env:GMA_REQUIRE_DOCKER_TESTS = 'true'
    & dotnet test (Join-Path $repositoryRoot 'tests\Gma.Modules.Notifications.IntegrationTests\Gma.Modules.Notifications.IntegrationTests.csproj') --no-build --logger 'console;verbosity=minimal'
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}

Write-Host 'Notifications verification passed.'
