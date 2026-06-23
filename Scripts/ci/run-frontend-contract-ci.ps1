$ErrorActionPreference = "Stop"

function Write-Section {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Name
    )

    Write-Host ""
    Write-Host "== $Name =="
}

$script:RepoRoot = Resolve-Path (Join-Path $PSScriptRoot "..\..")
$script:FrontendRoot = Join-Path $script:RepoRoot "IronDev.TauriShell"
$script:OpenApiSnapshot = Join-Path $script:FrontendRoot "openapi\irondev-api.openapi.json"
$script:GeneratedClient = Join-Path $script:FrontendRoot "src\api\generated\ironDevApiTypes.ts"

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

    Write-Section "Install frontend dependencies"
    npm ci

    Write-Section "Tauri frontend type-check"
    npx tsc --noEmit

    Write-Section "OpenAPI generated client drift check"
    New-Item -ItemType Directory -Path $tempRoot -Force | Out-Null
    npx openapi-typescript openapi/irondev-api.openapi.json -o $tempGeneratedClient

    $committedClient = (Get-Content -LiteralPath $script:GeneratedClient -Raw) -replace "`r`n", "`n" -replace "`r", "`n"
    $generatedClient = (Get-Content -LiteralPath $tempGeneratedClient -Raw) -replace "`r`n", "`n" -replace "`r", "`n"

    if ($committedClient -ne $generatedClient) {
        throw "OpenAPI/client drift detected. This is evidence only. Update the API contract/client in a separate reviewed PR."
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
