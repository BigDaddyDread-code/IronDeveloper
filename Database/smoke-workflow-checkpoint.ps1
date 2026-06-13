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
$workflowCheckpointId = [Guid]::NewGuid()
$projectId = [Guid]::Parse("10000000-dddd-4444-8888-990000000001")
$correlationId = [Guid]::NewGuid()
$causationId = [Guid]::NewGuid()
$groundingReferenceId = [Guid]::NewGuid()

$connection = [System.Data.SqlClient.SqlConnection]::new($ConnectionString)
$connection.Open()
try {
    Invoke-NonQuery -Connection $connection -Sql @"
EXEC workflow.usp_WorkflowRun_Create
    @WorkflowRunId = '$workflowRunId',
    @ProjectId = '$projectId',
    @WorkflowType = N'ManualDogfoodLoop',
    @WorkflowName = N'PR100 workflow checkpoint smoke',
    @Status = N'Created',
    @SubjectType = N'dogfood_receipt',
    @SubjectId = N'pr100-smoke',
    @SubjectSummary = N'Real DB workflow checkpoint smoke run.',
    @CorrelationId = '$correlationId',
    @CausationId = '$causationId',
    @CreatedByActorType = N'system_test_fixture',
    @CreatedByActorId = N'pr100-smoke',
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
    @StepKey = N'pr100-smoke-step',
    @StepName = N'PR100 smoke step',
    @StepType = N'DebugFinding',
    @Status = N'Created',
    @AgentRole = N'smoke',
    @AgentId = N'pr100-smoke',
    @SubjectType = N'workflow_step',
    @SubjectId = N'pr100-smoke-step-subject',
    @SafeSummary = N'Real DB workflow step smoke record before checkpoint.',
    @SequenceNumber = 1,
    @CorrelationId = '$correlationId',
    @CausationId = '$causationId',
    @MetadataVersion = 1,
    @MetadataJson = N'{"schema":"workflow.step.metadata.v1","smoke":true}',
    @EvidenceReferencesJson = N'[]',
    @GroundingReferencesJson = N'[]';
"@

    Invoke-NonQuery -Connection $connection -Sql @"
EXEC workflow.usp_WorkflowCheckpoint_Create
    @WorkflowCheckpointId = '$workflowCheckpointId',
    @WorkflowRunId = '$workflowRunId',
    @WorkflowRunStepId = '$workflowRunStepId',
    @ProjectId = '$projectId',
    @CheckpointKey = N'pr100-smoke-checkpoint',
    @CheckpointName = N'PR100 smoke checkpoint',
    @CheckpointType = N'ReviewSnapshot',
    @Status = N'Captured',
    @SubjectType = N'workflow_checkpoint',
    @SubjectId = N'pr100-smoke-checkpoint-subject',
    @SafeSummary = N'Real DB workflow checkpoint smoke record.',
    @StateVersion = 1,
    @StateJson = N'{"schema":"workflow.checkpoint.state.v1","smoke":true}',
    @StateHashSha256 = N'0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef',
    @CorrelationId = '$correlationId',
    @CausationId = '$causationId',
    @CreatedByActorType = N'system_test_fixture',
    @CreatedByActorId = N'pr100-smoke',
    @MetadataVersion = 1,
    @MetadataJson = N'{"schema":"workflow.checkpoint.metadata.v1","smoke":true}',
    @EvidenceReferencesJson = N'[{"evidenceType":"DogfoodReceipt","evidenceId":"pr100-smoke-receipt","evidenceLabel":"Smoke receipt","safeSummary":"Receipt evidence for checkpoint smoke.","allowedUse":"Review"}]',
    @GroundingReferencesJson = N'[{"groundingReferenceId":"$groundingReferenceId","claimType":"EvidenceSupport","claimId":"pr100-smoke-claim","safeSummary":"Grounding evidence for checkpoint smoke."}]';
"@

    $count = Invoke-Scalar -Connection $connection -Sql "SELECT COUNT(1) FROM workflow.WorkflowCheckpoint WHERE WorkflowCheckpointId = '$workflowCheckpointId' AND ProjectId = '$projectId';"
    if ([int]$count -ne 1) {
        throw "Workflow checkpoint smoke record was not persisted."
    }

    Write-Output "Workflow checkpoint smoke passed: $workflowCheckpointId"
}
finally {
    $connection.Dispose()
}
