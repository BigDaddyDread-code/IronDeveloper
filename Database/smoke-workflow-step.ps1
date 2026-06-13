param(
    [Parameter(Mandatory = $true)]
    [string]$ConnectionString
)

$ErrorActionPreference = "Stop"

Add-Type -AssemblyName System.Data

function Invoke-NonQuery {
    param(
        [System.Data.SqlClient.SqlConnection]$Connection,
        [string]$Sql
    )

    $command = $Connection.CreateCommand()
    $command.CommandText = $Sql
    $command.CommandTimeout = 60
    [void]$command.ExecuteNonQuery()
}

function Invoke-Scalar {
    param(
        [System.Data.SqlClient.SqlConnection]$Connection,
        [string]$Sql
    )

    $command = $Connection.CreateCommand()
    $command.CommandText = $Sql
    $command.CommandTimeout = 60
    return $command.ExecuteScalar()
}

$workflowRunId = [Guid]::NewGuid()
$workflowRunStepId = [Guid]::NewGuid()
$projectId = [Guid]::Parse("10000000-cccc-4444-8888-990000000001")
$correlationId = [Guid]::NewGuid()
$causationId = [Guid]::NewGuid()

$connection = [System.Data.SqlClient.SqlConnection]::new($ConnectionString)
$connection.Open()
try {
    Invoke-NonQuery -Connection $connection -Sql @"
EXEC workflow.usp_WorkflowRun_Create
    @WorkflowRunId = '$workflowRunId',
    @ProjectId = '$projectId',
    @WorkflowType = N'ManualDogfoodLoop',
    @WorkflowName = N'PR99 workflow step smoke',
    @Status = N'Created',
    @SubjectType = N'dogfood_receipt',
    @SubjectId = N'pr99-smoke',
    @SubjectSummary = N'Real DB workflow step smoke run.',
    @CorrelationId = '$correlationId',
    @CausationId = '$causationId',
    @CreatedByActorType = N'system_test_fixture',
    @CreatedByActorId = N'pr99-smoke',
    @MetadataVersion = 1,
    @MetadataJson = N'{"schema":"workflow.run.metadata.v1","smoke":true}',
    @StepsJson = N'[]',
    @EvidenceReferencesJson = N'[]',
    @GroundingReferencesJson = N'[]';
"@

    Invoke-NonQuery -Connection $connection -Sql @"
EXEC workflow.usp_WorkflowStep_Create
    @WorkflowRunStepId = '$workflowRunStepId',
    @WorkflowRunId = '$workflowRunId',
    @ProjectId = '$projectId',
    @StepKey = N'pr99-smoke-step',
    @StepName = N'PR99 smoke step',
    @StepType = N'DebugFinding',
    @Status = N'Created',
    @AgentRole = N'smoke',
    @AgentId = N'pr99-smoke',
    @SubjectType = N'workflow_step',
    @SubjectId = N'pr99-smoke-subject',
    @SafeSummary = N'Real DB workflow step smoke record.',
    @SequenceNumber = 1,
    @CorrelationId = '$correlationId',
    @CausationId = '$causationId',
    @MetadataVersion = 1,
    @MetadataJson = N'{"schema":"workflow.step.metadata.v1","smoke":true}',
    @EvidenceReferencesJson = N'[]',
    @GroundingReferencesJson = N'[]';
"@

    $count = Invoke-Scalar -Connection $connection -Sql "SELECT COUNT(1) FROM workflow.WorkflowRunStep WHERE WorkflowRunStepId = '$workflowRunStepId' AND ProjectId = '$projectId';"
    if ([int]$count -ne 1) {
        throw "Workflow step smoke record was not persisted."
    }

    Write-Output "Workflow step smoke passed: $workflowRunStepId"
}
finally {
    $connection.Dispose()
}
