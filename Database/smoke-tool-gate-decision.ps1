[CmdletBinding()]
param(
    [string]$Server,
    [string]$Database,
    [string]$ConnectionString,
    [switch]$TrustServerCertificate,
    [int]$CommandTimeoutSeconds = 120
)

$ErrorActionPreference = "Stop"

function New-ConnectionString {
    if (-not [string]::IsNullOrWhiteSpace($ConnectionString)) {
        return $ConnectionString
    }

    if ([string]::IsNullOrWhiteSpace($Server)) {
        throw "Server is required when ConnectionString is not supplied."
    }

    if ([string]::IsNullOrWhiteSpace($Database)) {
        throw "Database is required when ConnectionString is not supplied."
    }

    $builder = New-Object System.Data.SqlClient.SqlConnectionStringBuilder
    $builder["Data Source"] = $Server
    $builder["Initial Catalog"] = $Database
    $builder["Integrated Security"] = $true
    $builder["Encrypt"] = $true
    $builder["TrustServerCertificate"] = [bool]$TrustServerCertificate
    return $builder.ConnectionString
}

function Add-Parameter {
    param(
        [Parameter(Mandatory = $true)][System.Data.SqlClient.SqlCommand]$Command,
        [Parameter(Mandatory = $true)][string]$Name,
        [Parameter(Mandatory = $true)][System.Data.SqlDbType]$Type,
        [object]$Value,
        [int]$Size = 0
    )

    if ($Size -ne 0) {
        $parameter = $Command.Parameters.Add("@$Name", $Type, $Size)
    }
    else {
        $parameter = $Command.Parameters.Add("@$Name", $Type)
    }

    $parameter.Value = if ($null -eq $Value) { [DBNull]::Value } else { $Value }
}

function Invoke-Scalar {
    param(
        [Parameter(Mandatory = $true)][System.Data.SqlClient.SqlConnection]$Connection,
        [Parameter(Mandatory = $true)][string]$Sql
    )

    $command = $Connection.CreateCommand()
    $command.CommandText = $Sql
    $command.CommandTimeout = $CommandTimeoutSeconds
    try {
        return $command.ExecuteScalar()
    }
    finally {
        $command.Dispose()
    }
}

function Assert-Exists {
    param(
        [Parameter(Mandatory = $true)][System.Data.SqlClient.SqlConnection]$Connection,
        [Parameter(Mandatory = $true)][string]$Name,
        [Parameter(Mandatory = $true)][string]$Sql
    )

    $value = [int](Invoke-Scalar -Connection $Connection -Sql $Sql)
    if ($value -le 0) {
        throw "Missing required object or invariant: $Name"
    }

    Write-Host "Verified $Name"
}

function Invoke-ToolRequestCreate {
    param(
        [Parameter(Mandatory = $true)][System.Data.SqlClient.SqlConnection]$Connection,
        [Parameter(Mandatory = $true)][guid]$ToolRequestId,
        [Parameter(Mandatory = $true)][guid]$ProjectId,
        [Parameter(Mandatory = $true)][guid]$GovernanceEventId,
        [Parameter(Mandatory = $true)][guid]$CorrelationId
    )

    $command = $Connection.CreateCommand()
    $command.CommandText = "governance.usp_ToolRequest_Create"
    $command.CommandType = [System.Data.CommandType]::StoredProcedure
    $command.CommandTimeout = $CommandTimeoutSeconds

    Add-Parameter -Command $command -Name "ToolRequestId" -Type ([System.Data.SqlDbType]::UniqueIdentifier) -Value $ToolRequestId
    Add-Parameter -Command $command -Name "ProjectId" -Type ([System.Data.SqlDbType]::UniqueIdentifier) -Value $ProjectId
    Add-Parameter -Command $command -Name "GovernanceEventId" -Type ([System.Data.SqlDbType]::UniqueIdentifier) -Value $GovernanceEventId
    Add-Parameter -Command $command -Name "ToolName" -Type ([System.Data.SqlDbType]::NVarChar) -Size 160 -Value "smoke.tool_gate"
    Add-Parameter -Command $command -Name "OperationName" -Type ([System.Data.SqlDbType]::NVarChar) -Size 160 -Value "record_gate_decision"
    Add-Parameter -Command $command -Name "RequestedByActorType" -Type ([System.Data.SqlDbType]::NVarChar) -Size 80 -Value "system_test_fixture"
    Add-Parameter -Command $command -Name "RequestedByActorId" -Type ([System.Data.SqlDbType]::NVarChar) -Size 200 -Value "pr75-real-db-smoke"
    Add-Parameter -Command $command -Name "CorrelationId" -Type ([System.Data.SqlDbType]::UniqueIdentifier) -Value $CorrelationId
    Add-Parameter -Command $command -Name "CausationId" -Type ([System.Data.SqlDbType]::UniqueIdentifier) -Value $null
    Add-Parameter -Command $command -Name "Purpose" -Type ([System.Data.SqlDbType]::NVarChar) -Size 500 -Value "PR75 real DB tool gate decision smoke test"
    Add-Parameter -Command $command -Name "RequestPayloadVersion" -Type ([System.Data.SqlDbType]::Int) -Value 1
    Add-Parameter -Command $command -Name "RequestPayloadJson" -Type ([System.Data.SqlDbType]::NVarChar) -Size -1 -Value '{"schemaVersion":1,"purpose":"PR75 smoke"}'
    Add-Parameter -Command $command -Name "GovernanceEventPayloadJson" -Type ([System.Data.SqlDbType]::NVarChar) -Size -1 -Value '{"schemaVersion":1,"smoke":"tool-request"}'

    try { [void]$command.ExecuteNonQuery() } finally { $command.Dispose() }
}

