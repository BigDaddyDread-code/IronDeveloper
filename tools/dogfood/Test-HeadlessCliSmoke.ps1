param(
    [string]$RunId = "HeadlessCliSmoke-$(Get-Date -Format yyyyMMdd-HHmmss)",
    [switch]$SkipReplayBatch
)

$ErrorActionPreference = "Stop"

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..\..")
$runnerProject = Join-Path $repoRoot "tools\IronDev.ReplayRunner\IronDev.ReplayRunner.csproj"
$runsRoot = Join-Path $repoRoot "tools\dogfood\runs"
$scenario = Join-Path $repoRoot "tools\dogfood\dogfood-scenarios\BookSellerMvp.json"

function Assert-True {
    param(
        [bool]$Condition,
        [string]$Message
    )

    if (-not $Condition) {
        throw $Message
    }
}

function Invoke-RunnerJson {
    param([string[]]$Arguments)

    $output = & dotnet run --no-build --project $runnerProject -- @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "Runner command failed with exit code $LASTEXITCODE. Output: $output"
    }

    return ($output | Out-String | ConvertFrom-Json)
}

function New-SmokeDoc {
    param(
        [string]$Path
    )

    @"
# Smoke Imported Note

The Test Agent should use cheap model execution and report concise results back to Codex.
"@ | Set-Content -LiteralPath $Path -Encoding UTF8
}

Write-Host "Building headless runner..."
dotnet build $runnerProject -p:UseSharedCompilation=false -nr:false | Out-Host
if ($LASTEXITCODE -ne 0) {
    throw "Runner build failed."
}

Write-Host "Smoke 1: chat send returns structured clarification..."
$first = Invoke-RunnerJson @(
    "chat", "send", "I need to save data",
    "--workspace", "Chat",
    "--dogfood-run-id", $RunId
)
Assert-True ($first.Command -eq "chat send") "chat send did not return command name."
Assert-True ($first.DryRun -eq $true) "chat send did not default to dry-run."
Assert-True ($first.Intent -eq "GeneralChat") "Vague prompt should route to GeneralChat/clarification."
Assert-True ([string]::IsNullOrWhiteSpace($first.AssistantResponse) -eq $false) "Assistant response was empty."
Assert-True ($first.SimulatedFilesChanged -eq 0) "Dry-run chat changed files."

Write-Host "Smoke 2: follow-up answer routes to action with JSON feedback..."
$followUp = Invoke-RunnerJson @(
    "chat", "send", "BookSeller should save books, authors, stock counts, storage locations, and sales history in SQL Server with Dapper. Save that as project knowledge.",
    "--workspace", "Chat",
    "--previous-assistant", $first.AssistantResponse,
    "--previous-user", "I need to save data",
    "--dogfood-run-id", $RunId
)
Assert-True ($followUp.Intent -eq "SaveDiscussionDocument") "Follow-up did not route to SaveDiscussionDocument."
Assert-True ($followUp.AllowsProseResponse -eq $false) "Action command allowed prose fallback."
Assert-True ($followUp.ContextReference -eq "CurrentMessage") "Follow-up should use current message as source context."
Assert-True ($followUp.SimulatedDiscussionDocuments -ge 1) "Follow-up did not create a simulated discussion document."
Assert-True ($followUp.SimulatedFilesChanged -eq 0) "Dry-run follow-up changed files."

if (-not $SkipReplayBatch) {
    Write-Host "Smoke 3: replay batch runs and passes..."
    powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot "Start-BookSellerReplay.ps1") `
        -RunId "$RunId-replay" `
        -Scenario $scenario `
        -Reps 10 `
        -DryRun `
        -StopOnFailure `
        -RunnerCommand "dotnet run --no-build --project .\tools\IronDev.ReplayRunner\IronDev.ReplayRunner.csproj --" | Out-Host
    if ($LASTEXITCODE -ne 0) {
        throw "Replay smoke failed."
    }

    $summaryPath = Join-Path $runsRoot "$RunId-replay\replay\runner-summary.json"
    $summary = Get-Content $summaryPath -Raw | ConvertFrom-Json
