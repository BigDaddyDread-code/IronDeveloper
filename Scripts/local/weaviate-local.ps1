[CmdletBinding()]
param(
    [switch]$CheckOnly,
    [switch]$EnsureSchema,
    [switch]$Rebuild,
    [string]$Endpoint = "http://localhost:8080",
    [string]$SchemaPath,
    [string]$CollectionName = "IronDeveloper_Local",
    [string]$ConfirmRebuild,
    [switch]$NonInteractive
)

$ErrorActionPreference = "Stop"

$BoundaryStatement = "Local Weaviate state is a disposable derived index. Rebuilding it is setup convenience, not authority, approval, evidence, or readiness."

function Find-RepositoryRoot {
    $current = (Resolve-Path $PSScriptRoot).Path
    while (-not [string]::IsNullOrWhiteSpace($current)) {
        if (Test-Path (Join-Path $current "IronDev.slnx")) {
            return $current
        }

        $parent = Split-Path -Path $current -Parent
        if ($parent -eq $current) {
            break
        }

        $current = $parent
    }

    throw "Could not find repository root containing IronDev.slnx."
}

function Add-Status {
    param(
        [Parameter(Mandatory = $true)][string]$Item,
        [Parameter(Mandatory = $true)][string]$Status,
        [string]$Detail = ""
    )

    $script:StatusRows.Add([pscustomobject]@{
        Item = $Item
        Status = $Status
        Detail = $Detail
    }) | Out-Null
}

function Write-StatusTable {
    Write-Host ""
    Write-Host "Status"
    Write-Host "------"
    Write-Host "Item | Status | Detail"
    Write-Host "--- | --- | ---"
    foreach ($row in $script:StatusRows) {
        Write-Host ("{0} | {1} | {2}" -f $row.Item, $row.Status, $row.Detail)
    }
}

function Stop-WithStatus {
    param([Parameter(Mandatory = $true)][string]$ReasonCode)

    Add-Status "Action" "Blocked" $ReasonCode
    Write-StatusTable
    throw $ReasonCode
}

function Test-EqualsAny {
    param(
        [Parameter(Mandatory = $true)][string]$Value,
        [Parameter(Mandatory = $true)][string[]]$Candidates
    )

    foreach ($candidate in $Candidates) {
        if ($Value.Equals($candidate, [System.StringComparison]::OrdinalIgnoreCase)) {
            return $true
        }
    }

    return $false
}

function Test-PathInsideRoot {
    param(
        [Parameter(Mandatory = $true)][string]$Root,
        [Parameter(Mandatory = $true)][string]$Candidate
    )

    $rootPath = [System.IO.Path]::GetFullPath($Root).TrimEnd([System.IO.Path]::DirectorySeparatorChar, [System.IO.Path]::AltDirectorySeparatorChar)
    $candidatePath = [System.IO.Path]::GetFullPath($Candidate)

    return $candidatePath.Equals($rootPath, [System.StringComparison]::OrdinalIgnoreCase) -or
        $candidatePath.StartsWith($rootPath + [System.IO.Path]::DirectorySeparatorChar, [System.StringComparison]::OrdinalIgnoreCase)
}

