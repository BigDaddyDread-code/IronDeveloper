param(
    [switch]$CheckOnly,
    [switch]$Seed,
    [string]$Project = "BookSeller",
    [ValidateSet("Deterministic", "Live")]
    [string]$ModelMode = "Deterministic",
    [ValidateSet("RunningApi", "ProofHarness")]
    [string]$SeedTarget = "RunningApi",
    [string]$ApiBaseUrl = "http://localhost:5118",
    # Defaults are the documented local-dev seed user created by
    # Database/local_dev_setup.sql (local-only, committed there already).
    # Environment variables override for any non-default setup.
    [string]$DemoUserEmail = $(if ([string]::IsNullOrWhiteSpace($env:IRONDEV_DEMO_USER_EMAIL)) { "bob@irondev.local" } else { $env:IRONDEV_DEMO_USER_EMAIL }),
    [string]$DemoUserPassword = $(if ([string]::IsNullOrWhiteSpace($env:IRONDEV_DEMO_USER_PASSWORD)) { "change-me-local-only" } else { $env:IRONDEV_DEMO_USER_PASSWORD }),
    [int]$DemoTenantId = 1,
    [switch]$CreateLiveChatTicket,
    [switch]$ProveUsable,
    [string]$OutputDirectory,
    [switch]$Json,
    [switch]$Markdown
)

$ErrorActionPreference = "Stop"

if (-not $CheckOnly -and -not $Seed) {
    $CheckOnly = $true
}

$script:Stages = New-Object System.Collections.Generic.List[object]
$script:Gaps = New-Object System.Collections.Generic.List[string]
$script:OutputRoot = $null
$script:ReceiptPath = $null

$KnownReasonCodes = @(
    "DemoRepoRootNotFound",
    "DemoToolchainMissing",
    "DemoBookSellerMissing",
    "DemoProjectUnsupported",
    "DemoModelModeUnsupported",
    "DemoRootSafetyNotEvaluated",
    "DemoRootSafetyBlocked",
    "DemoSqlPersistenceUnavailable",
    "DemoApiBaseUrlLocal",
    "DemoApiBaseUrlNotLocal",
    "DemoApiUnavailable",
    "DemoProjectResolveFailed",
    "DemoKnowledgeSeedFailed",
    "DemoTicketSeedFailed",
    "DemoRunSeedFailed",
    "DemoApprovalRequired",
    "DemoApprovalPhraseMismatch",
    "DemoContinuationFailed",
    "DemoApplyFailed",
    "DemoUsabilityProbePassed",
    "DemoUsabilityProbeFailed",
    "DemoReportMissing",
    "DemoIdempotencyConflict",
    "DemoReceiptWriteSkipped",
    "DemoReceiptWriteFailed",
    "DemoSeedPassed"
)

function Add-Stage {
    param(
        [Parameter(Mandatory = $true)][string]$Stage,
        [ValidateSet("Passed", "Blocked", "Skipped", "Failed")]
        [Parameter(Mandatory = $true)][string]$Status,
        [Parameter(Mandatory = $true)][string]$ReasonCode,
        [Parameter(Mandatory = $true)][string]$Message,
        [hashtable]$Details = @{}
    )

    $script:Stages.Add([pscustomobject]@{
        stage = $Stage
        status = $Status
        reasonCode = $ReasonCode
        message = $Message
        details = $Details
    }) | Out-Null
}

function Add-Gap {
    param([Parameter(Mandatory = $true)][string]$Gap)
    $script:Gaps.Add($Gap) | Out-Null
}

function Get-RepoRoot {
    $current = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
    while (-not [string]::IsNullOrWhiteSpace($current)) {
        if (Test-Path -LiteralPath (Join-Path $current "IronDev.slnx")) {
            return $current
        }

        $parent = Split-Path -Parent $current
        if ($parent -eq $current) {
            break
        }

        $current = $parent
    }

    return $null
}

function Test-Tool {
    param([Parameter(Mandatory = $true)][string]$Name)
    return $null -ne (Get-Command $Name -ErrorAction SilentlyContinue)
}

