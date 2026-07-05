param(
    [switch]$CheckOnly,
    [string]$Project = "BookSeller",
    [string]$Ticket = "validate-book",
    [ValidateSet("Deterministic", "Live")]
    [string]$ModelMode = "Deterministic",
    [ValidateSet("CheckOnly", "Readiness", "Gate", "Report", "Applied")]
    [string]$RunUntil,
    [string]$ApiBaseUrl = "http://localhost:5118",
    [string]$OutputDirectory,
    [switch]$Json,
    [switch]$Markdown,
    [string]$ExistingTicketId,
    [string]$ExistingRunId,
    [switch]$RequireExistingAcceptedApproval,
    [switch]$RecordHumanApproval,
    [string]$ApprovalPhrase
)

$ErrorActionPreference = "Stop"

if (-not $PSBoundParameters.ContainsKey("RunUntil") -or $CheckOnly) {
    $RunUntil = "CheckOnly"
}

$script:Stages = New-Object System.Collections.Generic.List[object]
$script:NamedGaps = New-Object System.Collections.Generic.List[string]
$script:OutputRoot = $null
$script:ReceiptPath = $null
$script:KnownReasonCodes = @(
    "RepoRootNotFound",
    "BookSellerSampleMissing",
    "BookSellerTicketsMissing",
    "TicketKeyNotFound",
    "ExistingTicketIdNotSupported",
    "ExistingRunIdNotSupported",
    "DotnetMissing",
    "NodeMissing",
    "GitMissing",
    "ApiUnavailable",
    "ApiAvailable",
    "ApiAuthMissing",
    "SqlUnavailable",
    "SqlAvailable",
    "LocalOverrideMissing",
    "RootSafetyNotEvaluated",
    "UnsafeRoot",
    "RootSafetyBlocked",
    "DeterministicModelNotConfigured",
    "LiveModelNotConfigured",
    "LiveModelModeNotImplemented",
    "TicketPersistFailed",
    "TicketPersisted",
    "ReadinessBlocked",
    "SkeletonRunStartFailed",
    "CriticPackageMissing",
    "CriticReviewFailed",
    "CriticReviewRequestNotAutomated",
    "CriticReviewRecorded",
    "GateStateUnexpected",
    "AcceptedApprovalRequired",
    "AcceptedApprovalPersisted",
    "AcceptedApprovalRecorded",
    "ApprovalPhraseMissing",
    "ApprovalPhraseMismatch",
    "ApprovalTargetHashMismatch",
    "ContinuationRefused",
    "ContinuationUnblocked",
    "ContinuationRequiresCriticReview",
    "ContinuationRequiresFindingDisposition",
    "ApplyRefused",
    "Applied",
    "ApplyRequiresContinuation",
    "ApplyTargetMismatch",
    "ApplyReceiptMissing",
    "FinalReportMissing",
    "ReportMissing",
    "ReceiptWriteFailed",
    "ProjectImportNotAutomated",
    "ProjectNotFound",
    "FixtureInvalid",
    "ApiReturnedUnexpectedShape",
    "RunTimedOut",
    "BuildFailed",
    "TestsFailed",
    "CriticReviewReturnedFindings",
    "ReportHasNamedGaps",
    "SourceRootDirty",
    "SourceRootMutationDetected",
    "SourceRepoDirtyBeforeRun",
    "SourceRepoChangedUnexpectedly"
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
    $script:NamedGaps.Add($Gap) | Out-Null
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
        Join-Path $env:LOCALAPPDATA "IronDev\alpha-smoke"
    }
    else {
        Join-Path ([System.IO.Path]::GetTempPath()) "IronDev\alpha-smoke"
    }

    return Join-Path $base ([DateTimeOffset]::UtcNow.ToString("yyyyMMdd-HHmmss"))
}

function Invoke-CheckedCommand {
    param(
        [Parameter(Mandatory = $true)][string]$Stage,
        [Parameter(Mandatory = $true)][string]$ReasonCode,
        [Parameter(Mandatory = $true)][scriptblock]$Command
    )

    & $Command
    if ($LASTEXITCODE -ne 0) {
        Add-Stage $Stage "Failed" $ReasonCode "$Stage failed with exit code $LASTEXITCODE."
        return $false
    }

    return $true
}

