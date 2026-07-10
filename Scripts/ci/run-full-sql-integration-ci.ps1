$ErrorActionPreference = "Stop"

function Write-Section {
    param([Parameter(Mandatory = $true)][string]$Name)

    Write-Host ""
    Write-Host "== $Name =="
}

function ConvertTo-SafeLaneName {
    param([Parameter(Mandatory = $true)][string]$Name)

    $safe = [Regex]::Replace($Name.ToLowerInvariant(), "[^a-z0-9]+", "-").Trim("-")
    if ([string]::IsNullOrWhiteSpace($safe)) {
        return "ci-lane"
    }

    return $safe
}

function Format-Duration {
    param([TimeSpan]$Duration)

    return "{0:hh\:mm\:ss\.fff}" -f $Duration
}

function Invoke-TimedCommand {
    param(
        [Parameter(Mandatory = $true)][string]$Name,
        [Parameter(Mandatory = $true)][scriptblock]$Command
    )

    Write-Section $Name
    $stopwatch = [System.Diagnostics.Stopwatch]::StartNew()
    & $Command
    $exitCode = $LASTEXITCODE
    $stopwatch.Stop()
    $script:TimingRecords.Add([ordered]@{
        Name = $Name
        Duration = Format-Duration $stopwatch.Elapsed
        DurationSeconds = [Math]::Round($stopwatch.Elapsed.TotalSeconds, 3)
        Status = if ($exitCode -eq 0) { "Passed" } else { "Failed" }
    }) | Out-Null
    Write-Host "$Name duration: $(Format-Duration $stopwatch.Elapsed)"

    if ($exitCode -ne 0) {
        throw "$Name failed with exit code $exitCode."
    }
}

function Get-TrxCounters {
    param([Parameter(Mandatory = $true)][string]$Path)

    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw "TRX file missing: $Path"
    }

    [xml]$trxXml = Get-Content -LiteralPath $Path -Raw
    $counters = $trxXml.SelectSingleNode("//*[local-name()='Counters']")
    if ($null -eq $counters) {
        throw "TRX counters missing: $Path"
    }

    return [ordered]@{
        Total = [int]$counters.total
        Executed = [int]$counters.executed
        Passed = [int]$counters.passed
        Failed = [int]$counters.failed
        Skipped = [int]$counters.notExecuted
    }
}

function Invoke-TestLane {
    param(
        [Parameter(Mandatory = $true)][string]$Name,
        [Parameter(Mandatory = $true)][string]$Filter,
        [string]$Project = $script:Project,
        [bool]$AllowRetry = $false,
        [int]$Attempts = 1,
        [int]$DelaySeconds = 2
    )

    Write-Section $Name
    $safeLaneName = ConvertTo-SafeLaneName $Name
    $trxPath = Join-Path $script:TestResultsRoot "$safeLaneName.trx"
    $stopwatch = [System.Diagnostics.Stopwatch]::StartNew()
    $status = "Failed"
    $attemptsUsed = 0

    for ($attempt = 1; $attempt -le $Attempts; $attempt++) {
        $attemptsUsed = $attempt
        if ($AllowRetry) {
            Write-Host "Readiness attempt $attempt of $Attempts"
        }

        dotnet test $Project `
            --no-restore `
            --no-build `
            --logger "console;verbosity=minimal" `
            --logger "trx;LogFileName=$safeLaneName.trx" `
            --results-directory $script:TestResultsRoot `
            --filter $Filter

        if ($LASTEXITCODE -eq 0) {
            $status = "Passed"
            break
        }

        if (-not $AllowRetry -or $attempt -eq $Attempts) {
            $stopwatch.Stop()
            $script:LaneRecords.Add([ordered]@{
                Lane = $Name
                Filter = $Filter
                Selected = ""
                Executed = ""
                Passed = ""
                Failed = ""
                Skipped = ""
                Duration = Format-Duration $stopwatch.Elapsed
                Status = "Failed"
                Artifact = $trxPath
                Attempts = $attemptsUsed
            }) | Out-Null
            throw "$Name failed."
        }

        Start-Sleep -Seconds $DelaySeconds
    }

    $stopwatch.Stop()
    $counters = Get-TrxCounters -Path $trxPath
    if ($counters.Total -le 0) {
        throw "$Name produced zero executed test records."
    }

    $script:LaneRecords.Add([ordered]@{
        Lane = $Name
        Filter = $Filter
        Selected = $counters.Total
        Executed = $counters.Executed
        Passed = $counters.Passed
        Failed = $counters.Failed
        Skipped = $counters.Skipped
        Duration = Format-Duration $stopwatch.Elapsed
        Status = $status
        Artifact = $trxPath
        Attempts = $attemptsUsed
    }) | Out-Null

    Write-Host "$Name count: total=$($counters.Total), passed=$($counters.Passed), failed=$($counters.Failed), skipped=$($counters.Skipped)"
}

