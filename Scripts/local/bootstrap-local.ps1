[CmdletBinding()]
param(
    [switch]$CheckOnly,
    [switch]$Prepare,
    [switch]$CreateLocalOverride,
    [switch]$RestoreDotNet,
    [switch]$InstallFrontend,
    [switch]$NonInteractive
)

$ErrorActionPreference = "Stop"

$BoundaryStatement = "The local bootstrap script prepares local convenience. It is not evidence, approval, root safety proof, policy satisfaction, or permission to mutate source, SQL, Weaviate, evidence, or sandbox repositories."

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

function Invoke-QuietCommand {
    param(
        [Parameter(Mandatory = $true)][string]$FileName,
        [Parameter(Mandatory = $true)][string[]]$Arguments,
        [Parameter(Mandatory = $true)][string]$WorkingDirectory,
        [Parameter(Mandatory = $true)][string]$FailureMessage
    )

    Push-Location $WorkingDirectory
    try {
        $exitCode = Invoke-NativeQuiet $FileName $Arguments
        if ($exitCode -ne 0) {
            throw $FailureMessage
        }
    }
    finally {
        Pop-Location
    }
}

function Test-GitRepository {
    param([Parameter(Mandatory = $true)][string]$RepositoryRoot)

    if (-not (Test-CommandAvailable "git")) {
        return $false
    }

    $exitCode = Invoke-NativeQuiet "git" @("-C", $RepositoryRoot, "rev-parse", "--is-inside-work-tree")
    return $exitCode -eq 0
}

function Test-GitTracked {
    param(
        [Parameter(Mandatory = $true)][string]$RepositoryRoot,
        [Parameter(Mandatory = $true)][string]$RelativePath
    )

    if (-not (Test-GitRepository $RepositoryRoot)) {
        return $false
    }

    $exitCode = Invoke-NativeQuiet "git" @("-C", $RepositoryRoot, "ls-files", "--error-unmatch", "--", $RelativePath)
    return $exitCode -eq 0
}

function Test-GitIgnored {
    param(
        [Parameter(Mandatory = $true)][string]$RepositoryRoot,
        [Parameter(Mandatory = $true)][string]$RelativePath
    )

    if (-not (Test-GitRepository $RepositoryRoot)) {
        return $false
    }

    $exitCode = Invoke-NativeQuiet "git" @("-C", $RepositoryRoot, "check-ignore", "-q", "--", $RelativePath)
    return $exitCode -eq 0
}

