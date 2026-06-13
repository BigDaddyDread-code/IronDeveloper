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
    if (-not [string]::IsNullOrWhiteSpace($ConnectionString)) { return $ConnectionString }
    if ([string]::IsNullOrWhiteSpace($Server)) { throw "Server is required when ConnectionString is not supplied." }
    if ([string]::IsNullOrWhiteSpace($Database)) { throw "Database is required when ConnectionString is not supplied." }

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

    if ($Size -ne 0) { $parameter = $Command.Parameters.Add("@$Name", $Type, $Size) }
    else { $parameter = $Command.Parameters.Add("@$Name", $Type) }
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
    try { return $command.ExecuteScalar() }
    finally { $command.Dispose() }
}

function Assert-Exists {
    param(
        [Parameter(Mandatory = $true)][System.Data.SqlClient.SqlConnection]$Connection,
        [Parameter(Mandatory = $true)][string]$Name,
        [Parameter(Mandatory = $true)][string]$Sql
    )

    $value = [int](Invoke-Scalar -Connection $Connection -Sql $Sql)
    if ($value -le 0) { throw "Missing required object or invariant: $Name" }
    Write-Host "Verified $Name"
}

function Assert-NoRowsIfObjectExists {
    param(
        [Parameter(Mandatory = $true)][System.Data.SqlClient.SqlConnection]$Connection,
        [Parameter(Mandatory = $true)][string]$Name,
        [Parameter(Mandatory = $true)][string]$Sql
    )

    $exists = [int](Invoke-Scalar -Connection $Connection -Sql "SELECT CASE WHEN OBJECT_ID(N'$Name', N'U') IS NULL THEN 0 ELSE 1 END")
    if ($exists -eq 0) {
        Write-Host "Verified optional table absent $Name"
        return
    }

    $count = [int](Invoke-Scalar -Connection $Connection -Sql $Sql)
    if ($count -ne 0) { throw "Unexpected side-effect rows exist during PR98 smoke: $Name" }
    Write-Host "Verified no side-effect rows in $Name"
}