function Get-ResultObject {
    param(
        [Parameter(Mandatory = $true)][string]$RepoRoot,
        [Parameter(Mandatory = $true)][string]$OverallStatus
    )

    return [pscustomobject]@{
        project = $Project
        ticket = $Ticket
        modelMode = $ModelMode
        runUntil = $RunUntil
        apiBaseUrl = $ApiBaseUrl
        outputDirectory = $script:OutputRoot
        receiptPath = $script:ReceiptPath
        repoRoot = $RepoRoot
        stages = $script:Stages
        namedGaps = $script:NamedGaps
        boundary = "Root safety is a precondition for smoke execution. It is not evidence, approval, or execution authority. Smoke success is evidence, not alpha readiness, release readiness, deployment readiness, approval, policy satisfaction, continuation, or source apply authority."
        status = $OverallStatus
    }
}

function Convert-ToMarkdown {
    param([Parameter(Mandatory = $true)]$Result)

    $lines = New-Object System.Collections.Generic.List[string]
    $lines.Add("# Alpha Smoke Summary") | Out-Null
    $lines.Add("") | Out-Null
    $lines.Add("- Project: $($Result.project)") | Out-Null
    $lines.Add("- Ticket: $($Result.ticket)") | Out-Null
    $lines.Add("- Model mode: $($Result.modelMode)") | Out-Null
    $lines.Add("- Run until: $($Result.runUntil)") | Out-Null
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
    $lines.Add("## Named Gaps") | Out-Null
    if ($Result.namedGaps.Count -eq 0) {
        $lines.Add("") | Out-Null
        $lines.Add("- None recorded.") | Out-Null
    }
    else {
        foreach ($gap in $Result.namedGaps) {
            $lines.Add("- $gap") | Out-Null
        }
    }
    $lines.Add("") | Out-Null
    $lines.Add("## Boundary") | Out-Null
    $lines.Add("") | Out-Null
    $lines.Add($Result.boundary) | Out-Null
    return ($lines -join [Environment]::NewLine)
}

function Complete-Smoke {
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
        Set-Content -LiteralPath (Join-Path $script:OutputRoot "alpha-smoke-result.json") -Value $jsonText -Encoding UTF8
        Set-Content -LiteralPath (Join-Path $script:OutputRoot "alpha-smoke-summary.md") -Value $markdownText -Encoding UTF8
    }

    if ($Json) {
        $jsonText
    }
    elseif ($Markdown) {
        $markdownText
    }
    else {
        Write-Host "== IronDev alpha smoke =="
        Write-Host "Status: $OverallStatus"
        foreach ($stage in $script:Stages) {
            Write-Host ("{0}: {1} ({2})" -f $stage.stage, $stage.status, $stage.reasonCode)
        }
        if ($script:OutputRoot) {
            Write-Host "Output: $script:OutputRoot"
        }
    }

    exit $ExitCode
}

$repoRoot = Get-RepoRoot
if ([string]::IsNullOrWhiteSpace($repoRoot)) {
    Add-Stage "RepoCheck" "Failed" "RepoRootNotFound" "Could not locate IronDev.slnx."
    Complete-Smoke -RepoRoot "" -OverallStatus "Failed" -ExitCode 1
}

Set-Location $repoRoot
Add-Stage "RepoCheck" "Passed" "RepoRootFound" "Repository root located." @{ repoRoot = $repoRoot }

$dotnetAvailable = Test-Tool "dotnet"
$gitAvailable = Test-Tool "git"
$nodeAvailable = Test-Tool "node"
if (-not $dotnetAvailable) {
    Add-Stage "ToolchainCheck" "Failed" "DotnetMissing" "The .NET SDK is required."
    Complete-Smoke -RepoRoot $repoRoot -OverallStatus "Failed" -ExitCode 1
}
if (-not $gitAvailable) {
    Add-Stage "ToolchainCheck" "Failed" "GitMissing" "Git is required."
    Complete-Smoke -RepoRoot $repoRoot -OverallStatus "Failed" -ExitCode 1
}
Add-Stage "ToolchainCheck" "Passed" "ToolchainAvailable" "Required local tools are present." @{ dotnet = $dotnetAvailable; git = $gitAvailable; node = $nodeAvailable }
if (-not $nodeAvailable) {
    Add-Gap "NodeMissing: node is not required for current D-2a service-level smoke because the script does not start UI."
}