function Invoke-ToolGateDecisionRecord {
    param(
        [Parameter(Mandatory = $true)][System.Data.SqlClient.SqlConnection]$Connection,
        [Parameter(Mandatory = $true)][guid]$ToolGateDecisionId,
        [Parameter(Mandatory = $true)][guid]$ProjectId,
        [Parameter(Mandatory = $true)][guid]$ToolRequestId,
        [Parameter(Mandatory = $true)][guid]$GovernanceEventId,
        [Parameter(Mandatory = $true)][guid]$CorrelationId
    )

    $command = $Connection.CreateCommand()
    $command.CommandText = "governance.usp_ToolGateDecision_Record"
    $command.CommandType = [System.Data.CommandType]::StoredProcedure
    $command.CommandTimeout = $CommandTimeoutSeconds

    Add-Parameter -Command $command -Name "ToolGateDecisionId" -Type ([System.Data.SqlDbType]::UniqueIdentifier) -Value $ToolGateDecisionId
    Add-Parameter -Command $command -Name "ProjectId" -Type ([System.Data.SqlDbType]::UniqueIdentifier) -Value $ProjectId
    Add-Parameter -Command $command -Name "ToolRequestId" -Type ([System.Data.SqlDbType]::UniqueIdentifier) -Value $ToolRequestId
    Add-Parameter -Command $command -Name "GovernanceEventId" -Type ([System.Data.SqlDbType]::UniqueIdentifier) -Value $GovernanceEventId
    Add-Parameter -Command $command -Name "CorrelationId" -Type ([System.Data.SqlDbType]::UniqueIdentifier) -Value $CorrelationId
    Add-Parameter -Command $command -Name "CausationId" -Type ([System.Data.SqlDbType]::UniqueIdentifier) -Value $null
    Add-Parameter -Command $command -Name "Decision" -Type ([System.Data.SqlDbType]::NVarChar) -Size 40 -Value "Blocked"
    Add-Parameter -Command $command -Name "GateName" -Type ([System.Data.SqlDbType]::NVarChar) -Size 160 -Value "tool-request-gate"
    Add-Parameter -Command $command -Name "GateVersion" -Type ([System.Data.SqlDbType]::Int) -Value 1
    Add-Parameter -Command $command -Name "ActorType" -Type ([System.Data.SqlDbType]::NVarChar) -Size 80 -Value "system_test_fixture"
    Add-Parameter -Command $command -Name "ActorId" -Type ([System.Data.SqlDbType]::NVarChar) -Size 200 -Value "pr75-real-db-smoke"
    Add-Parameter -Command $command -Name "ReasonCode" -Type ([System.Data.SqlDbType]::NVarChar) -Size 160 -Value "SMOKE_BLOCKED"
    Add-Parameter -Command $command -Name "EvidenceVersion" -Type ([System.Data.SqlDbType]::Int) -Value 1
    Add-Parameter -Command $command -Name "EvidenceJson" -Type ([System.Data.SqlDbType]::NVarChar) -Size -1 -Value '{"schemaVersion":1,"smoke":"tool-gate-decision"}'
    Add-Parameter -Command $command -Name "GovernanceEventPayloadJson" -Type ([System.Data.SqlDbType]::NVarChar) -Size -1 -Value '{"schemaVersion":1,"smoke":"tool-gate-event"}'

    try { [void]$command.ExecuteNonQuery() } finally { $command.Dispose() }
}