function Get-WeaviateEndpointClassification {
    param([string]$Value)

    if ([string]::IsNullOrWhiteSpace($Value)) {
        return [pscustomobject]@{ IsLocal = $false; ReasonCode = "WeaviateEndpointMissingRejected"; Kind = "Missing"; Uri = $null }
    }

    $trimmed = $Value.Trim()
    $credentialMarkers = @(
        ("user" + ":"),
        ("pass" + "="),
        ("pwd" + "="),
        ("api" + "key"),
        ("to" + "ken" + "="),
        ("secret" + "="),
        ("authorization" + ":"),
        ("bearer" + " ")
    )

    foreach ($marker in $credentialMarkers) {
        if ($trimmed.IndexOf($marker, [System.StringComparison]::OrdinalIgnoreCase) -ge 0) {
            return [pscustomobject]@{ IsLocal = $false; ReasonCode = "WeaviateEndpointCredentialRejected"; Kind = "Credentialed"; Uri = $null }
        }
    }

    $uri = $null
    if (-not [System.Uri]::TryCreate($trimmed, [System.UriKind]::Absolute, [ref]$uri)) {
        return [pscustomobject]@{ IsLocal = $false; ReasonCode = "WeaviateEndpointMalformedRejected"; Kind = "Malformed"; Uri = $null }
    }

    if (-not (Test-EqualsAny $uri.Scheme @("http", "https"))) {
        return [pscustomobject]@{ IsLocal = $false; ReasonCode = "WeaviateEndpointSchemeRejected"; Kind = "UnsupportedScheme"; Uri = $uri }
    }

    if (-not [string]::IsNullOrWhiteSpace($uri.UserInfo)) {
        return [pscustomobject]@{ IsLocal = $false; ReasonCode = "WeaviateEndpointCredentialRejected"; Kind = "Credentialed"; Uri = $uri }
    }

    if ([string]::IsNullOrWhiteSpace($uri.Host)) {
        return [pscustomobject]@{ IsLocal = $false; ReasonCode = "WeaviateEndpointHostMissingRejected"; Kind = "MissingHost"; Uri = $uri }
    }

    $endpointHost = $uri.Host.Trim()

    if ($endpointHost.EndsWith(".weaviate.cloud", [System.StringComparison]::OrdinalIgnoreCase) -or
        $endpointHost.IndexOf("weaviate.cloud", [System.StringComparison]::OrdinalIgnoreCase) -ge 0 -or
        $endpointHost.IndexOf("wcs", [System.StringComparison]::OrdinalIgnoreCase) -ge 0) {
        return [pscustomobject]@{ IsLocal = $false; ReasonCode = "WeaviateEndpointCloudRejected"; Kind = "Cloud"; Uri = $uri }
    }

    if (Test-EqualsAny $endpointHost @("localhost", "127.0.0.1", "::1")) {
        return [pscustomobject]@{ IsLocal = $true; ReasonCode = "WeaviateEndpointLocal"; Kind = "Loopback"; Uri = $uri }
    }

    $address = $null
    if ([System.Net.IPAddress]::TryParse($endpointHost, [ref]$address)) {
        if ([System.Net.IPAddress]::IsLoopback($address)) {
            return [pscustomobject]@{ IsLocal = $true; ReasonCode = "WeaviateEndpointLocal"; Kind = "Loopback"; Uri = $uri }
        }

        return [pscustomobject]@{ IsLocal = $false; ReasonCode = "WeaviateEndpointRemoteRejected"; Kind = "RemoteIp"; Uri = $uri }
    }

    if ($endpointHost.Contains(".") -or $endpointHost.Contains("-") -or (Test-EqualsAny $endpointHost @("dev", "test", "staging", "uat", "accept", "prod", "live"))) {
        return [pscustomobject]@{ IsLocal = $false; ReasonCode = "WeaviateEndpointRemoteRejected"; Kind = "RemoteHost"; Uri = $uri }
    }

    return [pscustomobject]@{ IsLocal = $false; ReasonCode = "WeaviateEndpointUnknownRejected"; Kind = "UnknownHost"; Uri = $uri }
}

function Get-CollectionNameClassification {
    param([string]$Value)

    if ([string]::IsNullOrWhiteSpace($Value)) {
        return [pscustomobject]@{ IsSafe = $false; ReasonCode = "CollectionNameMissingRejected" }
    }

    $trimmed = $Value.Trim()

    if ($trimmed -match '[^A-Za-z0-9_]' -or $trimmed.Contains("__")) {
        return [pscustomobject]@{ IsSafe = $false; ReasonCode = "CollectionNameUnsafeCharactersRejected" }
    }

    if ($trimmed -match '(?i)(Prod|Production|Live|Accept|Acceptance|UAT|Stage|Staging|Shared|Release|Main|Customer|Client)') {
        return [pscustomobject]@{ IsSafe = $false; ReasonCode = "CollectionNameProductionLikeRejected" }
    }

    if ($trimmed -match '^IronDeveloper_(Local|Dev|Test)(_[A-Za-z0-9]+)?$' -or $trimmed -match '^IronDeveloper_J06_[A-Za-z0-9]+$') {
        return [pscustomobject]@{ IsSafe = $true; ReasonCode = "CollectionNameSafeLocal" }
    }

    return [pscustomobject]@{ IsSafe = $false; ReasonCode = "CollectionNameNotLocalPatternRejected" }
}