$localOverride = Join-Path $repoRoot "appsettings.Development.Local.json"
if (Test-Path -LiteralPath $localOverride) {
    Add-Stage "LocalConfigCheck" "Passed" "LocalOverridePresent" "Local override file exists. Contents were not read."
}
else {
    Add-Stage "LocalConfigCheck" "Skipped" "LocalOverrideMissing" "No local override file was found. D-2a service-level deterministic smoke does not require it."
    Add-Gap "Local override file not present; API-backed smoke may require it later."
}

if (-not [string]::IsNullOrWhiteSpace($ExistingTicketId)) {
    Add-Stage "TicketLoad" "Blocked" "ExistingTicketIdNotSupported" "D-2a does not resume or use existing ticket IDs." @{ existingTicketId = $ExistingTicketId }
    Complete-Smoke -RepoRoot $repoRoot -OverallStatus "Blocked" -ExitCode 1
}

if (-not [string]::IsNullOrWhiteSpace($ExistingRunId)) {
    Add-Stage "SkeletonRunStart" "Blocked" "ExistingRunIdNotSupported" "D-2a does not resume existing skeleton runs." @{ existingRunId = $ExistingRunId }
    Complete-Smoke -RepoRoot $repoRoot -OverallStatus "Blocked" -ExitCode 1
}

$ticketsPath = Join-Path $repoRoot "TestFixtures\BookSeller\tickets.json"
$sampleRoot = Join-Path $repoRoot "Samples\BookSeller"
if (-not (Test-Path -LiteralPath $sampleRoot)) {
    Add-Stage "FixtureCheck" "Blocked" "BookSellerSampleMissing" "Samples/BookSeller is missing."
    Complete-Smoke -RepoRoot $repoRoot -OverallStatus "Blocked" -ExitCode 1
}
if (-not (Test-Path -LiteralPath $ticketsPath)) {
    Add-Stage "FixtureCheck" "Blocked" "BookSellerTicketsMissing" "TestFixtures/BookSeller/tickets.json is missing."
    Complete-Smoke -RepoRoot $repoRoot -OverallStatus "Blocked" -ExitCode 1
}

$fixture = Get-Content -LiteralPath $ticketsPath -Raw | ConvertFrom-Json
$fixtureTicket = $fixture.tickets | Where-Object { $_.key -eq $Ticket } | Select-Object -First 1
if ($null -eq $fixtureTicket) {
    Add-Stage "TicketLoad" "Blocked" "TicketKeyNotFound" "Ticket '$Ticket' was not found in the BookSeller fixture."
    Complete-Smoke -RepoRoot $repoRoot -OverallStatus "Blocked" -ExitCode 1
}
Add-Stage "FixtureCheck" "Passed" "FixtureValid" "BookSeller sample and ticket fixture are present." @{ sampleRoot = "Samples/BookSeller"; tickets = "TestFixtures/BookSeller/tickets.json" }
Add-Stage "TicketLoad" "Passed" "TicketLoaded" "Loaded fixture ticket '$Ticket'." @{ title = $fixtureTicket.title }

if ($RunUntil -eq "CheckOnly") {
    Add-Stage "RootSafetyCheck" "Skipped" "RootSafetyNotEvaluated" "Check-only mode prints diagnostics only and writes no smoke artifacts."
    Add-Stage "SqlCheck" "Skipped" "SqlUnavailable" "Check-only mode does not connect to SQL."
    Add-Stage "ApiCheck" "Skipped" "ApiUnavailable" "Check-only mode does not call the API."
    Add-Stage "ReceiptWrite" "Skipped" "ReceiptWriteSkipped" "Check-only mode writes no receipt."
    Add-Gap "Check-only does not prove readiness, skeleton run, critic package, gate state, or report reconstruction."
    Complete-Smoke -RepoRoot $repoRoot -OverallStatus "Passed" -ExitCode 0
}

