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

function Invoke-Scalar {
    param(
        [Parameter(Mandatory = $true)][System.Data.SqlClient.SqlConnection]$Connection,
        [Parameter(Mandatory = $true)][string]$Sql,
        [hashtable]$Parameters = @{}
    )

    $command = $Connection.CreateCommand()
    $command.CommandText = $Sql
    $command.CommandTimeout = $CommandTimeoutSeconds
    foreach ($name in $Parameters.Keys) {
        $parameter = $command.Parameters.Add("@$name", [System.Data.SqlDbType]::NVarChar)
        $parameter.Value = $Parameters[$name]
    }

    try {
        return $command.ExecuteScalar()
    }
    finally {
        $command.Dispose()
    }
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

function Assert-ObjectAbsent {
    param(
        [Parameter(Mandatory = $true)][System.Data.SqlClient.SqlConnection]$Connection,
        [Parameter(Mandatory = $true)][string]$Name
    )

    $sql = "SELECT CASE WHEN OBJECT_ID(N'$Name', N'U') IS NULL THEN 1 ELSE 0 END"
    $value = [int](Invoke-Scalar -Connection $Connection -Sql $sql)
    if ($value -ne 1) {
        throw "Unexpected side-effect table exists during PR74C smoke: $Name"
    }

    Write-Host "Verified no side-effect table $Name"
}

function Invoke-ToolRequestCreate {
    param(
        [Parameter(Mandatory = $true)][System.Data.SqlClient.SqlConnection]$Connection,
        [Parameter(Mandatory = $true)][guid]$ToolRequestId,
        [Parameter(Mandatory = $true)][guid]$ProjectId,
        [Parameter(Mandatory = $true)][guid]$GovernanceEventId,
        [Parameter(Mandatory = $true)][guid]$CorrelationId,
        [Parameter(Mandatory = $true)][string]$RequestPayloadJson,
        [Parameter(Mandatory = $true)][string]$GovernanceEventPayloadJson
    )

    $command = $Connection.CreateCommand()
    $command.CommandText = "governance.usp_ToolRequest_Create"
    $command.CommandType = [System.Data.CommandType]::StoredProcedure
    $command.CommandTimeout = $CommandTimeoutSeconds

    Add-Parameter -Command $command -Name "ToolRequestId" -Type ([System.Data.SqlDbType]::UniqueIdentifier) -Value $ToolRequestId
    Add-Parameter -Command $command -Name "ProjectId" -Type ([System.Data.SqlDbType]::UniqueIdentifier) -Value $ProjectId
    Add-Parameter -Command $command -Name "GovernanceEventId" -Type ([System.Data.SqlDbType]::UniqueIdentifier) -Value $GovernanceEventId
    Add-Parameter -Command $command -Name "ToolName" -Type ([System.Data.SqlDbType]::NVarChar) -Size 160 -Value "smoke.tool_request"
    Add-Parameter -Command $command -Name "OperationName" -Type ([System.Data.SqlDbType]::NVarChar) -Size 160 -Value "create_read_list"
    Add-Parameter -Command $command -Name "RequestedByActorType" -Type ([System.Data.SqlDbType]::NVarChar) -Size 80 -Value "system_test_fixture"
    Add-Parameter -Command $command -Name "RequestedByActorId" -Type ([System.Data.SqlDbType]::NVarChar) -Size 200 -Value "pr74c-real-db-smoke"
    Add-Parameter -Command $command -Name "CorrelationId" -Type ([System.Data.SqlDbType]::UniqueIdentifier) -Value $CorrelationId
    Add-Parameter -Command $command -Name "CausationId" -Type ([System.Data.SqlDbType]::UniqueIdentifier) -Value $null
    Add-Parameter -Command $command -Name "Purpose" -Type ([System.Data.SqlDbType]::NVarChar) -Size 500 -Value "PR74C real DB smoke test"
    Add-Parameter -Command $command -Name "RequestPayloadVersion" -Type ([System.Data.SqlDbType]::Int) -Value 1
    Add-Parameter -Command $command -Name "RequestPayloadJson" -Type ([System.Data.SqlDbType]::NVarChar) -Size -1 -Value $RequestPayloadJson
    Add-Parameter -Command $command -Name "GovernanceEventPayloadJson" -Type ([System.Data.SqlDbType]::NVarChar) -Size -1 -Value $GovernanceEventPayloadJson

    try {
        $reader = $command.ExecuteReader()
        try {
            if (-not $reader.Read()) {
                throw "Tool request create procedure returned no row."
            }

            $status = [string]$reader["Status"]
            if ($status -ne "Recorded") {
                throw "Tool request create returned unexpected status '$status'."
            }
        }
        finally {
            $reader.Dispose()
        }
    }
    finally {
        $command.Dispose()
    }
}

function Assert-ToolRequestProcedureReturns {
    param(
        [Parameter(Mandatory = $true)][System.Data.SqlClient.SqlConnection]$Connection,
        [Parameter(Mandatory = $true)][string]$Procedure,
        [Parameter(Mandatory = $true)][guid]$ExpectedToolRequestId,
        [Parameter(Mandatory = $true)][hashtable]$Parameters,
        [Parameter(Mandatory = $true)][string]$Name
    )

    $command = $Connection.CreateCommand()
    $command.CommandText = $Procedure
    $command.CommandType = [System.Data.CommandType]::StoredProcedure
    $command.CommandTimeout = $CommandTimeoutSeconds

    foreach ($parameterName in $Parameters.Keys) {
        $value = $Parameters[$parameterName]
        if ($value -is [guid]) {
            Add-Parameter -Command $command -Name $parameterName -Type ([System.Data.SqlDbType]::UniqueIdentifier) -Value $value
        }
        elseif ($value -is [int]) {
            Add-Parameter -Command $command -Name $parameterName -Type ([System.Data.SqlDbType]::Int) -Value $value
        }
        else {
            Add-Parameter -Command $command -Name $parameterName -Type ([System.Data.SqlDbType]::NVarChar) -Size 200 -Value $value
        }
    }

    try {
        $reader = $command.ExecuteReader()
        try {
            $found = $false
            while ($reader.Read()) {
                if ([guid]$reader["ToolRequestId"] -eq $ExpectedToolRequestId) {
                    $found = $true
                    break
                }
            }

            if (-not $found) {
                throw "Stored procedure '$Procedure' did not return expected tool request for $Name."
            }

            Write-Host "Verified $Name"
        }
        finally {
            $reader.Dispose()
        }
    }
    finally {
        $command.Dispose()
    }
}

try {
    $sqlConnectionString = New-ConnectionString
    $connection = New-Object System.Data.SqlClient.SqlConnection $sqlConnectionString
    $connection.Open()

    try {
        Assert-Exists -Connection $connection -Name "governance.GovernanceEvent table" -Sql "SELECT CASE WHEN OBJECT_ID(N'governance.GovernanceEvent', N'U') IS NULL THEN 0 ELSE 1 END"
        Assert-Exists -Connection $connection -Name "governance.ToolRequest table" -Sql "SELECT CASE WHEN OBJECT_ID(N'governance.ToolRequest', N'U') IS NULL THEN 0 ELSE 1 END"
        Assert-Exists -Connection $connection -Name "governance.usp_ToolRequest_Create procedure" -Sql "SELECT CASE WHEN OBJECT_ID(N'governance.usp_ToolRequest_Create', N'P') IS NULL THEN 0 ELSE 1 END"
        Assert-Exists -Connection $connection -Name "governance.usp_ToolRequest_GetById procedure" -Sql "SELECT CASE WHEN OBJECT_ID(N'governance.usp_ToolRequest_GetById', N'P') IS NULL THEN 0 ELSE 1 END"
        Assert-Exists -Connection $connection -Name "governance.usp_ToolRequest_ListForProject procedure" -Sql "SELECT CASE WHEN OBJECT_ID(N'governance.usp_ToolRequest_ListForProject', N'P') IS NULL THEN 0 ELSE 1 END"
        Assert-Exists -Connection $connection -Name "governance.usp_ToolRequest_ListForCorrelation procedure" -Sql "SELECT CASE WHEN OBJECT_ID(N'governance.usp_ToolRequest_ListForCorrelation', N'P') IS NULL THEN 0 ELSE 1 END"

        $toolRequestId = [guid]::NewGuid()
        $projectId = [guid]::NewGuid()
        $governanceEventId = [guid]::NewGuid()
        $correlationId = [guid]::NewGuid()
        $createdUtc = [DateTimeOffset]::UtcNow.ToString("O")

        $requestPayload = [ordered]@{
            schema = "pr74c.tool_request_smoke.v1"
            purpose = "PR74C real DB smoke test"
            database = if ([string]::IsNullOrWhiteSpace($Database)) { "connection-string-database" } else { $Database }
            toolName = "smoke.tool_request"
            operationName = "create_read_list"
            createdUtc = $createdUtc
            boundary = [ordered]@{
                requestOnly = $true
                createsGateDecision = $false
                createsApprovalDecision = $false
                createsDogfoodReceipt = $false
                createsWorkflowState = $false
                createsA2aHandoff = $false
                mutatesSource = $false
                promotesMemory = $false
            }
        }
        $eventPayload = [ordered]@{
            schema = "tool.request.created.v1"
            toolRequestId = $toolRequestId
            toolName = "smoke.tool_request"
            operationName = "create_read_list"
            source = "pr74c-real-db-smoke"
        }

        $requestPayloadJson = $requestPayload | ConvertTo-Json -Depth 8 -Compress
        $eventPayloadJson = $eventPayload | ConvertTo-Json -Depth 8 -Compress

        Invoke-ToolRequestCreate `
            -Connection $connection `
            -ToolRequestId $toolRequestId `
            -ProjectId $projectId `
            -GovernanceEventId $governanceEventId `
            -CorrelationId $correlationId `
            -RequestPayloadJson $requestPayloadJson `
            -GovernanceEventPayloadJson $eventPayloadJson

        Assert-ToolRequestProcedureReturns `
            -Connection $connection `
            -Procedure "governance.usp_ToolRequest_GetById" `
            -ExpectedToolRequestId $toolRequestId `
            -Parameters @{ ToolRequestId = $toolRequestId } `
            -Name "tool request get by id"

        Assert-ToolRequestProcedureReturns `
            -Connection $connection `
            -Procedure "governance.usp_ToolRequest_ListForProject" `
            -ExpectedToolRequestId $toolRequestId `
            -Parameters @{ ProjectId = $projectId; Take = 25 } `
            -Name "tool request list for project"

        Assert-ToolRequestProcedureReturns `
            -Connection $connection `
            -Procedure "governance.usp_ToolRequest_ListForCorrelation" `
            -ExpectedToolRequestId $toolRequestId `
            -Parameters @{ CorrelationId = $correlationId; Take = 25 } `
            -Name "tool request list for correlation"

        Assert-Exists -Connection $connection -Name "linked tool.request.created governance event" -Sql "SELECT COUNT(*) FROM governance.GovernanceEvent WHERE EventId = '$governanceEventId' AND EventType = N'tool.request.created' AND SubjectType = N'tool_request' AND SubjectId = N'$toolRequestId'"

        $forbiddenSideEffectTables = @(
            "governance.ToolGateDecision",
            "governance.ApprovalDecision",
            "governance.PolicyDecision",
            "governance.DogfoodReceipt",
            "governance.WorkflowState",
            "governance.WorkflowStep",
            "governance.A2aHandoff",
            "governance.AgentHandoff",
            "governance.SourceApply",
            "governance.MemoryPromotion"
        )

        foreach ($table in $forbiddenSideEffectTables) {
            Assert-ObjectAbsent -Connection $connection -Name $table
        }

        $summary = [ordered]@{
            database = if ([string]::IsNullOrWhiteSpace($Database)) { "connection-string-database" } else { $Database }
            toolRequestId = $toolRequestId
            governanceEventId = $governanceEventId
            projectId = $projectId
            correlationId = $correlationId
            toolName = "smoke.tool_request"
            operationName = "create_read_list"
            requestOnly = $true
            gateDecisionCreated = $false
            approvalDecisionCreated = $false
            dogfoodReceiptCreated = $false
            workflowStateCreated = $false
            a2aHandoffCreated = $false
            sourceApplyCreated = $false
            memoryPromotionCreated = $false
        }

        Write-Host ($summary | ConvertTo-Json -Depth 4)
    }
    finally {
        $connection.Dispose()
    }

    Write-Host "PR74C real DB tool request smoke passed."
    exit 0
}
catch {
    Write-Error $_.Exception.Message
    exit 1
}
