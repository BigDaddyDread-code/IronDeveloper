[CmdletBinding()]
param(
    [string]$RepositoryUrl,
    [string]$Ref = "main",
    [string]$EvidencePath,
    [string]$ClonePath,
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

$cloneRoot = if ([string]::IsNullOrWhiteSpace($ClonePath)) {
    Join-Path ([System.IO.Path]::GetTempPath()) ("irondev-clean-clone-" + [Guid]::NewGuid().ToString("N"))
} else {
    [System.IO.Path]::GetFullPath($ClonePath)
}
if (Test-Path -LiteralPath $cloneRoot) {
    throw "Clean-clone target already exists: $cloneRoot"
}

$checks = [System.Collections.Generic.List[object]]::new()
function Invoke-Check([string]$Name, [scriptblock]$Command) {
    $started = [DateTimeOffset]::UtcNow
    try {
        & $Command
        if ($LASTEXITCODE -ne 0) { throw "$Name failed with exit code $LASTEXITCODE." }
        $checks.Add([ordered]@{ name = $Name; status = "PASS"; startedUtc = $started.ToString("O"); completedUtc = [DateTimeOffset]::UtcNow.ToString("O") })
    }
    catch {
        $checks.Add([ordered]@{ name = $Name; status = "FAIL"; startedUtc = $started.ToString("O"); completedUtc = [DateTimeOffset]::UtcNow.ToString("O"); detail = $_.Exception.Message })
        throw
    }
}

$result = "RepositoryQualificationFailed"
$failure = $null
try {
    Invoke-Check "clone" { git clone --quiet --no-tags $RepositoryUrl $cloneRoot }
    Invoke-Check "checkout" { git -C $cloneRoot checkout --quiet $Ref }
    Invoke-Check "dotnet-restore" { dotnet restore (Join-Path $cloneRoot "IronDev.slnx") }
    Invoke-Check "dotnet-vulnerability-audit" {
        $auditOutput = & dotnet package list --project (Join-Path $cloneRoot "IronDev.slnx") --vulnerable --include-transitive --format json
        if ($LASTEXITCODE -ne 0) { throw "dotnet vulnerability audit failed with exit code $LASTEXITCODE." }
        $audit = $auditOutput | ConvertFrom-Json
        $vulnerablePackages = @(
            $audit.projects |
                ForEach-Object { $_.frameworks } |
                ForEach-Object { @($_.topLevelPackages) + @($_.transitivePackages) } |
                Where-Object { $null -ne $_ -and $null -ne $_.vulnerabilities -and @($_.vulnerabilities).Count -gt 0 }
        )
        if ($vulnerablePackages.Count -gt 0) {
            throw "dotnet vulnerability audit found $($vulnerablePackages.Count) vulnerable package occurrence(s)."
        }
    }
    Invoke-Check "dotnet-build" { dotnet build (Join-Path $cloneRoot "IronDev.slnx") --no-restore }
    Invoke-Check "documentation-contract" { powershell -ExecutionPolicy Bypass -File (Join-Path $cloneRoot "Scripts\ci\run-documentation-contract-ci.ps1") }
    if (-not $SkipFrontend) {
        Push-Location (Join-Path $cloneRoot "IronDev.TauriShell")
        try {
            Invoke-Check "frontend-locked-install" { npm ci }
            Invoke-Check "frontend-vulnerability-audit" { npm audit --audit-level=low }
            Invoke-Check "frontend-build" { npm run build }
            Invoke-Check "tauri-cargo-check" { cargo check --manifest-path src-tauri\Cargo.toml }
        }
        finally { Pop-Location }
    }

    $result = if ($SkipFrontend) { "PartialRepositoryQualificationPassed" } else { "RepositoryQualificationPassed" }
    Write-Host "PASS clean-clone repository qualification. Evidence: $EvidencePath"
}
catch {
    $failure = $_.Exception.Message
    throw
}
finally {
    $checkedOutCommit = if (Test-Path -LiteralPath (Join-Path $cloneRoot ".git")) {
        (& git -C $cloneRoot rev-parse HEAD 2>$null).Trim()
    } else { $null }
    $evidence = [ordered]@{
        schemaVersion = 1
        result = $result
        repositoryRef = $Ref
        checkedOutCommit = $checkedOutCommit
        completedUtc = [DateTimeOffset]::UtcNow.ToString("O")
        checks = $checks
        frontendQualification = if ($SkipFrontend) { "Skipped" } elseif ($result -eq "RepositoryQualificationPassed") { "Passed" } else { "FailedOrIncomplete" }
        liveLocalTestJourney = "PendingManualQualification"
        boundary = "Repository qualification does not substitute for database reset, visible UI login, governed smoke, audit inspection, support export, or non-author qualification."
    }
    if ($failure) { $evidence["failure"] = $failure }
    New-Item -ItemType Directory -Path (Split-Path -Parent $EvidencePath) -Force | Out-Null
    $evidence | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $EvidencePath -Encoding UTF8
    if (-not $KeepClone -and (Test-Path -LiteralPath $cloneRoot)) {
        Remove-Item -LiteralPath $cloneRoot -Recurse -Force -ErrorAction SilentlyContinue
    } elseif (Test-Path -LiteralPath $cloneRoot) {
        Write-Host "Clean clone retained at '$cloneRoot'."
    }
}