if ($ModelMode -eq "Live") {
    Add-Stage "ReadinessCheck" "Blocked" "LiveModelModeNotImplemented" "Live model alpha smoke is intentionally not implemented in this D-2a command."
    Add-Gap "Live-model mode must be added as a later explicit slice and must never fall back to deterministic."
    Complete-Smoke -RepoRoot $repoRoot -OverallStatus "Blocked" -ExitCode 1
}

if ($Project -ne "BookSeller") {
    Add-Stage "TicketPersist" "Blocked" "ProjectNotFound" "Only the BookSeller fixture is supported by this deterministic alpha smoke."
    Complete-Smoke -RepoRoot $repoRoot -OverallStatus "Blocked" -ExitCode 1
}

if ($RunUntil -eq "Applied") {
    if ($RequireExistingAcceptedApproval) {
        # REL-3 path: the test owns the governed API approval request and proves SQL persistence.
    }
    elseif (-not $RecordHumanApproval) {
        Add-Stage "ApprovalCheck" "Blocked" "AcceptedApprovalRequired" "Applied mode requires explicit -RecordHumanApproval. The smoke never creates approval by default."
        Complete-Smoke -RepoRoot $repoRoot -OverallStatus "Blocked" -ExitCode 1
    }

    if (-not $RequireExistingAcceptedApproval) {
        if ([string]::IsNullOrWhiteSpace($ApprovalPhrase)) {
            Add-Stage "ApprovalCheck" "Blocked" "ApprovalPhraseMissing" "Applied mode requires -ApprovalPhrase `"I approve continuation for run <runId> package <hash>`"."
            Complete-Smoke -RepoRoot $repoRoot -OverallStatus "Blocked" -ExitCode 1
        }

        $expectedApprovalPhraseTemplate = "I approve continuation for run <runId> package <hash>"
        if ($ApprovalPhrase -ne $expectedApprovalPhraseTemplate) {
            Add-Stage "ApprovalCheck" "Blocked" "ApprovalPhraseMismatch" "Approval phrase must exactly match the documented template so the smoke can bind it to the generated run id and package hash."
            Complete-Smoke -RepoRoot $repoRoot -OverallStatus "Blocked" -ExitCode 1
        }
    }
}

if ($RunUntil -in @("Gate", "Report", "Applied")) {
    $repoStatus = git -C $repoRoot status --porcelain
    if (-not [string]::IsNullOrWhiteSpace($repoStatus)) {
        Add-Stage "RepoCheck" "Blocked" "SourceRepoDirtyBeforeRun" "The source repository has uncommitted changes. Commit/stash them before running mutation-shaped smoke."
        Complete-Smoke -RepoRoot $repoRoot -OverallStatus "Blocked" -ExitCode 1
    }
}

if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = New-DefaultOutputRoot
}
$outputFull = [System.IO.Path]::GetFullPath($OutputDirectory)
$unsafeReason = Test-SafeOutputRoot -RepoRoot $repoRoot -Path $outputFull
if ($unsafeReason) {
    Add-Stage "RootSafetyCheck" "Blocked" "RootSafetyBlocked" "Smoke output root is unsafe: $unsafeReason." @{ outputDirectory = $outputFull; unsafeReason = $unsafeReason; legacyReasonCode = "UnsafeRoot" }
    Complete-Smoke -RepoRoot $repoRoot -OverallStatus "Blocked" -ExitCode 1
}

$script:OutputRoot = $outputFull
if ($RunUntil -eq "Readiness") {
    $script:ReceiptPath = $null
    Remove-Item Env:ALPHA_SMOKE_RECEIPT -ErrorAction SilentlyContinue
}
else {
    $script:ReceiptPath = Join-Path $script:OutputRoot "run-receipt.json"
    $env:ALPHA_SMOKE_RECEIPT = $script:ReceiptPath
}
Add-Stage "RootSafetyCheck" "Passed" "RootSafetyPassed" "Smoke output root is outside the repository and not under a reparse-point ancestor." @{ outputDirectory = $script:OutputRoot }
if ($RunUntil -eq "Applied" -and $RequireExistingAcceptedApproval) {
    Add-Stage "SqlCheck" "Passed" "SqlAvailable" "REL-3 persisted mode uses the API test host with SQL-backed stores."
    Add-Stage "ApiCheck" "Passed" "ApiAvailable" "REL-3 persisted mode drives the authenticated API routes in-process."
}
else {
    Add-Stage "SqlCheck" "Skipped" "SqlUnavailable" "D-2a/REL-2 deterministic command uses the service-level in-memory smoke path, not SQL."
    Add-Stage "ApiCheck" "Skipped" "ApiUnavailable" "D-2a/REL-2 deterministic command does not start or call the API."
    Add-Gap "Service-level mode is in-memory; use -RunUntil Applied -RequireExistingAcceptedApproval for REL-3 SQL/API persistence."
}

if (-not (Invoke-CheckedCommand "ReadinessCheck" "ReadinessBlocked" {
    dotnet build IronDev.slnx --nologo --verbosity minimal
})) {
    Complete-Smoke -RepoRoot $repoRoot -OverallStatus "Failed" -ExitCode 1
}
Add-Stage "ReadinessCheck" "Passed" "ReadinessPassed" "Solution build passed before skeleton run."

if ($RunUntil -eq "Readiness") {
    Add-Stage "TicketPersist" "Skipped" "ProjectImportNotAutomated" "Readiness mode stops before service-level ticket resolution."
    Add-Stage "SkeletonRunStart" "Skipped" "SkeletonRunNotRequested" "Readiness mode stops before skeleton run."
    Add-Stage "RunEvidenceRefresh" "Skipped" "RunEvidenceNotRequested" "Readiness mode stops before run evidence refresh."
    Add-Stage "CriticPackageFetch" "Skipped" "CriticPackageNotRequested" "Readiness mode stops before critic package creation."
    Add-Stage "CriticReviewRequest" "Skipped" "CriticReviewRequestNotAutomated" "Readiness mode does not request critic review."
    Add-Stage "GateStateVerify" "Skipped" "GateStateNotRequested" "Readiness mode stops before the human gate."
    Add-Stage "ReportFetch" "Skipped" "ReportNotRequested" "Readiness mode stops before report reconstruction."
    Add-Stage "ReceiptWrite" "Skipped" "ReceiptWriteSkipped" "Readiness mode writes alpha-smoke result and summary only; no run receipt exists before skeleton run."
    Add-Gap "Readiness mode does not prove skeleton run, critic package, gate state, or report reconstruction."
    Complete-Smoke -RepoRoot $repoRoot -OverallStatus "Passed" -ExitCode 0
}

if ($RunUntil -eq "Applied" -and $RequireExistingAcceptedApproval) {
    Add-Stage "TicketPersist" "Passed" "TicketPersisted" "REL-3 creates the BookSeller project and ticket through the authenticated API backed by SQL."
}
else {
    Add-Stage "TicketPersist" "Skipped" "ProjectImportNotAutomated" "The deterministic smoke resolves the fixture ticket inside the service-level harness; API ticket persistence is a named gap."
}

$testFilter = if ($RunUntil -eq "Applied" -and $RequireExistingAcceptedApproval) {
    "FullyQualifiedName~AlphaSmokeApiPersistenceTests.Rel3_OneTicket_ReachesApplied_ThroughSqlBackedApi"
}
elseif ($RunUntil -eq "Applied") {
    "FullyQualifiedName~AlphaLoopSmokeTests.AlphaSmoke_OneTicket_ReachesApplied_WithDeterministicApproval"
}
else {
    "FullyQualifiedName~AlphaLoopSmokeTests.AlphaSmoke_OneTicket_ReachesHumanGate_WithADeterministicBuilder"
}

if ($RunUntil -eq "Applied" -and -not $RequireExistingAcceptedApproval) {
    $env:ALPHA_SMOKE_APPROVAL_PHRASE = $ApprovalPhrase
}
else {
    Remove-Item Env:ALPHA_SMOKE_APPROVAL_PHRASE -ErrorAction SilentlyContinue
}

if (-not (Invoke-CheckedCommand "SkeletonRunStart" "SkeletonRunStartFailed" {
    $projectUnderTest = if ($RunUntil -eq "Applied" -and $RequireExistingAcceptedApproval) {
        "IronDev.IntegrationTests.Api/IronDev.IntegrationTests.Api.csproj"
    }
    else {
        "IronDev.IntegrationTests/IronDev.IntegrationTests.csproj"
    }

    dotnet test $projectUnderTest `
        --no-build `
        --filter $testFilter `
        --logger "console;verbosity=minimal" `
        --logger "trx;LogFileName=alpha-smoke.trx" `
        --results-directory $script:OutputRoot
})) {
    Complete-Smoke -RepoRoot $repoRoot -OverallStatus "Failed" -ExitCode 1
}
Add-Stage "SkeletonRunStart" "Passed" "SkeletonRunStarted" "Alpha smoke test ran the governed loop to the human gate."

