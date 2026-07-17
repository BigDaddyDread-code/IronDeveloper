function ConvertTo-LocalTestPreviewId {
    [CmdletBinding()]
    param([string]$PreviewId = "default")

    $normalized = if ([string]::IsNullOrWhiteSpace($PreviewId)) { "default" } else { $PreviewId.Trim().ToLowerInvariant() }
    if ($normalized -notmatch '^[a-z0-9][a-z0-9-]{0,31}$') {
        throw "PreviewId must contain 1-32 lowercase letters, numbers, or hyphens and must start with a letter or number."
    }

    return $normalized
}

function Get-LocalTestSeedContract {
    [CmdletBinding()]
    param(
        [string]$Path = (Join-Path $PSScriptRoot "localtest-seed-contract.json"),
        [string]$PreviewId = "default"
    )

    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw "LocalTest seed contract was not found at '$Path'."
    }

    $contract = Get-Content -LiteralPath $Path -Raw | ConvertFrom-Json
    if ($contract.schemaVersion -ne 1 -or $contract.environment -ne "LocalTest") {
        throw "LocalTest seed contract '$Path' has an unsupported schema or environment."
    }
    if ($contract.productionEnabled -or -not $contract.resetAllowed) {
        throw "LocalTest seed contract must remain resettable and production-disabled."
    }
    if ([string]::IsNullOrWhiteSpace($contract.credentials.email) -or
        [string]::IsNullOrWhiteSpace($contract.credentials.password)) {
        throw "LocalTest seed contract credentials are incomplete."
    }
    if (@($contract.projects).Count -lt 2 -or @($contract.seededTickets).Count -eq 0 -or
        @($contract.seededRuns).Count -eq 0 -or @($contract.knownArtifacts).Count -eq 0) {
        throw "LocalTest seed contract must declare projects, tickets, runs, and known artifacts."
    }

    foreach ($collectionName in @("users", "projects", "seededTickets")) {
        $ids = @($contract.$collectionName | ForEach-Object { [long]$_.id })
        if (($ids | Where-Object { $_ -le 0 }).Count -gt 0 -or ($ids | Sort-Object -Unique).Count -ne $ids.Count) {
            throw "LocalTest seed contract '$collectionName' IDs must be positive and unique."
        }
    }

    $projectKeys = @($contract.projects | ForEach-Object { $_.key })
    foreach ($requiredKey in @("baseline", "bookseller", "setup")) {
        if ($requiredKey -notin $projectKeys) {
            throw "LocalTest seed contract is missing required project key '$requiredKey'."
        }
    }
    if (($projectKeys | Sort-Object -Unique).Count -ne $projectKeys.Count) {
        throw "LocalTest seed contract project keys must be unique."
    }
    if ($contract.credentials.email -notin @($contract.users | ForEach-Object { $_.email })) {
        throw "LocalTest seed credentials must identify a contracted user."
    }

    $projectIds = @($contract.projects | ForEach-Object { [long]$_.id })
    foreach ($ticket in $contract.seededTickets) {
        if ([long]$ticket.projectId -notin $projectIds) {
            throw "LocalTest ticket '$($ticket.id)' references an unknown project."
        }
    }
    $runIds = @($contract.seededRuns | ForEach-Object { [string]$_.runId })
    if (($runIds | Where-Object { [string]::IsNullOrWhiteSpace($_) }).Count -gt 0 -or
        ($runIds | Sort-Object -Unique).Count -ne $runIds.Count) {
        throw "LocalTest run IDs must be non-empty and unique."
    }
    foreach ($run in $contract.seededRuns) {
        if ([long]$run.projectId -notin $projectIds -or
            [long]$run.ticketId -notin @($contract.seededTickets | ForEach-Object { [long]$_.id })) {
            throw "LocalTest run '$($run.runId)' references an unknown project or ticket."
        }
    }
    $artifactKeys = @($contract.knownArtifacts | ForEach-Object { "$($_.kind):$($_.id)" })
    if (($artifactKeys | Sort-Object -Unique).Count -ne $artifactKeys.Count) {
        throw "LocalTest artifact kind/ID pairs must be unique."
    }

    $normalizedPreviewId = ConvertTo-LocalTestPreviewId -PreviewId $PreviewId
    $contract | Add-Member -NotePropertyName previewId -NotePropertyValue $normalizedPreviewId
    if ($normalizedPreviewId -ne "default") {
        $databaseSuffix = $normalizedPreviewId.Replace('-', '_')
        $contract.database.name = "IronDeveloper_Test_$databaseSuffix"
        $contract.database.requiredNamePattern = '^IronDeveloper_Test_[a-z0-9_]+$'
        $contract.paths.workspaceRoot = Join-Path ([string]$contract.paths.workspaceRoot) $normalizedPreviewId
        $contract.paths.logsRoot = Join-Path ([string]$contract.paths.logsRoot) $normalizedPreviewId
    }

    return $contract
}

