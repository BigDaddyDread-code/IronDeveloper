[CmdletBinding()]
param(
    [switch]$CheckOnly,
    [switch]$Json,
    [switch]$Markdown,
    [switch]$Strict,
    [switch]$NonInteractive,
    [string]$ApiBaseUrl = "http://localhost:5000",
    [string]$UiBaseUrl = "http://127.0.0.1:5173",
    [string]$SqlServer = "(localdb)\MSSQLLocalDB",
    [string]$DatabaseName = "IronDeveloper_Local",
    [string]$LocalTestDatabaseName = "IronDeveloper_Test",
    [string]$WeaviateEndpoint = "http://localhost:8080",
    [Parameter(ValueFromRemainingArguments = $true)]
    [string[]]$RemainingArguments
)

$ErrorActionPreference = "Stop"

$BoundaryStatement = "The developer doctor is diagnostic only. It reports local readiness blockers and next safe actions; it does not create readiness, evidence, approval, authority, or permission to run/mutate/apply/release."
$DefaultBoundary = "DiagnosticOnly; NotAuthority; NotEvidence; NotApproval; NotReadiness"

function New-SwitchText {
    param([Parameter(Mandatory = $true)][string]$Name)
    return "-" + $Name
}

function New-LocalCommand {
    param([Parameter(Mandatory = $true)][string[]]$Parts)
    return $Parts -join " "
}

function Get-UnsafeSwitches {
    return @(
        (New-SwitchText "Fix"),
        (New-SwitchText "Prepare"),
        (New-SwitchText "Create"),
        (New-SwitchText "Rebuild"),
        (New-SwitchText "ApplyLocalDevSetup"),
        (New-SwitchText "StartServices"),
        (New-SwitchText "StartApi"),
        (New-SwitchText "StartUi"),
        (New-SwitchText "StartDocker"),
        (New-SwitchText "StartWeaviate"),
        (New-SwitchText "Seed"),
        (New-SwitchText "SeedDemo"),
        (New-SwitchText "RunSmoke"),
        (New-SwitchText "RunAlpha"),
        (New-SwitchText "RunPlaywright"),
        (New-SwitchText "WriteEvidence"),
        (New-SwitchText "Approve"),
        (New-SwitchText "Continue"),
        (New-SwitchText "Apply")
    )
}

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
        [Parameter(Mandatory = $true)][string[]]$Arguments,
        [string]$WorkingDirectory
    )

    $previousErrorActionPreference = $ErrorActionPreference
    $ErrorActionPreference = "Continue"
    try {
        if ([string]::IsNullOrWhiteSpace($WorkingDirectory)) {
            & $FileName @Arguments *> $null
        }
        else {
            Push-Location $WorkingDirectory
            try {
                & $FileName @Arguments *> $null
            }
            finally {
                Pop-Location
            }
        }

        return $LASTEXITCODE
    }
    finally {
        $ErrorActionPreference = $previousErrorActionPreference
    }
}

function Test-GitRepository {
    param([Parameter(Mandatory = $true)][string]$RepositoryRoot)

    if (-not (Test-CommandAvailable "git")) {
        return $false
    }

    return (Invoke-NativeQuiet "git" @("-C", $RepositoryRoot, "rev-parse", "--is-inside-work-tree")) -eq 0
}

function Test-GitTracked {
    param(
        [Parameter(Mandatory = $true)][string]$RepositoryRoot,
        [Parameter(Mandatory = $true)][string]$RelativePath
    )

    if (-not (Test-GitRepository $RepositoryRoot)) {
        return $false
    }

    return (Invoke-NativeQuiet "git" @("-C", $RepositoryRoot, "ls-files", "--error-unmatch", "--", $RelativePath)) -eq 0
}