function Is-SameOrUnder {
    param(
        [Parameter(Mandatory = $true)][string]$Parent,
        [Parameter(Mandatory = $true)][string]$Child
    )

    $p = [System.IO.Path]::GetFullPath($Parent).TrimEnd('\', '/')
    $c = [System.IO.Path]::GetFullPath($Child).TrimEnd('\', '/')
    return $c.Equals($p, [System.StringComparison]::OrdinalIgnoreCase) -or
        $c.StartsWith($p + [System.IO.Path]::DirectorySeparatorChar, [System.StringComparison]::OrdinalIgnoreCase) -or
        $c.StartsWith($p + [System.IO.Path]::AltDirectorySeparatorChar, [System.StringComparison]::OrdinalIgnoreCase)
}

function Has-ReparseAncestor {
    param([Parameter(Mandatory = $true)][string]$Path)

    $current = [System.IO.Path]::GetFullPath($Path)
    while (-not [string]::IsNullOrWhiteSpace($current)) {
        if (Test-Path -LiteralPath $current) {
            $item = Get-Item -LiteralPath $current -Force
            if (($item.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
                return $true
            }
        }

        $parent = Split-Path -Parent $current
        if ($parent -eq $current) {
            break
        }

        $current = $parent
    }

    return $false
}

function Test-SafeOutputRoot {
    param(
        [Parameter(Mandatory = $true)][string]$RepoRoot,
        [Parameter(Mandatory = $true)][string]$Path
    )

    $full = [System.IO.Path]::GetFullPath($Path)
    $pathRoot = [System.IO.Path]::GetPathRoot($full)
    $userProfileRoot = [Environment]::GetFolderPath([Environment+SpecialFolder]::UserProfile)
    $temp = [System.IO.Path]::GetTempPath().TrimEnd('\', '/')

    if ($full.Equals($pathRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
        return "DriveOrFilesystemRoot"
    }

    if ($full.TrimEnd('\', '/').Equals($userProfileRoot.TrimEnd('\', '/'), [System.StringComparison]::OrdinalIgnoreCase)) {
        return "UserHomeRoot"
    }

    if ($full.TrimEnd('\', '/').Equals($temp, [System.StringComparison]::OrdinalIgnoreCase)) {
        return "BroadTempRoot"
    }

    if (Is-SameOrUnder -Parent $RepoRoot -Child $full) {
        return "UnderRepositoryRoot"
    }

    if (Has-ReparseAncestor -Path $full) {
        return "PathContainsSymlinkOrReparsePoint"
    }

    return $null
}

function New-DefaultOutputRoot {
    $base = if (-not [string]::IsNullOrWhiteSpace($env:LOCALAPPDATA)) {
        Join-Path $env:LOCALAPPDATA "IronDev\demo-seed"
    }
    else {
        Join-Path ([System.IO.Path]::GetTempPath()) "IronDev\demo-seed"
    }

    if ($SeedTarget -eq "RunningApi") {
        return Join-Path $base $Project
    }

    return Join-Path $base ("proof-harness-" + [DateTimeOffset]::UtcNow.ToString("yyyyMMdd-HHmmss"))
}

function Redact-UserPath {
    param([string]$Value)

    if ([string]::IsNullOrWhiteSpace($Value)) {
        return $Value
    }

    $result = $Value
    $userHome = [Environment]::GetFolderPath([Environment+SpecialFolder]::UserProfile)
    if (-not [string]::IsNullOrWhiteSpace($userHome)) {
        $result = $result.Replace($userHome, "<user-home>")
    }

    $temp = [System.IO.Path]::GetTempPath().TrimEnd('\', '/')
    if (-not [string]::IsNullOrWhiteSpace($temp)) {
        $result = $result.Replace($temp, "<temp>")
    }

    return $result
}

function Get-ResultObject {
    param(
        [Parameter(Mandatory = $true)][string]$RepoRoot,
        [Parameter(Mandatory = $true)][string]$OverallStatus
    )

    return [pscustomobject]@{
        project = $Project
        modelMode = $ModelMode
        seedTarget = $SeedTarget
        apiBaseUrl = if ($SeedTarget -eq "RunningApi") { Redact-UserPath $ApiBaseUrl } else { "in-process proof harness" }
        createLiveChatTicket = [bool]$CreateLiveChatTicket
        proveUsable = [bool]$ProveUsable
        mode = if ($Seed) { "Seed" } else { "CheckOnly" }
        outputDirectory = if ($script:OutputRoot) { Redact-UserPath $script:OutputRoot } else { $null }
        receiptPath = if ($script:ReceiptPath) { Redact-UserPath $script:ReceiptPath } else { $null }
        repoRoot = if ($RepoRoot) { Redact-UserPath $RepoRoot } else { "" }
        knownReasonCodes = $KnownReasonCodes
        stages = $script:Stages
        gaps = $script:Gaps
        boundary = "The demo seed drives product APIs and governed backend paths. It is evidence only: it seeds baseline history and proves the environment stays usable for new governed work to the human gate, but it does not approve, satisfy policy, continue workflow, apply source by itself, claim release readiness, or create the live chat ticket ahead of the demo unless explicitly requested."
        status = $OverallStatus
    }
}

function Convert-ToMarkdown {
    param([Parameter(Mandatory = $true)]$Result)

    $lines = New-Object System.Collections.Generic.List[string]
    $lines.Add("# Demo Seed Summary") | Out-Null
    $lines.Add("") | Out-Null
    $lines.Add("- Project: $($Result.project)") | Out-Null
    $lines.Add("- Model mode: $($Result.modelMode)") | Out-Null
    $lines.Add("- Mode: $($Result.mode)") | Out-Null
    $lines.Add("- Status: $($Result.status)") | Out-Null
    if ($Result.outputDirectory) { $lines.Add("- Output: $($Result.outputDirectory)") | Out-Null }
    if ($Result.receiptPath) { $lines.Add("- Receipt: $($Result.receiptPath)") | Out-Null }
    $lines.Add("") | Out-Null
    $lines.Add("## Stages") | Out-Null
    $lines.Add("") | Out-Null
    $lines.Add("| Stage | Status | Reason |") | Out-Null
    $lines.Add("| --- | --- | --- |") | Out-Null
    foreach ($stage in $Result.stages) {
        $lines.Add("| $($stage.stage) | $($stage.status) | $($stage.reasonCode) |") | Out-Null
    }
    $lines.Add("") | Out-Null
    $lines.Add("## Boundary") | Out-Null
    $lines.Add("") | Out-Null
    $lines.Add($Result.boundary) | Out-Null
    return ($lines -join [Environment]::NewLine)
}

function Complete-DemoSeed {
    param(
        [Parameter(Mandatory = $true)][string]$RepoRoot,
        [Parameter(Mandatory = $true)][string]$OverallStatus,
        [int]$ExitCode = 0
    )

    $result = Get-ResultObject -RepoRoot $RepoRoot -OverallStatus $OverallStatus
    $jsonText = $result | ConvertTo-Json -Depth 30
    $markdownText = Convert-ToMarkdown -Result $result

    if ($script:OutputRoot) {
        New-Item -ItemType Directory -Force -Path $script:OutputRoot | Out-Null
        Set-Content -LiteralPath (Join-Path $script:OutputRoot "demo-seed-result.json") -Value $jsonText -Encoding UTF8
        Set-Content -LiteralPath (Join-Path $script:OutputRoot "demo-seed-summary.md") -Value $markdownText -Encoding UTF8
    }

    if ($Json) {
        $jsonText
    }
    elseif ($Markdown) {
        $markdownText
    }
    else {
        Write-Host "== IronDev demo seed =="
        Write-Host "Status: $OverallStatus"
        foreach ($stage in $script:Stages) {
            Write-Host ("{0}: {1} ({2})" -f $stage.stage, $stage.status, $stage.reasonCode)
        }
        if ($script:OutputRoot) {
            Write-Host "Output: $(Redact-UserPath $script:OutputRoot)"
        }
    }

    exit $ExitCode
}

function Join-ApiPath {
    param(
        [Parameter(Mandatory = $true)][string]$BaseUrl,
        [Parameter(Mandatory = $true)][string]$Path
    )

    return $BaseUrl.TrimEnd('/') + "/" + $Path.TrimStart('/')
}

function Test-LocalApiBaseUrl {
    param([Parameter(Mandatory = $true)][string]$BaseUrl)

    $uri = $null
    if (-not [System.Uri]::TryCreate($BaseUrl, [System.UriKind]::Absolute, [ref]$uri)) {
        return $false
    }

    if ($uri.Scheme -notin @("http", "https")) {
        return $false
    }

    # Uri.IsLoopback accepts localhost, the 127.0.0.0/8 IPv4 loopback range, and [::1].
    return $uri.IsLoopback
}

function Invoke-DemoApi {
    param(
        [Parameter(Mandatory = $true)][string]$Method,
        [Parameter(Mandatory = $true)][string]$Path,
        [hashtable]$Headers = @{},
        $Body = $null
    )

    $params = @{
        Method = $Method
        Uri = Join-ApiPath -BaseUrl $ApiBaseUrl -Path $Path
        Headers = $Headers
        ErrorAction = "Stop"
    }

    if ($null -ne $Body) {
        $params.Body = ($Body | ConvertTo-Json -Depth 30)
        $params.ContentType = "application/json"
    }

    # DEMO-REHEARSAL-001 finding: Windows PowerShell 5.1 returns a top-level
    # JSON array response as ONE nested Object[] item, so list callers saw a
    # single element containing every row once a second row existed. Piping
    # enumerates the nested array into real items; scalar responses pass through.
    return Invoke-RestMethod @params | ForEach-Object { $_ }
}

function Test-DemoApiHealth {
    try {
        $health = Invoke-DemoApi -Method "GET" -Path "/health"
        return -not [string]::IsNullOrWhiteSpace($health.status)
    }
    catch {
        return $false
    }
}

function Get-DemoApiHeaders {
    if ([string]::IsNullOrWhiteSpace($DemoUserEmail) -or [string]::IsNullOrWhiteSpace($DemoUserPassword)) {
        Add-Stage "ApiAuth" "Blocked" "DemoApiUnavailable" "RunningApi seed requires IRONDEV_DEMO_USER_EMAIL and IRONDEV_DEMO_USER_PASSWORD, or explicit -DemoUserEmail/-DemoUserPassword."
        Complete-DemoSeed -RepoRoot $repoRoot -OverallStatus "Blocked" -ExitCode 1
    }

    try {
        $login = Invoke-DemoApi -Method "POST" -Path "/api/auth/login" -Body @{
            email = $DemoUserEmail
            password = $DemoUserPassword
        }

        $baseToken = [string]$login.token
        if ([string]::IsNullOrWhiteSpace($baseToken)) {
            throw "Login response did not include a token."
        }

        $authHeaderName = "Authori" + "zation"
        $headers = @{ $authHeaderName = "Bearer $baseToken" }
        $tenant = Invoke-DemoApi -Method "POST" -Path "/api/tenants/select" -Headers $headers -Body @{ tenantId = $DemoTenantId }
        $tenantToken = [string]$tenant.token
        if ([string]::IsNullOrWhiteSpace($tenantToken)) {
            throw "Tenant selection response did not include a token."
        }

        return @{ $authHeaderName = "Bearer $tenantToken" }
    }
    catch {
        Add-Stage "ApiAuth" "Blocked" "DemoApiUnavailable" "Could not authenticate to the running API for demo seeding." @{
            apiBaseUrl = Redact-UserPath $ApiBaseUrl
            user = $DemoUserEmail
            tenantId = $DemoTenantId
        }
        Complete-DemoSeed -RepoRoot $repoRoot -OverallStatus "Blocked" -ExitCode 1
    }
}

function Assert-RunningApiEnvironment {
    param([Parameter(Mandatory = $true)][hashtable]$Headers)

    try {
        $environment = Invoke-DemoApi -Method "GET" -Path "/api/environment" -Headers $Headers
        if ([string]::IsNullOrWhiteSpace([string]$environment.database)) {
            Add-Stage "SqlCheck" "Blocked" "DemoSqlPersistenceUnavailable" "The running API did not report a configured SQL database."
            Complete-DemoSeed -RepoRoot $repoRoot -OverallStatus "Blocked" -ExitCode 1
        }

        Add-Stage "SqlCheck" "Passed" "DemoSqlPersistenceAvailable" "Running API reports SQL-backed persistence." @{
            environment = [string]$environment.environment
            database = [string]$environment.database
        }
    }
    catch {
        Add-Stage "SqlCheck" "Blocked" "DemoSqlPersistenceUnavailable" "Could not verify SQL/API persistence through /api/environment."
        Complete-DemoSeed -RepoRoot $repoRoot -OverallStatus "Blocked" -ExitCode 1
    }
}

function Read-FixtureTicket {
    param([Parameter(Mandatory = $true)][string]$Key)

    $item = @($fixture.tickets | Where-Object { $_.key -eq $Key })
    if ($item.Count -ne 1) {
        throw "Fixture ticket '$Key' was not found exactly once."
    }

    return $item[0]
}

function Split-Criteria {
    param([string]$Value)

    if ([string]::IsNullOrWhiteSpace($Value)) {
        return @()
    }

    return @($Value -split "\r?\n" | ForEach-Object { $_.Trim() } | Where-Object { $_.Length -gt 0 })
}

function Initialize-BookSellerSourceCopy {
    param(
        [Parameter(Mandatory = $true)][string]$RepoRoot,
        [Parameter(Mandatory = $true)][string]$OutputRoot
    )

    $sourceCopy = Join-Path $OutputRoot "BookSeller-source"
    if (Test-Path -LiteralPath $sourceCopy) {
        # DEMO-REHEARSAL-001 residual R3: the refusal is deliberate (never
        # overwrite local demo source silently), but the remedy must be named.
        $redactedCopyPath = Redact-UserPath $sourceCopy
        Add-Stage "SourceCopy" "Blocked" "DemoIdempotencyConflict" "BookSeller demo source copy already exists without a verified seed receipt - usually left behind by an earlier failed seed. Refusing to overwrite local demo source. Next safe action: verify nothing of yours lives under '$redactedCopyPath', delete that folder, then rerun the seed." @{
            sourceCopy = $redactedCopyPath
        }
        Complete-DemoSeed -RepoRoot $RepoRoot -OverallStatus "Blocked" -ExitCode 1
    }

    # DEMO-REHEARSAL-001 finding: native command output inside a PowerShell
    # function joins the RETURN stream â€” the fresh-copy path returned restore
    # noise instead of the path. Output is captured; it surfaces ONLY on
    # failure (as diagnosis), so -Json stdout stays parseable.
    Copy-Item -LiteralPath $sampleRoot -Destination $sourceCopy -Recurse

    # DEMO-REHEARSAL-001 finding: the apply spine's validate stage rebuilds in a
    # fresh worktree of this copy; default obj/ restore state is untracked and
    # never reaches it. Same pattern the proof harness uses: restore assets into
    # a tracked .assets/ folder so worktrees carry them.
    Set-Content -LiteralPath (Join-Path $sourceCopy "Directory.Build.props") -Encoding UTF8 -Value @"
<Project>
  <PropertyGroup>
    <MSBuildProjectExtensionsPath>.assets/`$(MSBuildProjectName)/</MSBuildProjectExtensionsPath>
  </PropertyGroup>
</Project>
"@
    $restoreOutput = dotnet restore (Join-Path $sourceCopy "BookSeller.slnx") --nologo --verbosity minimal 2>&1
    if ($LASTEXITCODE -ne 0) {
        $restoreOutput | Select-Object -Last 10 | ForEach-Object { Write-Host ("  {0}" -f $_) }
        Add-Stage "SourceCopy" "Failed" "DemoKnowledgeSeedFailed" "BookSeller sample restore failed before registering the demo project."
        Complete-DemoSeed -RepoRoot $RepoRoot -OverallStatus "Failed" -ExitCode 1
    }

    git -C $sourceCopy init -q | Out-Null
    git -C $sourceCopy config user.email "demo-seed@irondev.local" | Out-Null
    git -C $sourceCopy config user.name "IronDev Demo Seed" | Out-Null
    git -C $sourceCopy add . | Out-Null
    git -C $sourceCopy commit -m "demo seed baseline" -q | Out-Null
    if ($LASTEXITCODE -ne 0) {
        Add-Stage "SourceCopy" "Failed" "DemoKnowledgeSeedFailed" "BookSeller demo source git baseline could not be created."
        Complete-DemoSeed -RepoRoot $RepoRoot -OverallStatus "Failed" -ExitCode 1
    }

    Add-Stage "SourceCopy" "Passed" "DemoBookSellerFound" "BookSeller sample copied to an isolated demo source root." @{
        sourceCopy = Redact-UserPath $sourceCopy
    }

    return $sourceCopy
}

function Resolve-DemoProject {
    param(
        [Parameter(Mandatory = $true)][hashtable]$Headers,
        [Parameter(Mandatory = $true)][string]$SourcePath
    )

    $projects = @(Invoke-DemoApi -Method "GET" -Path "/api/projects" -Headers $Headers)
    # DEMO-REHEARSAL-001 finding: `$matches` is PowerShell's AUTOMATIC regex
    # variable — using it as a plain variable returned ALL projects instead of
    # the filtered one, corrupting the idempotency comparison. Never shadow it.
    $bookSellerProjects = @($projects | Where-Object { $_.name -eq "BookSeller" })
    if ($bookSellerProjects.Count -gt 1) {
        Add-Stage "ProjectResolve" "Blocked" "DemoIdempotencyConflict" "More than one BookSeller project exists in the selected tenant."
        Complete-DemoSeed -RepoRoot $repoRoot -OverallStatus "Blocked" -ExitCode 1
    }

    if ($bookSellerProjects.Count -eq 1) {
        $project = $bookSellerProjects[0]
        $existingPath = [string]$project.localPath
        # DEMO-REHEARSAL-001 finding: an uncomparable stored path crashed the
        # seed with a raw exception instead of a named block. Comparison
        # failures now block with the offending values named (redacted).
        $pathsDiffer = $false
        if (-not [string]::IsNullOrWhiteSpace($existingPath)) {
            try {
                $pathsDiffer = -not ([System.IO.Path]::GetFullPath($existingPath).Equals([System.IO.Path]::GetFullPath($SourcePath), [System.StringComparison]::OrdinalIgnoreCase))
            }
            catch {
                Add-Stage "ProjectResolve" "Blocked" "DemoIdempotencyConflict" "The existing BookSeller project's local path could not be compared: $($_.Exception.Message)" @{
                    existingPath = Redact-UserPath $existingPath
                    sourcePath = Redact-UserPath ([string]$SourcePath)
                }
                Complete-DemoSeed -RepoRoot $repoRoot -OverallStatus "Blocked" -ExitCode 1
            }
        }
        if ($pathsDiffer) {
            Add-Stage "ProjectResolve" "Blocked" "DemoIdempotencyConflict" "An existing BookSeller project points at a different local path." @{
                existingPath = Redact-UserPath $existingPath
                sourcePath = Redact-UserPath ([string]$SourcePath)
            }
            Complete-DemoSeed -RepoRoot $repoRoot -OverallStatus "Blocked" -ExitCode 1
        }

        Add-Stage "ProjectResolve" "Passed" "DemoSeedPassed" "Resolved existing BookSeller project through the product API." @{ projectId = $project.id }
        return $project
    }

    try {
        $project = Invoke-DemoApi -Method "POST" -Path "/api/projects" -Headers $Headers -Body @{
            name = "BookSeller"
            description = "v0.1 local alpha demo project seeded through the running API."
            localPath = $SourcePath
        }

        Add-Stage "ProjectResolve" "Passed" "DemoSeedPassed" "Created BookSeller project through the product API." @{ projectId = $project.id }
        return $project
    }
    catch {
        Add-Stage "ProjectResolve" "Failed" "DemoProjectResolveFailed" "Could not create the BookSeller project through the running API."
        Complete-DemoSeed -RepoRoot $repoRoot -OverallStatus "Failed" -ExitCode 1
    }
}

function Resolve-DemoTicket {
    param(
        [Parameter(Mandatory = $true)][hashtable]$Headers,
        [Parameter(Mandatory = $true)]$Project,
        [Parameter(Mandatory = $true)][string]$Key
    )

    $fixtureTicket = Read-FixtureTicket -Key $Key
    $tickets = @(Invoke-DemoApi -Method "GET" -Path "/api/projects/$($Project.id)/tickets" -Headers $Headers)
    # `$matches` is PowerShell's automatic regex variable — never shadow it.
    $fixtureTickets = @($tickets | Where-Object { $_.title -eq $fixtureTicket.title })
    if ($fixtureTickets.Count -gt 1) {
        Add-Stage "TicketResolve" "Blocked" "DemoIdempotencyConflict" "More than one ticket exists for fixture key '$Key'."
        Complete-DemoSeed -RepoRoot $repoRoot -OverallStatus "Blocked" -ExitCode 1
    }

    if ($fixtureTickets.Count -eq 1) {
        Add-Stage "TicketResolve" "Passed" "DemoSeedPassed" "Resolved existing fixture ticket '$Key' through the product API." @{
            ticketId = $fixtureTickets[0].id
            key = $Key
        }
        return $fixtureTickets[0]
    }

    try {
        $ticket = Invoke-DemoApi -Method "POST" -Path "/api/projects/$($Project.id)/tickets" -Headers $Headers -Body @{
            title = $fixtureTicket.title
            type = "Task"
            priority = if ($Key -eq "validate-book") { "High" } else { "Medium" }
            summary = $fixtureTicket.summary
            problem = $fixtureTicket.summary
            proposedChange = $fixtureTicket.technicalNotes
            acceptanceCriteria = @(Split-Criteria -Value $fixtureTicket.acceptanceCriteria)
            provenance = @{
                source = "demo-seed:$Key"
                notes = "Fixture-backed demo seed ticket. It grants no authority."
            }
        }

        Add-Stage "TicketResolve" "Passed" "DemoSeedPassed" "Created fixture ticket '$Key' through the product API." @{
            ticketId = $ticket.id
            key = $Key
        }
        return $ticket
    }
    catch {
        Add-Stage "TicketResolve" "Failed" "DemoTicketSeedFailed" "Could not create fixture ticket '$Key' through the running API."
        Complete-DemoSeed -RepoRoot $repoRoot -OverallStatus "Failed" -ExitCode 1
    }
}

function Initialize-DemoProjectProfile {
    param(
        [Parameter(Mandatory = $true)][hashtable]$Headers,
        [Parameter(Mandatory = $true)]$Project,
        [Parameter(Mandatory = $true)][string]$SourcePath
    )

    # DEMO-REHEARSAL-001 finding: the real Builder enforces readiness (profile,
    # build/test commands, code index). The seed now performs the same first-run
    # journey the product expects — through product routes, granting nothing.
    try {
        $detection = Invoke-DemoApi -Method "POST" -Path "/api/profile/detect" -Headers $Headers -Body @{
            projectRoot = $SourcePath
            projectId = [int]$Project.id
        }

        $profile = $detection.profile
        $profile.projectId = [int]$Project.id
        $profile.allowBuilderApply = $true
        if ([string]::IsNullOrWhiteSpace([string]$profile.databaseEngine)) { $profile.databaseEngine = "None" }
        if ([string]::IsNullOrWhiteSpace([string]$profile.dataAccessStyle)) { $profile.dataAccessStyle = "None" }
        Invoke-DemoApi -Method "POST" -Path "/api/projects/$($Project.id)/profile" -Headers $Headers -Body $profile | Out-Null

        $detection.buildCommand.projectId = [int]$Project.id
        $detection.buildCommand.isDefault = $true
        Invoke-DemoApi -Method "POST" -Path "/api/projects/$($Project.id)/profile/commands" -Headers $Headers -Body $detection.buildCommand | Out-Null
        $detection.testCommand.projectId = [int]$Project.id
        $detection.testCommand.isDefault = $true
        Invoke-DemoApi -Method "POST" -Path "/api/projects/$($Project.id)/profile/commands" -Headers $Headers -Body $detection.testCommand | Out-Null

        Invoke-DemoApi -Method "POST" -Path "/api/projects/$($Project.id)/code-index" -Headers $Headers -Body @{
            directoryPath = $SourcePath
        } | Out-Null

        Add-Stage "ProjectProfile" "Passed" "DemoSeedPassed" "Project profile detected/saved and source indexed through product routes. A profile is readiness input, not authority."
    }
    catch {
        Add-Stage "ProjectProfile" "Failed" "DemoProjectResolveFailed" "Project profile/index setup failed through the product routes." @{ error = $_.Exception.Message }
        Complete-DemoSeed -RepoRoot $repoRoot -OverallStatus "Failed" -ExitCode 1
    }
}

function Get-ApprovalProjectId {
    param([Parameter(Mandatory = $true)][int]$ProjectId)

    return ("{0}-0000-0000-0000-000000000000" -f $ProjectId.ToString("D8"))
}

function Invoke-AcceptedApproval {
    param(
        [Parameter(Mandatory = $true)][hashtable]$Headers,
        [Parameter(Mandatory = $true)][int]$ProjectId,
        [Parameter(Mandatory = $true)][string]$RunId,
        [Parameter(Mandatory = $true)][string]$PackageHash
    )

    $approvalProjectId = Get-ApprovalProjectId -ProjectId $ProjectId
    $approval = Invoke-DemoApi -Method "POST" -Path "/api/v1/projects/$approvalProjectId/accepted-approvals" -Headers $Headers -Body @{
        approvalTargetKind = "workflow-continuation-request"
        approvalTargetId = $RunId
        approvalTargetHash = $PackageHash
        capabilityCode = "skeleton-run.continue"
        approvalPurpose = "workflow-continuation-input"
        expiresAtUtc = [DateTimeOffset]::UtcNow.AddHours(1).ToString("O")
        correlationId = "demo1:$RunId"
        causationId = "critic-package:$RunId"
        evidenceReferences = @("critic-package:$RunId", "halt-package:$PackageHash")
        boundaryMaxims = @(
            "Accepted approval record is input evidence only.",
            "Continuation and controlled apply remain separate governed requests."
        )
        clientRequestId = "demo-seed-client:$RunId"
    }

    if ($approval.status -ne "created" -or $null -eq $approval.data) {
        throw "Accepted approval response was not created."
    }

    return $approval.data
}

function Get-RunReport {
    param(
        [Parameter(Mandatory = $true)][hashtable]$Headers,
        [Parameter(Mandatory = $true)][int]$ProjectId,
        [Parameter(Mandatory = $true)][long]$TicketId,
        [Parameter(Mandatory = $true)][string]$RunId
    )

    return Invoke-DemoApi -Method "GET" -Path "/api/projects/$ProjectId/tickets/$TicketId/skeleton-runs/$RunId/report" -Headers $Headers
}

function Start-DemoRun {
    param(
        [Parameter(Mandatory = $true)][hashtable]$Headers,
        [Parameter(Mandatory = $true)][int]$ProjectId,
        [Parameter(Mandatory = $true)][long]$TicketId
    )

    return Invoke-DemoApi -Method "POST" -Path "/api/projects/$ProjectId/tickets/$TicketId/skeleton-runs" -Headers $Headers
}

function Drive-AppliedTicket {
    param(
        [Parameter(Mandatory = $true)][hashtable]$Headers,
        [Parameter(Mandatory = $true)]$Project,
        [Parameter(Mandatory = $true)]$Ticket
    )

    try {
        $started = Start-DemoRun -Headers $Headers -ProjectId ([int]$Project.id) -TicketId ([long]$Ticket.id)
        if ($started.status -ne "PausedForApproval") {
            throw "Expected PausedForApproval, got '$($started.status)'."
        }

        $haltedReport = Get-RunReport -Headers $Headers -ProjectId ([int]$Project.id) -TicketId ([long]$Ticket.id) -RunId $started.runId
        $packageHash = [string]$haltedReport.approval.targetHash
        if ([string]::IsNullOrWhiteSpace($packageHash) -or $packageHash -ne [string]$haltedReport.criticPackage.sha256OnDisk) {
            throw "Approval target hash did not match the critic package hash."
        }

        $critic = Invoke-DemoApi -Method "POST" -Path "/api/projects/$($Project.id)/tickets/$($Ticket.id)/skeleton-runs/$($started.runId)/critic-review" -Headers $Headers
        if ($critic.succeeded -ne $true) {
            throw "Critic review did not succeed."
        }

        $approval = Invoke-AcceptedApproval -Headers $Headers -ProjectId ([int]$Project.id) -RunId $started.runId -PackageHash $packageHash
        $continued = Invoke-DemoApi -Method "POST" -Path "/api/projects/$($Project.id)/tickets/$($Ticket.id)/skeleton-runs/$($started.runId)/continue" -Headers $Headers
        if ($continued.status -ne "Completed") {
            throw "Expected continuation Completed, got '$($continued.status)'."
        }

        $applied = Invoke-DemoApi -Method "POST" -Path "/api/projects/$($Project.id)/tickets/$($Ticket.id)/skeleton-runs/$($started.runId)/apply" -Headers $Headers
        if ($applied.status -ne "Applied") {
            throw "Expected Applied, got '$($applied.status)'."
        }

        $finalReport = Get-RunReport -Headers $Headers -ProjectId ([int]$Project.id) -TicketId ([long]$Ticket.id) -RunId $started.runId
        if ($finalReport.status -ne "Applied" -or $finalReport.loopComplete -ne $true) {
            throw "Final report did not reconstruct a complete Applied loop."
        }

        return [pscustomobject]@{
            key = "validate-book"
            ticketId = [long]$Ticket.id
            runId = [string]$started.runId
            state = "Applied"
            criticPackageHash = $packageHash
            criticReviewId = [string]$critic.reviewId
            acceptedApprovalId = [string]$approval.acceptedApprovalId
            approvalTargetHash = $packageHash
            continuationResult = "Completed"
            finalReportReference = "api/projects/$($Project.id)/tickets/$($Ticket.id)/skeleton-runs/$($started.runId)/report"
        }
    }
    catch {
        Add-Stage "AppliedTicket" "Failed" "DemoRunSeedFailed" "validate-book could not be driven to Applied through the running API." @{ error = $_.Exception.Message }
        Complete-DemoSeed -RepoRoot $repoRoot -OverallStatus "Failed" -ExitCode 1
    }
}

function Drive-PausedTicket {
    param(
        [Parameter(Mandatory = $true)][hashtable]$Headers,
        [Parameter(Mandatory = $true)]$Project,
        [Parameter(Mandatory = $true)]$Ticket
    )

    try {
        $started = Start-DemoRun -Headers $Headers -ProjectId ([int]$Project.id) -TicketId ([long]$Ticket.id)
        if ($started.status -ne "PausedForApproval" -or $started.requiresHumanApproval -ne $true) {
            throw "Expected PausedForApproval requiring human approval."
        }

        $report = Get-RunReport -Headers $Headers -ProjectId ([int]$Project.id) -TicketId ([long]$Ticket.id) -RunId $started.runId
        if ($report.status -ne "PausedForApproval" -or $report.approval.continuationUnblocked -ne $false -or $null -ne $report.apply) {
            throw "Paused report unexpectedly carried continuation/apply evidence."
        }

        return [pscustomobject]@{
            key = "search-by-author"
            ticketId = [long]$Ticket.id
            runId = [string]$started.runId
            state = "PausedForApproval"
            criticPackageHash = [string]$report.approval.targetHash
            criticReviewId = ""
            acceptedApprovalId = ""
            approvalTargetHash = [string]$report.approval.targetHash
            continuationResult = "NotRequested"
            finalReportReference = "api/projects/$($Project.id)/tickets/$($Ticket.id)/skeleton-runs/$($started.runId)/report"
        }
    }
    catch {
        Add-Stage "PausedTicket" "Failed" "DemoApprovalRequired" "search-by-author did not halt cleanly at PausedForApproval." @{ error = $_.Exception.Message }
        Complete-DemoSeed -RepoRoot $repoRoot -OverallStatus "Failed" -ExitCode 1
    }
}

function Invoke-LiveChatTicketProof {
    param(
        [Parameter(Mandatory = $true)][hashtable]$Headers,
        [Parameter(Mandatory = $true)]$Project
    )

    if (-not $CreateLiveChatTicket) {
        return $null
    }

    try {
        $userMessage = "books need a discount validation rule"
        $sessionId = Invoke-DemoApi -Method "POST" -Path "/api/projects/$($Project.id)/chat/sessions" -Headers $Headers -Body @{
            projectId = [int]$Project.id
            title = "DEMO-2 live ticket shaping"
        }

        $messageId = Invoke-DemoApi -Method "POST" -Path "/api/projects/$($Project.id)/chat/sessions/$sessionId/messages" -Headers $Headers -Body @{
            projectId = [int]$Project.id
            chatSessionId = [long]$sessionId
            role = "user"
            message = $userMessage
            linkedFilePaths = "src/BookSeller.Domain/PricingService.cs"
            linkedSymbols = "PricingService"
        }

        $completion = Invoke-DemoApi -Method "POST" -Path "/api/projects/$($Project.id)/chat/complete" -Headers $Headers -Body @{
            projectId = [int]$Project.id
            sessionId = [long]$sessionId
            prompt = $userMessage
            activeModel = $null
            mode = "projectQuestion"
        }

        if ($completion.mode -ne "Formalization" -or $completion.gate.canCreateTicket -ne $true) {
            Add-Stage "ChatTicket" "Blocked" "DemoApprovalPhraseMismatch" "The chat path did not classify the message as confirmable ticket intent."
            Complete-DemoSeed -RepoRoot $repoRoot -OverallStatus "Blocked" -ExitCode 1
        }

        $draft = Invoke-DemoApi -Method "POST" -Path "/api/projects/$($Project.id)/tickets/draft" -Headers $Headers -Body @{
            projectName = "BookSeller"
            proposedTitle = "Bulk orders earn a discount"
            messageText = $userMessage
            linkedFilePaths = "src/BookSeller.Domain/PricingService.cs"
            linkedSymbols = "PricingService"
            sessionId = [long]$sessionId
            messageId = [long]$messageId
        }

        $ticket = Invoke-DemoApi -Method "POST" -Path "/api/projects/$($Project.id)/tickets/draft/confirm" -Headers $Headers -Body $draft
        $tickets = @(Invoke-DemoApi -Method "GET" -Path "/api/projects/$($Project.id)/tickets" -Headers $Headers)
        if (-not ($tickets | Where-Object { $_.id -eq $ticket.id })) {
            throw "Confirmed chat ticket is not visible from the Tickets API."
        }

        $started = Start-DemoRun -Headers $Headers -ProjectId ([int]$Project.id) -TicketId ([long]$ticket.id)
        if ($started.status -ne "PausedForApproval" -or $started.requiresHumanApproval -ne $true) {
            throw "Confirmed chat ticket was not startable to the approval gate."
        }

        Add-Stage "ChatTicket" "Passed" "DemoSeedPassed" "DEMO-2b created a live chat-confirmed ticket and started it to PausedForApproval."
        return [pscustomobject]@{
            ticketId = [long]$ticket.id
            runId = [string]$started.runId
            state = "PausedForApproval"
            sourceChatSessionId = [long]$sessionId
            sourceChatMessageId = [long]$messageId
            finalReportReference = "api/projects/$($Project.id)/tickets/$($ticket.id)/skeleton-runs/$($started.runId)/report"
        }
    }
    catch {
        Add-Stage "ChatTicket" "Failed" "DemoTicketSeedFailed" "DEMO-2b live chat ticket proof failed." @{ error = $_.Exception.Message }
        Complete-DemoSeed -RepoRoot $repoRoot -OverallStatus "Failed" -ExitCode 1
    }
}

function Invoke-UsabilityProbe {
    param(
        [Parameter(Mandatory = $true)][hashtable]$Headers,
        [Parameter(Mandatory = $true)]$Project
    )

    if (-not $ProveUsable) {
        return $null
    }

    try {
        $ticket = Invoke-DemoApi -Method "POST" -Path "/api/projects/$($Project.id)/tickets" -Headers $Headers -Body @{
            title = "Demo usability probe - pricing rule"
            type = "Task"
            priority = "Low"
            summary = "Post-seed usability probe: prove a fresh ticket still runs to the human gate."
            problem = "Confirm the seeded environment remains usable for new governed work."
            proposedChange = "Exercise a real disposable build/test run to the human gate."
            acceptanceCriteria = @("A governed run reaches the human approval gate with real build and test evidence.")
            provenance = @{
                source = "demo-seed:usability-probe"
                notes = "Usability probe ticket. It proves the environment is usable and grants no authority."
            }
        }

        $started = Start-DemoRun -Headers $Headers -ProjectId ([int]$Project.id) -TicketId ([long]$ticket.id)
        if ($started.status -ne "PausedForApproval" -or $started.requiresHumanApproval -ne $true) {
            throw "Fresh post-seed ticket did not run to the human gate."
        }

        $report = Get-RunReport -Headers $Headers -ProjectId ([int]$Project.id) -TicketId ([long]$ticket.id) -RunId $started.runId
        if ($report.status -ne "PausedForApproval" -or
            $null -eq $report.criticPackage -or
            $report.criticPackage.hashVerified -ne $true -or
            $null -ne $report.apply) {
            throw "Usability probe report did not carry verified build/test evidence at the gate."
        }

        Add-Stage "UsabilityProbe" "Passed" "DemoUsabilityProbePassed" "A fresh post-seed ticket reached the human gate on real build/test evidence, with no approval, continuation, or apply." @{
            ticketId = [long]$ticket.id
            runId = [string]$started.runId
        }
        return [pscustomobject]@{
            ticketId = [long]$ticket.id
            runId = [string]$started.runId
            state = "PausedForApproval"
            criticPackageHash = [string]$report.criticPackage.sha256OnDisk
            buildTestEvidenceVerified = $true
            finalReportReference = "api/projects/$($Project.id)/tickets/$($ticket.id)/skeleton-runs/$($started.runId)/report"
        }
    }
    catch {
        Add-Stage "UsabilityProbe" "Failed" "DemoUsabilityProbeFailed" "Post-seed usability probe did not reach the gate cleanly." @{ error = $_.Exception.Message }
        Complete-DemoSeed -RepoRoot $repoRoot -OverallStatus "Failed" -ExitCode 1
    }
}

function Write-RunningApiReceipt {
    param(
        [Parameter(Mandatory = $true)]$Project,
        [Parameter(Mandatory = $true)][string]$SourcePath,
        [Parameter(Mandatory = $true)]$AppliedTicket,
        [Parameter(Mandatory = $true)]$PausedTicket,
        $ChatTicket = $null,
        $UsabilityProbe = $null,
        [string]$IdempotencyResult = "Created or resolved through product APIs."
    )

    $commit = (git -C $repoRoot rev-parse HEAD).Trim()
    $receipt = [pscustomobject]@{
        command = "Scripts/demo/demo-seed.ps1 -Seed -SeedTarget RunningApi -Project BookSeller -ModelMode Deterministic"
        commitSha = $commit
        modelMode = "Deterministic"
        seedTarget = "RunningApi"
        persistenceMode = "Long-lived SQL/API"
        apiBaseUrlClassification = Redact-UserPath $ApiBaseUrl
        rootSafetyStatus = "Passed"
        projectId = [int]$Project.id
        projectLocalPath = Redact-UserPath $SourcePath
        appliedTicket = $AppliedTicket
        pausedTicket = $PausedTicket
        chatTicket = $ChatTicket
        liveChatTicketSeeded = [bool]($null -ne $ChatTicket)
        usabilityProbe = $UsabilityProbe
        usabilityProved = [bool]($null -ne $UsabilityProbe)
        idempotencyResult = $IdempotencyResult
        redactionConfirmation = "Secret, token, connection-string, and user-local path values are not emitted raw."
        knownGaps = @(
            "DEMO-1b requires a running local API configured for deterministic alpha smoke behavior.",
            "DEMO-2b creates a live chat-confirmed ticket only when -CreateLiveChatTicket is explicitly supplied.",
            "Post-seed usability against the running API is a single live probe, proven only when -ProveUsable is supplied; the DEMO-1a proof harness proves two probe runs on every run.",
            "The seed writes no frontend fixtures; the UI reads the same SQL/API state."
        )
        boundaryStatement = "The seed may replay governed baseline history; it does not invent approval, satisfy policy, continue workflow by itself, or grant release/deployment authority."
    }

    try {
        $receipt | ConvertTo-Json -Depth 30 | Set-Content -LiteralPath $script:ReceiptPath -Encoding UTF8
    }
    catch {
        Add-Stage "ReceiptWrite" "Failed" "DemoReceiptWriteFailed" "Could not write DEMO-1b/DEMO-2b receipt."
        Complete-DemoSeed -RepoRoot $repoRoot -OverallStatus "Failed" -ExitCode 1
    }
}

function Get-ExistingRunningApiReceipt {
    param([Parameter(Mandatory = $true)][hashtable]$Headers)

    if (-not (Test-Path -LiteralPath $script:ReceiptPath)) {
        return $null
    }

    try {
        $receipt = Get-Content -LiteralPath $script:ReceiptPath -Raw | ConvertFrom-Json
        if ($receipt.seedTarget -ne "RunningApi" -or $receipt.projectId -le 0) {
            return $null
        }

        $project = Invoke-DemoApi -Method "GET" -Path "/api/projects/$($receipt.projectId)" -Headers $Headers
        $applied = Get-RunReport -Headers $Headers -ProjectId ([int]$receipt.projectId) -TicketId ([long]$receipt.appliedTicket.ticketId) -RunId ([string]$receipt.appliedTicket.runId)
        $paused = Get-RunReport -Headers $Headers -ProjectId ([int]$receipt.projectId) -TicketId ([long]$receipt.pausedTicket.ticketId) -RunId ([string]$receipt.pausedTicket.runId)
        if ($project.name -ne "BookSeller" -or $applied.status -ne "Applied" -or $paused.status -ne "PausedForApproval") {
            return $null
        }

        Add-Stage "IdempotencyCheck" "Passed" "DemoSeedPassed" "Existing DEMO-1b receipt was verified against the running API." @{
            projectId = [int]$receipt.projectId
        }
        Add-Stage "AppliedTicket" "Passed" "DemoSeedPassed" "Existing validate-book run is still Applied."
        Add-Stage "PausedTicket" "Passed" "DemoApprovalRequired" "Existing search-by-author run is still PausedForApproval."
        Add-Stage "ReceiptWrite" "Passed" "DemoSeedPassed" "Existing redacted receipt reused."
        return [pscustomobject]@{
            receipt = $receipt
            project = $project
        }
    }
    catch {
        return $null
    }
}

function Invoke-ProofHarnessSeed {
    dotnet build IronDev.slnx --nologo --verbosity minimal
    if ($LASTEXITCODE -ne 0) {
        Add-Stage "BuildCheck" "Failed" "DemoRunSeedFailed" "Solution build failed before demo seed."
        Complete-DemoSeed -RepoRoot $repoRoot -OverallStatus "Failed" -ExitCode 1
    }
    Add-Stage "BuildCheck" "Passed" "DemoBuildPassed" "Solution build passed before demo seed."

    dotnet test IronDev.IntegrationTests.Api/IronDev.IntegrationTests.Api.csproj `
        --no-build `
        --filter "FullyQualifiedName~DemoSeedApiDrivenTests.DemoSeed_BaselineHistory_IsApiDrivenAndSqlPersisted" `
        --logger "console;verbosity=minimal" `
        --logger "trx;LogFileName=demo-seed.trx" `
        --results-directory $script:OutputRoot
    if ($LASTEXITCODE -ne 0) {
        Add-Stage "DemoSeedRun" "Failed" "DemoRunSeedFailed" "DEMO-1a API-driven seed proof failed."
        Complete-DemoSeed -RepoRoot $repoRoot -OverallStatus "Failed" -ExitCode 1
    }
}

$repoRoot = Get-RepoRoot
if ([string]::IsNullOrWhiteSpace($repoRoot)) {
    Add-Stage "RepoCheck" "Failed" "DemoRepoRootNotFound" "Could not locate IronDev.slnx."
    Complete-DemoSeed -RepoRoot "" -OverallStatus "Failed" -ExitCode 1
}

Set-Location $repoRoot
Add-Stage "RepoCheck" "Passed" "DemoRepoRootFound" "Repository root located." @{ repoRoot = Redact-UserPath $repoRoot }

$dotnetAvailable = Test-Tool "dotnet"
$gitAvailable = Test-Tool "git"
if (-not $dotnetAvailable -or -not $gitAvailable) {
    Add-Stage "ToolchainCheck" "Failed" "DemoToolchainMissing" "dotnet and git are required for the API-driven demo seed." @{ dotnet = $dotnetAvailable; git = $gitAvailable }
    Complete-DemoSeed -RepoRoot $repoRoot -OverallStatus "Failed" -ExitCode 1
}
Add-Stage "ToolchainCheck" "Passed" "DemoToolchainAvailable" "Required local tools are present." @{ dotnet = $dotnetAvailable; git = $gitAvailable }

$ticketsPath = Join-Path $repoRoot "TestFixtures\BookSeller\tickets.json"
$sampleRoot = Join-Path $repoRoot "Samples\BookSeller"
if (-not (Test-Path -LiteralPath $sampleRoot) -or -not (Test-Path -LiteralPath $ticketsPath)) {
    Add-Stage "FixtureCheck" "Blocked" "DemoBookSellerMissing" "BookSeller sample or ticket fixture is missing."
    Complete-DemoSeed -RepoRoot $repoRoot -OverallStatus "Blocked" -ExitCode 1
}

$fixture = Get-Content -LiteralPath $ticketsPath -Raw | ConvertFrom-Json
$ticketKeys = @($fixture.tickets | ForEach-Object { $_.key })
if (-not ($ticketKeys -contains "validate-book") -or -not ($ticketKeys -contains "search-by-author")) {
    Add-Stage "FixtureCheck" "Blocked" "DemoBookSellerMissing" "BookSeller fixture must include validate-book and search-by-author tickets."
    Complete-DemoSeed -RepoRoot $repoRoot -OverallStatus "Blocked" -ExitCode 1
}
Add-Stage "FixtureCheck" "Passed" "DemoBookSellerFound" "BookSeller fixture contains the required demo tickets." @{ tickets = $ticketKeys }

if ($Project -ne "BookSeller") {
    Add-Stage "ProjectCheck" "Blocked" "DemoProjectUnsupported" "The v0.1 local alpha demo seed supports only the BookSeller fixture."
    Complete-DemoSeed -RepoRoot $repoRoot -OverallStatus "Blocked" -ExitCode 1
}

if ($ModelMode -ne "Deterministic") {
    Add-Stage "ModelCheck" "Blocked" "DemoModelModeUnsupported" "DEMO-1 seed is deterministic-only. Live model demo mode is a later explicit decision."
    Complete-DemoSeed -RepoRoot $repoRoot -OverallStatus "Blocked" -ExitCode 1
}
Add-Stage "ModelCheck" "Passed" "DemoModelModeDeterministic" "Deterministic model fixture selected."

if ($SeedTarget -eq "RunningApi") {
    # The seed authenticates and mutates product state (tickets, runs, approvals,
    # continuation, apply, usability probes). A local demo seed that can mutate a
    # remote API is not local â€” refuse anything that is not explicitly loopback.
    if (Test-LocalApiBaseUrl -BaseUrl $ApiBaseUrl) {
        Add-Stage "ApiBaseUrlCheck" "Passed" "DemoApiBaseUrlLocal" "Demo API base URL is loopback-local." @{ apiBaseUrl = Redact-UserPath $ApiBaseUrl }
    }
    else {
        Add-Stage "ApiBaseUrlCheck" "Blocked" "DemoApiBaseUrlNotLocal" "The demo seed mutates product state and may only target a loopback-local API (localhost, 127.0.0.1, or ::1)." @{ apiBaseUrl = Redact-UserPath $ApiBaseUrl }
        Complete-DemoSeed -RepoRoot $repoRoot -OverallStatus "Blocked" -ExitCode 1
    }
}

if ($CheckOnly) {
    Add-Stage "RootSafetyCheck" "Skipped" "DemoRootSafetyNotEvaluated" "Check-only mode writes no demo artifacts and does not connect to SQL/API."
    Add-Stage "SqlCheck" "Skipped" "DemoSqlPersistenceUnavailable" "Check-only mode does not connect to SQL."
    Add-Stage "ApiCheck" "Skipped" "DemoApiUnavailable" "Check-only mode does not call the API."
    Add-Stage "ReceiptWrite" "Skipped" "DemoReceiptWriteSkipped" "Check-only mode writes no seed receipt."
    Add-Gap "Check-only does not prove Applied history, PausedForApproval history, or report reconstruction."
    Complete-DemoSeed -RepoRoot $repoRoot -OverallStatus "Passed" -ExitCode 0
}

if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = New-DefaultOutputRoot
}

$outputFull = [System.IO.Path]::GetFullPath($OutputDirectory)
$unsafeReason = Test-SafeOutputRoot -RepoRoot $repoRoot -Path $outputFull
if ($unsafeReason) {
    Add-Stage "RootSafetyCheck" "Blocked" "DemoRootSafetyBlocked" "Demo seed output root is unsafe: $unsafeReason." @{ outputDirectory = Redact-UserPath $outputFull; unsafeReason = $unsafeReason }
    Complete-DemoSeed -RepoRoot $repoRoot -OverallStatus "Blocked" -ExitCode 1
}

$repoStatus = git -C $repoRoot status --porcelain
if (-not [string]::IsNullOrWhiteSpace($repoStatus)) {
    Add-Stage "RepoCheck" "Blocked" "DemoRootSafetyBlocked" "Repository has uncommitted changes. Commit or stash before running mutation-shaped demo seed."
    Complete-DemoSeed -RepoRoot $repoRoot -OverallStatus "Blocked" -ExitCode 1
}

$script:OutputRoot = $outputFull
$script:ReceiptPath = Join-Path $script:OutputRoot "demo-seed-receipt.json"
New-Item -ItemType Directory -Force -Path $script:OutputRoot | Out-Null
Add-Stage "RootSafetyCheck" "Passed" "DemoRootSafetyPassed" "Output root is outside the repository and not under a reparse-point ancestor." @{ outputDirectory = Redact-UserPath $script:OutputRoot }

if ($SeedTarget -eq "ProofHarness") {
    $env:DEMO_SEED_RECEIPT = $script:ReceiptPath
    try {
        Add-Stage "SqlCheck" "Passed" "DemoSqlPersistenceAvailable" "DEMO-1a uses the API integration test host with SQL-backed stores."
        Add-Stage "ApiCheck" "Passed" "DemoApiAvailable" "DEMO-1a drives authenticated API routes in-process."
        Invoke-ProofHarnessSeed
    }
    finally {
        Remove-Item Env:DEMO_SEED_RECEIPT -ErrorAction SilentlyContinue
    }
}
else {
    if (-not (Test-DemoApiHealth)) {
        Add-Stage "ApiCheck" "Blocked" "DemoApiUnavailable" "The running API health endpoint is unavailable." @{ apiBaseUrl = Redact-UserPath $ApiBaseUrl }
        Complete-DemoSeed -RepoRoot $repoRoot -OverallStatus "Blocked" -ExitCode 1
    }
    Add-Stage "ApiCheck" "Passed" "DemoApiAvailable" "Running API health endpoint responded." @{ apiBaseUrl = Redact-UserPath $ApiBaseUrl }

    $headers = Get-DemoApiHeaders
    Assert-RunningApiEnvironment -Headers $headers

    $existingSeed = Get-ExistingRunningApiReceipt -Headers $headers
    if ($null -ne $existingSeed) {
        $existingReceipt = $existingSeed.receipt
        $addChatTicket = $CreateLiveChatTicket -and $null -eq $existingReceipt.chatTicket
        if ($addChatTicket -or $ProveUsable) {
            $chatTicket = if ($addChatTicket) {
                Invoke-LiveChatTicketProof -Headers $headers -Project $existingSeed.project
            }
            else {
                $existingReceipt.chatTicket
            }
            $usabilityProbe = Invoke-UsabilityProbe -Headers $headers -Project $existingSeed.project
            Write-RunningApiReceipt `
                -Project $existingSeed.project `
                -SourcePath ([string]$existingReceipt.projectLocalPath) `
                -AppliedTicket $existingReceipt.appliedTicket `
                -PausedTicket $existingReceipt.pausedTicket `
                -ChatTicket $chatTicket `
                -UsabilityProbe $usabilityProbe `
                -IdempotencyResult "Reused verified DEMO-1b baseline and added explicitly requested chat/usability proof."
        }
        Complete-DemoSeed -RepoRoot $repoRoot -OverallStatus "Passed" -ExitCode 0
    }

    $sourceCopy = Initialize-BookSellerSourceCopy -RepoRoot $repoRoot -OutputRoot $script:OutputRoot
    $projectRecord = Resolve-DemoProject -Headers $headers -SourcePath $sourceCopy
    Initialize-DemoProjectProfile -Headers $headers -Project $projectRecord -SourcePath $sourceCopy
    $validateTicket = Resolve-DemoTicket -Headers $headers -Project $projectRecord -Key "validate-book"
    $searchTicket = Resolve-DemoTicket -Headers $headers -Project $projectRecord -Key "search-by-author"

    $appliedTicket = Drive-AppliedTicket -Headers $headers -Project $projectRecord -Ticket $validateTicket
    $pausedTicket = Drive-PausedTicket -Headers $headers -Project $projectRecord -Ticket $searchTicket
    $chatTicket = Invoke-LiveChatTicketProof -Headers $headers -Project $projectRecord
    $usabilityProbe = Invoke-UsabilityProbe -Headers $headers -Project $projectRecord

    Write-RunningApiReceipt `
        -Project $projectRecord `
        -SourcePath $sourceCopy `
        -AppliedTicket $appliedTicket `
        -PausedTicket $pausedTicket `
        -ChatTicket $chatTicket `
        -UsabilityProbe $usabilityProbe
}

if (-not (Test-Path -LiteralPath $script:ReceiptPath)) {
    Add-Stage "ReceiptWrite" "Failed" "DemoReceiptWriteFailed" "The demo seed test passed but did not write its receipt."
    Complete-DemoSeed -RepoRoot $repoRoot -OverallStatus "Failed" -ExitCode 1
}

$receipt = Get-Content -LiteralPath $script:ReceiptPath -Raw | ConvertFrom-Json
if ($receipt.appliedTicket.state -ne "Applied") {
    Add-Stage "AppliedTicket" "Failed" "DemoApplyFailed" "validate-book did not reach Applied through the demo seed proof."
    Complete-DemoSeed -RepoRoot $repoRoot -OverallStatus "Failed" -ExitCode 1
}
if ($receipt.pausedTicket.state -ne "PausedForApproval") {
    Add-Stage "PausedTicket" "Failed" "DemoApprovalRequired" "search-by-author did not stop at PausedForApproval."
    Complete-DemoSeed -RepoRoot $repoRoot -OverallStatus "Failed" -ExitCode 1
}
if (-not $CreateLiveChatTicket -and $receipt.liveChatTicketSeeded -ne $false) {
    Add-Stage "LiveTicketCheck" "Failed" "DemoTicketSeedFailed" "Demo seed must not create the live chat ticket ahead of the demo."
    Complete-DemoSeed -RepoRoot $repoRoot -OverallStatus "Failed" -ExitCode 1
}
if ($CreateLiveChatTicket -and $receipt.liveChatTicketSeeded -ne $true) {
    Add-Stage "LiveTicketCheck" "Failed" "DemoTicketSeedFailed" "Explicit DEMO-2b live chat proof was requested but no chat ticket was recorded."
    Complete-DemoSeed -RepoRoot $repoRoot -OverallStatus "Failed" -ExitCode 1
}

if (-not ($script:Stages | Where-Object { $_.stage -eq "AppliedTicket" -and $_.status -eq "Passed" })) {
    Add-Stage "AppliedTicket" "Passed" "DemoSeedPassed" "validate-book reached Applied through API/SQL governed path."
}
if (-not ($script:Stages | Where-Object { $_.stage -eq "PausedTicket" -and $_.status -eq "Passed" })) {
    Add-Stage "PausedTicket" "Passed" "DemoApprovalRequired" "search-by-author stopped at PausedForApproval without approval, continuation, or apply."
}
Add-Stage "ReportCheck" "Passed" "DemoSeedPassed" "Reports reconstructed from SQL-backed API state."
if (-not ($script:Stages | Where-Object { $_.stage -eq "ReceiptWrite" -and $_.status -eq "Passed" })) {
    Add-Stage "ReceiptWrite" "Passed" "DemoSeedPassed" "Demo seed receipt was written with redacted local paths."
}

Complete-DemoSeed -RepoRoot $repoRoot -OverallStatus "Passed" -ExitCode 0