function Get-SchemaPathClassification {
    param(
        [Parameter(Mandatory = $true)][string]$RepositoryRoot,
        [string]$Value
    )

    if ([string]::IsNullOrWhiteSpace($Value)) {
        return [pscustomobject]@{ IsProvided = $false; IsUsable = $true; ReasonCode = "SchemaPathNotProvided"; FullPath = $null }
    }

    $trimmed = $Value.Trim()
    if ($trimmed -match '^(?i)https?://') {
        return [pscustomobject]@{ IsProvided = $true; IsUsable = $false; ReasonCode = "SchemaPathRemoteRejected"; FullPath = $null }
    }

    if ($trimmed.StartsWith("\\", [System.StringComparison]::Ordinal)) {
        return [pscustomobject]@{ IsProvided = $true; IsUsable = $false; ReasonCode = "SchemaPathNetworkShareRejected"; FullPath = $null }
    }

    $candidate = if ([System.IO.Path]::IsPathRooted($trimmed)) {
        $trimmed
    }
    else {
        Join-Path $RepositoryRoot $trimmed
    }

    try {
        $fullPath = [System.IO.Path]::GetFullPath($candidate)
        $insideRepo = Test-PathInsideRoot $RepositoryRoot $fullPath
    }
    catch {
        return [pscustomobject]@{ IsProvided = $true; IsUsable = $false; ReasonCode = "SchemaPathInvalidRejected"; FullPath = $null }
    }

    if (-not $insideRepo) {
        $tempPath = [System.IO.Path]::GetFullPath([System.IO.Path]::GetTempPath()).TrimEnd([System.IO.Path]::DirectorySeparatorChar, [System.IO.Path]::AltDirectorySeparatorChar)
        if ($fullPath.StartsWith($tempPath + [System.IO.Path]::DirectorySeparatorChar, [System.StringComparison]::OrdinalIgnoreCase)) {
            return [pscustomobject]@{ IsProvided = $true; IsUsable = $false; ReasonCode = "SchemaPathTempRejected"; FullPath = $null }
        }

        $userHomePatterns = @(
            ('^[A-Za-z]:\\' + 'Users\\'),
            ('^/' + 'home/'),
            ('^/' + 'Users/')
        )
        foreach ($userHomePattern in $userHomePatterns) {
            if ($fullPath -match $userHomePattern) {
                return [pscustomobject]@{ IsProvided = $true; IsUsable = $false; ReasonCode = "SchemaPathUserHomeRejected"; FullPath = $null }
            }
        }

        return [pscustomobject]@{ IsProvided = $true; IsUsable = $false; ReasonCode = "SchemaPathOutsideRepositoryRejected"; FullPath = $null }
    }

    if (-not (Test-Path $fullPath -PathType Leaf)) {
        return [pscustomobject]@{ IsProvided = $true; IsUsable = $false; ReasonCode = "SchemaPathMissing"; FullPath = $fullPath }
    }

    return [pscustomobject]@{ IsProvided = $true; IsUsable = $true; ReasonCode = "SchemaPathPresent"; FullPath = $fullPath }
}

function Invoke-WeaviateJson {
    param(
        [Parameter(Mandatory = $true)][string]$Method,
        [Parameter(Mandatory = $true)][string]$Uri,
        [string]$Body
    )

    $arguments = @{
        Method = $Method
        Uri = $Uri
        TimeoutSec = 2
    }

    if (-not [string]::IsNullOrWhiteSpace($Body)) {
        $arguments["ContentType"] = "application/json"
        $arguments["Body"] = $Body
    }

    return Invoke-RestMethod @arguments
}

function Get-WeaviateReady {
    param([Parameter(Mandatory = $true)][System.Uri]$Uri)

    try {
        $readyUri = [System.Uri]::new($Uri, "/v1/.well-known/ready").AbsoluteUri
        Invoke-WeaviateJson "Get" $readyUri | Out-Null
        return $true
    }
    catch {
        return $false
    }
}

function Get-WeaviateSchema {
    param([Parameter(Mandatory = $true)][System.Uri]$Uri)

    $schemaUri = [System.Uri]::new($Uri, "/v1/schema").AbsoluteUri
    return Invoke-WeaviateJson "Get" $schemaUri
}