if (-not (Test-Path -LiteralPath $script:ReceiptPath)) {
    Add-Stage "ReceiptWrite" "Failed" "ReceiptWriteFailed" "The alpha smoke test passed but did not write its receipt."
    Complete-Smoke -RepoRoot $repoRoot -OverallStatus "Failed" -ExitCode 1
}

$runReceipt = Get-Content -LiteralPath $script:ReceiptPath -Raw | ConvertFrom-Json
Add-Stage "RunEvidenceRefresh" "Passed" "RunEvidenceRefreshed" "Run evidence receipt was written by the smoke test." @{ runId = $runReceipt.runId }

if ([string]::IsNullOrWhiteSpace($runReceipt.criticPackageSha256)) {
    Add-Stage "CriticPackageFetch" "Failed" "CriticPackageMissing" "The run receipt did not include a critic package hash."
    Complete-Smoke -RepoRoot $repoRoot -OverallStatus "Failed" -ExitCode 1
}
Add-Stage "CriticPackageFetch" "Passed" "CriticPackageFetched" "Critic package hash is present." @{ criticPackageSha256 = $runReceipt.criticPackageSha256 }

if ($RunUntil -eq "Applied") {
    if (-not $runReceipt.criticReviewRecorded) {
        Add-Stage "CriticReviewRequest" "Failed" "CriticReviewFailed" "Applied mode expected a deterministic critic review record, but the receipt did not name one."
        Complete-Smoke -RepoRoot $repoRoot -OverallStatus "Failed" -ExitCode 1
    }

    Add-Stage "CriticReviewRequest" "Passed" "CriticReviewRecorded" "Deterministic smoke recorded a clean critic review before continuation. This is service-level smoke evidence, not approval."
}
else {
    Add-Stage "CriticReviewRequest" "Skipped" "CriticReviewRequestNotAutomated" "D-2a prepares the critic package but does not simulate or request the independent critic review."
    Add-Gap "Independent critic review request remains a later product/API proof."
}

