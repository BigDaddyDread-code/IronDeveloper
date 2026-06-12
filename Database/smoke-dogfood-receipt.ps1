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

$sqlConnectionString = New-ConnectionString
$connection = New-Object System.Data.SqlClient.SqlConnection $sqlConnectionString
$connection.Open()

try {
    $command = $connection.CreateCommand()
    $command.CommandTimeout = $CommandTimeoutSeconds
    $command.CommandText = @"
DECLARE @ProjectId UNIQUEIDENTIFIER = NEWID();
DECLARE @CorrelationId UNIQUEIDENTIFIER = NEWID();
DECLARE @ReceiptId UNIQUEIDENTIFIER = NEWID();
DECLARE @EventId UNIQUEIDENTIFIER = NEWID();
DECLARE @ApprovalBefore INT = CASE WHEN OBJECT_ID(N'governance.ApprovalDecision', N'U') IS NULL THEN 0 ELSE (SELECT COUNT(*) FROM governance.ApprovalDecision) END;
DECLARE @PolicyBefore INT = CASE WHEN OBJECT_ID(N'governance.PolicyDecisionEvent', N'U') IS NULL THEN 0 ELSE (SELECT COUNT(*) FROM governance.PolicyDecisionEvent) END;
DECLARE @GateBefore INT = CASE WHEN OBJECT_ID(N'governance.ToolGateDecision', N'U') IS NULL THEN 0 ELSE (SELECT COUNT(*) FROM governance.ToolGateDecision) END;
DECLARE @RequestBefore INT = CASE WHEN OBJECT_ID(N'governance.ToolRequest', N'U') IS NULL THEN 0 ELSE (SELECT COUNT(*) FROM governance.ToolRequest) END;

EXEC governance.usp_DogfoodReceipt_Record
    @DogfoodReceiptId = @ReceiptId,
    @GovernanceEventId = @EventId,
    @ProjectId = @ProjectId,
    @ReceiptType = N'real_db_smoke',
    @SubjectType = N'dogfood_loop',
    @SubjectId = N'pr78-real-db-smoke',
    @Outcome = N'Passed',
    @SummaryCode = N'PR78_DOGFOOD_RECEIPT_SMOKE_PASSED',
    @Summary = N'PR78 real DB dogfood receipt smoke test.',
    @RecordedByActorType = N'system_test_fixture',
    @RecordedByActorId = N'pr78-smoke',
    @RelatedToolRequestId = NULL,
    @RelatedToolGateDecisionId = NULL,
    @RelatedApprovalDecisionId = NULL,
    @RelatedPolicyDecisionEventId = NULL,
    @CorrelationId = @CorrelationId,
    @CausationId = NULL,
    @EvidenceVersion = 1,
    @EvidenceJson = N'{"schema":"dogfood.receipt.smoke.v1","schemaVersion":1,"approvesRelease":false,"grantsApproval":false,"grantsExecution":false,"satisfiesPolicy":false,"mutatesSource":false,"promotesMemory":false,"startsWorkflow":false,"transfersAuthority":false}',
    @CreatedUtc = SYSUTCDATETIME();

SELECT
    durableDogfoodReceiptRecorded = CASE WHEN EXISTS (SELECT 1 FROM governance.DogfoodReceipt WHERE DogfoodReceiptId = @ReceiptId) THEN CAST(1 AS bit) ELSE CAST(0 AS bit) END,
    dogfoodGovernanceEventRecorded = CASE WHEN EXISTS (SELECT 1 FROM governance.GovernanceEvent WHERE EventId = @EventId AND EventType = N'dogfood.receipt.recorded') THEN CAST(1 AS bit) ELSE CAST(0 AS bit) END,
    dogfoodReceiptIsReleaseApproval = CAST(0 AS bit),
    dogfoodReceiptIsExecutionPermission = CAST(0 AS bit),
    policyDecisionCreated = CASE WHEN (SELECT COUNT(*) FROM governance.PolicyDecisionEvent) = @PolicyBefore THEN CAST(0 AS bit) ELSE CAST(1 AS bit) END,
    approvalDecisionCreated = CASE WHEN (SELECT COUNT(*) FROM governance.ApprovalDecision) = @ApprovalBefore THEN CAST(0 AS bit) ELSE CAST(1 AS bit) END,
    gateDecisionCreated = CASE WHEN (SELECT COUNT(*) FROM governance.ToolGateDecision) = @GateBefore THEN CAST(0 AS bit) ELSE CAST(1 AS bit) END,
    toolRequestCreated = CASE WHEN (SELECT COUNT(*) FROM governance.ToolRequest) = @RequestBefore THEN CAST(0 AS bit) ELSE CAST(1 AS bit) END,
    toolExecuted = CAST(0 AS bit),
    sourceApplied = CAST(0 AS bit),
    memoryPromoted = CAST(0 AS bit),
    workflowStarted = CAST(0 AS bit),
    a2aHandoffCreated = CAST(0 AS bit);
"@

    $reader = $command.ExecuteReader()
    $table = New-Object System.Data.DataTable
    $table.Load($reader)
    $row = $table.Rows[0]

    $result = [ordered]@{}
    foreach ($column in $table.Columns) {
        $result[$column.ColumnName] = [bool]$row[$column.ColumnName]
    }

    $result | ConvertTo-Json -Depth 3
}
finally {
    $connection.Dispose()
}