function Find-CollectionSchema {
    param(
        [object]$Schema,
        [Parameter(Mandatory = $true)][string]$Name
    )

    if ($null -eq $Schema -or $null -eq $Schema.classes) {
        return $null
    }

    return $Schema.classes | Where-Object { $_.class -eq $Name } | Select-Object -First 1
}

function Test-CompatibleSchema {
    param([object]$ClassSchema)

    if ($null -eq $ClassSchema) {
        return $false
    }

    return $ClassSchema.vectorizer -eq "none"
}

function New-DefaultSchemaJson {
    param([Parameter(Mandatory = $true)][string]$Name)

    return @{
        class = $Name
        vectorizer = "none"
        properties = @(
            @{
                name = "sourceRef"
                dataType = @("text")
            },
            @{
                name = "summary"
                dataType = @("text")
            }
        )
    } | ConvertTo-Json -Depth 10
}

function Get-SchemaJson {
    param(
        [Parameter(Mandatory = $true)][object]$SchemaClassification,
        [Parameter(Mandatory = $true)][string]$Name
    )

    if (-not $SchemaClassification.IsProvided) {
        return New-DefaultSchemaJson $Name
    }

    try {
        $json = Get-Content -Path $SchemaClassification.FullPath -Raw
        $parsed = $json | ConvertFrom-Json
    }
    catch {
        throw "SchemaJsonInvalid"
    }

    if ($parsed.class -ne $Name) {
        throw "SchemaClassMismatch"
    }

    return $json
}

function Ensure-LocalSchema {
    param(
        [Parameter(Mandatory = $true)][System.Uri]$Uri,
        [Parameter(Mandatory = $true)][string]$Name,
        [Parameter(Mandatory = $true)][string]$SchemaJson
    )

    $schema = Get-WeaviateSchema $Uri
    $existing = Find-CollectionSchema $schema $Name
    if ($existing) {
        if (Test-CompatibleSchema $existing) {
            Add-Status "Schema result" "ExistingCompatible" "local collection already exists"
            return
        }

        Stop-WithStatus "SchemaIncompatibleUseExactRebuild"
    }

    $schemaUri = [System.Uri]::new($Uri, "/v1/schema").AbsoluteUri
    Invoke-WeaviateJson "Post" $schemaUri $SchemaJson | Out-Null
    Add-Status "Schema result" "Created" "local collection schema created"
}

function Rebuild-LocalSchema {
    param(
        [Parameter(Mandatory = $true)][System.Uri]$Uri,
        [Parameter(Mandatory = $true)][string]$Name,
        [Parameter(Mandatory = $true)][string]$SchemaJson
    )

    Write-Host "Destructive warning: Rebuild will delete and recreate only collection '$Name' on a loopback Weaviate endpoint."

    $schema = Get-WeaviateSchema $Uri
    $existing = Find-CollectionSchema $schema $Name
    $collectionPath = "/v1/schema/$Name"

    if ($existing) {
        $deleteUri = [System.Uri]::new($Uri, $collectionPath).AbsoluteUri
        Invoke-WeaviateJson "Delete" $deleteUri | Out-Null
        Add-Status "Delete result" "Completed" "named local collection removed"
    }
    else {
        Add-Status "Delete result" "NotRun" "named local collection was absent"
    }

    $schemaUri = [System.Uri]::new($Uri, "/v1/schema").AbsoluteUri
    Invoke-WeaviateJson "Post" $schemaUri $SchemaJson | Out-Null
    Add-Status "Rebuild result" "Completed" "named local collection recreated"
}

$explicitCheckOnly = $PSBoundParameters.ContainsKey("CheckOnly")
$mutationRequested = $EnsureSchema -or $Rebuild

if (-not $explicitCheckOnly -and -not $mutationRequested) {
    $CheckOnly = $true
}

if ($explicitCheckOnly -and $mutationRequested) {
    throw "CheckOnly cannot be combined with EnsureSchema or Rebuild."
}

if ($EnsureSchema -and $Rebuild) {
    throw "EnsureSchema and Rebuild are mutually exclusive."
}