function Assert-LocalTestSeedTarget {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]$Contract,
        [Parameter(Mandatory = $true)][string]$DatabaseName,
        [Parameter(Mandatory = $true)][string]$WorkspaceRoot,
        [Parameter(Mandatory = $true)][string]$LogsRoot
    )

    if ($Contract.productionEnabled -or -not $Contract.resetAllowed) {
        throw "Refusing a production-enabled or non-resettable seed contract."
    }
    if ($DatabaseName -ne $Contract.database.name -or $DatabaseName -notmatch $Contract.database.requiredNamePattern) {
        throw "Refusing LocalTest seed target '$DatabaseName'; expected '$($Contract.database.name)'."
    }
    $expectedWorkspace = [System.IO.Path]::GetFullPath([string]$Contract.paths.workspaceRoot).TrimEnd('\')
    $expectedLogs = [System.IO.Path]::GetFullPath([string]$Contract.paths.logsRoot).TrimEnd('\')
    $actualWorkspace = [System.IO.Path]::GetFullPath($WorkspaceRoot).TrimEnd('\')
    $actualLogs = [System.IO.Path]::GetFullPath($LogsRoot).TrimEnd('\')
    if (-not $actualWorkspace.Equals($expectedWorkspace, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing LocalTest workspace '$actualWorkspace'; expected '$expectedWorkspace'."
    }
    if (-not $actualLogs.Equals($expectedLogs, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing LocalTest logs root '$actualLogs'; expected '$expectedLogs'."
    }
}

function ConvertTo-LocalTestSqlLiteral {
    param([Parameter(Mandatory = $true)][string]$Value)
    return $Value.Replace("'", "''")
}

function New-LocalTestSeedValidationSql {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]$Contract,
        [Parameter(Mandatory = $true)][string]$WorkspaceRoot
    )

    $lines = [System.Collections.Generic.List[string]]::new()
    $lines.Add("SET NOCOUNT ON;")
    $database = ConvertTo-LocalTestSqlLiteral $Contract.database.name
    $lines.Add("IF DB_NAME() <> N'$database' THROW 51000, N'LocalTest seed validation targeted the wrong database.', 1;")

    $tenantName = ConvertTo-LocalTestSqlLiteral $Contract.tenant.name
    $tenantSlug = ConvertTo-LocalTestSqlLiteral $Contract.tenant.slug
    $lines.Add("IF NOT EXISTS (SELECT 1 FROM dbo.Tenants WHERE Id = $($Contract.tenant.id) AND Name = N'$tenantName' AND Slug = N'$tenantSlug') THROW 51000, N'LocalTest tenant contract mismatch.', 1;")

    foreach ($user in $Contract.users) {
        $email = ConvertTo-LocalTestSqlLiteral $user.email
        $role = ConvertTo-LocalTestSqlLiteral $user.tenantRole
        $lines.Add("IF NOT EXISTS (SELECT 1 FROM dbo.Users WHERE Id = $($user.id) AND Email = N'$email') THROW 51000, N'LocalTest user contract mismatch.', 1;")
        $lines.Add("IF NOT EXISTS (SELECT 1 FROM dbo.TenantUsers WHERE TenantId = $($Contract.tenant.id) AND UserId = $($user.id) AND Role = N'$role') THROW 51000, N'LocalTest tenant membership contract mismatch.', 1;")
    }

    foreach ($project in $Contract.projects) {
        $name = ConvertTo-LocalTestSqlLiteral $project.name
        $path = ConvertTo-LocalTestSqlLiteral (Join-Path $WorkspaceRoot $project.fixtureDirectory)
        $lines.Add("IF NOT EXISTS (SELECT 1 FROM dbo.Projects WHERE Id = $($project.id) AND TenantId = $($Contract.tenant.id) AND Name = N'$name' AND LocalPath = N'$path') THROW 51000, N'LocalTest project contract mismatch.', 1;")
    }

    foreach ($ticket in $Contract.seededTickets) {
        $title = ConvertTo-LocalTestSqlLiteral $ticket.title
        $status = ConvertTo-LocalTestSqlLiteral $ticket.status
        $lines.Add("IF NOT EXISTS (SELECT 1 FROM dbo.ProjectTickets WHERE Id = $($ticket.id) AND ProjectId = $($ticket.projectId) AND Title = N'$title' AND Status = N'$status') THROW 51000, N'LocalTest ticket contract mismatch.', 1;")
    }

    foreach ($run in $Contract.seededRuns) {
        $runId = ConvertTo-LocalTestSqlLiteral $run.runId
        $state = ConvertTo-LocalTestSqlLiteral $run.state
        $isDisposable = if ($run.isDisposable) { 1 } else { 0 }
        $lines.Add("IF NOT EXISTS (SELECT 1 FROM dbo.Runs WHERE RunId = N'$runId' AND ProjectId = $($run.projectId) AND TicketId = $($run.ticketId) AND State = N'$state' AND IsDisposable = $isDisposable) THROW 51000, N'LocalTest run contract mismatch.', 1;")
    }

    $artifactTables = @{
        ProjectChannel = @{ Table = "dbo.ProjectChannels"; IdColumn = "Id"; HasProject = $true }
        ProjectChannelMessage = @{ Table = "dbo.ProjectChannelMessages"; IdColumn = "Id"; HasProject = $true }
        ProjectChatSession = @{ Table = "dbo.ProjectChatSessions"; IdColumn = "Id"; HasProject = $true }
        ChatMessage = @{ Table = "dbo.ChatMessages"; IdColumn = "Id"; HasProject = $true }
        ProjectDocument = @{ Table = "dbo.ProjectDocuments"; IdColumn = "Id"; HasProject = $true }
        ProjectDocumentVersion = @{ Table = "dbo.ProjectDocumentVersions"; IdColumn = "Id"; HasProject = $false }
    }
    foreach ($artifact in $Contract.knownArtifacts) {
        $spec = $artifactTables[$artifact.kind]
        if ($null -eq $spec) {
            throw "Unknown LocalTest artifact kind '$($artifact.kind)'."
        }
        $projectPredicate = if ($spec.HasProject) { " AND ProjectId = $($artifact.projectId)" } else { "" }
        $lines.Add("IF NOT EXISTS (SELECT 1 FROM $($spec.Table) WHERE $($spec.IdColumn) = $($artifact.id)$projectPredicate) THROW 51000, N'LocalTest artifact contract mismatch.', 1;")
    }

    $lines.Add("PRINT 'PASS LocalTest seed contract.';")
    return $lines -join [Environment]::NewLine
}
