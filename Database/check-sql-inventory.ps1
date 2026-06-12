[CmdletBinding()]
param(
    [string]$RepositoryRoot
)

$ErrorActionPreference = "Stop"

function Get-RepositoryRoot {
    if (-not [string]::IsNullOrWhiteSpace($RepositoryRoot)) {
        return (Resolve-Path $RepositoryRoot).Path
    }

    $directory = Get-Location
    while ($null -ne $directory -and -not (Test-Path (Join-Path $directory "IronDev.slnx"))) {
        $directory = Split-Path $directory -Parent
        if ([string]::IsNullOrWhiteSpace($directory)) {
            break
        }
    }

    if ([string]::IsNullOrWhiteSpace($directory) -or -not (Test-Path (Join-Path $directory "IronDev.slnx"))) {
        throw "Could not locate repository root. Pass -RepositoryRoot."
    }

    return $directory
}

function Fail($message) {
    Write-Error $message
    exit 1
}

$root = Get-RepositoryRoot
$inventoryPath = Join-Path $root "Database/sql-inventory.json"
$manifestPath = Join-Path $root "Database/migrations.json"

if (-not (Test-Path $inventoryPath)) { Fail "Missing Database/sql-inventory.json." }
if (-not (Test-Path $manifestPath)) { Fail "Missing Database/migrations.json." }

$inventory = Get-Content -Raw -Path $inventoryPath | ConvertFrom-Json
$manifest = Get-Content -Raw -Path $manifestPath | ConvertFrom-Json
$entries = @($inventory.entries)

if ($entries.Count -eq 0) { Fail "SQL inventory contains no entries." }

$paths = @{}
foreach ($entry in $entries) {
    if ([string]::IsNullOrWhiteSpace($entry.id)) { Fail "Inventory entry has empty id." }
    if ([string]::IsNullOrWhiteSpace($entry.path)) { Fail "Inventory entry $($entry.id) has empty path." }
    if ([string]::IsNullOrWhiteSpace($entry.bucket)) { Fail "Inventory entry $($entry.id) has empty bucket." }
    if ([string]::IsNullOrWhiteSpace($entry.ownerArea)) { Fail "Inventory entry $($entry.id) has empty ownerArea." }

    $fullPath = Join-Path $root $entry.path
    if (-not (Test-Path $fullPath)) { Fail "Inventory path does not exist: $($entry.path)." }

    $paths[$entry.path.Replace('\\', '/')] = $true

    if ($entry.bucket -eq "required-runtime-schema") {
        if ($null -eq $entry.appliedByManifest) { Fail "Required runtime schema entry $($entry.id) does not declare appliedByManifest." }
        if ($null -eq $entry.verifiedByScript) { Fail "Required runtime schema entry $($entry.id) does not declare verifiedByScript." }
    }
}

$databaseSqlFiles = Get-ChildItem -Path (Join-Path $root "Database") -Filter "*.sql" -File | ForEach-Object { "Database/$($_.Name)" }
foreach ($sqlFile in $databaseSqlFiles) {
    if (-not $paths.ContainsKey($sqlFile)) { Fail "Database SQL file missing from inventory: $sqlFile." }
}

foreach ($migration in @($manifest.migrations)) {
    $manifestPathValue = [string]$migration.path
    if (-not $paths.ContainsKey($manifestPathValue)) { Fail "Migration manifest path missing from inventory: $manifestPathValue." }

    $matching = @($entries | Where-Object { $_.path -eq $manifestPathValue })
    if ($matching.Count -eq 0) { Fail "Migration manifest path missing inventory entry: $manifestPathValue." }
    foreach ($entry in $matching) {
        if (-not [bool]$entry.appliedByManifest) { Fail "Manifest migration is not marked appliedByManifest: $manifestPathValue." }
        if (-not [bool]$entry.verifiedByScript) { Fail "Manifest migration is not marked verifiedByScript: $manifestPathValue." }
    }
}

Write-Host "SQL inventory check passed. Entries: $($entries.Count). Database SQL files: $($databaseSqlFiles.Count)."
exit 0
