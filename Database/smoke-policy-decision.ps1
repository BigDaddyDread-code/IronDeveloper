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
'DECLARE @PolicyDecisionEventId UNIQUEIDENTIFIER = NEWID();',
'DECLARE @PolicyEventId UNIQUEIDENTIFIER = NEWID();',
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
'    @Purpose = N''PR77 durable policy decision smoke request.'',',
'    @RequestPayloadVersion = 1,',
'    @RequestPayloadJson = N''{"schemaVersion":1,"purpose":"policy-decision-smoke"}'',',
'    @GovernanceEventPayloadJson = N''{"schemaVersion":1,"source":"smoke-policy-decision"}'';',
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
'    @GovernanceEventPayloadJson = N''{"schemaVersion":1,"source":"smoke-policy-decision"}'';',
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
'    @GovernanceEventPayloadJson = N''{"schema":"approval.decision.recorded.v1","source":"smoke-policy-decision","grantsExecution":false,"mutatesSource":false,"promotesMemory":false,"startsWorkflow":false}'';',
'',
'EXEC governance.usp_PolicyDecisionEvent_Record',
'    @PolicyDecisionEventId = @PolicyDecisionEventId,',
'    @ProjectId = @ProjectId,',
'    @GovernanceEventId = @PolicyEventId,',
'    @PolicyScope = N''tool_execution'',',
'    @PolicyName = N''tool-execution-policy'',',
'    @PolicyVersion = 1,',
'    @SubjectType = N''tool_request'',',
'    @SubjectId = @SubjectId,',
'    @Decision = N''RequiresApproval'',',
'    @RequirementCode = N''HUMAN_APPROVAL_REQUIRED'',',
'    @ReasonCode = N''SOURCE_MUTATION_REQUIRES_APPROVAL'',',
'    @Reason = N''Policy check recorded approval requirement only.'',',
'    @DecidedByActorType = N''system'',',
'    @DecidedByActorId = N''smoke-policy-check'',',
'    @RelatedToolRequestId = @ToolRequestId,',
'    @RelatedToolGateDecisionId = @ToolGateDecisionId,',
'    @RelatedApprovalDecisionId = @ApprovalDecisionId,',
'    @CorrelationId = @CorrelationId,',
'    @CausationId = @ApprovalEventId,',
'    @EvidenceVersion = 1,',
'    @EvidenceJson = N''{"schema":"policy.decision.evidence.v1","inputRefs":["tool_request:smoke","tool_gate_decision:smoke","approval_decision:smoke"],"result":{"decision":"RequiresApproval","requirementCode":"HUMAN_APPROVAL_REQUIRED"},"grantsApproval":false,"grantsExecution":false,"mutatesSource":false,"promotesMemory":false,"startsWorkflow":false,"satisfiesPolicy":false,"transfersAuthority":false}'',',
'    @GovernanceEventPayloadJson = N''{"schema":"policy.decision.recorded.v1","source":"smoke-policy-decision","grantsApproval":false,"grantsExecution":false,"mutatesSource":false,"promotesMemory":false,"startsWorkflow":false,"satisfiesPolicy":false,"transfersAuthority":false}'';',
'',
'SELECT',
'    @ProjectId AS projectId,',
'    @ToolRequestId AS toolRequestId,',
'    @ToolGateDecisionId AS toolGateDecisionId,',
'    @ApprovalDecisionId AS approvalDecisionId,',
'    @PolicyDecisionEventId AS policyDecisionEventId,',
'    CAST(CASE WHEN EXISTS (SELECT 1 FROM governance.PolicyDecisionEvent WHERE PolicyDecisionEventId = @PolicyDecisionEventId) THEN 1 ELSE 0 END AS bit) AS durablePolicyDecisionRecorded,',
'    CAST(CASE WHEN EXISTS (SELECT 1 FROM governance.GovernanceEvent WHERE EventId = @PolicyEventId AND EventType = N''policy.decision.recorded'') THEN 1 ELSE 0 END AS bit) AS policyGovernanceEventRecorded,',
'    CAST(0 AS bit) AS policyDecisionIsApproval,',
'    CAST(0 AS bit) AS policyDecisionIsExecutionPermission,',
'    CAST(0 AS bit) AS toolExecuted,',
'    CAST(0 AS bit) AS sourceApplied,',
'    CAST(0 AS bit) AS memoryPromoted,',
'    CAST(0 AS bit) AS workflowStarted,',
'    CAST(0 AS bit) AS a2aHandoffCreated,',
'    CAST(0 AS bit) AS dogfoodReceiptCreated,',
'    CAST(0 AS bit) AS externalEffectCreated;'
) -join [Environment]::NewLine

$result = @(Invoke-Sqlcmd @invokeParams -Query $query) | Select-Object -Last 1

if (-not $result.durablePolicyDecisionRecorded -or -not $result.policyGovernanceEventRecorded) {
    throw 'Policy decision smoke did not record durable policy decision and governance event.'
}

if ($result.policyDecisionIsApproval -or $result.policyDecisionIsExecutionPermission -or $result.toolExecuted -or $result.sourceApplied -or $result.memoryPromoted -or $result.workflowStarted -or $result.a2aHandoffCreated -or $result.dogfoodReceiptCreated -or $result.externalEffectCreated) {
    throw 'Policy decision smoke produced forbidden side effects.'
}

$result | Select-Object `
    projectId, `
    toolRequestId, `
    toolGateDecisionId, `
    approvalDecisionId, `
    policyDecisionEventId, `
    durablePolicyDecisionRecorded, `
    policyGovernanceEventRecorded, `
    policyDecisionIsApproval, `
    policyDecisionIsExecutionPermission, `
    toolExecuted, `
    sourceApplied, `
    memoryPromoted, `
    workflowStarted, `
    a2aHandoffCreated, `
    dogfoodReceiptCreated, `
    externalEffectCreated | ConvertTo-Json -Depth 4
