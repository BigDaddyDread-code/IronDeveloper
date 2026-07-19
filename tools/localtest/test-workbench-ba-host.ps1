[CmdletBinding()]
param(
    [string]$ApiBaseUrl = "http://127.0.0.1:5230",
    [string]$Email = "bob@irondev.local",
    [string]$Password = "change-me-local-only",
    [int]$TenantId = 1,
    [int]$ProjectId = 0,
    [long]$ChatSessionId = 0,
    [string]$PreviewId = "workbench-pr02b",
    [string]$ProjectName = "PR-02B BA host manual proof",
    [string]$Message = "Help me shape a small appointment reminder product. Ask the single most useful next question.",
    [string]$ConfigPath,
    [switch]$TakeOver,
    [int]$TimeoutSeconds = 60
)

$ErrorActionPreference = "Stop"

function Invoke-IronDevJson {
    param(
        [Parameter(Mandatory = $true)][string]$Method,
        [Parameter(Mandatory = $true)][string]$Uri,
        [object]$Body,
        [string]$BearerToken
    )

    $parameters = @{
        Method = $Method
        Uri = $Uri
        ContentType = "application/json"
    }
    if ($null -ne $Body) {
        $parameters.Body = $Body | ConvertTo-Json -Depth 8 -Compress
    }
    if (-not [string]::IsNullOrWhiteSpace($BearerToken)) {
        $parameters.Headers = @{ Authorization = "Bearer $BearerToken" }
    }

    try {
        Invoke-RestMethod @parameters
    }
    catch {
        $statusCode = $null
        $responseBody = $null
        if ($null -ne $_.Exception.Response) {
            $statusCode = [int]$_.Exception.Response.StatusCode
            try {
                $stream = $_.Exception.Response.GetResponseStream()
                if ($null -ne $stream) {
                    $reader = [System.IO.StreamReader]::new($stream)
                    try {
                        $responseBody = $reader.ReadToEnd()
                    }
                    finally {
                        $reader.Dispose()
                    }
                }
            }
            catch {
                $responseBody = $null
            }
        }

        $safeDetail = if ([string]::IsNullOrWhiteSpace($responseBody)) {
            $_.Exception.Message
        }
        elseif ($responseBody.Length -gt 1024) {
            $responseBody.Substring(0, 1024)
        }
        else {
            $responseBody
        }
        throw "$Method $Uri failed with HTTP $statusCode. $safeDetail"
    }
}

function Assert-Sha256 {
    param(
        [Parameter(Mandatory = $true)][string]$Value,
        [Parameter(Mandatory = $true)][string]$Label
    )

    if ($Value -cnotmatch '^[0-9a-f]{64}$') {
        throw "$Label was not a lowercase SHA-256 value."
    }
}