function Invoke-WorkflowRunCreate {
    param(
        [Parameter(Mandatory = $true)][System.Data.SqlClient.SqlConnection]$Connection,
        [Parameter(Mandatory = $true)][guid]$WorkflowRunId,
        [Parameter(Mandatory = $true)][guid]$ProjectId,
        [Parameter(Mandatory = $true)][guid]$CorrelationId,
        [Parameter(Mandatory = $true)][guid]$CausationId,
        [Parameter(Mandatory = $true)][guid]$GroundingEvidenceReferenceId
    )

    $stepsJson = @(
        [ordered]@{ StepKey = "plan"; StepName = "Plan review"; stepType = "Planning"; status = "Created"; AgentRole = "coordinator"; AgentId = "workflow-smoke"; SubjectType = "dogfood_receipt"; SubjectId = "receipt-pr98"; SafeSummary = "Records the planned evidence review step."; MetadataVersion = 1; MetadataJson = "{`"schema`":`"workflow.step.metadata.v1`"}" },
        [ordered]@{ StepKey = "review"; StepName = "Human review support"; stepType = "HumanDecisionSupport"; status = "ReadyForReview"; AgentRole = "reviewer"; AgentId = "workflow-smoke"; SubjectType = "dogfood_receipt"; SubjectId = "receipt-pr98"; SafeSummary = "Records human review support evidence."; MetadataVersion = 1; MetadataJson = "{`"schema`":`"workflow.step.metadata.v1`"}" }
    ) | ConvertTo-Json -Depth 8 -Compress

    $evidenceJson = @(
        [ordered]@{ StepKey = "plan"; evidenceType = "DogfoodReceipt"; EvidenceId = "dogfood-receipt-pr98"; EvidenceLabel = "Dogfood receipt"; SafeSummary = "Receipt evidence for workflow run recording."; allowedUse = "Traceability"; GovernanceEventId = $null; AgentHandoffId = $null; ThoughtLedgerEntryId = $null; GroundingEvidenceReferenceId = $null },
        [ordered]@{ StepKey = "review"; evidenceType = "GroundingEvidenceReference"; EvidenceId = $GroundingEvidenceReferenceId.ToString(); EvidenceLabel = "Grounding reference"; SafeSummary = "Grounding evidence for human review support."; allowedUse = "Grounding"; governanceEventId = $null; agentHandoffId = $null; thoughtLedgerEntryId = $null; GroundingEvidenceReferenceId = $GroundingEvidenceReferenceId }
    ) | ConvertTo-Json -Depth 8 -Compress

    $groundingJson = @(
        [ordered]@{ stepKey = "review"; GroundingEvidenceReferenceId = $GroundingEvidenceReferenceId; claimType = "EvidenceSupport"; claimId = "claim-pr98"; safeSummary = "Grounding supports evidence review only." }
    ) | ConvertTo-Json -Depth 8 -Compress

    $command = $Connection.CreateCommand()
    $command.CommandText = "workflow.usp_WorkflowRun_Create"
    $command.CommandType = [System.Data.CommandType]::StoredProcedure
    $command.CommandTimeout = $CommandTimeoutSeconds

    Add-Parameter -Command $command -Name "WorkflowRunId" -Type ([System.Data.SqlDbType]::UniqueIdentifier) -Value $WorkflowRunId
    Add-Parameter -Command $command -Name "ProjectId" -Type ([System.Data.SqlDbType]::UniqueIdentifier) -Value $ProjectId
    Add-Parameter -Command $command -Name "WorkflowType" -Type ([System.Data.SqlDbType]::NVarChar) -Size 120 -Value "ManualDogfoodLoop"
    Add-Parameter -Command $command -Name "WorkflowName" -Type ([System.Data.SqlDbType]::NVarChar) -Size 200 -Value "PR98 smoke workflow run"
    Add-Parameter -Command $command -Name "Status" -Type ([System.Data.SqlDbType]::NVarChar) -Size 80 -Value "Created"
    Add-Parameter -Command $command -Name "SubjectType" -Type ([System.Data.SqlDbType]::NVarChar) -Size 120 -Value "dogfood_receipt"
    Add-Parameter -Command $command -Name "SubjectId" -Type ([System.Data.SqlDbType]::NVarChar) -Size 300 -Value "receipt-pr98"
    Add-Parameter -Command $command -Name "SubjectSummary" -Type ([System.Data.SqlDbType]::NVarChar) -Size 1000 -Value "Workflow run record for evidence review."
    Add-Parameter -Command $command -Name "CorrelationId" -Type ([System.Data.SqlDbType]::UniqueIdentifier) -Value $CorrelationId
    Add-Parameter -Command $command -Name "CausationId" -Type ([System.Data.SqlDbType]::UniqueIdentifier) -Value $CausationId
    Add-Parameter -Command $command -Name "CreatedByActorType" -Type ([System.Data.SqlDbType]::NVarChar) -Size 80 -Value "system_test_fixture"
    Add-Parameter -Command $command -Name "CreatedByActorId" -Type ([System.Data.SqlDbType]::NVarChar) -Size 200 -Value "pr98-real-db-smoke"
    Add-Parameter -Command $command -Name "MetadataVersion" -Type ([System.Data.SqlDbType]::Int) -Value 1
    Add-Parameter -Command $command -Name "MetadataJson" -Type ([System.Data.SqlDbType]::NVarChar) -Size -1 -Value "{`"schema`":`"workflow.run.metadata.v1`",`"recordsEvidenceOnly`":true}"
    Add-Parameter -Command $command -Name "StepsJson" -Type ([System.Data.SqlDbType]::NVarChar) -Size -1 -Value $stepsJson
    Add-Parameter -Command $command -Name "EvidenceReferencesJson" -Type ([System.Data.SqlDbType]::NVarChar) -Size -1 -Value $evidenceJson
    Add-Parameter -Command $command -Name "GroundingReferencesJson" -Type ([System.Data.SqlDbType]::NVarChar) -Size -1 -Value $groundingJson
    Add-Parameter -Command $command -Name "CreatedUtc" -Type ([System.Data.SqlDbType]::DateTimeOffset) -Value ([DateTimeOffset]::UtcNow)

    try {
        $reader = $command.ExecuteReader()
        try {
            if (-not $reader.Read()) { throw "Workflow run create procedure returned no row." }
            if ([guid]$reader["WorkflowRunId"] -ne $WorkflowRunId) { throw "Workflow run create returned an unexpected run ID." }
        }
        finally { $reader.Dispose() }
    }
    finally { $command.Dispose() }
}