function Test-GitIgnored {
    param(
        [Parameter(Mandatory = $true)][string]$RepositoryRoot,
        [Parameter(Mandatory = $true)][string]$RelativePath
    )

    if (-not (Test-GitRepository $RepositoryRoot)) {
        return $false
    }

    return (Invoke-NativeQuiet "git" @("-C", $RepositoryRoot, "check-ignore", "-q", "--", $RelativePath)) -eq 0
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

    if (Test-EqualsAny $trimmed @("master", "model", "msdb", "tempdb")) {
        return [pscustomobject]@{ IsSafe = $false; ReasonCode = "DatabaseNameSystemRejected" }
    }

    if (Test-EqualsAny $trimmed @("IronDeveloper", "Production", "Prod", "Live", "Accept", "Staging", "Shared")) {
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

    $hostName = $uri.Host.Trim()
    if ([string]::IsNullOrWhiteSpace($hostName)) {
        return [pscustomobject]@{ IsLocal = $false; ReasonCode = "WeaviateEndpointHostMissingRejected"; Kind = "MissingHost"; Uri = $uri }
    }

    if ($hostName.IndexOf("weaviate.cloud", [System.StringComparison]::OrdinalIgnoreCase) -ge 0 -or
        $hostName.IndexOf("wcs", [System.StringComparison]::OrdinalIgnoreCase) -ge 0) {
        return [pscustomobject]@{ IsLocal = $false; ReasonCode = "WeaviateEndpointCloudRejected"; Kind = "Cloud"; Uri = $uri }
    }

    if (Test-EqualsAny $hostName @("localhost", "127.0.0.1", "::1")) {
        return [pscustomobject]@{ IsLocal = $true; ReasonCode = "WeaviateEndpointLocal"; Kind = "Loopback"; Uri = $uri }
    }

    $address = $null
    if ([System.Net.IPAddress]::TryParse($hostName, [ref]$address)) {
        if ([System.Net.IPAddress]::IsLoopback($address)) {
            return [pscustomobject]@{ IsLocal = $true; ReasonCode = "WeaviateEndpointLocal"; Kind = "Loopback"; Uri = $uri }
        }

        return [pscustomobject]@{ IsLocal = $false; ReasonCode = "WeaviateEndpointRemoteRejected"; Kind = "RemoteIp"; Uri = $uri }
    }

    if ($hostName.Contains(".") -or $hostName.Contains("-") -or (Test-EqualsAny $hostName @("dev", "test", "staging", "uat", "accept", "prod", "live"))) {
        return [pscustomobject]@{ IsLocal = $false; ReasonCode = "WeaviateEndpointRemoteRejected"; Kind = "RemoteHost"; Uri = $uri }
    }

    return [pscustomobject]@{ IsLocal = $false; ReasonCode = "WeaviateEndpointUnknownRejected"; Kind = "UnknownHost"; Uri = $uri }
}

function Get-LoopbackUrlClassification {
    param([string]$Value)

    if ([string]::IsNullOrWhiteSpace($Value)) {
        return [pscustomobject]@{ IsLocal = $false; ReasonCode = "UrlMissingRejected"; Uri = $null }
    }

    $uri = $null
    if (-not [System.Uri]::TryCreate($Value.Trim(), [System.UriKind]::Absolute, [ref]$uri)) {
        return [pscustomobject]@{ IsLocal = $false; ReasonCode = "UrlMalformedRejected"; Uri = $null }
    }

    if (-not (Test-EqualsAny $uri.Scheme @("http", "https"))) {
        return [pscustomobject]@{ IsLocal = $false; ReasonCode = "UrlSchemeRejected"; Uri = $uri }
    }

    if (-not [string]::IsNullOrWhiteSpace($uri.UserInfo)) {
        return [pscustomobject]@{ IsLocal = $false; ReasonCode = "UrlCredentialRejected"; Uri = $uri }
    }

    if (Test-EqualsAny $uri.Host @("localhost", "127.0.0.1", "::1")) {
        return [pscustomobject]@{ IsLocal = $true; ReasonCode = "UrlLocal"; Uri = $uri }
    }

    $address = $null
    if ([System.Net.IPAddress]::TryParse($uri.Host, [ref]$address) -and [System.Net.IPAddress]::IsLoopback($address)) {
        return [pscustomobject]@{ IsLocal = $true; ReasonCode = "UrlLocal"; Uri = $uri }
    }

    return [pscustomobject]@{ IsLocal = $false; ReasonCode = "UrlRemoteRejected"; Uri = $uri }
}

function Invoke-ChildCheckOnly {
    param(
        [Parameter(Mandatory = $true)][string]$ScriptPath,
        [Parameter(Mandatory = $true)][string[]]$Arguments,
        [Parameter(Mandatory = $true)][string]$WorkingDirectory
    )

    $previousErrorActionPreference = $ErrorActionPreference
    $ErrorActionPreference = "Continue"
    try {
        Push-Location $WorkingDirectory
        try {
            & powershell -NoProfile -ExecutionPolicy Bypass -File $ScriptPath @Arguments *> $null
            return $LASTEXITCODE
        }
        finally {
            Pop-Location
        }
    }
    finally {
        $ErrorActionPreference = $previousErrorActionPreference
    }
}

function Invoke-GetProbe {
    param([Parameter(Mandatory = $true)][System.Uri]$Uri)

    try {
        Invoke-WebRequest -Uri $Uri.AbsoluteUri -Method Get -UseBasicParsing -TimeoutSec 2 | Out-Null
        return $true
    }
    catch {
        return $false
    }
}

function Add-DoctorCheck {
    param(
        [Parameter(Mandatory = $true)][string]$Name,
        [Parameter(Mandatory = $true)][string]$Status,
        [Parameter(Mandatory = $true)][string]$Severity,
        [Parameter(Mandatory = $true)][string]$Detail,
        [string]$NextSafeAction = "",
        [string]$EvidenceKind = "DiagnosticCheck",
        [string]$AuthorityBoundary = $DefaultBoundary,
        [int]$Priority = 1000
    )

    $script:Checks.Add([pscustomobject]@{
        Name = $Name
        Status = $Status
        Severity = $Severity
        Detail = $Detail
        NextSafeAction = $NextSafeAction
        EvidenceKind = $EvidenceKind
        AuthorityBoundary = $AuthorityBoundary
        Priority = $Priority
    }) | Out-Null
}

function Get-PrimaryNextSafeAction {
    $candidate = $script:Checks |
        Where-Object { -not [string]::IsNullOrWhiteSpace($_.NextSafeAction) -and $_.Severity -eq "Blocker" } |
        Sort-Object Priority, Name |
        Select-Object -First 1

    if ($candidate) {
        return $candidate.NextSafeAction
    }

    $candidate = $script:Checks |
        Where-Object { -not [string]::IsNullOrWhiteSpace($_.NextSafeAction) -and $_.Severity -eq "Warning" } |
        Sort-Object Priority, Name |
        Select-Object -First 1

    if ($candidate) {
        return $candidate.NextSafeAction
    }

    return New-LocalCommand @("powershell", "-ExecutionPolicy", "Bypass", "-File", ".\tools\localtest\start-alpha-localtest.ps1", "-Reset", "-BrowserOnly")
}

function Get-DoctorStatus {
    if ($script:Checks | Where-Object { $_.Severity -eq "Blocker" } | Select-Object -First 1) {
        return "Blocked"
    }

    if ($Strict -and ($script:Checks | Where-Object { $_.Severity -eq "Warning" -or $_.Status -eq "NotEvaluated" -or $_.Status -eq "Unknown" } | Select-Object -First 1)) {
        return "Blocked"
    }

    if ($script:Checks | Where-Object { $_.Severity -eq "Warning" } | Select-Object -First 1) {
        return "Warning"
    }

    if ($script:Checks | Where-Object { $_.Status -eq "NotEvaluated" -or $_.Status -eq "Unknown" } | Select-Object -First 1) {
        return "Unknown"
    }

    return "Ready"
}

function Write-TextReport {
    param([Parameter(Mandatory = $true)][object]$Model)

    Write-Host "IronDev developer environment doctor"
    Write-Host "Mode: CheckOnly"
    Write-Host "Boundary: $($Model.BoundaryStatement)"
    Write-Host ""
    Write-Host "Checks"
    Write-Host "------"
    Write-Host "Name | Status | Severity | Detail | Next safe action | Evidence kind | Boundary"
    Write-Host "--- | --- | --- | --- | --- | --- | ---"
    foreach ($check in $Model.Checks) {
        Write-Host ("{0} | {1} | {2} | {3} | {4} | {5} | {6}" -f $check.Name, $check.Status, $check.Severity, $check.Detail, $check.NextSafeAction, $check.EvidenceKind, $check.AuthorityBoundary)
    }

    Write-Host ""
    Write-Host "Doctor result: $($Model.DoctorStatus)"
    Write-Host ""
    Write-Host "Blockers:"
    foreach ($blocker in $Model.Blockers) {
        Write-Host ("- {0}: {1}" -f $blocker.Name, $blocker.Detail)
    }
    if ($Model.Blockers.Count -eq 0) {
        Write-Host "- none"
    }

    Write-Host ""
    Write-Host "Warnings:"
    foreach ($warning in $Model.Warnings) {
        Write-Host ("- {0}: {1}" -f $warning.Name, $warning.Detail)
    }
    if ($Model.Warnings.Count -eq 0) {
        Write-Host "- none"
    }

    Write-Host ""
    Write-Host "Next safe action:"
    Write-Host $Model.NextSafeAction
}

function Write-MarkdownReport {
    param([Parameter(Mandatory = $true)][object]$Model)

    Write-Host "# IronDev Developer Environment Doctor"
    Write-Host ""
    Write-Host "**Doctor result:** $($Model.DoctorStatus)"
    Write-Host ""
    Write-Host "**Boundary:** $($Model.BoundaryStatement)"
    Write-Host ""
    Write-Host "## Checks"
    Write-Host ""
    Write-Host "| Name | Status | Severity | Detail | Next safe action | Evidence kind | Boundary |"
    Write-Host "| --- | --- | --- | --- | --- | --- | --- |"
    foreach ($check in $Model.Checks) {
        Write-Host ("| {0} | {1} | {2} | {3} | {4} | {5} | {6} |" -f $check.Name, $check.Status, $check.Severity, $check.Detail, $check.NextSafeAction, $check.EvidenceKind, $check.AuthorityBoundary)
    }
    Write-Host ""
    Write-Host "## Next Safe Action"
    Write-Host ""
    Write-Host '```powershell'
    Write-Host $Model.NextSafeAction
    Write-Host '```'
}

try {
    if (-not $PSBoundParameters.ContainsKey("CheckOnly")) {
        $CheckOnly = $true
    }

    foreach ($argument in $RemainingArguments) {
        foreach ($unsafeSwitch in Get-UnsafeSwitches) {
            if ($argument.Equals($unsafeSwitch, [System.StringComparison]::OrdinalIgnoreCase)) {
                Write-Host "Unsafe requested option rejected: $unsafeSwitch"
                Write-Host $BoundaryStatement
                exit 3
            }
        }
    }

    $repoRoot = Find-RepositoryRoot
    $script:Checks = New-Object System.Collections.Generic.List[object]

    $requiredRepositoryFiles = @(
        "IronDev.slnx",
        "IronDev.Api",
        "IronDev.TauriShell",
        "Scripts/local/bootstrap-local.ps1",
        "Scripts/local/sql-local.ps1",
        "tools/localtest/reset-localtest-data.ps1",
        "tools/localtest/start-alpha-localtest.ps1",
        "tools/localtest/Invoke-LocalTestSmoke.ps1"
    )

    $missingRepositoryFiles = @(
        foreach ($relativePath in $requiredRepositoryFiles) {
            if (-not (Test-Path (Join-Path $repoRoot $relativePath))) {
                $relativePath
            }
        }
    )

    if ($missingRepositoryFiles.Count -gt 0) {
        Add-DoctorCheck "Repository" "Missing" "Blocker" ("MissingRequiredRepositoryShape:" + ($missingRepositoryFiles -join ",")) "Restore the repository checkout before running LocalTest setup." "FilePresence" $DefaultBoundary 1
    }
    else {
        Add-DoctorCheck "Repository" "Pass" "Info" "RequiredRepositoryShapePresent" "" "FilePresence"
    }

    $toolFindings = @()
    $dotnetFound = Test-CommandAvailable "dotnet"
    $gitFound = Test-CommandAvailable "git"
    $nodeFound = Test-CommandAvailable "node"
    $npmFound = Test-CommandAvailable "npm"
    $sqlcmdFound = Test-CommandAvailable "sqlcmd"
    $dockerFound = Test-CommandAvailable "docker"

    if (-not $dotnetFound) { $toolFindings += "DotNetMissing" }
    if (-not $gitFound) { $toolFindings += "GitMissing" }
    if (-not $nodeFound) { $toolFindings += "NodeMissing" }
    if (-not $npmFound) { $toolFindings += "NpmMissing" }
    if (-not $sqlcmdFound) { $toolFindings += "SqlcmdMissing" }
    if (-not $dockerFound) { $toolFindings += "DockerMissingOptional" }

    if (-not $dotnetFound) {
        Add-DoctorCheck "Toolchain" "Missing" "Blocker" "DotNetMissing" "Install the required .NET SDK, then rerun the doctor." "CommandAvailability" $DefaultBoundary 1
    }
    elseif (-not $nodeFound -or -not $npmFound) {
        Add-DoctorCheck "Toolchain" "Missing" "Blocker" ($toolFindings -join ";") "Install Node.js/npm, then rerun the doctor." "CommandAvailability" $DefaultBoundary 2
    }
    elseif (-not $sqlcmdFound) {
        Add-DoctorCheck "Toolchain" "Missing" "Blocker" ($toolFindings -join ";") "Install sqlcmd, then rerun the doctor." "CommandAvailability" $DefaultBoundary 3
    }
    elseif (-not $dockerFound) {
        Add-DoctorCheck "Toolchain" "Warn" "Warning" ($toolFindings -join ";") "Install Docker only if local Weaviate setup requires it." "CommandAvailability" $DefaultBoundary 8
    }
    else {
        Add-DoctorCheck "Toolchain" "Pass" "Info" "RequiredCommandsAvailable" "" "CommandAvailability"
    }

    $frontendPackageJson = Join-Path $repoRoot "IronDev.TauriShell/package.json"
    $frontendNodeModules = Join-Path $repoRoot "IronDev.TauriShell/node_modules"
    if (-not (Test-Path $frontendPackageJson)) {
        Add-DoctorCheck "Frontend" "Missing" "Blocker" "PackageJsonMissing" "Restore the frontend checkout before running LocalTest." "FilePresence" $DefaultBoundary 4
    }
    else {
        try {
            $package = Get-Content -Path $frontendPackageJson -Raw | ConvertFrom-Json
            $requiredScripts = @("build", "dev", "dev:localtest", "test")
            $missingScripts = @(
                foreach ($scriptName in $requiredScripts) {
                    if ($null -eq $package.scripts.$scriptName) {
                        $scriptName
                    }
                }
            )

            if ($missingScripts.Count -gt 0) {
                Add-DoctorCheck "Frontend" "Missing" "Blocker" ("PackageScriptsMissing:" + ($missingScripts -join ",")) "Restore frontend package scripts before running LocalTest." "PackageMetadata" $DefaultBoundary 4
            }
            elseif (-not (Test-Path $frontendNodeModules)) {
                Add-DoctorCheck "Frontend" "Missing" "Blocker" "NodeModulesMissing" (New-LocalCommand @("powershell", "-ExecutionPolicy", "Bypass", "-File", ".\Scripts\local\bootstrap-local.ps1", (New-SwitchText "Prepare"), (New-SwitchText "InstallFrontend"))) "PackageMetadata" $DefaultBoundary 4
            }
            else {
                Add-DoctorCheck "Frontend" "Pass" "Info" "FrontendPackageShapePresent" "" "PackageMetadata"
            }
        }
        catch {
            Add-DoctorCheck "Frontend" "Blocked" "Blocker" "PackageJsonUnreadable" "Fix the frontend package manifest before running LocalTest." "PackageMetadata" $DefaultBoundary 4
        }
    }

    if ($dotnetFound -and (Test-Path (Join-Path $repoRoot "IronDev.slnx"))) {
        Add-DoctorCheck "DotNetReadiness" "NotEvaluated" "Warning" "RestoreBuildNotRunByDoctor" (New-LocalCommand @("powershell", "-ExecutionPolicy", "Bypass", "-File", ".\Scripts\local\bootstrap-local.ps1", (New-SwitchText "Prepare"), (New-SwitchText "RestoreDotNet"))) "ReadinessPreflight" $DefaultBoundary 4
    }
    else {
        Add-DoctorCheck "DotNetReadiness" "Missing" "Blocker" "DotNetOrSolutionMissing" "Install .NET and restore the checkout before running LocalTest." "ReadinessPreflight" $DefaultBoundary 1
    }

    $localOverrideExample = Join-Path $repoRoot "IronDev.Api/appsettings.Development.Local.example.json"
    $localOverrideTarget = Join-Path $repoRoot "IronDev.Api/appsettings.Development.Local.json"
    $localOverrideRelative = "IronDev.Api/appsettings.Development.Local.json"
    $examplePresent = Test-Path $localOverrideExample
    $overridePresent = Test-Path $localOverrideTarget
    $overrideTracked = if ($overridePresent) { Test-GitTracked $repoRoot $localOverrideRelative } else { $false }
    $overrideIgnored = if ($overridePresent) { Test-GitIgnored $repoRoot $localOverrideRelative } else { $false }

    if (-not $examplePresent) {
        Add-DoctorCheck "LocalOverride" "Missing" "Blocker" "LocalOverrideExampleMissing" "Restore the local override example before setup." "ConfigShape" $DefaultBoundary 5
    }
    elseif ($overrideTracked) {
        Add-DoctorCheck "LocalOverride" "Blocked" "Blocker" "TrackedLocalOverrideRejected" "Remove the tracked local override from git and keep machine settings ignored." "ConfigShape" $DefaultBoundary 5
    }
    elseif (-not $overridePresent) {
        Add-DoctorCheck "LocalOverride" "Missing" "Warning" "LocalOverrideMissing" (New-LocalCommand @("powershell", "-ExecutionPolicy", "Bypass", "-File", ".\Scripts\local\bootstrap-local.ps1", (New-SwitchText "Prepare"), (New-SwitchText "CreateLocalOverride"))) "ConfigShape" $DefaultBoundary 5
    }
    elseif (-not $overrideIgnored) {
        Add-DoctorCheck "LocalOverride" "Warn" "Warning" "LocalOverrideIgnoreStatusUnknown" "Confirm IronDev.Api/appsettings.Development.Local.json is ignored and untracked." "ConfigShape" $DefaultBoundary 5
    }
    else {
        Add-DoctorCheck "LocalOverride" "Pass" "Info" "LocalOverrideIgnoredAndUntracked" "" "ConfigShape"
    }

    $configSummaryFiles = @(
        "IronDev.Core/Configuration/RedactedConfigSummaryModels.cs",
        "IronDev.Core/Configuration/RedactedConfigSummaryService.cs"
    )
    if ($configSummaryFiles | Where-Object { -not (Test-Path (Join-Path $repoRoot $_)) } | Select-Object -First 1) {
        Add-DoctorCheck "ConfigurationSummary" "Unavailable" "Warning" "J08ConfigSummaryUnavailable" "Use manual config inspection without printing secrets." "ContractPresence" $DefaultBoundary 6
    }
    else {
        Add-DoctorCheck "ConfigurationSummary" "Pass" "Info" "J08ConfigSummaryAvailableNotInvoked" "" "ContractPresence"
    }

    $rootSafetyCandidates = @(
        "IronDev.Core/Configuration/LocalRootSafetyValidator.cs",
        "IronDev.Core/Configuration/RootSafetyValidator.cs",
        "IronDev.Core/Configuration/ConfiguredRootSafetyValidator.cs",
        "Scripts/local/root-safety-local.ps1"
    )
    if ($rootSafetyCandidates | Where-Object { Test-Path (Join-Path $repoRoot $_) } | Select-Object -First 1) {
        Add-DoctorCheck "RootSafety" "NotEvaluated" "Warning" "RootSafetyContractPresentButNotInvoked" "Run the explicit root-safety validator before external alpha." "ContractPresence" "DiagnosticOnly; NotAuthority; NotEvidence; NotApproval; NotReadiness" 9
    }
    else {
        Add-DoctorCheck "RootSafety" "NotEvaluated" "Blocker" "J10RootSafetyUnavailable" "Implement or run J10 root safety before external alpha." "ContractPresence" "DiagnosticOnly; NotAuthority; NotEvidence; NotApproval; NotReadiness" 9
    }

    $sqlScriptPath = Join-Path $repoRoot "Scripts/local/sql-local.ps1"
    $sqlTarget = Get-SqlTargetClassification $SqlServer
    $databaseTarget = Get-DatabaseNameClassification $DatabaseName
    if (-not (Test-Path $sqlScriptPath)) {
        Add-DoctorCheck "SqlLocal" "Missing" "Blocker" "J05SqlLocalScriptMissing" "Add/run J05 before local SQL checks are required." "ScriptPresence" $DefaultBoundary 7
    }
    elseif (-not $sqlTarget.IsLocal) {
        Add-DoctorCheck "SqlLocal" "Blocked" "Blocker" $sqlTarget.ReasonCode "Use only loopback/local SQL targets with the J05 command." "TargetClassification" $DefaultBoundary 7
    }
    elseif (-not $databaseTarget.IsSafe) {
        Add-DoctorCheck "SqlLocal" "Blocked" "Blocker" $databaseTarget.ReasonCode "Use a developer-local database name such as IronDeveloper_Local or IronDeveloper_Test." "TargetClassification" $DefaultBoundary 7
    }
    elseif (-not $sqlcmdFound) {
        Add-DoctorCheck "SqlLocal" "Missing" "Blocker" "SqlcmdMissing" "Install sqlcmd, then rerun the doctor." "CommandAvailability" $DefaultBoundary 3
    }
    else {
        $sqlExit = Invoke-ChildCheckOnly $sqlScriptPath @((New-SwitchText "CheckOnly"), (New-SwitchText "ServerInstance"), $SqlServer, (New-SwitchText "DatabaseName"), $DatabaseName) $repoRoot
        if ($sqlExit -eq 0) {
            Add-DoctorCheck "SqlLocal" "Pass" "Info" "J05CheckOnlyCompleted" "" "CheckOnlyDelegation"
        }
        else {
            Add-DoctorCheck "SqlLocal" "Unavailable" "Warning" "J05CheckOnlyReturnedNonZero" (New-LocalCommand @("powershell", "-ExecutionPolicy", "Bypass", "-File", ".\Scripts\local\sql-local.ps1", (New-SwitchText "CheckOnly"))) "CheckOnlyDelegation" $DefaultBoundary 7
        }
    }

    $weaviateScriptPath = Join-Path $repoRoot "Scripts/local/weaviate-local.ps1"
    $weaviateTarget = Get-WeaviateEndpointClassification $WeaviateEndpoint
    if (-not (Test-Path $weaviateScriptPath)) {
        Add-DoctorCheck "WeaviateLocal" "NotEvaluated" "Warning" "J06WeaviateLocalScriptMissing" "Add/run J06 when local Weaviate checks are required." "ScriptPresence" $DefaultBoundary 8
    }
    elseif (-not $weaviateTarget.IsLocal) {
        Add-DoctorCheck "WeaviateLocal" "Blocked" "Blocker" $weaviateTarget.ReasonCode "Use a loopback Weaviate endpoint or disable local Weaviate." "EndpointClassification" "DiagnosticOnly; NotAuthority; NotEvidence; NotApproval; NotReadiness" 8
    }
    else {
        $weaviateExit = Invoke-ChildCheckOnly $weaviateScriptPath @((New-SwitchText "CheckOnly"), (New-SwitchText "Endpoint"), $WeaviateEndpoint, (New-SwitchText "CollectionName"), "IronDeveloper_Local") $repoRoot
        if ($weaviateExit -eq 0) {
            Add-DoctorCheck "WeaviateLocal" "Pass" "Info" "J06CheckOnlyCompleted" "" "CheckOnlyDelegation" "DiagnosticOnly; NotAuthority; NotEvidence; NotApproval; NotReadiness"
        }
        else {
            Add-DoctorCheck "WeaviateLocal" "Unavailable" "Warning" "LocalWeaviateUnavailableOrIncomplete" (New-LocalCommand @("powershell", "-ExecutionPolicy", "Bypass", "-File", ".\Scripts\local\weaviate-local.ps1", (New-SwitchText "CheckOnly"))) "CheckOnlyDelegation" "DiagnosticOnly; NotAuthority; NotEvidence; NotApproval; NotReadiness" 8
        }
    }

    $localTestConfig = Join-Path $repoRoot "IronDev.Api/appsettings.LocalTest.json"
    $localTestRequiredFiles = @(
        "IronDev.Api/appsettings.LocalTest.json",
        "tools/localtest/reset-localtest-data.ps1",
        "tools/localtest/start-alpha-localtest.ps1",
        "tools/localtest/Invoke-LocalTestSmoke.ps1",
        "IronDev.TauriShell/tests/localtest-manual-smoke.spec.ts"
    )
    $missingLocalTestFiles = @(
        foreach ($relativePath in $localTestRequiredFiles) {
            if (-not (Test-Path (Join-Path $repoRoot $relativePath))) {
                $relativePath
            }
        }
    )

    if ($missingLocalTestFiles.Count -gt 0) {
        Add-DoctorCheck "LocalTest" "Missing" "Blocker" ("LocalTestFilesMissing:" + ($missingLocalTestFiles -join ",")) "Restore LocalTest scripts before running LocalTest." "FilePresence" $DefaultBoundary 6
    }
    else {
        try {
            $localTest = Get-Content -Path $localTestConfig -Raw | ConvertFrom-Json
            $connection = [string]$localTest.ConnectionStrings.IronDeveloperDb
            $configuredDatabase = if ($connection -match '(?i)(Database|Initial Catalog)\s*=\s*([^;]+)') { $Matches[2] } else { $LocalTestDatabaseName }
            $workspaceRoot = [string]$localTest.LocalTest.WorkspaceRoot
            $logsRoot = [string]$localTest.LocalTest.LogsRoot
            $dangerRealRepoWrites = [bool]$localTest.LocalTest.DangerRealRepoWritesEnabled
            $localTestFailures = @()

            if ($configuredDatabase.IndexOf("Test", [System.StringComparison]::OrdinalIgnoreCase) -lt 0) { $localTestFailures += "LocalTestDatabaseMustContainTest" }
            if ($workspaceRoot.IndexOf("Test", [System.StringComparison]::OrdinalIgnoreCase) -lt 0) { $localTestFailures += "LocalTestWorkspaceRootMustContainTest" }
            if ($logsRoot.IndexOf("Test", [System.StringComparison]::OrdinalIgnoreCase) -lt 0) { $localTestFailures += "LocalTestLogsRootMustContainTest" }
            if ($dangerRealRepoWrites) { $localTestFailures += "LocalTestRealRepoWritesEnabledRejected" }

            if ($localTestFailures.Count -gt 0) {
                Add-DoctorCheck "LocalTest" "Blocked" "Blocker" ($localTestFailures -join ";") "Fix IronDev.Api/appsettings.LocalTest.json so database, workspace, and logs are test-isolated." "ConfigShape" $DefaultBoundary 6
            }
            else {
                Add-DoctorCheck "LocalTest" "Pass" "Info" "LocalTestConfigShapeSafe" "" "ConfigShape"
            }
        }
        catch {
            Add-DoctorCheck "LocalTest" "Blocked" "Blocker" "LocalTestConfigUnreadable" "Fix IronDev.Api/appsettings.LocalTest.json before LocalTest startup." "ConfigShape" $DefaultBoundary 6
        }
    }

    $apiClassification = Get-LoopbackUrlClassification $ApiBaseUrl
    if (-not $apiClassification.IsLocal) {
        Add-DoctorCheck "Api" "Blocked" "Blocker" $apiClassification.ReasonCode "Use only loopback API base URLs for local diagnostics." "GetOnlyProbe" $DefaultBoundary 10
    }
    else {
        $healthUri = [System.Uri]::new($apiClassification.Uri, "/health")
        $environmentUri = [System.Uri]::new($apiClassification.Uri, "/api/environment")
        $healthOk = Invoke-GetProbe $healthUri
        $environmentOk = Invoke-GetProbe $environmentUri
        if ($healthOk -or $environmentOk) {
            Add-DoctorCheck "Api" "Pass" "Info" "ApiGetProbeResponded" "" "GetOnlyProbe"
        }
        else {
            Add-DoctorCheck "Api" "Unavailable" "Warning" "ApiUnavailableNoServiceStarted" (New-LocalCommand @("powershell", "-ExecutionPolicy", "Bypass", "-File", ".\tools\localtest\start-alpha-localtest.ps1", (New-SwitchText "Reset"), (New-SwitchText "BrowserOnly"))) "GetOnlyProbe" $DefaultBoundary 10
        }
    }

    $uiClassification = Get-LoopbackUrlClassification $UiBaseUrl
    if (-not $uiClassification.IsLocal) {
        Add-DoctorCheck "Ui" "Blocked" "Blocker" $uiClassification.ReasonCode "Use only loopback UI base URLs for local diagnostics." "GetOnlyProbe" $DefaultBoundary 10
    }
    else {
        if (Invoke-GetProbe $uiClassification.Uri) {
            Add-DoctorCheck "Ui" "Pass" "Info" "UiGetProbeResponded" "" "GetOnlyProbe"
        }
        else {
            Add-DoctorCheck "Ui" "Unavailable" "Warning" "UiUnavailableNoServiceStarted" (New-LocalCommand @("powershell", "-ExecutionPolicy", "Bypass", "-File", ".\tools\localtest\start-alpha-localtest.ps1", (New-SwitchText "Reset"), (New-SwitchText "BrowserOnly"))) "GetOnlyProbe" $DefaultBoundary 10
        }
    }

    if ((Test-Path (Join-Path $repoRoot "tools/localtest/Invoke-LocalTestSmoke.ps1")) -and
        (Test-Path (Join-Path $repoRoot "IronDev.TauriShell/tests/localtest-manual-smoke.spec.ts"))) {
        try {
            $package = Get-Content -Path $frontendPackageJson -Raw | ConvertFrom-Json
            if ($null -ne $package.devDependencies.'@playwright/test' -or $null -ne $package.dependencies.'@playwright/test') {
                Add-DoctorCheck "SmokePath" "Pass" "Info" "SmokePathAvailableNotRun" "" "FilePresence"
            }
            else {
                Add-DoctorCheck "SmokePath" "Missing" "Warning" "PlaywrightPackageMissing" "Install frontend dependencies before smoke proof." "FilePresence" $DefaultBoundary 11
            }
        }
        catch {
            Add-DoctorCheck "SmokePath" "Unknown" "Warning" "SmokePathPackageMetadataUnreadable" "Fix frontend package metadata before smoke proof." "FilePresence" $DefaultBoundary 11
        }
    }
    else {
        Add-DoctorCheck "SmokePath" "Missing" "Blocker" "SmokePathMissing" "Restore the LocalTest smoke scripts before proof runs." "FilePresence" $DefaultBoundary 11
    }

    $doctorStatus = Get-DoctorStatus
    $nextSafeAction = Get-PrimaryNextSafeAction
    Add-DoctorCheck "NextSafeAction" "Pass" "Info" "SinglePrimaryNextSafeActionSelected" $nextSafeAction "DiagnosticRecommendation" $DefaultBoundary 100

    $blockers = @($script:Checks | Where-Object { $_.Severity -eq "Blocker" } | Select-Object Name, Detail, NextSafeAction)
    $warnings = @($script:Checks | Where-Object { $_.Severity -eq "Warning" } | Select-Object Name, Detail, NextSafeAction)
    $model = [pscustomobject]@{
        DoctorStatus = $doctorStatus
        Mode = "CheckOnly"
        BoundaryStatement = $BoundaryStatement
        Checks = @($script:Checks | Sort-Object Name | Select-Object Name, Status, Severity, Detail, NextSafeAction, EvidenceKind, AuthorityBoundary)
        Blockers = $blockers
        Warnings = $warnings
        NextSafeAction = $nextSafeAction
    }

    if ($Json) {
        $model | ConvertTo-Json -Depth 8
    }
    elseif ($Markdown) {
        Write-MarkdownReport $model
    }
    else {
        Write-TextReport $model
    }

    if ($doctorStatus -eq "Blocked") {
        exit 2
    }

    exit 0
}
catch {
    Write-Error ("Developer doctor failed unexpectedly: " + $_.Exception.Message)
    exit 1
}