if ($runReceipt.gateState -ne "PausedForApproval") {
    Add-Stage "GateStateVerify" "Failed" "GateStateUnexpected" "Expected PausedForApproval but got '$($runReceipt.gateState)'."
    Complete-Smoke -RepoRoot $repoRoot -OverallStatus "Failed" -ExitCode 1
}
Add-Stage "GateStateVerify" "Passed" "GateStateVerified" "Run halted at the human approval gate." @{ gateState = $runReceipt.gateState }

if ($RunUntil -eq "Applied") {
    if ($RequireExistingAcceptedApproval -and -not $runReceipt.sqlPersisted) {
        Add-Stage "SqlCheck" "Failed" "SqlUnavailable" "REL-3 expected SQL-persisted run, event, and accepted-approval rows."
        Complete-Smoke -RepoRoot $repoRoot -OverallStatus "Failed" -ExitCode 1
    }

    if ($RequireExistingAcceptedApproval -and -not $runReceipt.apiPersisted) {
        Add-Stage "ApiCheck" "Failed" "ApiUnavailable" "REL-3 expected authenticated API-backed project, ticket, approval, continuation, apply, and report requests."
        Complete-Smoke -RepoRoot $repoRoot -OverallStatus "Failed" -ExitCode 1
    }

    if (-not $runReceipt.acceptedApprovalRecorded -and -not $runReceipt.apiPersisted) {
        Add-Stage "ApprovalCheck" "Failed" "AcceptedApprovalRequired" "Applied mode expected a recorded accepted approval bound to the critic package."
        Complete-Smoke -RepoRoot $repoRoot -OverallStatus "Failed" -ExitCode 1
    }

    if ($runReceipt.approvalTargetHash -ne $runReceipt.criticPackageSha256) {
        Add-Stage "ApprovalCheck" "Failed" "ApprovalTargetHashMismatch" "Approval target hash did not match the critic package hash."
        Complete-Smoke -RepoRoot $repoRoot -OverallStatus "Failed" -ExitCode 1
    }

    if ($RequireExistingAcceptedApproval) {
        Add-Stage "ApprovalCheck" "Passed" "AcceptedApprovalPersisted" "Hash-bound accepted approval was created through the API, persisted in SQL, and consumed by continuation." @{ acceptedApprovalId = $runReceipt.acceptedApprovalId }
    }
    else {
        Add-Stage "ApprovalCheck" "Passed" "AcceptedApprovalRecorded" "Hash-bound accepted approval was recorded explicitly for this deterministic smoke run." @{ acceptedApprovalId = $runReceipt.acceptedApprovalId }
    }

    if (-not $runReceipt.continuationRequested) {
        Add-Stage "ContinuationRequest" "Failed" "ContinuationRefused" "Continuation was not unblocked after accepted approval."
        Complete-Smoke -RepoRoot $repoRoot -OverallStatus "Failed" -ExitCode 1
    }

    Add-Stage "ContinuationRequest" "Passed" "ContinuationUnblocked" "Continuation consumed the accepted approval. Continuation is not apply permission."

    if (-not $runReceipt.applyRequested -or $runReceipt.finalState -ne "Applied") {
        Add-Stage "ApplyRequest" "Failed" "ApplyRefused" "Controlled apply did not reach Applied."
        Complete-Smoke -RepoRoot $repoRoot -OverallStatus "Failed" -ExitCode 1
    }

    if ([string]::IsNullOrWhiteSpace($runReceipt.applyReceiptPath) -or -not (Test-Path -LiteralPath $runReceipt.applyReceiptPath)) {
        Add-Stage "ApplyRequest" "Failed" "ApplyReceiptMissing" "Applied mode did not leave the apply-copy receipt on disk."
        Complete-Smoke -RepoRoot $repoRoot -OverallStatus "Failed" -ExitCode 1
    }

    Add-Stage "ApplyRequest" "Passed" "Applied" "Controlled copy-only apply reached Applied and left an apply receipt." @{ applyReceiptPath = $runReceipt.applyReceiptPath; applyReceiptSha256 = $runReceipt.applyReceiptSha256 }
}