$repoRoot = Find-RepositoryRoot
$endpointClassification = Get-WeaviateEndpointClassification $Endpoint
$collectionClassification = Get-CollectionNameClassification $CollectionName
$schemaClassification = Get-SchemaPathClassification $repoRoot $SchemaPath
$script:StatusRows = New-Object System.Collections.Generic.List[object]
$mode = if ($CheckOnly) { "CheckOnly" } elseif ($EnsureSchema) { "EnsureSchema" } elseif ($Rebuild) { "Rebuild" } else { "Invalid" }

Write-Host "IronDev local Weaviate command"
Write-Host "Mode: $mode"
Write-Host "Boundary: $BoundaryStatement"
Write-Host ""

Add-Status "Repo root" "Found" "IronDev.slnx present"
Add-Status "Endpoint" ($(if ($endpointClassification.IsLocal) { "Local" } else { "Rejected" })) ($endpointClassification.ReasonCode + "; Kind=" + $endpointClassification.Kind)
Add-Status "Collection" ($(if ($collectionClassification.IsSafe) { "SafeLocal" } else { "Rejected" })) ($(if ($collectionClassification.IsSafe) { $CollectionName + "; " + $collectionClassification.ReasonCode } else { $collectionClassification.ReasonCode }))
Add-Status "Schema source" ($(if ($schemaClassification.IsUsable) { "Usable" } else { "Rejected" })) $schemaClassification.ReasonCode
Add-Status "EnsureSchema" ($(if ($EnsureSchema) { "Requested" } else { "NotRun" })) ($(if ($EnsureSchema) { "explicit mode selected" } else { "requires EnsureSchema" }))
Add-Status "Rebuild" ($(if ($Rebuild) { "Requested" } else { "NotRun" })) ($(if ($Rebuild) { "explicit mode selected" } else { "requires Rebuild" }))
Add-Status "Demo import" "NotRun" "J06 never loads demo or BookSeller vectors"
Add-Status "Service start" "NotRun" "J06 never starts Weaviate or Docker"
Add-Status "Evidence write" "NotRun" "J06 never writes evidence"

if (-not $endpointClassification.IsLocal) {
    Stop-WithStatus $endpointClassification.ReasonCode
}

if (-not $collectionClassification.IsSafe) {
    Stop-WithStatus $collectionClassification.ReasonCode
}

if (-not $schemaClassification.IsUsable) {
    Stop-WithStatus $schemaClassification.ReasonCode
}

if ($Rebuild) {
    $expectedConfirmation = "REBUILD $CollectionName"
    if ($ConfirmRebuild -ne $expectedConfirmation) {
        Stop-WithStatus "RebuildConfirmationRejected"
    }
}

$reachable = Get-WeaviateReady $endpointClassification.Uri
Add-Status "Weaviate reachability" ($(if ($reachable) { "Reachable" } else { "Unavailable" })) ($(if ($reachable) { "ready endpoint responded" } else { "WeaviateUnavailable" }))

if ($CheckOnly) {
    if ($reachable) {
        try {
            $schema = Get-WeaviateSchema $endpointClassification.Uri
            $existing = Find-CollectionSchema $schema $CollectionName
            if ($existing) {
                Add-Status "Collection check" ($(if (Test-CompatibleSchema $existing) { "Present" } else { "Incompatible" })) ($(if (Test-CompatibleSchema $existing) { "compatible local schema" } else { "SchemaIncompatibleUseExactRebuild" }))
            }
            else {
                Add-Status "Collection check" "Missing" "local collection not found"
            }
        }
        catch {
            Add-Status "Collection check" "Unknown" "SchemaReadFailed"
        }
    }
    else {
        Add-Status "Collection check" "Unknown" "WeaviateUnavailable"
    }

    Add-Status "Action" "NotRun" "CheckOnly"
    Add-Status "Next safe action" "Review" "run EnsureSchema only for a safe local endpoint and collection"
    Write-StatusTable
    return
}

if (-not $reachable) {
    Stop-WithStatus "WeaviateUnavailable"
}

$schemaJson = Get-SchemaJson $schemaClassification $CollectionName

if ($EnsureSchema) {
    Ensure-LocalSchema $endpointClassification.Uri $CollectionName $schemaJson
}

if ($Rebuild) {
    Rebuild-LocalSchema $endpointClassification.Uri $CollectionName $schemaJson
}

Add-Status "Action" "Completed" "local Weaviate command finished"
Add-Status "Next safe action" "Review" "enable local Weaviate configuration only through ignored local settings"
Write-StatusTable