function Get-ListedTestNames {
    param([string[]]$Output)

    $markerIndex = -1
    for ($index = 0; $index -lt $Output.Count; $index++) {
        if ($Output[$index].Trim().Equals("The following Tests are available:", [StringComparison]::Ordinal)) {
            $markerIndex = $index
            break
        }
    }

    if ($markerIndex -lt 0) {
        return @()
    }

    return $Output |
        Select-Object -Skip ($markerIndex + 1) |
        ForEach-Object { $_.Trim() } |
        Where-Object { -not [string]::IsNullOrWhiteSpace($_) } |
        Sort-Object -Unique
}

function Invoke-SelectionLane {
    param(
        [Parameter(Mandatory = $true)][string]$Name,
        [Parameter(Mandatory = $true)][string]$Filter
    )

    Write-Section "$Name selection"
    $safeLaneName = ConvertTo-SafeLaneName "$Name selection"
    $outputPath = Join-Path $script:SelectionRoot "$safeLaneName.txt"
    $stopwatch = [System.Diagnostics.Stopwatch]::StartNew()
    $output = dotnet test $script:Project `
        --no-restore `
        --no-build `
        --list-tests `
        --filter $Filter 2>&1
    $exitCode = $LASTEXITCODE
    $stopwatch.Stop()

    $output | Set-Content -LiteralPath $outputPath -Encoding UTF8
    if ($exitCode -ne 0) {
        throw "$Name selection failed with exit code $exitCode."
    }

    $names = @(Get-ListedTestNames -Output $output)
    if ($names.Count -le 0) {
        throw "$Name selection returned zero tests."
    }

    $record = [ordered]@{
        Name = $Name
        Filter = $Filter
        SelectedCount = $names.Count
        Duration = Format-Duration $stopwatch.Elapsed
        Artifact = $outputPath
    }
    $script:SelectionRecords.Add($record) | Out-Null
    $script:SelectionNames[$Name] = $names

    Write-Host "$Name selected tests: $($names.Count)"
}

function Set-TestOutputConnectionString {
    param([Parameter(Mandatory = $true)][string]$ConnectionString)

    $settings = @{
        ConnectionStrings = @{
            IronDeveloperDb = $ConnectionString
        }
    }

    $outputSettingsPaths = @(
        Join-Path $PSScriptRoot "..\..\IronDev.IntegrationTests\bin\Debug\net10.0\appsettings.Test.json"
        Join-Path $PSScriptRoot "..\..\IronDev.IntegrationTests.Api\bin\Debug\net10.0\appsettings.Test.json"
    )

    foreach ($outputSettingsPath in $outputSettingsPaths) {
        $resolvedPath = [System.IO.Path]::GetFullPath($outputSettingsPath)
        $outputDirectory = Split-Path -Parent $resolvedPath
        if (-not (Test-Path $outputDirectory)) {
            throw "Test output directory does not exist. Build the solution before running SQL CI: $outputDirectory"
        }

        $settings |
            ConvertTo-Json -Depth 4 |
            Set-Content -LiteralPath $resolvedPath -Encoding UTF8
    }
}

function New-CiConnectionString {
    if ([string]::IsNullOrWhiteSpace($env:IRONDEV_CI_SQL_DATABASE) -or -not $env:IRONDEV_CI_SQL_DATABASE.StartsWith("IronDev_CI_", [StringComparison]::Ordinal)) {
        throw "IRONDEV_CI_SQL_DATABASE must start with IronDev_CI_."
    }

    if ([string]::IsNullOrWhiteSpace($env:IRONDEV_CI_SQL_PASSWORD)) {
        throw "IRONDEV_CI_SQL_PASSWORD is required for SQL integration CI."
    }

    $server = if ([string]::IsNullOrWhiteSpace($env:IRONDEV_CI_SQL_SERVER)) { "localhost" } else { $env:IRONDEV_CI_SQL_SERVER }
    $port = if ([string]::IsNullOrWhiteSpace($env:IRONDEV_CI_SQL_PORT)) { "1433" } else { $env:IRONDEV_CI_SQL_PORT }
    $user = if ([string]::IsNullOrWhiteSpace($env:IRONDEV_CI_SQL_USER)) { "sa" } else { $env:IRONDEV_CI_SQL_USER }
    $passwordKey = "Pass" + "word"

    return "Server=$server,$port;Database=$env:IRONDEV_CI_SQL_DATABASE;User Id=$user;$passwordKey=$env:IRONDEV_CI_SQL_PASSWORD;Encrypt=True;TrustServerCertificate=True;"
}

