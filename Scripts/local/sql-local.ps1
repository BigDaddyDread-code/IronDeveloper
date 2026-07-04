[CmdletBinding()]
param(
    [switch]$CheckOnly,
    [switch]$Create,
    [switch]$Rebuild,
    [switch]$ApplyLocalDevSetup,
    [string]$ServerInstance = "(localdb)\MSSQLLocalDB",
    [string]$DatabaseName = "IronDeveloper_Local",
    [string]$SetupScript = "Database/local_dev_setup.sql",
    [string]$ConfirmRebuild,
    [switch]$NonInteractive
)

$ErrorActionPreference = "Stop"

$BoundaryStatement = "The local SQL command may create or rebuild a developer-local database. It is not evidence, approval, root safety proof, policy satisfaction, schema authority, or permission to mutate source, workflows, evidence, or shared SQL targets."

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

function Test-CommandAvailable {
    param([Parameter(Mandatory = $true)][string]$Name)

    return [bool](Get-Command $Name -ErrorAction SilentlyContinue)
}

function Invoke-NativeQuiet {
    param(
        [Parameter(Mandatory = $true)][string]$FileName,
        [Parameter(Mandatory = $true)][string[]]$Arguments
    )

    $previousErrorActionPreference = $ErrorActionPreference
    $ErrorActionPreference = "Continue"
    try {
        & $FileName @Arguments *> $null
        return $LASTEXITCODE
    }
    finally {
        $ErrorActionPreference = $previousErrorActionPreference
    }
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

function Get-SqlTargetClassification {
    param([string]$Value)

    if ([string]::IsNullOrWhiteSpace($Value)) {
        return [pscustomobject]@{ IsLocal = $false; ReasonCode = "SqlTargetUnknownRejected"; Kind = "Unknown" }
    }

    $trimmed = $Value.Trim()
    $credentialMarkers = @(
        ("Pass" + "word"),
        ("Pwd" + "="),
        ("User Id" + "="),
        ("User ID" + "="),
        ("SqlCredential"),
        ("Connection" + "String"),
        ("Token" + "="),
        ("Secret" + "=")
    )

    foreach ($marker in $credentialMarkers) {
        if ($trimmed.IndexOf($marker, [System.StringComparison]::OrdinalIgnoreCase) -ge 0) {
            return [pscustomobject]@{ IsLocal = $false; ReasonCode = "SqlTargetCredentialedRejected"; Kind = "Credentialed" }
        }
    }

    if ($trimmed.Contains(";")) {
        return [pscustomobject]@{ IsLocal = $false; ReasonCode = "SqlTargetCredentialedRejected"; Kind = "ConnectionString" }
    }

    if ($trimmed -match '(?i)(database\.windows\.net|\.database\.azure\.com|azure)') {
        return [pscustomobject]@{ IsLocal = $false; ReasonCode = "SqlTargetAzureRejected"; Kind = "Azure" }
    }

    if ($trimmed -eq "(localdb)\MSSQLLocalDB") {
        return [pscustomobject]@{ IsLocal = $true; ReasonCode = "SqlTargetLocal"; Kind = "LocalDB" }
    }

    if ($trimmed -eq "." -or $trimmed -eq "(local)" -or $trimmed -eq "localhost" -or $trimmed -eq "127.0.0.1" -or $trimmed -eq "[::1]") {
        return [pscustomobject]@{ IsLocal = $true; ReasonCode = "SqlTargetLocal"; Kind = "Loopback" }
    }

    if ($trimmed -match '^(?i)localhost,\d{1,5}$' -or $trimmed -match '^127\.0\.0\.1,\d{1,5}$') {
        return [pscustomobject]@{ IsLocal = $true; ReasonCode = "SqlTargetLocal"; Kind = "LoopbackPort" }
    }

    if ($trimmed -match '^\.\\[A-Za-z0-9_$-]+$' -or $trimmed -match '^(?i)localhost\\[A-Za-z0-9_$-]+$') {
        return [pscustomobject]@{ IsLocal = $true; ReasonCode = "SqlTargetLocal"; Kind = "LocalNamedInstance" }
    }

    if ($trimmed -match '^\d{1,3}(\.\d{1,3}){3}(,\d{1,5})?$') {
        return [pscustomobject]@{ IsLocal = $false; ReasonCode = "SqlTargetRemoteRejected"; Kind = "RemoteIp" }
    }

    if ($trimmed.Contains(".")) {
        return [pscustomobject]@{ IsLocal = $false; ReasonCode = "SqlTargetRemoteRejected"; Kind = "RemoteHost" }
    }

    return [pscustomobject]@{ IsLocal = $false; ReasonCode = "SqlTargetUnknownRejected"; Kind = "Unknown" }
}

function Get-DatabaseNameClassification {
    param([string]$Value)

    if ([string]::IsNullOrWhiteSpace($Value)) {
        return [pscustomobject]@{ IsSafe = $false; ReasonCode = "DatabaseNameNotLocalPatternRejected" }
    }

    $trimmed = $Value.Trim()

    if ($trimmed -match "['"";]" -or $trimmed.Contains("--") -or $trimmed.Contains("/*") -or $trimmed.Contains("*/") -or $trimmed.Contains("[") -or $trimmed.Contains("]")) {
        return [pscustomobject]@{ IsSafe = $false; ReasonCode = "DatabaseNameUnsafeCharactersRejected" }
    }

    $systemNames = @("master", "model", "msdb", "tempdb")
    if (Test-EqualsAny $trimmed $systemNames) {
        return [pscustomobject]@{ IsSafe = $false; ReasonCode = "DatabaseNameSystemRejected" }
    }

    $productionExact = @("IronDeveloper", "Production", "Prod", "Live", "Accept", "Staging", "Shared")
    if (Test-EqualsAny $trimmed $productionExact) {
        return [pscustomobject]@{ IsSafe = $false; ReasonCode = "DatabaseNameProductionLikeRejected" }
    }

    if ($trimmed -match '(?i)(^|_)(Prod|Production|Live|Accept|UAT|Staging|Shared)($|_)') {
        return [pscustomobject]@{ IsSafe = $false; ReasonCode = "DatabaseNameProductionLikeRejected" }
    }

    if ($trimmed -match '^IronDeveloper_(Local|Dev|Test)(_[A-Za-z0-9]+)?$' -or $trimmed -match '^IronDeveloper_J05_[A-Za-z0-9]+$') {
        return [pscustomobject]@{ IsSafe = $true; ReasonCode = "DatabaseNameSafeLocal" }
    }

    return [pscustomobject]@{ IsSafe = $false; ReasonCode = "DatabaseNameNotLocalPatternRejected" }
}

function Get-SetupScriptClassification {
    param(
        [Parameter(Mandatory = $true)][string]$RepositoryRoot,
        [string]$Value
    )

    if ([string]::IsNullOrWhiteSpace($Value)) {
        return [pscustomobject]@{ IsUsable = $false; ReasonCode = "SetupScriptMissing"; FullPath = $null }
    }

    $trimmed = $Value.Trim()
    if ($trimmed -match '^(?i)https?://') {
        return [pscustomobject]@{ IsUsable = $false; ReasonCode = "SetupScriptRemoteRejected"; FullPath = $null }
    }

    $candidate = if ([System.IO.Path]::IsPathRooted($trimmed)) {
        $trimmed
    }
    else {
        Join-Path $RepositoryRoot $trimmed
    }

    try {
        $fullPath = [System.IO.Path]::GetFullPath($candidate)
        $rootPath = [System.IO.Path]::GetFullPath($RepositoryRoot).TrimEnd([System.IO.Path]::DirectorySeparatorChar, [System.IO.Path]::AltDirectorySeparatorChar)
    }
    catch {
        return [pscustomobject]@{ IsUsable = $false; ReasonCode = "SetupScriptPathInvalid"; FullPath = $null }
    }

    if (-not ($fullPath.Equals($rootPath, [System.StringComparison]::OrdinalIgnoreCase) -or $fullPath.StartsWith($rootPath + [System.IO.Path]::DirectorySeparatorChar, [System.StringComparison]::OrdinalIgnoreCase))) {
        return [pscustomobject]@{ IsUsable = $false; ReasonCode = "SetupScriptOutsideRepositoryRejected"; FullPath = $null }
    }

    if (-not (Test-Path $fullPath -PathType Leaf)) {
        return [pscustomobject]@{ IsUsable = $false; ReasonCode = "SetupScriptMissing"; FullPath = $fullPath }
    }

    return [pscustomobject]@{ IsUsable = $true; ReasonCode = "SetupScriptPresent"; FullPath = $fullPath }
}

function Invoke-SqlQuery {
    param(
        [Parameter(Mandatory = $true)][string]$Server,
        [Parameter(Mandatory = $true)][string]$Database,
        [Parameter(Mandatory = $true)][string]$Query,
        [Parameter(Mandatory = $true)][string]$FailureReason
    )

    $exitCode = Invoke-NativeQuiet "sqlcmd" @("-b", "-S", $Server, "-d", $Database, "-E", "-Q", $Query)
    if ($exitCode -ne 0) {
        throw $FailureReason
    }
}

function Invoke-SqlScript {
    param(
        [Parameter(Mandatory = $true)][string]$Server,
        [Parameter(Mandatory = $true)][string]$Database,
        [Parameter(Mandatory = $true)][string]$ScriptPath,
        [Parameter(Mandatory = $true)][string]$FailureReason
    )

    $exitCode = Invoke-NativeQuiet "sqlcmd" @("-b", "-S", $Server, "-d", $Database, "-E", "-i", $ScriptPath)
    if ($exitCode -ne 0) {
        throw $FailureReason
    }
}

function New-DatabaseScopedSetupScript {
    param(
        [Parameter(Mandatory = $true)][string]$SourcePath,
        [Parameter(Mandatory = $true)][string]$Database
    )

    $tempDirectory = Join-Path ([System.IO.Path]::GetTempPath()) ("IronDevJ05-" + [System.Guid]::NewGuid().ToString("N"))
    New-Item -ItemType Directory -Path $tempDirectory | Out-Null
    $tempScript = Join-Path $tempDirectory "local_dev_setup.sql"
    $text = Get-Content -Path $SourcePath -Raw
    $text = $text.Replace("name = N'IronDeveloper'", "name = N'$Database'")
    $text = $text.Replace("CREATE DATABASE [IronDeveloper]", "CREATE DATABASE [$Database]")
    $text = $text.Replace("USE [IronDeveloper];", "USE [$Database];")
    Set-Content -Path $tempScript -Value $text -Encoding UTF8

    return [pscustomobject]@{ Directory = $tempDirectory; Script = $tempScript }
}

$explicitCheckOnly = $PSBoundParameters.ContainsKey("CheckOnly")
$mutationRequested = $Create -or $Rebuild -or $ApplyLocalDevSetup

if (-not $explicitCheckOnly -and -not $mutationRequested) {
    $CheckOnly = $true
}

if ($explicitCheckOnly -and $mutationRequested) {
    throw "CheckOnly cannot be combined with Create, Rebuild, or ApplyLocalDevSetup."
}

if ($Create -and $Rebuild) {
    throw "Create and Rebuild are mutually exclusive."
}

if ($ApplyLocalDevSetup -and -not ($Create -or $Rebuild)) {
    throw "ApplyLocalDevSetup requires Create or Rebuild."
}

$repoRoot = Find-RepositoryRoot
$targetClassification = Get-SqlTargetClassification $ServerInstance
$databaseClassification = Get-DatabaseNameClassification $DatabaseName
$setupClassification = Get-SetupScriptClassification $repoRoot $SetupScript
$sqlToolFound = Test-CommandAvailable "sqlcmd"

$script:StatusRows = New-Object System.Collections.Generic.List[object]
$mode = if ($CheckOnly) { "CheckOnly" } elseif ($Create) { "Create" } elseif ($Rebuild) { "Rebuild" } else { "Invalid" }

Write-Host "IronDev local SQL command"
Write-Host "Mode: $mode"
Write-Host "Boundary: $BoundaryStatement"
Write-Host ""

Add-Status "Repo root" "Found" "IronDev.slnx present"
Add-Status "SQL tool" ($(if ($sqlToolFound) { "Found" } else { "Missing" })) ($(if ($sqlToolFound) { "sqlcmd available" } else { "SqlToolMissing" }))
Add-Status "SQL target" ($(if ($targetClassification.IsLocal) { "Local" } else { "Rejected" })) ($targetClassification.ReasonCode + "; Kind=" + $targetClassification.Kind)
Add-Status "Database" ($(if ($databaseClassification.IsSafe) { "SafeLocal" } else { "Rejected" })) ($(if ($databaseClassification.IsSafe) { $DatabaseName + "; " + $databaseClassification.ReasonCode } else { $databaseClassification.ReasonCode }))
Add-Status "Setup script" ($(if ($setupClassification.IsUsable) { "Present" } else { "Rejected" })) $setupClassification.ReasonCode
Add-Status "Create" ($(if ($Create) { "Requested" } else { "NotRun" })) ($(if ($Create) { "explicit mode selected" } else { "requires Create" }))
Add-Status "Rebuild" ($(if ($Rebuild) { "Requested" } else { "NotRun" })) ($(if ($Rebuild) { "explicit mode selected" } else { "requires Rebuild" }))
Add-Status "ApplyLocalDevSetup" ($(if ($ApplyLocalDevSetup) { "Requested" } else { "NotRun" })) ($(if ($ApplyLocalDevSetup) { "explicit setup application requested" } else { "requires ApplyLocalDevSetup" }))

if ($CheckOnly) {
    Add-Status "Action" "NotRun" "CheckOnly"
    Add-Status "Next safe action" "Review" "run Create with ApplyLocalDevSetup only for a safe local database"
    Write-StatusTable
    return
}

if (-not $sqlToolFound) {
    Stop-WithStatus "SqlToolMissing"
}

if (-not $targetClassification.IsLocal) {
    Stop-WithStatus $targetClassification.ReasonCode
}

if (-not $databaseClassification.IsSafe) {
    Stop-WithStatus $databaseClassification.ReasonCode
}

if ($ApplyLocalDevSetup -and -not $setupClassification.IsUsable) {
    Stop-WithStatus $setupClassification.ReasonCode
}

if ($Rebuild) {
    $expectedConfirmation = "REBUILD $DatabaseName"
    if ($ConfirmRebuild -ne $expectedConfirmation) {
        Stop-WithStatus "RebuildConfirmationRejected"
    }
}

if ($Create) {
    $createQuery = "IF DB_ID(N'$DatabaseName') IS NULL CREATE DATABASE [$DatabaseName];"
    Invoke-SqlQuery $ServerInstance "master" $createQuery "SqlCreateFailed"
    Add-Status "Create result" "Completed" "database exists or was created"
}

if ($Rebuild) {
    $rebuildQuery = "IF DB_ID(N'$DatabaseName') IS NOT NULL BEGIN ALTER DATABASE [$DatabaseName] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [$DatabaseName]; END; CREATE DATABASE [$DatabaseName];"
    Invoke-SqlQuery $ServerInstance "master" $rebuildQuery "SqlRebuildFailed"
    Add-Status "Rebuild result" "Completed" "exact safe local database rebuilt"
}

if ($ApplyLocalDevSetup) {
    $scopedSetup = $null
    try {
        $scopedSetup = New-DatabaseScopedSetupScript $setupClassification.FullPath $DatabaseName
        Invoke-SqlScript $ServerInstance $DatabaseName $scopedSetup.Script "SqlSetupScriptFailed"
        Add-Status "Setup result" "Completed" "local setup script applied"
    }
    finally {
        if ($scopedSetup -and (Test-Path $scopedSetup.Directory)) {
            Remove-Item -Path $scopedSetup.Directory -Recurse -Force
        }
    }
}

Add-Status "Action" "Completed" "local SQL command finished"
Add-Status "Next safe action" "Review" "update ignored local override manually if this database should be used"
Write-StatusTable