function Assert-WorkflowRunProcedureReturns {
    param(
        [Parameter(Mandatory = $true)][System.Data.SqlClient.SqlConnection]$Connection,
        [Parameter(Mandatory = $true)][string]$Procedure,
        [Parameter(Mandatory = $true)][guid]$ExpectedWorkflowRunId,
        [Parameter(Mandatory = $true)][hashtable]$Parameters,
        [Parameter(Mandatory = $true)][string]$Name
    )

    $command = $Connection.CreateCommand()
    $command.CommandText = $Procedure
    $command.CommandType = [System.Data.CommandType]::StoredProcedure
    $command.CommandTimeout = $CommandTimeoutSeconds
    foreach ($parameterName in $Parameters.Keys) {
        $value = $Parameters[$parameterName]
        if ($value -is [guid]) { Add-Parameter -Command $command -Name $parameterName -Type ([System.Data.SqlDbType]::UniqueIdentifier) -Value $value }
        elseif ($value -is [int]) { Add-Parameter -Command $command -Name $parameterName -Type ([System.Data.SqlDbType]::Int) -Value $value }
        else { Add-Parameter -Command $command -Name $parameterName -Type ([System.Data.SqlDbType]::NVarChar) -Size 300 -Value $value }
    }

    try {
        $reader = $command.ExecuteReader()
        try {
            $found = $false
            while ($reader.Read()) {
                if ([guid]$reader["WorkflowRunId"] -eq $ExpectedWorkflowRunId) { $found = $true; break }
            }
            if (-not $found) { throw "Stored procedure '$Procedure' did not return expected workflow run for $Name." }
            Write-Host "Verified $Name"
        }
        finally { $reader.Dispose() }
    }
    finally { $command.Dispose() }
}