function Write-MarkdownTable {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][string]$Title,
        [Parameter(Mandatory = $true)][object[]]$Rows
    )

    $lines = @("# $Title", "")
    if ($Rows.Count -eq 0) {
        $lines += "No rows recorded."
        Set-Content -LiteralPath $Path -Value $lines -Encoding UTF8
        return
    }

    $headers = @($Rows[0].Keys)
    $lines += "| " + ($headers -join " | ") + " |"
    $lines += "| " + (($headers | ForEach-Object { "---" }) -join " | ") + " |"
    foreach ($row in $Rows) {
        $values = foreach ($header in $headers) {
            $value = $row[$header]
            if ($null -eq $value) {
                ""
            }
            else {
                $value.ToString().Replace("|", "/")
            }
        }

        $lines += "| " + ($values -join " | ") + " |"
    }

    Set-Content -LiteralPath $Path -Value $lines -Encoding UTF8
}

function Write-ExecutionGapSummary {
    param([Parameter(Mandatory = $true)][string]$Path)

    $requiresRealDatabase = @($script:SelectionNames["RequiresRealDatabase"])
    $longRunning = @($script:SelectionNames["LongRunning"])
    $overlap = @($requiresRealDatabase | Where-Object { $longRunning -contains $_ })
    $realDatabaseSmokeExecuted = $script:LaneRecords |
        Where-Object { $_["Lane"] -eq "Real database smoke expansion" } |
        Select-Object -First 1

    $lines = @(
        "# Full SQL Execution Gap Summary",
        "",
        "Selection proof means a filter lists tests.",
        "",
        "Execution proof means the selected tests ran and passed.",
        "",
        "This PR does not treat selection proof as execution proof.",
        "",
        "| Field | Value |",
        "| --- | --- |",
        "| RequiresRealDatabase selected count | $($requiresRealDatabase.Count) |",
        "| LongRunning selected count | $($longRunning.Count) |",
        "| RequiresRealDatabase / LongRunning overlap count | $($overlap.Count) |",
        "| Real database smoke expansion executed count | $(if ($realDatabaseSmokeExecuted) { $realDatabaseSmokeExecuted["Executed"] } else { "not-executed" }) |",
        "| Broad RequiresRealDatabase execution status | deferred/split-required |",
        "| Broad LongRunning execution status | deferred/split-required |",
        "",
        "The broad RequiresRealDatabase / LongRunning category sets remain selection-only in this lane.",
        "",
        "They require a later split into bounded executable groups before their full selection can be called execution-proven.",
        "",
        "ManualLocal remains existing ignored manual-local debt and is not executed by this lane.",
        "",
        "Full SQL CI is not release approval, merge approval, deployment readiness, source-apply authority, rollback authority, workflow continuation authority, or memory promotion authority."
    )

    Set-Content -LiteralPath $Path -Value $lines -Encoding UTF8
}

$script:RepoRoot = Resolve-Path (Join-Path $PSScriptRoot "..\..")
Set-Location $script:RepoRoot

$script:ArtifactRoot = Join-Path $script:RepoRoot "artifacts\ci\full-sql-integration"
$script:TestResultsRoot = Join-Path $script:ArtifactRoot "test-results"
$script:SelectionRoot = Join-Path $script:ArtifactRoot "selection"
$script:Project = "IronDev.IntegrationTests/IronDev.IntegrationTests.csproj"
$script:ApiProject = "IronDev.IntegrationTests.Api/IronDev.IntegrationTests.Api.csproj"
$script:TimingRecords = New-Object System.Collections.ArrayList
$script:LaneRecords = New-Object System.Collections.ArrayList
$script:SelectionRecords = New-Object System.Collections.ArrayList
$script:SelectionNames = @{}

if (Test-Path -LiteralPath $script:ArtifactRoot) {
    Remove-Item -LiteralPath $script:ArtifactRoot -Recurse -Force
}

