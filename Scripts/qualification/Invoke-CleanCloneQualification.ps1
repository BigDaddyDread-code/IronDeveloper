[CmdletBinding()]
param(
    [string]$RepositoryUrl,
    [string]$Ref = "main",
    [string]$EvidencePath,
    [switch]$SkipFrontend,
    [switch]$KeepClone
)

$ErrorActionPreference = "Stop"
$sourceRoot = Resolve-Path (Join-Path $PSScriptRoot "..\..")
if ([string]::IsNullOrWhiteSpace($RepositoryUrl)) {
    $RepositoryUrl = (& git -C $sourceRoot remote get-url origin).Trim()
}
if ([string]::IsNullOrWhiteSpace($EvidencePath)) {
    $EvidencePath = Join-Path $sourceRoot "artifacts\qualification\clean-clone.json"
}

$cloneRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("irondev-clean-clone-" + [Guid]::NewGuid().ToString("N"))
$checks = [System.Collections.Generic.List[object]]::new()
function Invoke-Check([string]$Name, [scriptblock]$Command) {
    $started = [DateTimeOffset]::UtcNow
    & $Command
    if ($LASTEXITCODE -ne 0) { throw "$Name failed with exit code $LASTEXITCODE." }
    $checks.Add([ordered]@{ name = $Name; status = "PASS"; startedUtc = $started.ToString("O"); completedUtc = [DateTimeOffset]::UtcNow.ToString("O") })
}

try {
    Invoke-Check "clone" { git clone --quiet --no-tags $RepositoryUrl $cloneRoot }
    Invoke-Check "checkout" { git -C $cloneRoot checkout --quiet $Ref }
    Invoke-Check "dotnet-restore" { dotnet restore (Join-Path $cloneRoot "IronDev.slnx") }
    Invoke-Check "dotnet-build" { dotnet build (Join-Path $cloneRoot "IronDev.slnx") --no-restore }
    Invoke-Check "documentation-contract" { powershell -ExecutionPolicy Bypass -File (Join-Path $cloneRoot "Scripts\ci\run-documentation-contract-ci.ps1") }
    if (-not $SkipFrontend) {
        Push-Location (Join-Path $cloneRoot "IronDev.TauriShell")
        try {
            Invoke-Check "frontend-locked-install" { npm ci }
            Invoke-Check "frontend-build" { npm run build }
            Invoke-Check "tauri-cargo-check" { cargo check --manifest-path src-tauri\Cargo.toml }
        }
        finally { Pop-Location }
    }

    $evidence = [ordered]@{
        schemaVersion = 1
        result = if ($SkipFrontend) { "PartialRepositoryQualificationPassed" } else { "RepositoryQualificationPassed" }
        repositoryRef = $Ref
        completedUtc = [DateTimeOffset]::UtcNow.ToString("O")
        checks = $checks
        frontendQualification = if ($SkipFrontend) { "Skipped" } else { "Passed" }
        liveLocalTestJourney = "PendingManualQualification"
        boundary = "Repository qualification does not substitute for database reset, visible UI login, governed smoke, audit inspection, support export, or non-author qualification."
    }
    New-Item -ItemType Directory -Path (Split-Path -Parent $EvidencePath) -Force | Out-Null
    $evidence | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $EvidencePath -Encoding UTF8
    Write-Host "PASS clean-clone repository qualification. Evidence: $EvidencePath"
}
finally {
    if (-not $KeepClone -and (Test-Path -LiteralPath $cloneRoot)) {
        Remove-Item -LiteralPath $cloneRoot -Recurse -Force -ErrorAction SilentlyContinue
    }
}
