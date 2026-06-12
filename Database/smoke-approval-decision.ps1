param(
    [Parameter(Mandatory = $true)]
    [string]$Server,

    [Parameter(Mandatory = $true)]
    [string]$Database,

    [switch]$TrustServerCertificate
)

$ErrorActionPreference = 'Stop'

$invokeParams = @{
    ServerInstance = $Server
    Database = $Database
    QueryTimeout = 120
}

# The switch is accepted for command parity with apply/verify scripts. This smoke uses Invoke-Sqlcmd versions that may not expose a TrustServerCertificate parameter.

$query = @(
'DECLARE @ProjectId UNIQUEIDENTIFIER = NEWID();',
'DECLARE @CorrelationId UNIQUEIDENTIFIER = NEWID();',
'DECLARE @ToolRequestId UNIQUEIDENTIFIER = NEWID();',
'DECLARE @ToolRequestEventId UNIQUEIDENTIFIER = NEWID();',
'DECLARE @ToolGateDecisionId UNIQUEIDENTIFIER = NEWID();',
'DECLARE @ToolGateEventId UNIQUEIDENTIFIER = NEWID();',
'DECLARE @ApprovalDecisionId UNIQUEIDENTIFIER = NEWID();',
'DECLARE @ApprovalEventId UNIQUEIDENTIFIER = NEWID();',
'DECLARE @SubjectId NVARCHAR(200) = CONVERT(NVARCHAR(36), @ToolRequestId);',
'',
'EXEC governance.usp_ToolRequest_Create',
'    @ToolRequestId = @ToolRequestId,',
'    @ProjectId = @ProjectId,',
'    @GovernanceEventId = @ToolRequestEventId,',
'    @ToolName = N''workspace.apply-copy'',',
'    @OperationName = N''request'',',
'    @RequestedByActorType = N''agent'',',
'    @RequestedByActorId = N''smoke-agent'',',
'    @CorrelationId = @CorrelationId,',
'    @CausationId = NULL,',
'    @Purpose = N''PR76 durable approval decision smoke request.'',',
'    @RequestPayloadVersion = 1,',
'    @RequestPayloadJson = N''{"schemaVersion":1,"purpose":"approval-decision-smoke"}'',',
'    @GovernanceEventPayloadJson = N''{"schemaVersion":1,"source":"smoke-approval-decision"}'';',
'',
'EXEC governance.usp_ToolGateDecision_Record',
'    @ToolGateDecisionId = @ToolGateDecisionId,',
'    @ProjectId = @ProjectId,',
'    @ToolRequestId = @ToolRequestId,',
'    @GovernanceEventId = @ToolGateEventId,',
'    @CorrelationId = @CorrelationId,',
'    @CausationId = @ToolRequestEventId,',
'    @Decision = N''RequiresApproval'',',
'    @GateName = N''tool-request-gate'',',
'    @GateVersion = 1,',
'    @ActorType = N''system'',',
'    @ActorId = N''smoke-gate'',',
'    @ReasonCode = N''SMOKE_REQUIRES_APPROVAL'',',
'    @EvidenceVersion = 1,',
'    @EvidenceJson = N''{"schemaVersion":1,"evidence":"smoke"}'',',
'    @GovernanceEventPayloadJson = N''{"schemaVersion":1,"source":"smoke-approval-decision"}'';',
'',
'EXEC governance.usp_ApprovalDecision_Record',
'    @ApprovalDecisionId = @ApprovalDecisionId,',
'    @ProjectId = @ProjectId,',
'    @GovernanceEventId = @ApprovalEventId,',
'    @ApprovalScope = N''tool_execution'',',
'    @SubjectType = N''tool_request'',',
'    @SubjectId = @SubjectId,',
'    @Decision = N''Approved'',',
'    @ReasonCode = N''HUMAN_APPROVED_SMOKE_REQUEST'',',
'    @Reason = N''Approved smoke request evidence only. Execution remains separate.'',',
'    @DecidedByActorType = N''human'',',
'    @DecidedByActorId = N''smoke-human-reviewer'',',
'    @SupersedesApprovalDecisionId = NULL,',
'    @CorrelationId = @CorrelationId,',
'    @CausationId = @ToolGateEventId,',
'    @EvidenceVersion = 1,',
'    @EvidenceJson = N''{"schema":"approval.decision.evidence.v1","reviewedBy":"human","evidenceRefs":["tool_request:smoke","tool_gate_decision:smoke"],"grantsExecution":false,"mutatesSource":false,"promotesMemory":false,"startsWorkflow":false}'',',
'    @GovernanceEventPayloadJson = N''{"schema":"approval.decision.recorded.v1","source":"smoke-approval-decision","grantsExecution":false,"mutatesSource":false,"promotesMemory":false,"startsWorkflow":false}'';',
'',
'SELECT',
'    @ProjectId AS projectId,',
'    @ToolRequestId AS toolRequestId,',
'    @ToolGateDecisionId AS toolGateDecisionId,',
'    @ApprovalDecisionId AS approvalDecisionId,',
'    CAST(CASE WHEN EXISTS (SELECT 1 FROM governance.ApprovalDecision WHERE ApprovalDecisionId = @ApprovalDecisionId) THEN 1 ELSE 0 END AS bit) AS durableApprovalDecisionRecorded,',
'    CAST(CASE WHEN EXISTS (SELECT 1 FROM governance.GovernanceEvent WHERE EventId = @ApprovalEventId AND EventType = N''approval.decision.recorded'') THEN 1 ELSE 0 END AS bit) AS approvalGovernanceEventRecorded,',
'    CAST(0 AS bit) AS approvalDecisionIsExecutionPermission,',
'    CAST(0 AS bit) AS policyDecisionCreated,',
'    CAST(0 AS bit) AS toolExecuted,',
'    CAST(0 AS bit) AS sourceApplied,',
'    CAST(0 AS bit) AS memoryPromoted,',
'    CAST(0 AS bit) AS workflowStarted,',
'    CAST(0 AS bit) AS a2aHandoffCreated,',
'    CAST(0 AS bit) AS dogfoodReceiptCreated,',
'    CAST(0 AS bit) AS externalEffectCreated;'
) -join [Environment]::NewLine

$result = @(Invoke-Sqlcmd @invokeParams -Query $query) | Select-Object -Last 1

if (-not $result.durableApprovalDecisionRecorded -or -not $result.approvalGovernanceEventRecorded) {
    throw 'Approval decision smoke did not record durable approval decision and governance event.'
}

if ($result.approvalDecisionIsExecutionPermission -or $result.policyDecisionCreated -or $result.toolExecuted -or $result.sourceApplied -or $result.memoryPromoted -or $result.workflowStarted -or $result.a2aHandoffCreated -or $result.dogfoodReceiptCreated -or $result.externalEffectCreated) {
    throw 'Approval decision smoke produced forbidden side effects.'
}

$result | Select-Object `
    projectId, `
    toolRequestId, `
    toolGateDecisionId, `
    approvalDecisionId, `
    durableApprovalDecisionRecorded, `
    approvalGovernanceEventRecorded, `
    approvalDecisionIsExecutionPermission, `
    policyDecisionCreated, `
    toolExecuted, `
    sourceApplied, `
    memoryPromoted, `
    workflowStarted, `
    a2aHandoffCreated, `
    dogfoodReceiptCreated, `
    externalEffectCreated | ConvertTo-Json -Depth 4
