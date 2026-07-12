$ErrorActionPreference = "Stop"

function Write-Section {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Name
    )

    Write-Host ""
    Write-Host "== $Name =="
}

function Invoke-LoggedNativeCommand {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Name,

        [Parameter(Mandatory = $true)]
        [string]$OutputPath,

        [Parameter(Mandatory = $true)]
        [scriptblock]$Command
    )

    Write-Section $Name
    Add-Content -LiteralPath $OutputPath -Value ""
    Add-Content -LiteralPath $OutputPath -Value "== $Name =="
    & $Command 2>&1 | Tee-Object -FilePath $OutputPath -Append
    if ($LASTEXITCODE -ne 0) {
        throw "$Name failed."
    }
}

$script:RepoRoot = Resolve-Path (Join-Path $PSScriptRoot "..\..")
$script:FrontendRoot = Join-Path $script:RepoRoot "IronDev.TauriShell"
$script:OpenApiSnapshot = Join-Path $script:FrontendRoot "openapi\irondev-api.openapi.json"
$script:GeneratedClient = Join-Path $script:FrontendRoot "src\api\generated\ironDevApiTypes.ts"
$script:ArtifactRoot = Join-Path $script:RepoRoot "artifacts\ci\frontend-contract"
$script:FrontendOutput = Join-Path $script:ArtifactRoot "frontend-contract-output.txt"
$script:OpenApiDriftSummary = Join-Path $script:ArtifactRoot "openapi-drift-summary.txt"

if (Test-Path -LiteralPath $script:ArtifactRoot) {
    Remove-Item -LiteralPath $script:ArtifactRoot -Recurse -Force
}

New-Item -ItemType Directory -Path $script:ArtifactRoot -Force | Out-Null
& (Join-Path $PSScriptRoot "write-ci-evidence-summary.ps1") `
    -ArtifactDirectory $script:ArtifactRoot `
    -WorkflowName "frontend-contract-ci" `
    -LaneName "frontend-contract" `
    -CommandCategory "frontend type-check and OpenAPI drift" `
    -ResultStatus "Started"
Set-Content -LiteralPath $script:FrontendOutput -Value "Frontend contract output. Evidence only; not approval or permission." -Encoding UTF8
Set-Content -LiteralPath $script:OpenApiDriftSummary -Value "OpenAPI drift summary. Evidence only; not approval or permission." -Encoding UTF8

Write-Section "Frontend contract CI"
Write-Host "Frontend contract CI reports evidence only."
Write-Host "Type-check and OpenAPI drift success are not approval, API authority, generated-client approval, release readiness, deployment readiness, package publication, or workflow continuation."

if (-not (Test-Path -LiteralPath $script:FrontendRoot -PathType Container)) {
    throw "IronDev.TauriShell was not found."
}

if (-not (Test-Path -LiteralPath (Join-Path $script:FrontendRoot "package-lock.json") -PathType Leaf)) {
    throw "IronDev.TauriShell package-lock.json was not found."
}

if (-not (Test-Path -LiteralPath $script:OpenApiSnapshot -PathType Leaf)) {
    throw "Committed OpenAPI snapshot was not found."
}

if (-not (Test-Path -LiteralPath $script:GeneratedClient -PathType Leaf)) {
    throw "Committed generated API client types were not found."
}

$tempRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("irondev-openapi-drift-" + [System.Guid]::NewGuid().ToString("N"))
$tempGeneratedClient = Join-Path $tempRoot "ironDevApiTypes.ts"

try {
    Push-Location $script:FrontendRoot

    Invoke-LoggedNativeCommand `
        -Name "Install frontend dependencies" `
        -OutputPath $script:FrontendOutput `
        -Command {
            if ($env:CI -eq "true") {
                npm ci
            }
            else {
                # A running Vite process holds esbuild.exe on Windows. Local
                # validation keeps the lockfile authoritative without deleting
                # the live node_modules tree out from under that process.
                npm install --no-audit --no-fund
            }
        }

    Invoke-LoggedNativeCommand `
        -Name "Tauri frontend type-check" `
        -OutputPath $script:FrontendOutput `
        -Command { npx tsc --noEmit }

    Invoke-LoggedNativeCommand `
        -Name "OpenAPI generated client drift check" `
        -OutputPath $script:OpenApiDriftSummary `
        -Command {
            New-Item -ItemType Directory -Path $tempRoot -Force | Out-Null
            npx openapi-typescript openapi/irondev-api.openapi.json -o $tempGeneratedClient
        }

    $committedClient = (Get-Content -LiteralPath $script:GeneratedClient -Raw) -replace "`r`n", "`n" -replace "`r", "`n"
    $generatedClient = (Get-Content -LiteralPath $tempGeneratedClient -Raw) -replace "`r`n", "`n" -replace "`r", "`n"

    if ($committedClient -ne $generatedClient) {
        Add-Content -LiteralPath $script:OpenApiDriftSummary -Value "OpenAPI/client drift detected."
        throw "OpenAPI/client drift detected. This is evidence only. Update the API contract/client in a separate reviewed PR."
    }

    Add-Content -LiteralPath $script:OpenApiDriftSummary -Value "Committed generated API client matches the OpenAPI snapshot."

    Invoke-LoggedNativeCommand `
        -Name "Live OpenAPI regeneration and dirty-tree check" `
        -OutputPath $script:OpenApiDriftSummary `
        -Command {
            & (Join-Path $script:RepoRoot "tools\contracts\update-openapi-contract.ps1") -Check -VerifyDeterminism
            git -C $script:RepoRoot diff --exit-code -- `
                IronDev.TauriShell/openapi/irondev-api.openapi.json `
                IronDev.TauriShell/src/api/generated/ironDevApiTypes.ts
        }
}
finally {
    Pop-Location

    if (Test-Path -LiteralPath $tempRoot) {
        Remove-Item -LiteralPath $tempRoot -Recurse -Force
    }
}

Write-Section "Frontend contract CI complete"
Write-Host "A clean type-check and OpenAPI drift check is evidence, not permission."
& (Join-Path $PSScriptRoot "write-ci-evidence-summary.ps1") `
    -ArtifactDirectory $script:ArtifactRoot `
    -WorkflowName "frontend-contract-ci" `
    -LaneName "frontend-contract" `
    -CommandCategory "frontend type-check and OpenAPI drift" `
    -ResultStatus "Passed"
& (Join-Path $PSScriptRoot "test-ci-evidence-artifact-safety.ps1") `
    -ArtifactDirectory $script:ArtifactRoot