try {
    $connection = New-Object System.Data.SqlClient.SqlConnection (New-ConnectionString)
    $connection.Open()
    try {
        Assert-Exists -Connection $connection -Name "workflow.WorkflowRun table" -Sql "SELECT CASE WHEN OBJECT_ID(N'workflow.WorkflowRun', N'U') IS NULL THEN 0 ELSE 1 END"
        Assert-Exists -Connection $connection -Name "workflow.usp_WorkflowRun_Create procedure" -Sql "SELECT CASE WHEN OBJECT_ID(N'workflow.usp_WorkflowRun_Create', N'P') IS NULL THEN 0 ELSE 1 END"
        Assert-Exists -Connection $connection -Name "workflow.usp_WorkflowRun_Get procedure" -Sql "SELECT CASE WHEN OBJECT_ID(N'workflow.usp_WorkflowRun_Get', N'P') IS NULL THEN 0 ELSE 1 END"
        Assert-Exists -Connection $connection -Name "workflow.usp_WorkflowRun_ListByCorrelation procedure" -Sql "SELECT CASE WHEN OBJECT_ID(N'workflow.usp_WorkflowRun_ListByCorrelation', N'P') IS NULL THEN 0 ELSE 1 END"
        Assert-Exists -Connection $connection -Name "workflow.usp_WorkflowRun_ListBySubject procedure" -Sql "SELECT CASE WHEN OBJECT_ID(N'workflow.usp_WorkflowRun_ListBySubject', N'P') IS NULL THEN 0 ELSE 1 END"

        $workflowRunId = [guid]::NewGuid()
        $projectId = [guid]::NewGuid()
        $correlationId = [guid]::NewGuid()
        $causationId = [guid]::NewGuid()
        $groundingId = [guid]::NewGuid()

        Invoke-WorkflowRunCreate -Connection $connection -WorkflowRunId $workflowRunId -ProjectId $projectId -CorrelationId $correlationId -CausationId $causationId -GroundingEvidenceReferenceId $groundingId
        Assert-WorkflowRunProcedureReturns -Connection $connection -Procedure "workflow.usp_WorkflowRun_Get" -ExpectedWorkflowRunId $workflowRunId -Parameters @{ ProjectId = $projectId; WorkflowRunId = $workflowRunId } -Name "workflow run get"
        Assert-WorkflowRunProcedureReturns -Connection $connection -Procedure "workflow.usp_WorkflowRun_ListByCorrelation" -ExpectedWorkflowRunId $workflowRunId -Parameters @{ ProjectId = $projectId; CorrelationId = $correlationId; Take = 25 } -Name "workflow run list by correlation"
        Assert-WorkflowRunProcedureReturns -Connection $connection -Procedure "workflow.usp_WorkflowRun_ListBySubject" -ExpectedWorkflowRunId $workflowRunId -Parameters @{ ProjectId = $projectId; SubjectType = "dogfood_receipt"; SubjectId = "receipt-pr98"; Take = 25 } -Name "workflow run list by subject"

        Assert-NoRowsIfObjectExists -Connection $connection -Name "governance.ApprovalDecision" -Sql "SELECT COUNT(*) FROM governance.ApprovalDecision WHERE ProjectId = '$projectId'"
        Assert-NoRowsIfObjectExists -Connection $connection -Name "governance.PolicyDecisionEvent" -Sql "SELECT COUNT(*) FROM governance.PolicyDecisionEvent WHERE ProjectId = '$projectId'"
        Assert-NoRowsIfObjectExists -Connection $connection -Name "governance.DogfoodReceipt" -Sql "SELECT COUNT(*) FROM governance.DogfoodReceipt WHERE ProjectId = '$projectId'"
        Assert-NoRowsIfObjectExists -Connection $connection -Name "governance.ToolGateDecision" -Sql "SELECT COUNT(*) FROM governance.ToolGateDecision WHERE ProjectId = '$projectId'"
        Assert-NoRowsIfObjectExists -Connection $connection -Name "governance.ToolRequest" -Sql "SELECT COUNT(*) FROM governance.ToolRequest WHERE ProjectId = '$projectId'"
        Assert-NoRowsIfObjectExists -Connection $connection -Name "a2a.AgentHandoff" -Sql "SELECT COUNT(*) FROM a2a.AgentHandoff WHERE ProjectId = '$projectId'"
        Assert-NoRowsIfObjectExists -Connection $connection -Name "agent.CollectiveMemoryItem" -Sql "SELECT COUNT(*) FROM agent.CollectiveMemoryItem"

        $summary = [ordered]@{
            workflowRunId = $workflowRunId
            projectId = $projectId
            correlationId = $correlationId
            workflowRunRecorded = $true
            workflowStarted = $false
            workflowContinued = $false
            toolExecuted = $false
            approvalGranted = $false
            policySatisfied = $false
            sourceApplied = $false
            memoryPromoted = $false
            authorityTransferred = $false
        }
        Write-Host ($summary | ConvertTo-Json -Depth 4)
    }
    finally { $connection.Dispose() }

    Write-Host "PR98 real DB workflow run smoke passed."
    exit 0
}
catch {
    Write-Error $_.Exception.Message
    exit 1
}