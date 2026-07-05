#!/usr/bin/env pwsh
# D-2 — the alpha smoke. Stands the governed loop up clean and drives ONE ticket
# through it for real, with only the model faked (a deterministic Builder).
#
# What it actually does, from a clean checkout:
#   1. Builds and tests Samples/BookSeller as-is (the loop's workspace baseline).
#   2. Runs AlphaLoopSmokeTests, which wires the REAL orchestrator + the REAL
#      disposable-workspace executor and drives Start -> real dotnet build/test of
#      the sample against the proposed change -> hash-sealed critic package ->
#      independent critic review -> a live, hash-matched human approval ->
#      Completed. It writes a hash-bearing receipt.
#   3. Prints the receipt.
#
# What it deliberately does NOT prove yet (stated in the receipt, no pretending):
#   - a live model (D-3 swaps one in),
#   - SQL/API persistence (in-memory stores here),
#   - the copy-only apply spine to Applied (D-2b).
#
# "A demo that only works because the machine already knows the trick is not a
#  demo. It is a lie with screenshots." This script hides no local knowledge:
#  it needs only the .NET SDK and git on PATH.

$ErrorActionPreference = 'Stop'
$repoRoot = Resolve-Path (Join-Path (Join-Path $PSScriptRoot '..') '..')
Set-Location $repoRoot

function Require-Tool([string]$name) {
    if (-not (Get-Command $name -ErrorAction SilentlyContinue)) {
        throw "Required tool '$name' is not on PATH. A clean machine needs the .NET SDK and git; nothing else is assumed."
    }
}

Write-Host '== IronDeveloper alpha smoke ==' -ForegroundColor Cyan
Require-Tool 'dotnet'
Require-Tool 'git'

$receipt = Join-Path (Join-Path (Join-Path $repoRoot 'artifacts') 'alpha-smoke') 'receipt.json'
New-Item -ItemType Directory -Force -Path (Split-Path $receipt) | Out-Null
if (Test-Path $receipt) { Remove-Item $receipt -Force }
$env:ALPHA_SMOKE_RECEIPT = $receipt

Write-Host '-> Building the solution (needed to run the smoke test)...' -ForegroundColor Yellow
dotnet build IronDev.slnx --nologo --verbosity minimal
if ($LASTEXITCODE -ne 0) { throw 'Solution build failed.' }

Write-Host '-> Driving one ticket through the real governed loop (deterministic Builder)...' -ForegroundColor Yellow
dotnet test IronDev.IntegrationTests/IronDev.IntegrationTests.csproj `
    --no-build `
    --filter 'FullyQualifiedName~AlphaLoopSmoke' `
    --logger 'console;verbosity=minimal'
if ($LASTEXITCODE -ne 0) { throw 'Alpha smoke failed. The governed loop did not complete cleanly.' }

if (-not (Test-Path $receipt)) { throw "Smoke passed but wrote no receipt at $receipt." }

Write-Host ''
Write-Host '== Receipt ==' -ForegroundColor Cyan
Get-Content $receipt | Write-Host
Write-Host ''
Write-Host "Receipt: $receipt" -ForegroundColor Green
Write-Host 'Alpha smoke PASSED — the loop ran one ticket for real (deterministic model).' -ForegroundColor Green