function Get-BusinessAnalystPreparationProvenance {
    param(
        [Parameter(Mandatory = $true)][Guid]$AgentRunId,
        [Parameter(Mandatory = $true)][int]$AttemptNumber,
        [Parameter(Mandatory = $true)][string]$DatabaseConnectionString,
        [Parameter(Mandatory = $true)][string]$ExpectedDatabaseName
    )

    $sqlcmd = Get-Command sqlcmd -ErrorAction SilentlyContinue
    if ($null -eq $sqlcmd) {
        throw "sqlcmd is required to verify sanitized Workbench BA preparation provenance."
    }

    Add-Type -AssemblyName System.Data
    $builder = [System.Data.SqlClient.SqlConnectionStringBuilder]::new($DatabaseConnectionString)
    if ([string]::IsNullOrWhiteSpace($builder.DataSource) -or
        [string]::IsNullOrWhiteSpace($builder.InitialCatalog)) {
        throw "The LocalTest connection string must identify one server and database."
    }
    if (-not $builder.InitialCatalog.Equals($ExpectedDatabaseName, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing provenance query against '$($builder.InitialCatalog)'; expected preview database '$ExpectedDatabaseName'."
    }
    $escapedDatabaseName = $ExpectedDatabaseName.Replace("'", "''")

    $query = @"
SET NOCOUNT ON;
IF DB_NAME() <> N'$escapedDatabaseName'
    THROW 51000, N'Workbench BA proof targeted the wrong preview database.', 1;
DECLARE @AgentRunId UNIQUEIDENTIFIER = '$($AgentRunId.ToString("D"))';
DECLARE @AttemptNumber INT = $AttemptNumber;

SELECT CONCAT(
    'P|', preparation.AttemptNumber,
    '|', preparation.ActualProvider,
    '|', preparation.ActualModel,
    '|', preparation.ProviderTimeoutSeconds,
    '|', preparation.EffectiveAnalystProfileHash,
    '|', preparation.PromptHash,
    '|', preparation.ToolManifestHash,
    '|', preparation.PreparationHash)
FROM dbo.WorkbenchBusinessAnalystPreparations preparation
WHERE preparation.AgentRunId=@AgentRunId
  AND preparation.AttemptNumber=@AttemptNumber;

SELECT CONCAT(
    'T|', tool.ToolName,
    '|', tool.DefinitionVersion,
    '|', tool.PolicyVersion,
    '|', tool.Status,
    '|', tool.InputHash,
    '|', tool.OutputHash,
    '|', tool.ToolCallHash)
FROM dbo.WorkbenchBusinessAnalystToolCallAudits tool
WHERE tool.AgentRunId=@AgentRunId
  AND tool.AttemptNumber=@AttemptNumber
ORDER BY tool.ToolName;
"@

    $arguments = @(
        "-S", $builder.DataSource,
        "-d", $builder.InitialCatalog,
        "-h", "-1",
        "-w", "4096",
        "-y", "1024",
        "-b",
        "-Q", $query
    )
    if ($builder.IntegratedSecurity) {
        $arguments += "-E"
    }
    else {
        $arguments += @("-U", $builder.UserID, "-P", $builder.Password)
    }

    $raw = & $sqlcmd.Source @arguments 2>&1
    if ($LASTEXITCODE -ne 0) {
        throw "Sanitized Workbench BA provenance query failed: $($raw -join ' ')"
    }

    $safeRows = @($raw | ForEach-Object { ([string]$_).Trim() })
    $preparationRows = @($safeRows | Where-Object { $_ -like "P|*" })
    $toolRows = @($safeRows | Where-Object { $_ -like "T|*" })
    if ($preparationRows.Count -ne 1) {
        throw "Expected exactly one preparation provenance row for the run attempt; found $($preparationRows.Count)."
    }
    if ($toolRows.Count -ne 3) {
        throw "Expected exactly three snapshot-tool provenance rows; found $($toolRows.Count)."
    }

    $preparation = $preparationRows[0] -split '\|'
    if ($preparation.Count -ne 9 -or [int]$preparation[1] -ne $AttemptNumber) {
        throw "Preparation provenance did not retain the exact run attempt identity."
    }
    if ($preparation[2] -cne "alpha-smoke-deterministic" -or
        $preparation[3] -cne "workbench-business-analyst-localtest-v1" -or
        [int]$preparation[4] -ne 30) {
        throw "Preparation provenance did not retain the LocalTest provider, model, and timeout."
    }
    Assert-Sha256 -Value $preparation[5] -Label "Effective Analyst profile hash"
    Assert-Sha256 -Value $preparation[6] -Label "Prompt hash"
    Assert-Sha256 -Value $preparation[7] -Label "Tool manifest hash"
    Assert-Sha256 -Value $preparation[8] -Label "Preparation hash"

    $expectedToolNames = @(
        "workbench.bounded-trusted-conversation.read",
        "workbench.captured-understanding.read",
        "workbench.project-identity.read"
    )
    $actualToolNames = @()
    foreach ($toolRow in $toolRows) {
        $tool = $toolRow -split '\|'
        if ($tool.Count -ne 8) {
            throw "A snapshot-tool provenance row had an unexpected safe shape."
        }
        $actualToolNames += $tool[1]
        if ($tool[2] -cne "workbench-ba-readonly-v1" -or
            $tool[3] -cne "workbench-ba-readonly-v1" -or
            $tool[4] -cne "Completed") {
            throw "Snapshot tool '$($tool[1])' was not recorded as the pinned completed read-only contract."
        }
        Assert-Sha256 -Value $tool[5] -Label "$($tool[1]) input hash"
        Assert-Sha256 -Value $tool[6] -Label "$($tool[1]) output hash"
        Assert-Sha256 -Value $tool[7] -Label "$($tool[1]) call hash"
    }
    $toolNameDifference = @(Compare-Object $expectedToolNames ($actualToolNames | Sort-Object))
    if ($toolNameDifference.Count -ne 0) {
        throw "Preparation provenance did not contain the exact three read-only snapshot tools."
    }

    return [ordered]@{
        preparationHash = $preparation[8]
        promptHash = $preparation[6]
        toolManifestHash = $preparation[7]
        provider = $preparation[2]
        model = $preparation[3]
        providerTimeoutSeconds = [int]$preparation[4]
        toolCount = $toolRows.Count
        tools = $actualToolNames | Sort-Object
    }
}

$baseUrl = $ApiBaseUrl.TrimEnd('/')
$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..\..")
. (Join-Path $PSScriptRoot "localtest-seed-contract.ps1")
$seedContract = Get-LocalTestSeedContract -PreviewId $PreviewId
$PreviewId = [string]$seedContract.previewId
if ([string]::IsNullOrWhiteSpace($ConfigPath)) {
    $ConfigPath = Join-Path $repoRoot "IronDev.Api\appsettings.LocalTest.json"
}
if (-not (Test-Path $ConfigPath -PathType Leaf)) {
    throw "LocalTest config was not found at '$ConfigPath'."
}
$ConfigPath = (Resolve-Path $ConfigPath).Path
$settings = Get-Content -Path $ConfigPath -Raw | ConvertFrom-Json
$baseConnectionString = [string]$settings.ConnectionStrings.IronDeveloperDb
if ([string]::IsNullOrWhiteSpace($baseConnectionString)) {
    throw "ConnectionStrings:IronDeveloperDb is required for sanitized provenance verification."
}
Add-Type -AssemblyName System.Data
$connectionBuilder = [System.Data.SqlClient.SqlConnectionStringBuilder]::new($baseConnectionString)
$connectionBuilder["Initial Catalog"] = [string]$seedContract.database.name
$ConnectionString = $connectionBuilder.ConnectionString
$safeConfigPathForCommand = $ConfigPath.Replace("'", "''")
$followUpMode = $ProjectId -gt 0 -and $ChatSessionId -gt 0
$login = Invoke-IronDevJson -Method POST -Uri "$baseUrl/api/auth/login" -Body @{
    email = $Email
    password = $Password
}
if ([string]::IsNullOrWhiteSpace([string]$login.token)) {
    throw "LocalTest login returned no token."
}

$tenant = Invoke-IronDevJson -Method POST -Uri "$baseUrl/api/tenants/select" -BearerToken $login.token -Body @{
    tenantId = $TenantId
}
if ([string]::IsNullOrWhiteSpace([string]$tenant.token)) {
    throw "Tenant selection returned no tenant-scoped token."
}
$token = [string]$tenant.token

if ($ProjectId -le 0) {
    $entry = Invoke-IronDevJson -Method POST -Uri "$baseUrl/api/projects/start" -BearerToken $token -Body @{
        name = $ProjectName
        clientOperationId = [Guid]::NewGuid()
    }
    $ProjectId = [int]$entry.projectId
}
else {
    $entry = Invoke-IronDevJson -Method POST -Uri "$baseUrl/api/workbench/projects/$ProjectId/open" -BearerToken $token -Body @{
        clientOperationId = [Guid]::NewGuid()
        takeOver = [bool]$TakeOver
    }
}

if ($ProjectId -le 0 -or [long]$entry.workbenchSessionId -le 0 -or [long]$entry.leaseEpoch -le 0) {
    throw "Project entry did not return the exact Workbench session and lease fence."
}
if ($null -ne $entry.repositoryBinding) {
    throw "Repository-independent PR-02B proof unexpectedly returned a repository binding."
}

if ($ChatSessionId -le 0) {
    $savedSession = Invoke-IronDevJson -Method POST -Uri "$baseUrl/api/projects/$ProjectId/chat/sessions" -BearerToken $token -Body @{
        id = $null
        projectId = $ProjectId
        title = "PR-02B Business Analyst proof"
        summary = $null
        workbenchSessionId = [long]$entry.workbenchSessionId
        leaseEpoch = [long]$entry.leaseEpoch
        clientOperationId = [Guid]::NewGuid()
    }
    $ChatSessionId = [long]$savedSession
}
if ($ChatSessionId -le 0) {
    throw "Chat session creation returned no session ID."
}

$submitted = Invoke-IronDevJson -Method POST -Uri "$baseUrl/api/workbench/projects/$ProjectId/agent-runs" -BearerToken $token -Body @{
    workbenchSessionId = [long]$entry.workbenchSessionId
    leaseEpoch = [long]$entry.leaseEpoch
    clientOperationId = [Guid]::NewGuid()
    chatSessionId = $ChatSessionId
    message = $Message
}

$terminalStates = @("Completed", "NeedsInput", "Failed", "Cancelled", "Superseded", "Stale")
$deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
$run = $submitted
while ($terminalStates -notcontains [string]$run.status -and [DateTime]::UtcNow -lt $deadline) {
    Start-Sleep -Milliseconds 500
    $run = Invoke-IronDevJson -Method GET -Uri "$baseUrl/api/workbench/projects/$ProjectId/agent-runs/$($submitted.agentRunId)" -BearerToken $token
}

if ($run.status -notin @("Completed", "NeedsInput")) {
    throw "Business Analyst run ended as '$($run.status)' instead of materializing one safe response."
}

$messageResponse = Invoke-IronDevJson -Method GET -Uri "$baseUrl/api/projects/$ProjectId/chat/sessions/$ChatSessionId/messages?take=50" -BearerToken $token
# Windows PowerShell can preserve a REST JSON array as one nested pipeline object
# when it crosses a function boundary. Enumerate it once before filtering rows.
$messages = @($messageResponse | Write-Output)
$sourceUserMessageId = [long]$submitted.userMessageId
$assistant = @($messages | Where-Object {
    $_.role -eq "assistant" -and
    $null -ne $_.replyToMessageId -and
    [long]$_.replyToMessageId -eq $sourceUserMessageId
})
if ($assistant.Count -ne 1) {
    throw "Expected exactly one assistant message linked to the source user message; found $($assistant.Count)."
}

$continuityMatch = [regex]::Match(
    [string]$assistant[0].message,
    'Bounded-context continuity: prior-user-turns=(?<count>\d+)\.')
if (-not $continuityMatch.Success) {
    throw "The deterministic LocalTest response did not expose the safe bounded-context continuity marker."
}
$priorUserTurns = [int]$continuityMatch.Groups["count"].Value
if ($followUpMode -and $priorUserTurns -lt 1) {
    throw "Follow-up mode did not observe any prior trusted user turn after the host restart."
}
if (-not $followUpMode -and $priorUserTurns -ne 0) {
    throw "A new conversation unexpectedly reported prior trusted user turns."
}

$provenance = Get-BusinessAnalystPreparationProvenance `
    -AgentRunId ([Guid]$run.agentRunId) `
    -AttemptNumber ([int]$run.attemptCount) `
    -DatabaseConnectionString $ConnectionString `
    -ExpectedDatabaseName ([string]$seedContract.database.name)

[ordered]@{
    previewId = $PreviewId
    database = [string]$seedContract.database.name
    projectId = $ProjectId
    workbenchSessionId = [long]$entry.workbenchSessionId
    leaseEpoch = [long]$entry.leaseEpoch
    chatSessionId = $ChatSessionId
    agentRunId = [Guid]$run.agentRunId
    status = [string]$run.status
    attemptCount = [int]$run.attemptCount
    assistantMessageId = [long]$assistant[0].id
    assistantMessage = [string]$assistant[0].message
    priorUserTurns = $priorUserTurns
    preparationProvenance = $provenance
    repositoryBinding = $entry.repositoryBinding
    followUpCommand = ".\tools\localtest\test-workbench-ba-host.ps1 -ApiBaseUrl $baseUrl -PreviewId $PreviewId -ConfigPath '$safeConfigPathForCommand' -ProjectId $ProjectId -ChatSessionId $ChatSessionId -Message 'Continue from the durable project conversation after the host restart.'"
} | ConvertTo-Json -Depth 6