if (-not $runReceipt.reportReconstructable) {
    Add-Stage "ReportFetch" "Failed" "ReportMissing" "The run report could not be reconstructed."
    Complete-Smoke -RepoRoot $repoRoot -OverallStatus "Failed" -ExitCode 1
}
if ($RunUntil -eq "Applied" -and -not $runReceipt.loopComplete) {
    Add-Stage "ReportFetch" "Failed" "FinalReportMissing" "The final report did not reconstruct a complete applied loop."
    Complete-Smoke -RepoRoot $repoRoot -OverallStatus "Failed" -ExitCode 1
}
Add-Stage "ReportFetch" "Passed" "ReportFetched" "Run report was reconstructed by the smoke test."

$postRepoStatus = git -C $repoRoot status --porcelain
if (-not [string]::IsNullOrWhiteSpace($postRepoStatus)) {
    Add-Stage "RepoCheck" "Failed" "SourceRepoChangedUnexpectedly" "The source repository changed during smoke execution." @{ gitStatus = $postRepoStatus }
    Complete-Smoke -RepoRoot $repoRoot -OverallStatus "Failed" -ExitCode 1
}

Add-Stage "ReceiptWrite" "Passed" "ReceiptWritten" "Smoke receipt and summary were written under the safe output root."
Complete-Smoke -RepoRoot $repoRoot -OverallStatus "Passed" -ExitCode 0