try {
    $sqlConnectionString = New-ConnectionString
    $connection = New-Object System.Data.SqlClient.SqlConnection $sqlConnectionString
    $connection.Open()

    try {
        Assert-Exists -Connection $connection -Name "governance.ToolRequest table" -Sql "SELECT CASE WHEN OBJECT_ID(N'governance.ToolRequest', N'U') IS NULL THEN 0 ELSE 1 END"
        Assert-Exists -Connection $connection -Name "governance.ToolGateDecision table" -Sql "SELECT CASE WHEN OBJECT_ID(N'governance.ToolGateDecision', N'U') IS NULL THEN 0 ELSE 1 END"
        Assert-Exists -Connection $connection -Name "governance.usp_ToolGateDecision_Record procedure" -Sql "SELECT CASE WHEN OBJECT_ID(N'governance.usp_ToolGateDecision_Record', N'P') IS NULL THEN 0 ELSE 1 END"
        Assert-Exists -Connection $connection -Name "ToolGateDecision no approval grant constraint" -Sql "SELECT COUNT(*) FROM sys.check_constraints WHERE name = N'CK_ToolGateDecision_NoApprovalGrant'"
        Assert-Exists -Connection $connection -Name "ToolGateDecision no execution grant constraint" -Sql "SELECT COUNT(*) FROM sys.check_constraints WHERE name = N'CK_ToolGateDecision_NoExecutionGrant'"

        $projectId = [guid]::NewGuid()
        $correlationId = [guid]::NewGuid()
        $toolRequestId = [guid]::NewGuid()
        $toolRequestEventId = [guid]::NewGuid()
        $toolGateDecisionId = [guid]::NewGuid()
        $toolGateEventId = [guid]::NewGuid()

        Invoke-ToolRequestCreate -Connection $connection -ToolRequestId $toolRequestId -ProjectId $projectId -GovernanceEventId $toolRequestEventId -CorrelationId $correlationId
        Invoke-ToolGateDecisionRecord -Connection $connection -ToolGateDecisionId $toolGateDecisionId -ProjectId $projectId -ToolRequestId $toolRequestId -GovernanceEventId $toolGateEventId -CorrelationId $correlationId

        Assert-Exists -Connection $connection -Name "recorded tool gate decision" -Sql "SELECT COUNT(*) FROM governance.ToolGateDecision WHERE ToolGateDecisionId = '$toolGateDecisionId' AND Decision = N'Blocked' AND ToolRequestId = '$toolRequestId'"
        Assert-Exists -Connection $connection -Name "linked tool.gate.decision.recorded governance event" -Sql "SELECT COUNT(*) FROM governance.GovernanceEvent WHERE EventId = '$toolGateEventId' AND EventType = N'tool.gate.decision.recorded' AND SubjectType = N'tool_gate_decision' AND SubjectId = N'$toolGateDecisionId'"
        Assert-Exists -Connection $connection -Name "tool request list returns gate decision" -Sql "SELECT COUNT(*) FROM governance.ToolGateDecision WHERE ProjectId = '$projectId' AND ToolRequestId = '$toolRequestId'"
        Assert-Exists -Connection $connection -Name "correlation list returns gate decision" -Sql "SELECT COUNT(*) FROM governance.ToolGateDecision WHERE ProjectId = '$projectId' AND CorrelationId = '$correlationId'"

        $summary = [ordered]@{
            database = if ([string]::IsNullOrWhiteSpace($Database)) { "connection-string-database" } else { $Database }
            toolRequestId = $toolRequestId
            toolGateDecisionId = $toolGateDecisionId
            projectId = $projectId
            correlationId = $correlationId
            decision = "Blocked"
            durableGateDecisionRecorded = $true
            gateDecisionIsApproval = $false
            gatePassIsHumanApproval = $false
            executionPermissionGranted = $false
            toolExecuted = $false
            sourceApplied = $false
            memoryPromoted = $false
        }

        Write-Host ($summary | ConvertTo-Json -Depth 4)
    }
    finally {
        $connection.Dispose()
    }

    Write-Host "PR75 real DB tool gate decision smoke passed."
    exit 0
}
catch {
    Write-Error $_.Exception.Message
    exit 1
}