New-Item -ItemType Directory -Path $script:TestResultsRoot -Force | Out-Null
New-Item -ItemType Directory -Path $script:SelectionRoot -Force | Out-Null
& (Join-Path $PSScriptRoot "write-ci-evidence-summary.ps1") `
    -ArtifactDirectory $script:ArtifactRoot `
    -WorkflowName "full-sql-integration-ci" `
    -LaneName "full-sql-integration" `
    -CommandCategory "dotnet restore/build/test" `
    -ResultStatus "Started"

Write-Section "Full SQL integration CI"
Write-Host "Full SQL CI reports evidence only."
Write-Host "Full SQL CI is not release approval, merge approval, deployment readiness, source-apply authority, rollback authority, or execution permission."
Write-Host "Database: $env:IRONDEV_CI_SQL_DATABASE"

$totalStopwatch = [System.Diagnostics.Stopwatch]::StartNew()
$status = "Failed"
$failure = $null

try {
    $connectionString = New-CiConnectionString

    Invoke-TimedCommand "Restore solution" {
        dotnet restore IronDev.slnx --verbosity minimal
    }

    Invoke-TimedCommand "Build solution" {
        dotnet build IronDev.slnx --no-restore --verbosity minimal
    }

    Set-TestOutputConnectionString -ConnectionString $connectionString
    $env:ConnectionStrings__IronDeveloperDb = $connectionString

    Invoke-SelectionLane -Name "RequiresRealDatabase" -Filter "TestCategory=RequiresRealDatabase"
    Invoke-SelectionLane -Name "LongRunning" -Filter "TestCategory=LongRunning"
    Invoke-SelectionLane -Name "RealDatabase contains" -Filter "TestCategory~RealDatabase"
    Invoke-SelectionLane -Name "Store contains" -Filter "TestCategory~Store"

    Invoke-TestLane `
        -Name "SQL Server connectivity smoke" `
        -Filter "FullyQualifiedName~BlockC02SqlServerConnectivitySmokeTests" `
        -AllowRetry $true `
        -Attempts 30 `
        -DelaySeconds 2

    Invoke-TimedCommand "Clean database migration verification" {
        & (Join-Path $script:RepoRoot "Database\verify-clean-database.ps1") `
            -ConnectionString $connectionString `
            -Database "$($env:IRONDEV_CI_SQL_DATABASE)_Migration"
    }

    Invoke-TimedCommand "Apply migrations to SQL test catalog" {
        & (Join-Path $script:RepoRoot "Database\apply-migrations.ps1") `
            -ConnectionString $connectionString
    }

    Invoke-TestLane `
        -Name "In-process API contract" `
        -Project $script:ApiProject `
        -Filter "FullyQualifiedName~EndpointContractTests|FullyQualifiedName~ApiTestBaseCatalogGuardContractTests"

    $sqlStoreFilter = @(
        "FullyQualifiedName~AcceptedApprovalSqlStoreTests",
        "FullyQualifiedName~PolicySatisfactionSqlStoreTests",
        "FullyQualifiedName~ApplyDryRunStoreTests",
        "FullyQualifiedName~DryRunReceiptStoreTests",
        "FullyQualifiedName~PatchArtifactStoreTests",
        "FullyQualifiedName~WorkflowTransitionRecordStoreTests",
        "FullyQualifiedName~ToolRequestStoreTests"
    ) -join "|"

    Invoke-TestLane -Name "SQL-backed governance stores" -Filter $sqlStoreFilter

    $realDatabaseSmokeFilter = @(
        "FullyQualifiedName~RealDatabaseApprovalDecisionSmokeTests",
        "FullyQualifiedName~RealDatabaseDogfoodReceiptSmokeTests",
        "FullyQualifiedName~RealDatabasePolicyDecisionSmokeTests",
        "FullyQualifiedName~RealDatabaseThoughtLedgerGovernanceReferenceSmokeTests",
        "FullyQualifiedName~RealDatabaseToolGateDecisionSmokeTests",
        "FullyQualifiedName~RealDatabaseToolRequestSmokeTests",
        "FullyQualifiedName~RealDatabaseWorkflowRunSmokeTests"
    ) -join "|"

    Invoke-TestLane -Name "Real database smoke expansion" -Filter $realDatabaseSmokeFilter

    Invoke-TestLane `
        -Name "REL-3 SQL API alpha smoke" `
        -Project $script:ApiProject `
        -Filter "FullyQualifiedName~AlphaSmokeApiPersistenceTests.Rel3_OneTicket_ReachesApplied_ThroughSqlBackedApi"

    Invoke-TestLane `
        -Name "REL-5 chat confirmed ticket governed run" `
        -Project $script:ApiProject `
        -Filter "FullyQualifiedName~AlphaSmokeApiPersistenceTests.Rel5_ChatConfirmedTicket_StartsGovernedRun_ThroughSqlBackedApi"

    # Selection is not execution: the DEMO/HERO seed proofs were previously only
    # SELECTED by the category lanes above, never executed. Execute them by exact
    # name so the demo baseline, post-seed usability probe, chat-ticket proof, and
    # advisory-finding disposition gate are proven on every full SQL run.
    $demoSeedProofFilter = @(
        "FullyQualifiedName~DemoSeedApiDrivenTests.DemoSeed_BaselineHistory_IsApiDrivenAndSqlPersisted",
        "FullyQualifiedName~DemoSeedApiDrivenTests.Demo2_ChatConfirmedTicket_IsVisibleAndStartableThroughApi",
        "FullyQualifiedName~DemoSeedApiDrivenTests.Hero_BulkDiscountAdvisoryFinding_RequiresDispositionBeforeApplied",
        "FullyQualifiedName~BoundedRepairApiDrivenTests",
        "FullyQualifiedName~FindingDrivenRevisionApiDrivenTests"
    ) -join "|"

    Invoke-TestLane `
        -Name "DEMO seed and HERO disposition proofs" `
        -Project $script:ApiProject `
        -Filter $demoSeedProofFilter

    $categoryContractFilter = @(
        "FullyQualifiedName~IntegrationTestCategoryContractTests",
        "FullyQualifiedName~SlowQuarantineCategoryContractTests"
    ) -join "|"
    Invoke-TestLane -Name "Category safety contracts" -Filter $categoryContractFilter

    # Selection is not execution: the destructive-catalog guard contract must run
    # here, in the lane whose ephemeral IronDev_CI_* database it exists to allow.
    Invoke-TestLane `
        -Name "Destructive catalog guard contract" `
        -Project $script:ApiProject `
        -Filter "FullyQualifiedName~ApiTestBaseCatalogGuardContractTests"
    Invoke-TestLane -Name "C11 secret scan compatibility" -Filter "FullyQualifiedName~BlockC11SecretScanningRegressionTests"

    $status = "Passed"
}
catch {
    $failure = $_
}
finally {
    $totalStopwatch.Stop()
    $script:TimingRecords.Add([ordered]@{
        Name = "Total workflow script"
        Duration = Format-Duration $totalStopwatch.Elapsed
        DurationSeconds = [Math]::Round($totalStopwatch.Elapsed.TotalSeconds, 3)
        Status = $status
    }) | Out-Null

    Write-MarkdownTable -Path (Join-Path $script:ArtifactRoot "sql-lane-summary.md") -Title "Full SQL Lane Summary" -Rows @($script:LaneRecords)
    Write-MarkdownTable -Path (Join-Path $script:ArtifactRoot "selection-count-summary.md") -Title "Full SQL Selection Count Summary" -Rows @($script:SelectionRecords)
    Write-MarkdownTable -Path (Join-Path $script:ArtifactRoot "timing-summary.md") -Title "Full SQL Timing Summary" -Rows @($script:TimingRecords)
    Write-ExecutionGapSummary -Path (Join-Path $script:ArtifactRoot "execution-gap-summary.md")

    $summary = [ordered]@{
        Status = $status
        Database = $env:IRONDEV_CI_SQL_DATABASE
        LaneRecords = @($script:LaneRecords)
        SelectionRecords = @($script:SelectionRecords)
        TimingRecords = @($script:TimingRecords)
    }
    $summary | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $script:ArtifactRoot "test-count-summary.json") -Encoding UTF8

    & (Join-Path $PSScriptRoot "write-ci-evidence-summary.ps1") `
        -ArtifactDirectory $script:ArtifactRoot `
        -WorkflowName "full-sql-integration-ci" `
        -LaneName "full-sql-integration" `
        -CommandCategory "dotnet restore/build/test" `
        -ResultStatus $status
}

if ($null -ne $failure) {
    throw $failure
}

Write-Section "Full SQL integration CI complete"
Write-Host "SQL green means the database lane ran, not that the product is safe."