Assert-True ($summary.TotalCases -eq 10) "Replay did not run requested repetition count."
Assert-True ($summary.Failed -eq 0) "Replay had failures."
}

Write-Host "Smoke 4: docs command cleans, imports, and searches local knowledge..."
$docsStore = Join-Path $runsRoot "$RunId-docs-store"
$docsClean = Invoke-RunnerJson @(
    "docs", "clean",
    "--project", "IronDev",
    "--store-root", $docsStore,
    "--force"
)
Assert-True ($docsClean.SeededDocuments -ge 4) "docs clean did not seed baseline docs."
Assert-True ($docsClean.TotalDocuments -ge 4) "docs clean did not return baseline documents."

$smokeDocPath = Join-Path $docsStore "smoke-note.md"
New-SmokeDoc -Path $smokeDocPath
$docsImport = Invoke-RunnerJson @(
    "docs", "import",
    "--file", $smokeDocPath,
    "--project", "IronDev",
    "--store-root", $docsStore,
    "--type", "Discussion",
    "--authority", "WorkingDraft",
    "--dogfood-run-id", $RunId
)
Assert-True ($docsImport.TotalDocuments -ge 5) "docs import did not add a document."

$docsSearch = Invoke-RunnerJson @(
    "docs", "search",
    "cheap model execution",
    "--project", "IronDev",
    "--store-root", $docsStore,
    "--take", "3"
)
Assert-True (@($docsSearch.Matches).Count -gt 0) "docs search returned no matches."

Write-Host "Smoke 5: failure package command creates Markdown and JSON..."
$failureRunId = "$RunId-failure"
$failureReplay = Join-Path $runsRoot "$failureRunId\replay"
New-Item -ItemType Directory -Force -Path $failureReplay | Out-Null
$failurePlanPath = Join-Path $failureReplay "replay-plan.json"

$failurePlan = [ordered]@{
    dogfoodRunId = $failureRunId
    scenarioId = "HeadlessCliSmoke"
    cases = @(
        [ordered]@{
            dogfoodRunId = $failureRunId
            caseId = "0001-intent-failure"
            caseNumber = 1
            scenarioId = "HeadlessCliSmoke"
            seed = 1
            step = 1
            name = "Intent failure package smoke"
            workspace = "Chat"
            prompt = "what is a ticket?"
            expected = [ordered]@{
                intent = "CreateMultipleDraftTickets"
                minDraftTickets = 3
                allowsProseResponse = $false
                noUnsafeWrites = $true
            }
        }
    )
}
$failurePlan | ConvertTo-Json -Depth 20 | Set-Content -Path $failurePlanPath -Encoding UTF8

& dotnet run --no-build --project $runnerProject -- $failurePlanPath | Out-Host
Assert-True ($LASTEXITCODE -eq 1) "Intent failure plan should fail with exit code 1."

$package = Invoke-RunnerJson @(
    "failure", "latest",
    "--for-codex",
    "--runs-root", $runsRoot,
    "--run-id", $failureRunId
)
Assert-True (Test-Path $package.JsonPath) "failure-package.json was not written."
Assert-True (Test-Path $package.MarkdownPath) "failure-package.md was not written."
Assert-True ($package.ExpectedIntent -eq "CreateMultipleDraftTickets") "Failure package missing expected intent."
Assert-True ($package.ActualIntent -eq "GeneralChat") "Failure package missing actual intent."
Assert-True ([string]::IsNullOrWhiteSpace($package.ReproCommand) -eq $false) "Failure package missing repro command."
Assert-True ([string]::IsNullOrWhiteSpace($package.ValidationCommand) -eq $false) "Failure package missing validation command."

Write-Host "Headless CLI smoke passed."
