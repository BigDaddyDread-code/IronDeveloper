[CmdletBinding()]
param(
    [string]$ArtifactDirectory,
    [string]$RepositoryRoot
)

$ErrorActionPreference = "Stop"
$repoRoot = if ([string]::IsNullOrWhiteSpace($RepositoryRoot)) {
    [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot "..\.."))
} else {
    [System.IO.Path]::GetFullPath($RepositoryRoot)
}
$docsRoot = Join-Path $repoRoot "Docs"
$inventoryPath = Join-Path $docsRoot "cleanup\DOCUMENTATION_TRUTH_INVENTORY.md"
$productRoutesPath = Join-Path $repoRoot "IronDev.TauriShell\src\flow\navigation\productRoutes.ts"

if ([string]::IsNullOrWhiteSpace($ArtifactDirectory)) {
    $ArtifactDirectory = Join-Path $repoRoot "artifacts\ci\documentation-contract"
}

$artifactRoot = [System.IO.Path]::GetFullPath($ArtifactDirectory)
$allowedArtifactRoot = [System.IO.Path]::GetFullPath((Join-Path $repoRoot "artifacts\ci"))
if (-not $artifactRoot.StartsWith($allowedArtifactRoot + [System.IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
    throw "Documentation evidence must stay under '$allowedArtifactRoot'."
}

if (Test-Path -LiteralPath $artifactRoot) {
    Remove-Item -LiteralPath $artifactRoot -Recurse -Force
}
New-Item -ItemType Directory -Path $artifactRoot -Force | Out-Null

$checks = [System.Collections.Generic.List[object]]::new()
$failures = [System.Collections.Generic.List[string]]::new()

function Add-Check {
    param(
        [Parameter(Mandatory = $true)][string]$Name,
        [Parameter(Mandatory = $true)][bool]$Passed,
        [Parameter(Mandatory = $true)][string]$Detail
    )

    $checks.Add([ordered]@{ name = $Name; passed = $Passed; detail = $Detail })
    if (-not $Passed) {
        $failures.Add("$Name`: $Detail")
    }
}

function Get-RepoRelativePath {
    param([Parameter(Mandatory = $true)][string]$Path)

    $full = [System.IO.Path]::GetFullPath($Path)
    return $full.Substring($repoRoot.Length + 1).Replace("\", "/")
}

function Get-LinkTargetPath {
    param([Parameter(Mandatory = $true)][string]$RawTarget)

    $value = $RawTarget.Trim()
    if ($value.StartsWith("<", [StringComparison]::Ordinal)) {
        $close = $value.IndexOf(">", [StringComparison]::Ordinal)
        if ($close -gt 0) {
            return $value.Substring(1, $close - 1)
        }
    }

    return ($value -split "\s+", 2)[0]
}

function Test-KnownProductRoute {
    param([Parameter(Mandatory = $true)][string]$Route)

    $normalized = $Route.Trim().TrimEnd(".", ",", ";").ToLowerInvariant()
    if ($normalized -eq "//") { return $true }
    if ($normalized -eq "/projects/:projectid/library/tools/add") { return $false }
    if ($normalized -eq "/library/administration/users") { return $false }
    if ($normalized -like "/governance/*") { return $true }

    $normalized = [Regex]::Replace($normalized, "/:[a-z][a-z0-9]*", "/1")
    $normalized = [Regex]::Replace($normalized, "/\{[a-z][a-z0-9]*\}", "/1")

    $patterns = @(
        "^/$",
        "^/(sign-in|tenants/select|projects|projects/connect)$",
        "^/projects/1/(setup|board|workshop|chat)$",
        "^/projects/1/(workshop|chat)/(sessions|channels)/[^/]+$",
        "^/projects/1/work-items/(new|[^/]+)$",
        "^/projects/1/library$",
        "^/projects/1/library/(explorer|documents|tools|members|governance|provisioning|audit|settings)$",
        "^/projects/1/library/governance/(controls|exceptions|decisions|technical)$",
        "^/projects/1/library/documents/(upload|[^/]+)$",
        "^/projects/1/library/documents/[^/]+/versions/[^/]+$",
        "^/projects/1/library/tools/[^/]+$",
        "^/projects/1/library/audit/events/[^/]+$",
        "^/(chat|settings|knowledge|runs|batch|tickets|build)$"
    )

    return @($patterns | Where-Object { $normalized -match $_ }).Count -gt 0
}

if (-not (Test-Path -LiteralPath $inventoryPath -PathType Leaf)) {
    throw "Documentation inventory is missing: $inventoryPath"
}

$inventoryText = Get-Content -LiteralPath $inventoryPath
$header = $inventoryText | Where-Object { $_ -like "| Path | Title | Area | Status |*" } | Select-Object -First 1
$requiredColumns = @("Path", "Title", "Area", "Status", "Last verified against code", "Canonical replacement", "Required action", "Owner")
$missingColumns = @($requiredColumns | Where-Object { $null -eq $header -or $header -notlike "*| $_ |*" })
Add-Check -Name "Required inventory columns" -Passed ($missingColumns.Count -eq 0) -Detail $(if ($missingColumns.Count) { "Missing: $($missingColumns -join ', ')" } else { "8 of 8 present" })

$validStatuses = @("Canonical", "Supporting", "HistoricalReceipt", "Superseded", "ParkingLot", "ArchiveCandidate", "DeleteCandidate")
$entries = [System.Collections.Generic.List[object]]::new()
foreach ($line in $inventoryText) {
    if ($line -match '^\| `(?<path>Docs/[^`]+)` \|.*\| `(?<status>Canonical|Supporting|HistoricalReceipt|Superseded|ParkingLot|ArchiveCandidate|DeleteCandidate)` \|') {
        $entries.Add([pscustomobject]@{ Path = $Matches.path; Status = $Matches.status })
    }
}

$duplicatePaths = @($entries | Group-Object Path | Where-Object Count -gt 1)
Add-Check -Name "Unique inventory paths" -Passed ($duplicatePaths.Count -eq 0) -Detail $(if ($duplicatePaths.Count) { ($duplicatePaths.Name -join ", ") } else { "$($entries.Count) unique rows" })

$unknownStatuses = @($entries | Where-Object { $_.Status -notin $validStatuses })
Add-Check -Name "Inventory status vocabulary" -Passed ($unknownStatuses.Count -eq 0) -Detail $(if ($unknownStatuses.Count) { "$($unknownStatuses.Count) unknown rows" } else { "7 allowed statuses" })

$documentPaths = @(Get-ChildItem -LiteralPath $docsRoot -Recurse -File -Filter "*.md" | ForEach-Object { Get-RepoRelativePath $_.FullName } | Sort-Object -Unique)
$inventoryPaths = @($entries.Path | Sort-Object -Unique)
$missingInventoryRows = @($documentPaths | Where-Object { $_ -notin $inventoryPaths })
$extraInventoryRows = @($inventoryPaths | Where-Object { $_ -notin $documentPaths })
$inventoryComplete = $missingInventoryRows.Count -eq 0 -and $extraInventoryRows.Count -eq 0 -and $documentPaths.Count -eq $inventoryPaths.Count
$inventoryDetail = if ($inventoryComplete) {
    "$($documentPaths.Count) documents match $($inventoryPaths.Count) inventory rows"
} else {
    "missing=[$($missingInventoryRows -join ', ')] extra=[$($extraInventoryRows -join ', ')]"
}
Add-Check -Name "Complete documentation inventory" -Passed $inventoryComplete -Detail $inventoryDetail

$statusByPath = @{}
foreach ($entry in $entries) { $statusByPath[$entry.Path] = $entry.Status }

$brokenLinks = [System.Collections.Generic.List[string]]::new()
foreach ($relativePath in $documentPaths) {
    $fullPath = Join-Path $repoRoot ($relativePath.Replace("/", "\"))
    $basePath = Split-Path $fullPath
    $text = Get-Content -LiteralPath $fullPath -Raw
    $targets = @()
    $targets += [Regex]::Matches($text, '!?\[[^\]]*\]\((?<target>[^)]+)\)') | ForEach-Object { $_.Groups["target"].Value }
    $targets += [Regex]::Matches($text, '(?m)^\s*\[[^\]]+\]:\s*(?<target>\S+)') | ForEach-Object { $_.Groups["target"].Value }

    foreach ($rawTarget in @($targets | Sort-Object -Unique)) {
        $target = Get-LinkTargetPath $rawTarget
        if ($target -match '^(https?:|mailto:|data:|#|/)') { continue }
        $target = ($target -split "#", 2)[0]
        $target = ($target -split "\?", 2)[0]
        if ([string]::IsNullOrWhiteSpace($target)) { continue }

        try {
            $decodedTarget = [Uri]::UnescapeDataString($target).Replace("/", "\")
            $resolved = [System.IO.Path]::GetFullPath((Join-Path $basePath $decodedTarget))
            $insideRepo = $resolved.StartsWith($repoRoot + [System.IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)
            if (-not $insideRepo -or -not (Test-Path -LiteralPath $resolved)) {
                $brokenLinks.Add("$relativePath -> $rawTarget")
            }
        }
        catch {
            $brokenLinks.Add("$relativePath -> $rawTarget")
        }
    }
}
Add-Check -Name "Relative Markdown links" -Passed ($brokenLinks.Count -eq 0) -Detail $(if ($brokenLinks.Count) { ($brokenLinks -join "; ") } else { "$($documentPaths.Count) documents, 0 broken targets" })

$activeTitles = [System.Collections.Generic.List[object]]::new()
foreach ($entry in $entries | Where-Object { $_.Status -in @("Canonical", "Supporting") }) {
    $fullPath = Join-Path $repoRoot ($entry.Path.Replace("/", "\"))
    $heading = Get-Content -LiteralPath $fullPath | Where-Object { $_ -match '^#\s+' } | Select-Object -First 1
    if ($null -ne $heading) {
        $title = ([string]$heading -replace '^#\s+', '').Trim().ToLowerInvariant()
        if (-not [string]::IsNullOrWhiteSpace($title)) {
            $activeTitles.Add([pscustomobject]@{ Path = $entry.Path; Title = $title })
        }
    }
}
$duplicateTitles = @($activeTitles | Group-Object Title | Where-Object Count -gt 1)
$duplicateTitleDetail = @($duplicateTitles | ForEach-Object { "$($_.Name): $($_.Group.Path -join ', ')" })
Add-Check -Name "Duplicate active document identities" -Passed ($duplicateTitles.Count -eq 0) -Detail $(if ($duplicateTitles.Count) { ($duplicateTitleDetail -join "; ") } else { "$($activeTitles.Count) active H1 identities are unique" })

$statusBannerFailures = [System.Collections.Generic.List[string]]::new()
foreach ($entry in $entries | Where-Object { $_.Status -in @("Superseded", "ParkingLot") }) {
    $fullPath = Join-Path $repoRoot ($entry.Path.Replace("/", "\"))
    $opening = (Get-Content -LiteralPath $fullPath -TotalCount 30) -join "`n"
    if ($entry.Status -eq "Superseded" -and $opening -notmatch '(?i)status:\s*superseded') {
        $statusBannerFailures.Add("$($entry.Path) lacks a Superseded banner")
    }
    if ($entry.Status -eq "ParkingLot" -and $opening -notmatch '(?i)parking.?lot|not (?:an? )?(?:active|current) implementation') {
        $statusBannerFailures.Add("$($entry.Path) lacks a non-active parking-lot boundary")
    }
}
Add-Check -Name "Non-current status banners" -Passed ($statusBannerFailures.Count -eq 0) -Detail $(if ($statusBannerFailures.Count) { ($statusBannerFailures -join "; ") } else { "All Superseded and ParkingLot documents are explicit" })

$deprecatedTerms = @("Project cockpit", "Viewer catalogue", "Technical viewers", "FixRequired", "RejectFinding", "DeferFix", "AI approval", "Green CI approval", "Memory truth", "Chat decision")
$deprecatedHits = [System.Collections.Generic.List[string]]::new()
$termExclusions = @("Docs/cleanup/TERMINOLOGY_DEPRECATION_MAP.md", "Docs/cleanup/DOCUMENTATION_TRUTH_INVENTORY.md")
foreach ($entry in $entries | Where-Object { $_.Status -eq "Canonical" -and $_.Path -notin $termExclusions }) {
    foreach ($term in $deprecatedTerms) {
        $matches = Select-String -LiteralPath (Join-Path $repoRoot ($entry.Path.Replace("/", "\"))) -SimpleMatch $term -CaseSensitive:$false
        foreach ($match in $matches) { $deprecatedHits.Add("$($entry.Path):$($match.LineNumber): $term") }
    }
}
$sourceRoots = @("IronDev.TauriShell\src", "IronDev.Api")
foreach ($sourceRoot in $sourceRoots) {
    Get-ChildItem -LiteralPath (Join-Path $repoRoot $sourceRoot) -Recurse -File | Where-Object { $_.Extension -in @(".ts", ".tsx", ".cs") } | ForEach-Object {
        $sourcePath = Get-RepoRelativePath $_.FullName
        foreach ($term in $deprecatedTerms) {
            $matches = Select-String -LiteralPath $_.FullName -SimpleMatch $term -CaseSensitive:$false
            foreach ($match in $matches) { $deprecatedHits.Add("$sourcePath`:$($match.LineNumber): $term") }
        }
    }
}
Add-Check -Name "Deprecated active terminology" -Passed ($deprecatedHits.Count -eq 0) -Detail $(if ($deprecatedHits.Count) { ($deprecatedHits -join "; ") } else { "0 deprecated phrases in canonical docs or user-facing source" })

$routeFailures = [System.Collections.Generic.List[string]]::new()
$knownWorkbenchCommandTokens = @("/help", "/ticket")
foreach ($entry in $entries | Where-Object { $_.Status -eq "Canonical" }) {
    $fullPath = Join-Path $repoRoot ($entry.Path.Replace("/", "\"))
    $text = Get-Content -LiteralPath $fullPath -Raw
    foreach ($match in [Regex]::Matches($text, '`(?<route>/[^`\s]+)`')) {
        $routeReference = $match.Groups["route"].Value
        $normalizedSlashToken = $routeReference.Trim().TrimEnd(".", ",", ";").ToLowerInvariant()
        if ($normalizedSlashToken -in $knownWorkbenchCommandTokens) { continue }
        if ($routeReference -match '^/api/' -or $routeReference -match '^/[A-Za-z]:') { continue }
        if (-not (Test-KnownProductRoute $routeReference)) {
            $routeFailures.Add("$($entry.Path) -> $routeReference")
        }
    }
}

$productRoutesText = Get-Content -LiteralPath $productRoutesPath -Raw
$requiredRouteEvidence = @("boardMatch", "workshopSessionMatch", "workshopChannelMatch", "workshopMatch", "workItemMatch", "libraryGovernanceMatch", "auditEventMatch")
foreach ($evidence in $requiredRouteEvidence) {
    if ($productRoutesText -notmatch [Regex]::Escape($evidence)) {
        $routeFailures.Add("productRoutes.ts lacks $evidence")
    }
}
Add-Check -Name "Current product route references" -Passed ($routeFailures.Count -eq 0) -Detail $(if ($routeFailures.Count) { ($routeFailures -join "; ") } else { "Canonical route references resolve to implemented or named compatibility routes" })

$requiredReferences = [ordered]@{
    "Docs/README.md" = @("product/CURRENT_PRODUCT_CAPABILITIES.md", "product/IRONDEV_CLEANUP_AND_PRODUCT_COMPLETION_PLAN.md", "architecture/CANONICAL_ARCHITECTURE_INDEX.md", "cleanup/DOCUMENTATION_TRUTH_INVENTORY.md")
    "Docs/architecture/README.md" = @("CANONICAL_ARCHITECTURE_INDEX.md")
    "Docs/product/README.md" = @("CURRENT_PRODUCT_CAPABILITIES.md", "IRONDEV_CLEANUP_AND_PRODUCT_COMPLETION_PLAN.md", "IRONDEV_PRODUCT_UX_SPEC_V2.md")
    "Docs/cleanup/README.md" = @("DOCUMENTATION_TRUTH_INVENTORY.md", "DOCUMENTATION_STRUCTURE.md", "TERMINOLOGY_DEPRECATION_MAP.md")
    "Docs/memory/README.md" = @("ADR-001-SQL-source-of-truth.md", "ADR-003-memory-candidate-proposal-promotion-boundary.md")
}
$referenceFailures = [System.Collections.Generic.List[string]]::new()
foreach ($path in $requiredReferences.Keys) {
    $text = Get-Content -LiteralPath (Join-Path $repoRoot ($path.Replace("/", "\"))) -Raw
    foreach ($reference in $requiredReferences[$path]) {
        if ($text -notmatch [Regex]::Escape($reference)) {
            $referenceFailures.Add("$path lacks $reference")
        }
    }
}
Add-Check -Name "Canonical document references" -Passed ($referenceFailures.Count -eq 0) -Detail $(if ($referenceFailures.Count) { ($referenceFailures -join "; ") } else { "All canonical entry points link to their authority documents" })

$dogfoodUxIntegrityTest = Join-Path $repoRoot "tools\dogfood\Test-DogfoodUxEvidenceIntegrity.ps1"
try {
    & $dogfoodUxIntegrityTest -RepositoryRoot $repoRoot
    Add-Check -Name "DOGFOOD-UX evidence integrity" -Passed $true -Detail "Findings-derived severity and timestamp-bound timing tamper tests passed"
}
catch {
    Add-Check -Name "DOGFOOD-UX evidence integrity" -Passed $false -Detail $_.Exception.Message
}

$report = [ordered]@{
    schemaVersion = 1
    generatedAtUtc = [DateTime]::UtcNow.ToString("O")
    workflow = "documentation-contract-ci"
    result = $(if ($failures.Count) { "Failed" } else { "Passed" })
    documentCount = $documentPaths.Count
    inventoryRowCount = $entries.Count
    checks = $checks
    failures = $failures
    boundary = "Documentation checks report evidence only. They do not approve, release, deploy, continue, apply, or grant authority."
}
$report | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $artifactRoot "documentation-contract-report.json") -Encoding utf8

foreach ($check in $checks) {
    Write-Host "$(if ($check.passed) { 'PASS' } else { 'FAIL' }) $($check.name): $($check.detail)"
}

if ($failures.Count) {
    throw "Documentation contract failed with $($failures.Count) finding(s)."
}

Write-Host "PASS documentation contract ($($documentPaths.Count) documents)."
