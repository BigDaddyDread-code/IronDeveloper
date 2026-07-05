param(
    [switch]$CheckOnly,
    [switch]$Seed,
    [string]$Project = "BookSeller",
    [ValidateSet("Deterministic", "Live")]
    [string]$ModelMode = "Deterministic",
    [string]$OutputDirectory,
    [switch]$Json,
    [switch]$Markdown
)

$ErrorActionPreference = "Stop"

if (-not $CheckOnly -and -not $Seed) {
    $CheckOnly = $true
}

$script:Stages = New-Object System.Collections.Generic.List[object]
$script:Gaps = New-Object System.Collections.Generic.List[string]
$script:OutputRoot = $null
$script:ReceiptPath = $null

$KnownReasonCodes = @(
    "DemoRepoRootNotFound",
    "DemoToolchainMissing",
    "DemoBookSellerMissing",
    "DemoProjectUnsupported",
    "DemoModelModeUnsupported",
    "DemoRootSafetyNotEvaluated",
    "DemoRootSafetyBlocked",
    "DemoSqlPersistenceUnavailable",
    "DemoApiUnavailable",
    "DemoTicketSeedFailed",
    "DemoRunSeedFailed",
    "DemoApprovalRequired",
    "DemoContinuationFailed",
    "DemoApplyFailed",
    "DemoReportMissing",
    "DemoReceiptWriteSkipped",
    "DemoReceiptWriteFailed",
    "DemoSeedPassed"
)

function Add-Stage {
    param(
        [Parameter(Mandatory = $true)][string]$Stage,
        [ValidateSet("Passed", "Blocked", "Skipped", "Failed")]
        [Parameter(Mandatory = $true)][string]$Status,
        [Parameter(Mandatory = $true)][string]$ReasonCode,
        [Parameter(Mandatory = $true)][string]$Message,
        [hashtable]$Details = @{}
    )

    $script:Stages.Add([pscustomobject]@{
        stage = $Stage
        status = $Status
        reasonCode = $ReasonCode
        message = $Message
        details = $Details
    }) | Out-Null
}

function Add-Gap {
    param([Parameter(Mandatory = $true)][string]$Gap)
    $script:Gaps.Add($Gap) | Out-Null
}

function Get-RepoRoot {
    $current = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
    while (-not [string]::IsNullOrWhiteSpace($current)) {
        if (Test-Path -LiteralPath (Join-Path $current "IronDev.slnx")) {
            return $current
        }

        $parent = Split-Path -Parent $current
        if ($parent -eq $current) {
            break
        }

        $current = $parent
    }

    return $null
}

function Test-Tool {
    param([Parameter(Mandatory = $true)][string]$Name)
    return $null -ne (Get-Command $Name -ErrorAction SilentlyContinue)
}

