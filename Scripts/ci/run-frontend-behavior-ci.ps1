param(
    [string]$ArtifactDirectory = "artifacts/ci/frontend-behavior"
)

$ErrorActionPreference = "Stop"

function Invoke-NativeCommand {
    param(
        [Parameter(Mandatory = $true)][string]$Name,
        [Parameter(Mandatory = $true)][scriptblock]$Command
    )

    Write-Host ""
    Write-Host "== $Name =="
    & $Command
    if ($LASTEXITCODE -ne 0) {
        throw "$Name failed with exit code $LASTEXITCODE."
    }
}

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..\..")
$frontendRoot = Join-Path $repoRoot "IronDev.TauriShell"
$artifactRoot = Join-Path $repoRoot $ArtifactDirectory
$testResultsRoot = Join-Path $artifactRoot "test-results"
$junitPath = Join-Path $artifactRoot "playwright-results.xml"
$summaryPath = Join-Path $artifactRoot "frontend-behavior-summary.md"

$testFiles = @(
    "tests/project-entry.spec.ts",
    "tests/project-routing.spec.ts",
    "tests/project-setup.spec.ts",
    "tests/ux-start.spec.ts",
    "tests/flow-shell-smoke.spec.ts",
    "tests/board-ux.spec.ts",
    "tests/chat-conversation-first.spec.ts",
    "tests/workbench-agent-run.spec.ts",
    "tests/workbench-slash-commands.spec.ts",
    "tests/ticket-proposals.spec.ts",
    "tests/project-understanding.spec.ts",
    "tests/chat-session-navigation.spec.ts",
    "tests/chat-ticket-draft-review.spec.ts",
    "tests/library-documents.spec.ts",
    "tests/tools-catalogue.spec.ts",
    "tests/members-directory.spec.ts",
    "tests/governance-information-architecture.spec.ts",
    "tests/governance-overview.spec.ts",
    "tests/workitem-ux.spec.ts",
    "tests/agent-profiles.spec.ts",
    "tests/ai-connections.spec.ts",
    "tests/product-identity.spec.ts"
)

if (Test-Path -LiteralPath $artifactRoot) {
    Remove-Item -LiteralPath $artifactRoot -Recurse -Force
}
New-Item -ItemType Directory -Path $testResultsRoot -Force | Out-Null

& (Join-Path $PSScriptRoot "write-ci-evidence-summary.ps1") `
    -ArtifactDirectory $artifactRoot `
    -WorkflowName "frontend-behavior-ci" `
    -LaneName "frontend-behavior" `
    -CommandCategory "npm build and bounded Playwright current-product suite" `
    -ResultStatus "Started"

Write-Host "Frontend behavior CI reports evidence only."
Write-Host "A passing browser suite is not approval, workflow authority, source mutation permission, or release readiness."

$status = "Failed"
$failure = $null
$selectedCount = 0
$executedCount = 0
$passedCount = 0
$failedCount = 0
$skippedCount = 0

Push-Location $frontendRoot
try {
    if ($env:CI -eq "true") {
        Invoke-NativeCommand "Install locked frontend dependencies" { npm ci }
    }
    else {
        Invoke-NativeCommand "Restore frontend dependencies" { npm install --no-audit --no-fund }
    }

    Invoke-NativeCommand "Install Chromium" { npx playwright install chromium }
    Invoke-NativeCommand "Build production frontend" { npm run build }

    Write-Host ""
    Write-Host "== List bounded frontend tests =="
    $selectionOutput = & npx playwright test @testFiles --list --reporter=line 2>&1
    if ($LASTEXITCODE -ne 0) {
        throw "Playwright selection failed with exit code $LASTEXITCODE."
    }
    $selectionText = $selectionOutput -join [Environment]::NewLine
    $selectionMatch = [regex]::Match($selectionText, "Total:\s+(\d+)\s+tests")
    if (-not $selectionMatch.Success) {
        throw "Playwright selection did not report a test count."
    }
    $selectedCount = [int]$selectionMatch.Groups[1].Value
    if ($selectedCount -eq 0) {
        throw "Frontend behavior lane selected zero tests."
    }
    Write-Host "Selected tests: $selectedCount across $($testFiles.Count) files."

    $env:PLAYWRIGHT_JUNIT_OUTPUT_FILE = $junitPath
    Invoke-NativeCommand "Execute bounded frontend tests" {
        npx playwright test @testFiles `
            --workers=4 `
            --reporter=line,junit `
            --output=$testResultsRoot
    }

    if (-not (Test-Path -LiteralPath $junitPath -PathType Leaf)) {
        throw "Playwright did not produce the required JUnit evidence."
    }

    [xml]$junit = Get-Content -LiteralPath $junitPath -Raw
    $executedCount = [int]$junit.testsuites.tests
    $failedCount = [int]$junit.testsuites.failures
    $skippedCount = [int]$junit.testsuites.skipped
    $passedCount = $executedCount - $failedCount - $skippedCount

    if ($executedCount -ne $selectedCount) {
        throw "Playwright selected $selectedCount tests but reported $executedCount executions."
    }
    if ($failedCount -ne 0) {
        throw "Playwright reported $failedCount failed tests."
    }

    $status = "Passed"
}
catch {
    $failure = $_
}
finally {
    Pop-Location

    $lines = @(
        "# Frontend Behavior CI Summary",
        "",
        "| Field | Value |",
        "| --- | --- |",
        "| Status | $status |",
        "| Test files | $($testFiles.Count) |",
        "| Selected tests | $selectedCount |",
        "| Executed tests | $executedCount |",
        "| Passed tests | $passedCount |",
        "| Failed tests | $failedCount |",
        "| Skipped tests | $skippedCount |",
        "| Workers | 4 |",
        "| Production build | $(if ($status -eq 'Passed') { 'Passed' } else { 'Not proven' }) |",
        "",
        "## Owned Files",
        ""
    ) + ($testFiles | ForEach-Object { "- ``$_``" }) + @(
        "",
        "Evidence only. Browser success grants no authority or release approval."
    )
    Set-Content -LiteralPath $summaryPath -Value $lines -Encoding UTF8

    & (Join-Path $PSScriptRoot "write-ci-evidence-summary.ps1") `
        -ArtifactDirectory $artifactRoot `
        -WorkflowName "frontend-behavior-ci" `
        -LaneName "frontend-behavior" `
        -CommandCategory "npm build and bounded Playwright current-product suite" `
        -ResultStatus $status
}

& (Join-Path $PSScriptRoot "test-ci-evidence-artifact-safety.ps1") `
    -ArtifactDirectory $artifactRoot

if ($null -ne $failure) {
    throw $failure
}

Write-Host "Frontend behavior CI passed: $passedCount of $executedCount tests."