function Test-PlaceholderOnlyExample {
    param([Parameter(Mandatory = $true)][string]$Path)

    $text = Get-Content -Path $Path -Raw
    $unsafeMarkers = @(
        ("Pass" + "word="),
        ("Pwd" + "="),
        ("Token" + "="),
        ("Secret" + "="),
        ("Bearer "),
        ("Authorization" + ":"),
        ("sk-" + "live"),
        ("ghp" + "_"),
        ("-----" + "BEGIN " + "PRI" + "VATE " + "KE" + "Y-----")
    )

    foreach ($marker in $unsafeMarkers) {
        if ($text.IndexOf($marker, [System.StringComparison]::OrdinalIgnoreCase) -ge 0) {
            return $false
        }
    }

    return $true
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

$mutationRequested = $CreateLocalOverride -or $RestoreDotNet -or $InstallFrontend
$explicitCheckOnly = $PSBoundParameters.ContainsKey("CheckOnly")

if (-not $explicitCheckOnly -and -not $Prepare -and -not $mutationRequested) {
    $CheckOnly = $true
}

if ($explicitCheckOnly -and ($Prepare -or $mutationRequested)) {
    throw "CheckOnly cannot be combined with Prepare, CreateLocalOverride, RestoreDotNet, or InstallFrontend."
}

if ($mutationRequested -and -not $Prepare) {
    throw "CreateLocalOverride, RestoreDotNet, and InstallFrontend require Prepare."
}

$repoRoot = Find-RepositoryRoot
$solutionPath = Join-Path $repoRoot "IronDev.slnx"
$apiDirectory = Join-Path $repoRoot "IronDev.Api"
$localOverrideExample = Join-Path $apiDirectory "appsettings.Development.Local.example.json"
$localOverrideTarget = Join-Path $apiDirectory "appsettings.Development.Local.json"
$localOverrideRelativePath = "IronDev.Api/appsettings.Development.Local.json"
$frontendDirectory = Join-Path $repoRoot "IronDev.TauriShell"
$frontendPackageJson = Join-Path $frontendDirectory "package.json"
$frontendNodeModules = Join-Path $frontendDirectory "node_modules"
$configSummaryService = Join-Path $repoRoot "IronDev.Core\Configuration\RedactedConfigSummaryService.cs"
$rootSafetyCandidates = @(
    (Join-Path $repoRoot "IronDev.Core\Configuration\LocalRootSafetyValidator.cs"),
    (Join-Path $repoRoot "IronDev.Core\Configuration\RootSafetyValidator.cs"),
    (Join-Path $repoRoot "IronDev.Core\Configuration\ConfiguredRootSafetyValidator.cs")
)

$script:StatusRows = New-Object System.Collections.Generic.List[object]
$mode = if ($CheckOnly) { "CheckOnly" } else { "Prepare" }

Write-Host "IronDev local bootstrap"
Write-Host "Mode: $mode"
Write-Host "Boundary: $BoundaryStatement"
Write-Host ""

Add-Status "Repo root" "Found" "IronDev.slnx present"

if (Test-CommandAvailable "dotnet") {
    $dotnetVersion = (& dotnet --version 2>$null)
    Add-Status ".NET SDK" "Found" $dotnetVersion
}
else {
    Add-Status ".NET SDK" "Missing" "Install the required SDK before restore/build"
}

if (Test-CommandAvailable "git") {
    Add-Status "Git" "Found" "repository checks available"
}
else {
    Add-Status "Git" "Missing" "ignored/untracked checks unavailable"
}

if (Test-Path $frontendPackageJson) {
    if (Test-Path $frontendNodeModules) {
        Add-Status "Frontend deps" "Present" "package manifest found"
    }
    else {
        Add-Status "Frontend deps" "Missing" "run Prepare with InstallFrontend when needed"
    }
}
else {
    Add-Status "Frontend deps" "Unavailable" "frontend package manifest missing"
}

$overrideExists = Test-Path $localOverrideTarget
$overrideIgnored = Test-GitIgnored $repoRoot $localOverrideRelativePath
$overrideTracked = Test-GitTracked $repoRoot $localOverrideRelativePath
$overrideGitDetail = if ($overrideIgnored -and -not $overrideTracked) {
    "ignored and untracked"
}
elseif ($overrideTracked) {
    "tracked; remove from shared configuration"
}
elseif (Test-GitRepository $repoRoot) {
    "not confirmed ignored"
}
else {
    "git metadata unavailable"
}

if ($CreateLocalOverride) {
    if ($overrideExists) {
        Add-Status "Local override" "AlreadyPresent" "not overwritten; $overrideGitDetail"
    }
    else {
        if (-not (Test-Path $localOverrideExample)) {
            throw "Local override example file is missing."
        }

        if (-not (Test-PlaceholderOnlyExample $localOverrideExample)) {
            throw "Local override example failed placeholder safety checks."
        }

        Copy-Item -Path $localOverrideExample -Destination $localOverrideTarget -ErrorAction Stop
        $overrideExists = $true
        $overrideIgnored = Test-GitIgnored $repoRoot $localOverrideRelativePath
        $overrideTracked = Test-GitTracked $repoRoot $localOverrideRelativePath
        $overrideGitDetail = if ($overrideIgnored -and -not $overrideTracked) {
            "ignored and untracked"
        }
        elseif ($overrideTracked) {
            "tracked; remove from shared configuration"
        }
        elseif (Test-GitRepository $repoRoot) {
            "not confirmed ignored"
        }
        else {
            "git metadata unavailable"
        }

        Add-Status "Local override" "Created" "copied placeholder example; $overrideGitDetail"
    }
}
elseif ($overrideExists) {
    Add-Status "Local override" "Present" $overrideGitDetail
}
else {
    Add-Status "Local override" "Missing" "run Prepare with CreateLocalOverride when needed"
}

if ($RestoreDotNet) {
    if (-not (Test-CommandAvailable "dotnet")) {
        throw ".NET SDK is required for RestoreDotNet."
    }

    Invoke-QuietCommand `
        -FileName "dotnet" `
        -Arguments @("restore", "IronDev.slnx") `
        -WorkingDirectory $repoRoot `
        -FailureMessage "dotnet restore failed. Re-run the command manually for detailed output."

    Add-Status ".NET restore" "Completed" "dotnet restore IronDev.slnx"
}
else {
    Add-Status ".NET restore" "NotRun" "requires Prepare with RestoreDotNet"
}

if ($InstallFrontend) {
    if (-not (Test-Path $frontendPackageJson)) {
        throw "Frontend package manifest is missing."
    }

    if (-not (Test-CommandAvailable "npm")) {
        throw "npm is required for InstallFrontend."
    }

    Invoke-QuietCommand `
        -FileName "npm" `
        -Arguments @("install", "--no-audit", "--no-fund") `
        -WorkingDirectory $frontendDirectory `
        -FailureMessage "npm install failed. Re-run the command manually inside the frontend folder for detailed output."

    Add-Status "Frontend install" "Completed" "npm install completed without starting the UI"
}
else {
    Add-Status "Frontend install" "NotRun" "requires Prepare with InstallFrontend"
}

if (Test-Path $configSummaryService) {
    Add-Status "Config summary" "Available" "J08 Core contract present; not invoked by bootstrap"
}
else {
    Add-Status "Config summary" "Unavailable" "J08 Core contract not found"
}

if ($rootSafetyCandidates | Where-Object { Test-Path $_ } | Select-Object -First 1) {
    Add-Status "Root safety" "Available" "root safety contract present; not invoked by bootstrap"
}
else {
    Add-Status "Root safety" "NotEvaluated" "J10 root safety validator not available"
}

Add-Status "SQL bootstrap" "NotRun" "J04 never creates, rebuilds, or seeds SQL"
Add-Status "Weaviate bootstrap" "NotRun" "J04 never starts or rebuilds Weaviate"
Add-Status "Next safe action" "Review" "create ignored local override, then run explicit restore/install only if needed"

Write-StatusTable
