$ErrorActionPreference = "Stop"

function Write-Section {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Name
    )

    Write-Host ""
    Write-Host "== $Name =="
}

function ConvertTo-SafeLaneName {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Name
    )

    $safe = [Regex]::Replace($Name.ToLowerInvariant(), "[^a-z0-9]+", "-").Trim("-")
    if ([string]::IsNullOrWhiteSpace($safe)) {
        return "ci-lane"
    }

    return $safe
}

function Invoke-TestLane {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Name,

        [Parameter(Mandatory = $true)]
        [string]$Filter
    )

    Write-Section $Name
    $safeLaneName = ConvertTo-SafeLaneName $Name
    dotnet test $script:Project `
        --no-restore `
        --no-build `
        --logger "console;verbosity=minimal" `
        --logger "trx;LogFileName=$safeLaneName.trx" `
        --results-directory $script:TestResultsRoot `
        --filter $Filter
    if ($LASTEXITCODE -ne 0) {
        throw "$Name failed."
    }
}

function Invoke-TestLaneWithRetry {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Name,

        [Parameter(Mandatory = $true)]
        [string]$Filter,

        [int]$Attempts = 30,

        [int]$DelaySeconds = 2
    )

    Write-Section "$Name readiness"
    $safeLaneName = ConvertTo-SafeLaneName $Name
    for ($attempt = 1; $attempt -le $Attempts; $attempt++) {
        Write-Host "Attempt $attempt of $Attempts"
        dotnet test $script:Project `
            --no-restore `
            --no-build `
            --logger "console;verbosity=minimal" `
            --logger "trx;LogFileName=$safeLaneName.trx" `
            --results-directory $script:TestResultsRoot `
            --filter $Filter

        if ($LASTEXITCODE -eq 0) {
            Write-Host "$Name passed."
            return
        }

        if ($attempt -lt $Attempts) {
            Start-Sleep -Seconds $DelaySeconds
        }
    }

    throw "$Name did not pass after $Attempts attempts."
}

function Set-TestOutputConnectionString {
    param(
        [Parameter(Mandatory = $true)]
        [string]$ConnectionString
    )

    $outputSettingsPath = Join-Path $PSScriptRoot "..\..\IronDev.IntegrationTests\bin\Debug\net10.0\appsettings.Test.json"
    $resolvedPath = [System.IO.Path]::GetFullPath($outputSettingsPath)
    $outputDirectory = Split-Path -Parent $resolvedPath
    if (-not (Test-Path $outputDirectory)) {
        throw "Test output directory does not exist. Build the solution before running SQL CI: $outputDirectory"
    }

    $settings = @{
        ConnectionStrings = @{
            IronDeveloperDb = $ConnectionString
        }
    }

    $settings |
        ConvertTo-Json -Depth 4 |
        Set-Content -LiteralPath $resolvedPath -Encoding UTF8
}

$script:RepoRoot = Resolve-Path (Join-Path $PSScriptRoot "..\..")
$script:ArtifactRoot = Join-Path $script:RepoRoot "artifacts\ci\sql-integration"
$script:TestResultsRoot = Join-Path $script:ArtifactRoot "test-results"
$script:Project = "IronDev.IntegrationTests/IronDev.IntegrationTests.csproj"

if (Test-Path -LiteralPath $script:ArtifactRoot) {
    Remove-Item -LiteralPath $script:ArtifactRoot -Recurse -Force
}

New-Item -ItemType Directory -Path $script:TestResultsRoot -Force | Out-Null
& (Join-Path $PSScriptRoot "write-ci-evidence-summary.ps1") `
    -ArtifactDirectory $script:ArtifactRoot `
    -WorkflowName "sql-integration-ci" `
    -LaneName "sql-integration" `
    -CommandCategory "dotnet test" `
    -ResultStatus "Started"

Write-Section "SQL integration CI"
Write-Host "SQL CI reports evidence only."
Write-Host "SQL CI is not approval, merge readiness, release readiness, deployment readiness, policy satisfaction, or execution permission."

if ([string]::IsNullOrWhiteSpace($env:ConnectionStrings__IronDeveloperDb)) {
    throw "ConnectionStrings__IronDeveloperDb is required for SQL integration CI."
}

if ([string]::IsNullOrWhiteSpace($env:IRONDEV_CI_SQL_DATABASE) -or -not $env:IRONDEV_CI_SQL_DATABASE.StartsWith("IronDev_CI_", [StringComparison]::Ordinal)) {
    throw "IRONDEV_CI_SQL_DATABASE must start with IronDev_CI_."
}

Write-Host "Database: $env:IRONDEV_CI_SQL_DATABASE"
Set-TestOutputConnectionString -ConnectionString $env:ConnectionStrings__IronDeveloperDb

$sqlSmokeFilter = "FullyQualifiedName~BlockC02SqlServerConnectivitySmokeTests"

$sqlStoreFilter = @(
    "FullyQualifiedName~AcceptedApprovalSqlStoreTests",
    "FullyQualifiedName~PolicySatisfactionSqlStoreTests",
    "FullyQualifiedName~ApplyDryRunStoreTests",
    "FullyQualifiedName~DryRunReceiptStoreTests",
    "FullyQualifiedName~PatchArtifactStoreTests",
    "FullyQualifiedName~WorkflowTransitionRecordStoreTests",
    "FullyQualifiedName~ToolRequestStoreTests"
) -join "|"

Invoke-TestLaneWithRetry `
    -Name "SQL Server connectivity smoke" `
    -Filter $sqlSmokeFilter

Invoke-TestLane `
    -Name "SQL-backed governance stores" `
    -Filter $sqlStoreFilter

Write-Section "SQL integration CI complete"
Write-Host "A database-backed green check is evidence, not permission."
& (Join-Path $PSScriptRoot "write-ci-evidence-summary.ps1") `
    -ArtifactDirectory $script:ArtifactRoot `
    -WorkflowName "sql-integration-ci" `
    -LaneName "sql-integration" `
    -CommandCategory "dotnet test" `
    -ResultStatus "Passed"
& (Join-Path $PSScriptRoot "test-ci-evidence-artifact-safety.ps1") `
    -ArtifactDirectory $script:ArtifactRoot