function Is-SameOrUnder {
    param(
        [Parameter(Mandatory = $true)][string]$Parent,
        [Parameter(Mandatory = $true)][string]$Child
    )

    $p = [System.IO.Path]::GetFullPath($Parent).TrimEnd('\', '/')
    $c = [System.IO.Path]::GetFullPath($Child).TrimEnd('\', '/')
    return $c.Equals($p, [System.StringComparison]::OrdinalIgnoreCase) -or
        $c.StartsWith($p + [System.IO.Path]::DirectorySeparatorChar, [System.StringComparison]::OrdinalIgnoreCase) -or
        $c.StartsWith($p + [System.IO.Path]::AltDirectorySeparatorChar, [System.StringComparison]::OrdinalIgnoreCase)
}

function Has-ReparseAncestor {
    param([Parameter(Mandatory = $true)][string]$Path)

    $current = [System.IO.Path]::GetFullPath($Path)
    while (-not [string]::IsNullOrWhiteSpace($current)) {
        if (Test-Path -LiteralPath $current) {
            $item = Get-Item -LiteralPath $current -Force
            if (($item.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
                return $true
            }
        }

        $parent = Split-Path -Parent $current
        if ($parent -eq $current) {
            break
        }

        $current = $parent
    }

    return $false
}

function Test-SafeOutputRoot {
    param(
        [Parameter(Mandatory = $true)][string]$RepoRoot,
        [Parameter(Mandatory = $true)][string]$Path
    )

    $full = [System.IO.Path]::GetFullPath($Path)
    $pathRoot = [System.IO.Path]::GetPathRoot($full)
    $userProfileRoot = [Environment]::GetFolderPath([Environment+SpecialFolder]::UserProfile)
    $temp = [System.IO.Path]::GetTempPath().TrimEnd('\', '/')

    if ($full.Equals($pathRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
        return "DriveOrFilesystemRoot"
    }

    if ($full.TrimEnd('\', '/').Equals($userProfileRoot.TrimEnd('\', '/'), [System.StringComparison]::OrdinalIgnoreCase)) {
        return "UserHomeRoot"
    }

    if ($full.TrimEnd('\', '/').Equals($temp, [System.StringComparison]::OrdinalIgnoreCase)) {
        return "BroadTempRoot"
    }

    if (Is-SameOrUnder -Parent $RepoRoot -Child $full) {
        return "UnderRepositoryRoot"
    }

    if (Has-ReparseAncestor -Path $full) {
        return "PathContainsSymlinkOrReparsePoint"
    }

    return $null
}

function New-DefaultOutputRoot {
    $base = if (-not [string]::IsNullOrWhiteSpace($env:LOCALAPPDATA)) {
        Join-Path $env:LOCALAPPDATA "IronDev\demo-seed"
    }
    else {
        Join-Path ([System.IO.Path]::GetTempPath()) "IronDev\demo-seed"
    }

    return Join-Path $base ([DateTimeOffset]::UtcNow.ToString("yyyyMMdd-HHmmss"))
}

function Redact-UserPath {
    param([string]$Value)

    if ([string]::IsNullOrWhiteSpace($Value)) {
        return $Value
    }

    $result = $Value
    $userHome = [Environment]::GetFolderPath([Environment+SpecialFolder]::UserProfile)
    if (-not [string]::IsNullOrWhiteSpace($userHome)) {
        $result = $result.Replace($userHome, "<user-home>")
    }

    $temp = [System.IO.Path]::GetTempPath().TrimEnd('\', '/')
    if (-not [string]::IsNullOrWhiteSpace($temp)) {
        $result = $result.Replace($temp, "<temp>")
    }

    return $result
}

function Get-ResultObject {
    param(
        [Parameter(Mandatory = $true)][string]$RepoRoot,
        [Parameter(Mandatory = $true)][string]$OverallStatus
    )

    return [pscustomobject]@{
        project = $Project
        modelMode = $ModelMode
        mode = if ($Seed) { "Seed" } else { "CheckOnly" }
        outputDirectory = if ($script:OutputRoot) { Redact-UserPath $script:OutputRoot } else { $null }
        receiptPath = if ($script:ReceiptPath) { Redact-UserPath $script:ReceiptPath } else { $null }
        repoRoot = if ($RepoRoot) { Redact-UserPath $RepoRoot } else { "" }
        knownReasonCodes = $KnownReasonCodes
        stages = $script:Stages
        gaps = $script:Gaps
        boundary = "The demo seed drives product APIs and governed backend paths. It is evidence only: it does not approve, satisfy policy, continue workflow, apply source by itself, claim release readiness, or create the live chat ticket ahead of the demo."
        status = $OverallStatus
    }
}

function Convert-ToMarkdown {
    param([Parameter(Mandatory = $true)]$Result)

    $lines = New-Object System.Collections.Generic.List[string]
    $lines.Add("# Demo Seed Summary") | Out-Null
    $lines.Add("") | Out-Null
    $lines.Add("- Project: $($Result.project)") | Out-Null
    $lines.Add("- Model mode: $($Result.modelMode)") | Out-Null
    $lines.Add("- Mode: $($Result.mode)") | Out-Null
    $lines.Add("- Status: $($Result.status)") | Out-Null
    if ($Result.outputDirectory) { $lines.Add("- Output: $($Result.outputDirectory)") | Out-Null }
    if ($Result.receiptPath) { $lines.Add("- Receipt: $($Result.receiptPath)") | Out-Null }
    $lines.Add("") | Out-Null
    $lines.Add("## Stages") | Out-Null
    $lines.Add("") | Out-Null
    $lines.Add("| Stage | Status | Reason |") | Out-Null
    $lines.Add("| --- | --- | --- |") | Out-Null
    foreach ($stage in $Result.stages) {
        $lines.Add("| $($stage.stage) | $($stage.status) | $($stage.reasonCode) |") | Out-Null
    }
    $lines.Add("") | Out-Null
    $lines.Add("## Boundary") | Out-Null
    $lines.Add("") | Out-Null
    $lines.Add($Result.boundary) | Out-Null
    return ($lines -join [Environment]::NewLine)
}

function Complete-DemoSeed {
    param(
        [Parameter(Mandatory = $true)][string]$RepoRoot,
        [Parameter(Mandatory = $true)][string]$OverallStatus,
        [int]$ExitCode = 0
    )

    $result = Get-ResultObject -RepoRoot $RepoRoot -OverallStatus $OverallStatus
    $jsonText = $result | ConvertTo-Json -Depth 30
    $markdownText = Convert-ToMarkdown -Result $result

    if ($script:OutputRoot) {
        New-Item -ItemType Directory -Force -Path $script:OutputRoot | Out-Null
        Set-Content -LiteralPath (Join-Path $script:OutputRoot "demo-seed-result.json") -Value $jsonText -Encoding UTF8
        Set-Content -LiteralPath (Join-Path $script:OutputRoot "demo-seed-summary.md") -Value $markdownText -Encoding UTF8
    }

    if ($Json) {
        $jsonText
    }
    elseif ($Markdown) {
        $markdownText
    }
    else {
        Write-Host "== IronDev demo seed =="
        Write-Host "Status: $OverallStatus"
        foreach ($stage in $script:Stages) {
            Write-Host ("{0}: {1} ({2})" -f $stage.stage, $stage.status, $stage.reasonCode)
        }
        if ($script:OutputRoot) {
            Write-Host "Output: $(Redact-UserPath $script:OutputRoot)"
        }
    }

    exit $ExitCode
}

$repoRoot = Get-RepoRoot
if ([string]::IsNullOrWhiteSpace($repoRoot)) {
    Add-Stage "RepoCheck" "Failed" "DemoRepoRootNotFound" "Could not locate IronDev.slnx."
    Complete-DemoSeed -RepoRoot "" -OverallStatus "Failed" -ExitCode 1
}

Set-Location $repoRoot
Add-Stage "RepoCheck" "Passed" "DemoRepoRootFound" "Repository root located." @{ repoRoot = Redact-UserPath $repoRoot }

$dotnetAvailable = Test-Tool "dotnet"
$gitAvailable = Test-Tool "git"
if (-not $dotnetAvailable -or -not $gitAvailable) {
    Add-Stage "ToolchainCheck" "Failed" "DemoToolchainMissing" "dotnet and git are required for the API-driven demo seed." @{ dotnet = $dotnetAvailable; git = $gitAvailable }
    Complete-DemoSeed -RepoRoot $repoRoot -OverallStatus "Failed" -ExitCode 1
}
Add-Stage "ToolchainCheck" "Passed" "DemoToolchainAvailable" "Required local tools are present." @{ dotnet = $dotnetAvailable; git = $gitAvailable }

$ticketsPath = Join-Path $repoRoot "TestFixtures\BookSeller\tickets.json"
$sampleRoot = Join-Path $repoRoot "Samples\BookSeller"
if (-not (Test-Path -LiteralPath $sampleRoot) -or -not (Test-Path -LiteralPath $ticketsPath)) {
    Add-Stage "FixtureCheck" "Blocked" "DemoBookSellerMissing" "BookSeller sample or ticket fixture is missing."
    Complete-DemoSeed -RepoRoot $repoRoot -OverallStatus "Blocked" -ExitCode 1
}

$fixture = Get-Content -LiteralPath $ticketsPath -Raw | ConvertFrom-Json
$ticketKeys = @($fixture.tickets | ForEach-Object { $_.key })
if (-not ($ticketKeys -contains "validate-book") -or -not ($ticketKeys -contains "search-by-author")) {
    Add-Stage "FixtureCheck" "Blocked" "DemoBookSellerMissing" "BookSeller fixture must include validate-book and search-by-author tickets."
    Complete-DemoSeed -RepoRoot $repoRoot -OverallStatus "Blocked" -ExitCode 1
}
Add-Stage "FixtureCheck" "Passed" "DemoBookSellerFound" "BookSeller fixture contains the required demo tickets." @{ tickets = $ticketKeys }

if ($Project -ne "BookSeller") {
    Add-Stage "ProjectCheck" "Blocked" "DemoProjectUnsupported" "The v0.1 local alpha demo seed supports only the BookSeller fixture."
    Complete-DemoSeed -RepoRoot $repoRoot -OverallStatus "Blocked" -ExitCode 1
}

if ($ModelMode -ne "Deterministic") {
    Add-Stage "ModelCheck" "Blocked" "DemoModelModeUnsupported" "DEMO-1 seed is deterministic-only. Live model demo mode is a later explicit decision."
    Complete-DemoSeed -RepoRoot $repoRoot -OverallStatus "Blocked" -ExitCode 1
}
Add-Stage "ModelCheck" "Passed" "DemoModelModeDeterministic" "Deterministic model fixture selected."

if ($CheckOnly) {
    Add-Stage "RootSafetyCheck" "Skipped" "DemoRootSafetyNotEvaluated" "Check-only mode writes no demo artifacts and does not connect to SQL/API."
    Add-Stage "SqlCheck" "Skipped" "DemoSqlPersistenceUnavailable" "Check-only mode does not connect to SQL."
    Add-Stage "ApiCheck" "Skipped" "DemoApiUnavailable" "Check-only mode does not call the API."
    Add-Stage "ReceiptWrite" "Skipped" "DemoReceiptWriteSkipped" "Check-only mode writes no seed receipt."
    Add-Gap "Check-only does not prove Applied history, PausedForApproval history, or report reconstruction."
    Complete-DemoSeed -RepoRoot $repoRoot -OverallStatus "Passed" -ExitCode 0
}

if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = New-DefaultOutputRoot
}

$outputFull = [System.IO.Path]::GetFullPath($OutputDirectory)
$unsafeReason = Test-SafeOutputRoot -RepoRoot $repoRoot -Path $outputFull
if ($unsafeReason) {
    Add-Stage "RootSafetyCheck" "Blocked" "DemoRootSafetyBlocked" "Demo seed output root is unsafe: $unsafeReason." @{ outputDirectory = Redact-UserPath $outputFull; unsafeReason = $unsafeReason }
    Complete-DemoSeed -RepoRoot $repoRoot -OverallStatus "Blocked" -ExitCode 1
}

$repoStatus = git -C $repoRoot status --porcelain
if (-not [string]::IsNullOrWhiteSpace($repoStatus)) {
    Add-Stage "RepoCheck" "Blocked" "DemoRootSafetyBlocked" "Repository has uncommitted changes. Commit or stash before running mutation-shaped demo seed."
    Complete-DemoSeed -RepoRoot $repoRoot -OverallStatus "Blocked" -ExitCode 1
}

$script:OutputRoot = $outputFull
$script:ReceiptPath = Join-Path $script:OutputRoot "demo-seed-receipt.json"
New-Item -ItemType Directory -Force -Path $script:OutputRoot | Out-Null
Add-Stage "RootSafetyCheck" "Passed" "DemoRootSafetyPassed" "Output root is outside the repository and not under a reparse-point ancestor." @{ outputDirectory = Redact-UserPath $script:OutputRoot }

$env:DEMO_SEED_RECEIPT = $script:ReceiptPath
try {
    Add-Stage "SqlCheck" "Passed" "DemoSqlPersistenceAvailable" "DEMO-1 uses the API integration test host with SQL-backed stores."
    Add-Stage "ApiCheck" "Passed" "DemoApiAvailable" "DEMO-1 drives authenticated API routes in-process."

    dotnet build IronDev.slnx --nologo --verbosity minimal
    if ($LASTEXITCODE -ne 0) {
        Add-Stage "BuildCheck" "Failed" "DemoRunSeedFailed" "Solution build failed before demo seed."
        Complete-DemoSeed -RepoRoot $repoRoot -OverallStatus "Failed" -ExitCode 1
    }
    Add-Stage "BuildCheck" "Passed" "DemoBuildPassed" "Solution build passed before demo seed."

    dotnet test IronDev.IntegrationTests.Api/IronDev.IntegrationTests.Api.csproj `
        --no-build `
        --filter "FullyQualifiedName~DemoSeedApiDrivenTests.DemoSeed_BaselineHistory_IsApiDrivenAndSqlPersisted" `
        --logger "console;verbosity=minimal" `
        --logger "trx;LogFileName=demo-seed.trx" `
        --results-directory $script:OutputRoot
    if ($LASTEXITCODE -ne 0) {
        Add-Stage "DemoSeedRun" "Failed" "DemoRunSeedFailed" "DEMO-1 API-driven seed proof failed."
        Complete-DemoSeed -RepoRoot $repoRoot -OverallStatus "Failed" -ExitCode 1
    }
}
finally {
    Remove-Item Env:DEMO_SEED_RECEIPT -ErrorAction SilentlyContinue
}

if (-not (Test-Path -LiteralPath $script:ReceiptPath)) {
    Add-Stage "ReceiptWrite" "Failed" "DemoReceiptWriteFailed" "The demo seed test passed but did not write its receipt."
    Complete-DemoSeed -RepoRoot $repoRoot -OverallStatus "Failed" -ExitCode 1
}

$receipt = Get-Content -LiteralPath $script:ReceiptPath -Raw | ConvertFrom-Json
if ($receipt.appliedTicket.state -ne "Applied") {
    Add-Stage "AppliedTicket" "Failed" "DemoApplyFailed" "validate-book did not reach Applied through the demo seed proof."
    Complete-DemoSeed -RepoRoot $repoRoot -OverallStatus "Failed" -ExitCode 1
}
if ($receipt.pausedTicket.state -ne "PausedForApproval") {
    Add-Stage "PausedTicket" "Failed" "DemoApprovalRequired" "search-by-author did not stop at PausedForApproval."
    Complete-DemoSeed -RepoRoot $repoRoot -OverallStatus "Failed" -ExitCode 1
}
if ($receipt.liveChatTicketSeeded -ne $false) {
    Add-Stage "LiveTicketCheck" "Failed" "DemoTicketSeedFailed" "Demo seed must not create the live chat ticket ahead of the demo."
    Complete-DemoSeed -RepoRoot $repoRoot -OverallStatus "Failed" -ExitCode 1
}

Add-Stage "AppliedTicket" "Passed" "DemoSeedPassed" "validate-book reached Applied through API/SQL governed path."
Add-Stage "PausedTicket" "Passed" "DemoApprovalRequired" "search-by-author stopped at PausedForApproval without approval, continuation, or apply."
Add-Stage "ReportCheck" "Passed" "DemoSeedPassed" "Reports reconstructed from SQL-backed API state."
Add-Stage "ReceiptWrite" "Passed" "DemoSeedPassed" "Demo seed receipt was written with redacted local paths."

Complete-DemoSeed -RepoRoot $repoRoot -OverallStatus "Passed" -ExitCode 0
