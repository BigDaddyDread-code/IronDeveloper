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
    if ([string]::IsNullOrWhiteSpace($ConnectionString)) {
        $ConnectionString = $env:IRONDEV_MIGRATION_CONNECTION_STRING
    }

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

function Test-ScalarExists {
    param(
        [Parameter(Mandatory = $true)][System.Data.SqlClient.SqlConnection]$Connection,
        [Parameter(Mandatory = $true)][string]$Name,
        [Parameter(Mandatory = $true)][string]$Sql
    )

    $command = $Connection.CreateCommand()
    $command.CommandText = $Sql
    $command.CommandTimeout = $CommandTimeoutSeconds
    try {
        $value = $command.ExecuteScalar()
        if ($null -eq $value -or [System.Convert]::ToInt32($value) -le 0) {
            throw "Missing required database object or invariant: $Name"
        }

        Write-Host "Verified $Name"
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
        $checks = @(
            @{ Name = "governance schema"; Sql = "SELECT CASE WHEN SCHEMA_ID(N'governance') IS NULL THEN 0 ELSE 1 END" },
            @{ Name = "governance.GovernanceEvent table"; Sql = "SELECT CASE WHEN OBJECT_ID(N'governance.GovernanceEvent', N'U') IS NULL THEN 0 ELSE 1 END" },
            @{ Name = "governance.AppendGovernanceEvent procedure"; Sql = "SELECT CASE WHEN OBJECT_ID(N'governance.AppendGovernanceEvent', N'P') IS NULL THEN 0 ELSE 1 END" },
            @{ Name = "governance.GetGovernanceEvent procedure"; Sql = "SELECT CASE WHEN OBJECT_ID(N'governance.GetGovernanceEvent', N'P') IS NULL THEN 0 ELSE 1 END" },
            @{ Name = "governance.ListGovernanceEventsForProject procedure"; Sql = "SELECT CASE WHEN OBJECT_ID(N'governance.ListGovernanceEventsForProject', N'P') IS NULL THEN 0 ELSE 1 END" },
            @{ Name = "governance.ListGovernanceEventsForCorrelation procedure"; Sql = "SELECT CASE WHEN OBJECT_ID(N'governance.ListGovernanceEventsForCorrelation', N'P') IS NULL THEN 0 ELSE 1 END" },
            @{ Name = "governance.ListGovernanceEventsForSubject procedure"; Sql = "SELECT CASE WHEN OBJECT_ID(N'governance.ListGovernanceEventsForSubject', N'P') IS NULL THEN 0 ELSE 1 END" },
            @{ Name = "governance.ListGovernanceEventsCausedBy procedure"; Sql = "SELECT CASE WHEN OBJECT_ID(N'governance.ListGovernanceEventsCausedBy', N'P') IS NULL THEN 0 ELSE 1 END" },
            @{ Name = "governance.TR_GovernanceEvent_BlockUpdateDelete trigger"; Sql = "SELECT CASE WHEN OBJECT_ID(N'governance.TR_GovernanceEvent_BlockUpdateDelete', N'TR') IS NULL THEN 0 ELSE 1 END" },
            @{ Name = "governance.ToolRequest table"; Sql = "SELECT CASE WHEN OBJECT_ID(N'governance.ToolRequest', N'U') IS NULL THEN 0 ELSE 1 END" },
            @{ Name = "governance.usp_ToolRequest_Create procedure"; Sql = "SELECT CASE WHEN OBJECT_ID(N'governance.usp_ToolRequest_Create', N'P') IS NULL THEN 0 ELSE 1 END" },
            @{ Name = "governance.usp_ToolRequest_GetById procedure"; Sql = "SELECT CASE WHEN OBJECT_ID(N'governance.usp_ToolRequest_GetById', N'P') IS NULL THEN 0 ELSE 1 END" },
            @{ Name = "governance.usp_ToolRequest_ListForProject procedure"; Sql = "SELECT CASE WHEN OBJECT_ID(N'governance.usp_ToolRequest_ListForProject', N'P') IS NULL THEN 0 ELSE 1 END" },
            @{ Name = "governance.usp_ToolRequest_ListForCorrelation procedure"; Sql = "SELECT CASE WHEN OBJECT_ID(N'governance.usp_ToolRequest_ListForCorrelation', N'P') IS NULL THEN 0 ELSE 1 END" },
            @{ Name = "FK_ToolRequest_GovernanceEvent"; Sql = "SELECT COUNT(*) FROM sys.foreign_keys WHERE parent_object_id = OBJECT_ID(N'governance.ToolRequest') AND referenced_object_id = OBJECT_ID(N'governance.GovernanceEvent') AND name = N'FK_ToolRequest_GovernanceEvent'" },
            @{ Name = "GovernanceEvent payload JSON check constraint"; Sql = "SELECT COUNT(*) FROM sys.check_constraints WHERE parent_object_id = OBJECT_ID(N'governance.GovernanceEvent') AND name = N'CK_GovernanceEvent_PayloadJson_IsJson'" },
            @{ Name = "GovernanceEvent payload version check constraint"; Sql = "SELECT COUNT(*) FROM sys.check_constraints WHERE parent_object_id = OBJECT_ID(N'governance.GovernanceEvent') AND name = N'CK_GovernanceEvent_PayloadVersion_Positive'" },
            @{ Name = "ToolRequest payload JSON check constraint"; Sql = "SELECT COUNT(*) FROM sys.check_constraints WHERE parent_object_id = OBJECT_ID(N'governance.ToolRequest') AND name = N'CK_ToolRequest_RequestPayloadJson_IsJson'" },
            @{ Name = "ToolRequest payload version check constraint"; Sql = "SELECT COUNT(*) FROM sys.check_constraints WHERE parent_object_id = OBJECT_ID(N'governance.ToolRequest') AND name = N'CK_ToolRequest_RequestPayloadVersion_Positive'" },
            @{ Name = "governance.ToolGateDecision table"; Sql = "SELECT CASE WHEN OBJECT_ID(N'governance.ToolGateDecision', N'U') IS NULL THEN 0 ELSE 1 END" },
            @{ Name = "governance.usp_ToolGateDecision_Record procedure"; Sql = "SELECT CASE WHEN OBJECT_ID(N'governance.usp_ToolGateDecision_Record', N'P') IS NULL THEN 0 ELSE 1 END" },
            @{ Name = "governance.usp_ToolGateDecision_GetById procedure"; Sql = "SELECT CASE WHEN OBJECT_ID(N'governance.usp_ToolGateDecision_GetById', N'P') IS NULL THEN 0 ELSE 1 END" },
            @{ Name = "governance.usp_ToolGateDecision_ListForToolRequest procedure"; Sql = "SELECT CASE WHEN OBJECT_ID(N'governance.usp_ToolGateDecision_ListForToolRequest', N'P') IS NULL THEN 0 ELSE 1 END" },
            @{ Name = "governance.usp_ToolGateDecision_ListForProject procedure"; Sql = "SELECT CASE WHEN OBJECT_ID(N'governance.usp_ToolGateDecision_ListForProject', N'P') IS NULL THEN 0 ELSE 1 END" },
            @{ Name = "governance.usp_ToolGateDecision_ListForCorrelation procedure"; Sql = "SELECT CASE WHEN OBJECT_ID(N'governance.usp_ToolGateDecision_ListForCorrelation', N'P') IS NULL THEN 0 ELSE 1 END" },
            @{ Name = "governance.TR_ToolGateDecision_BlockUpdateDelete trigger"; Sql = "SELECT CASE WHEN OBJECT_ID(N'governance.TR_ToolGateDecision_BlockUpdateDelete', N'TR') IS NULL THEN 0 ELSE 1 END" },
            @{ Name = "FK_ToolGateDecision_ToolRequest"; Sql = "SELECT COUNT(*) FROM sys.foreign_keys WHERE parent_object_id = OBJECT_ID(N'governance.ToolGateDecision') AND referenced_object_id = OBJECT_ID(N'governance.ToolRequest') AND name = N'FK_ToolGateDecision_ToolRequest'" },
            @{ Name = "FK_ToolGateDecision_GovernanceEvent"; Sql = "SELECT COUNT(*) FROM sys.foreign_keys WHERE parent_object_id = OBJECT_ID(N'governance.ToolGateDecision') AND referenced_object_id = OBJECT_ID(N'governance.GovernanceEvent') AND name = N'FK_ToolGateDecision_GovernanceEvent'" },
            @{ Name = "ToolGateDecision evidence JSON check constraint"; Sql = "SELECT COUNT(*) FROM sys.check_constraints WHERE parent_object_id = OBJECT_ID(N'governance.ToolGateDecision') AND name = N'CK_ToolGateDecision_EvidenceJson_IsJson'" },
            @{ Name = "ToolGateDecision no approval grant check constraint"; Sql = "SELECT COUNT(*) FROM sys.check_constraints WHERE parent_object_id = OBJECT_ID(N'governance.ToolGateDecision') AND name = N'CK_ToolGateDecision_NoApprovalGrant'" },
            @{ Name = "ToolGateDecision no execution grant check constraint"; Sql = "SELECT COUNT(*) FROM sys.check_constraints WHERE parent_object_id = OBJECT_ID(N'governance.ToolGateDecision') AND name = N'CK_ToolGateDecision_NoExecutionGrant'" },
            @{ Name = "ToolGateDecision no source mutation check constraint"; Sql = "SELECT COUNT(*) FROM sys.check_constraints WHERE parent_object_id = OBJECT_ID(N'governance.ToolGateDecision') AND name = N'CK_ToolGateDecision_NoSourceMutation'" },
            @{ Name = "ToolGateDecision no memory promotion check constraint"; Sql = "SELECT COUNT(*) FROM sys.check_constraints WHERE parent_object_id = OBJECT_ID(N'governance.ToolGateDecision') AND name = N'CK_ToolGateDecision_NoMemoryPromotion'" },
            @{ Name = "governance.ApprovalDecision table"; Sql = "SELECT CASE WHEN OBJECT_ID(N'governance.ApprovalDecision', N'U') IS NULL THEN 0 ELSE 1 END" },
            @{ Name = "governance.usp_ApprovalDecision_Record procedure"; Sql = "SELECT CASE WHEN OBJECT_ID(N'governance.usp_ApprovalDecision_Record', N'P') IS NULL THEN 0 ELSE 1 END" },
            @{ Name = "governance.usp_ApprovalDecision_GetById procedure"; Sql = "SELECT CASE WHEN OBJECT_ID(N'governance.usp_ApprovalDecision_GetById', N'P') IS NULL THEN 0 ELSE 1 END" },
            @{ Name = "governance.usp_ApprovalDecision_ListForSubject procedure"; Sql = "SELECT CASE WHEN OBJECT_ID(N'governance.usp_ApprovalDecision_ListForSubject', N'P') IS NULL THEN 0 ELSE 1 END" },
            @{ Name = "governance.usp_ApprovalDecision_ListForProject procedure"; Sql = "SELECT CASE WHEN OBJECT_ID(N'governance.usp_ApprovalDecision_ListForProject', N'P') IS NULL THEN 0 ELSE 1 END" },
            @{ Name = "governance.usp_ApprovalDecision_ListForCorrelation procedure"; Sql = "SELECT CASE WHEN OBJECT_ID(N'governance.usp_ApprovalDecision_ListForCorrelation', N'P') IS NULL THEN 0 ELSE 1 END" },
            @{ Name = "governance.TR_ApprovalDecision_ValidateInsert trigger"; Sql = "SELECT CASE WHEN OBJECT_ID(N'governance.TR_ApprovalDecision_ValidateInsert', N'TR') IS NULL THEN 0 ELSE 1 END" },
            @{ Name = "governance.TR_ApprovalDecision_BlockUpdateDelete trigger"; Sql = "SELECT CASE WHEN OBJECT_ID(N'governance.TR_ApprovalDecision_BlockUpdateDelete', N'TR') IS NULL THEN 0 ELSE 1 END" },
            @{ Name = "FK_ApprovalDecision_GovernanceEvent"; Sql = "SELECT COUNT(*) FROM sys.foreign_keys WHERE parent_object_id = OBJECT_ID(N'governance.ApprovalDecision') AND referenced_object_id = OBJECT_ID(N'governance.GovernanceEvent') AND name = N'FK_ApprovalDecision_GovernanceEvent'" },
            @{ Name = "FK_ApprovalDecision_Supersedes"; Sql = "SELECT COUNT(*) FROM sys.foreign_keys WHERE parent_object_id = OBJECT_ID(N'governance.ApprovalDecision') AND referenced_object_id = OBJECT_ID(N'governance.ApprovalDecision') AND name = N'FK_ApprovalDecision_Supersedes'" },
            @{ Name = "ApprovalDecision decision allowed check constraint"; Sql = "SELECT COUNT(*) FROM sys.check_constraints WHERE parent_object_id = OBJECT_ID(N'governance.ApprovalDecision') AND name = N'CK_ApprovalDecision_Decision_Allowed'" },
            @{ Name = "ApprovalDecision decision not execution check constraint"; Sql = "SELECT COUNT(*) FROM sys.check_constraints WHERE parent_object_id = OBJECT_ID(N'governance.ApprovalDecision') AND name = N'CK_ApprovalDecision_Decision_NotExecution'" },
            @{ Name = "ApprovalDecision evidence JSON check constraint"; Sql = "SELECT COUNT(*) FROM sys.check_constraints WHERE parent_object_id = OBJECT_ID(N'governance.ApprovalDecision') AND name = N'CK_ApprovalDecision_EvidenceJson_IsJson'" },
            @{ Name = "ApprovalDecision evidence JSON versioned check constraint"; Sql = "SELECT COUNT(*) FROM sys.check_constraints WHERE parent_object_id = OBJECT_ID(N'governance.ApprovalDecision') AND name = N'CK_ApprovalDecision_EvidenceJson_Versioned'" }
            ,@{ Name = "governance.AcceptedApproval table"; Sql = "SELECT CASE WHEN OBJECT_ID(N'governance.AcceptedApproval', N'U') IS NULL THEN 0 ELSE 1 END" }
            ,@{ Name = "governance.usp_AcceptedApproval_Save procedure"; Sql = "SELECT CASE WHEN OBJECT_ID(N'governance.usp_AcceptedApproval_Save', N'P') IS NULL THEN 0 ELSE 1 END" }
            ,@{ Name = "governance.usp_AcceptedApproval_Get procedure"; Sql = "SELECT CASE WHEN OBJECT_ID(N'governance.usp_AcceptedApproval_Get', N'P') IS NULL THEN 0 ELSE 1 END" }
            ,@{ Name = "governance.usp_AcceptedApproval_ListByTarget procedure"; Sql = "SELECT CASE WHEN OBJECT_ID(N'governance.usp_AcceptedApproval_ListByTarget', N'P') IS NULL THEN 0 ELSE 1 END" }
            ,@{ Name = "governance.usp_AcceptedApproval_ListByCorrelation procedure"; Sql = "SELECT CASE WHEN OBJECT_ID(N'governance.usp_AcceptedApproval_ListByCorrelation', N'P') IS NULL THEN 0 ELSE 1 END" }
            ,@{ Name = "governance.usp_AcceptedApproval_ListByProjectAndCorrelation procedure"; Sql = "SELECT CASE WHEN OBJECT_ID(N'governance.usp_AcceptedApproval_ListByProjectAndCorrelation', N'P') IS NULL THEN 0 ELSE 1 END" }
            ,@{ Name = "governance.TR_AcceptedApproval_ValidateInsert trigger"; Sql = "SELECT CASE WHEN OBJECT_ID(N'governance.TR_AcceptedApproval_ValidateInsert', N'TR') IS NULL THEN 0 ELSE 1 END" }
            ,@{ Name = "governance.TR_AcceptedApproval_BlockUpdateDelete trigger"; Sql = "SELECT CASE WHEN OBJECT_ID(N'governance.TR_AcceptedApproval_BlockUpdateDelete', N'TR') IS NULL THEN 0 ELSE 1 END" }
            ,@{ Name = "AcceptedApproval expiry check constraint"; Sql = "SELECT COUNT(*) FROM sys.check_constraints WHERE parent_object_id = OBJECT_ID(N'governance.AcceptedApproval') AND name = N'CK_AcceptedApproval_Expiry_AfterAccepted'" }
            ,@{ Name = "AcceptedApproval evidence JSON check constraint"; Sql = "SELECT COUNT(*) FROM sys.check_constraints WHERE parent_object_id = OBJECT_ID(N'governance.AcceptedApproval') AND name = N'CK_AcceptedApproval_EvidenceJson_IsJson'" }
            ,@{ Name = "AcceptedApproval boundary JSON check constraint"; Sql = "SELECT COUNT(*) FROM sys.check_constraints WHERE parent_object_id = OBJECT_ID(N'governance.AcceptedApproval') AND name = N'CK_AcceptedApproval_BoundaryJson_IsJson'" }
            ,@{ Name = "governance.PolicySatisfaction table"; Sql = "SELECT CASE WHEN OBJECT_ID(N'governance.PolicySatisfaction', N'U') IS NULL THEN 0 ELSE 1 END" }
            ,@{ Name = "governance.usp_PolicySatisfaction_Save procedure"; Sql = "SELECT CASE WHEN OBJECT_ID(N'governance.usp_PolicySatisfaction_Save', N'P') IS NULL THEN 0 ELSE 1 END" }
            ,@{ Name = "governance.usp_PolicySatisfaction_Get procedure"; Sql = "SELECT CASE WHEN OBJECT_ID(N'governance.usp_PolicySatisfaction_Get', N'P') IS NULL THEN 0 ELSE 1 END" }
            ,@{ Name = "governance.usp_PolicySatisfaction_ListBySubject procedure"; Sql = "SELECT CASE WHEN OBJECT_ID(N'governance.usp_PolicySatisfaction_ListBySubject', N'P') IS NULL THEN 0 ELSE 1 END" }
            ,@{ Name = "governance.usp_PolicySatisfaction_ListByAcceptedApproval procedure"; Sql = "SELECT CASE WHEN OBJECT_ID(N'governance.usp_PolicySatisfaction_ListByAcceptedApproval', N'P') IS NULL THEN 0 ELSE 1 END" }
            ,@{ Name = "governance.usp_PolicySatisfaction_ListByProjectAndCorrelation procedure"; Sql = "SELECT CASE WHEN OBJECT_ID(N'governance.usp_PolicySatisfaction_ListByProjectAndCorrelation', N'P') IS NULL THEN 0 ELSE 1 END" }
            ,@{ Name = "governance.TR_PolicySatisfaction_ValidateInsert trigger"; Sql = "SELECT CASE WHEN OBJECT_ID(N'governance.TR_PolicySatisfaction_ValidateInsert', N'TR') IS NULL THEN 0 ELSE 1 END" }
            ,@{ Name = "governance.TR_PolicySatisfaction_BlockUpdateDelete trigger"; Sql = "SELECT CASE WHEN OBJECT_ID(N'governance.TR_PolicySatisfaction_BlockUpdateDelete', N'TR') IS NULL THEN 0 ELSE 1 END" }
            ,@{ Name = "PolicySatisfaction expiry check constraint"; Sql = "SELECT COUNT(*) FROM sys.check_constraints WHERE parent_object_id = OBJECT_ID(N'governance.PolicySatisfaction') AND name = N'CK_PolicySatisfaction_Expiry_AfterSatisfied'" }
            ,@{ Name = "PolicySatisfaction evidence JSON check constraint"; Sql = "SELECT COUNT(*) FROM sys.check_constraints WHERE parent_object_id = OBJECT_ID(N'governance.PolicySatisfaction') AND name = N'CK_PolicySatisfaction_EvidenceJson_IsJson'" }
            ,@{ Name = "PolicySatisfaction boundary JSON check constraint"; Sql = "SELECT COUNT(*) FROM sys.check_constraints WHERE parent_object_id = OBJECT_ID(N'governance.PolicySatisfaction') AND name = N'CK_PolicySatisfaction_BoundaryJson_IsJson'" }
            ,@{ Name = "governance.PolicyDecisionEvent table"; Sql = "SELECT CASE WHEN OBJECT_ID(N'governance.PolicyDecisionEvent', N'U') IS NULL THEN 0 ELSE 1 END" }
            ,@{ Name = "governance.usp_PolicyDecisionEvent_Record procedure"; Sql = "SELECT CASE WHEN OBJECT_ID(N'governance.usp_PolicyDecisionEvent_Record', N'P') IS NULL THEN 0 ELSE 1 END" }
            ,@{ Name = "governance.usp_PolicyDecisionEvent_GetById procedure"; Sql = "SELECT CASE WHEN OBJECT_ID(N'governance.usp_PolicyDecisionEvent_GetById', N'P') IS NULL THEN 0 ELSE 1 END" }
            ,@{ Name = "governance.usp_PolicyDecisionEvent_ListForSubject procedure"; Sql = "SELECT CASE WHEN OBJECT_ID(N'governance.usp_PolicyDecisionEvent_ListForSubject', N'P') IS NULL THEN 0 ELSE 1 END" }
            ,@{ Name = "governance.usp_PolicyDecisionEvent_ListForProject procedure"; Sql = "SELECT CASE WHEN OBJECT_ID(N'governance.usp_PolicyDecisionEvent_ListForProject', N'P') IS NULL THEN 0 ELSE 1 END" }
            ,@{ Name = "governance.usp_PolicyDecisionEvent_ListForCorrelation procedure"; Sql = "SELECT CASE WHEN OBJECT_ID(N'governance.usp_PolicyDecisionEvent_ListForCorrelation', N'P') IS NULL THEN 0 ELSE 1 END" }
            ,@{ Name = "governance.TR_PolicyDecisionEvent_ValidateInsert trigger"; Sql = "SELECT CASE WHEN OBJECT_ID(N'governance.TR_PolicyDecisionEvent_ValidateInsert', N'TR') IS NULL THEN 0 ELSE 1 END" }
            ,@{ Name = "governance.TR_PolicyDecisionEvent_BlockUpdateDelete trigger"; Sql = "SELECT CASE WHEN OBJECT_ID(N'governance.TR_PolicyDecisionEvent_BlockUpdateDelete', N'TR') IS NULL THEN 0 ELSE 1 END" }
            ,@{ Name = "FK_PolicyDecisionEvent_GovernanceEvent"; Sql = "SELECT COUNT(*) FROM sys.foreign_keys WHERE parent_object_id = OBJECT_ID(N'governance.PolicyDecisionEvent') AND referenced_object_id = OBJECT_ID(N'governance.GovernanceEvent') AND name = N'FK_PolicyDecisionEvent_GovernanceEvent'" }
            ,@{ Name = "FK_PolicyDecisionEvent_ToolRequest"; Sql = "SELECT COUNT(*) FROM sys.foreign_keys WHERE parent_object_id = OBJECT_ID(N'governance.PolicyDecisionEvent') AND referenced_object_id = OBJECT_ID(N'governance.ToolRequest') AND name = N'FK_PolicyDecisionEvent_ToolRequest'" }
            ,@{ Name = "FK_PolicyDecisionEvent_ToolGateDecision"; Sql = "SELECT COUNT(*) FROM sys.foreign_keys WHERE parent_object_id = OBJECT_ID(N'governance.PolicyDecisionEvent') AND referenced_object_id = OBJECT_ID(N'governance.ToolGateDecision') AND name = N'FK_PolicyDecisionEvent_ToolGateDecision'" }
            ,@{ Name = "FK_PolicyDecisionEvent_ApprovalDecision"; Sql = "SELECT COUNT(*) FROM sys.foreign_keys WHERE parent_object_id = OBJECT_ID(N'governance.PolicyDecisionEvent') AND referenced_object_id = OBJECT_ID(N'governance.ApprovalDecision') AND name = N'FK_PolicyDecisionEvent_ApprovalDecision'" }
            ,@{ Name = "PolicyDecisionEvent decision allowed check constraint"; Sql = "SELECT COUNT(*) FROM sys.check_constraints WHERE parent_object_id = OBJECT_ID(N'governance.PolicyDecisionEvent') AND name = N'CK_PolicyDecisionEvent_Decision_Allowed'" }
            ,@{ Name = "PolicyDecisionEvent decision not authority check constraint"; Sql = "SELECT COUNT(*) FROM sys.check_constraints WHERE parent_object_id = OBJECT_ID(N'governance.PolicyDecisionEvent') AND name = N'CK_PolicyDecisionEvent_Decision_NotAuthority'" }
            ,@{ Name = "PolicyDecisionEvent evidence JSON check constraint"; Sql = "SELECT COUNT(*) FROM sys.check_constraints WHERE parent_object_id = OBJECT_ID(N'governance.PolicyDecisionEvent') AND name = N'CK_PolicyDecisionEvent_EvidenceJson_IsJson'" }
            ,@{ Name = "PolicyDecisionEvent evidence JSON versioned check constraint"; Sql = "SELECT COUNT(*) FROM sys.check_constraints WHERE parent_object_id = OBJECT_ID(N'governance.PolicyDecisionEvent') AND name = N'CK_PolicyDecisionEvent_EvidenceJson_Versioned'" }
            ,@{ Name = "PolicyDecisionEvent policy version check constraint"; Sql = "SELECT COUNT(*) FROM sys.check_constraints WHERE parent_object_id = OBJECT_ID(N'governance.PolicyDecisionEvent') AND name = N'CK_PolicyDecisionEvent_PolicyVersion_Positive'" }
            ,@{ Name = "governance.DogfoodReceipt table"; Sql = "SELECT CASE WHEN OBJECT_ID(N'governance.DogfoodReceipt', N'U') IS NULL THEN 0 ELSE 1 END" }
            ,@{ Name = "governance.usp_DogfoodReceipt_Record procedure"; Sql = "SELECT CASE WHEN OBJECT_ID(N'governance.usp_DogfoodReceipt_Record', N'P') IS NULL THEN 0 ELSE 1 END" }
            ,@{ Name = "governance.usp_DogfoodReceipt_GetById procedure"; Sql = "SELECT CASE WHEN OBJECT_ID(N'governance.usp_DogfoodReceipt_GetById', N'P') IS NULL THEN 0 ELSE 1 END" }
            ,@{ Name = "governance.usp_DogfoodReceipt_ListForSubject procedure"; Sql = "SELECT CASE WHEN OBJECT_ID(N'governance.usp_DogfoodReceipt_ListForSubject', N'P') IS NULL THEN 0 ELSE 1 END" }
            ,@{ Name = "governance.usp_DogfoodReceipt_ListForProject procedure"; Sql = "SELECT CASE WHEN OBJECT_ID(N'governance.usp_DogfoodReceipt_ListForProject', N'P') IS NULL THEN 0 ELSE 1 END" }
            ,@{ Name = "governance.usp_DogfoodReceipt_ListForCorrelation procedure"; Sql = "SELECT CASE WHEN OBJECT_ID(N'governance.usp_DogfoodReceipt_ListForCorrelation', N'P') IS NULL THEN 0 ELSE 1 END" }
            ,@{ Name = "governance.TR_DogfoodReceipt_ValidateInsert trigger"; Sql = "SELECT CASE WHEN OBJECT_ID(N'governance.TR_DogfoodReceipt_ValidateInsert', N'TR') IS NULL THEN 0 ELSE 1 END" }
            ,@{ Name = "governance.TR_DogfoodReceipt_BlockUpdateDelete trigger"; Sql = "SELECT CASE WHEN OBJECT_ID(N'governance.TR_DogfoodReceipt_BlockUpdateDelete', N'TR') IS NULL THEN 0 ELSE 1 END" }
            ,@{ Name = "FK_DogfoodReceipt_GovernanceEvent"; Sql = "SELECT COUNT(*) FROM sys.foreign_keys WHERE parent_object_id = OBJECT_ID(N'governance.DogfoodReceipt') AND referenced_object_id = OBJECT_ID(N'governance.GovernanceEvent') AND name = N'FK_DogfoodReceipt_GovernanceEvent'" }
            ,@{ Name = "FK_DogfoodReceipt_ToolRequest"; Sql = "SELECT COUNT(*) FROM sys.foreign_keys WHERE parent_object_id = OBJECT_ID(N'governance.DogfoodReceipt') AND referenced_object_id = OBJECT_ID(N'governance.ToolRequest') AND name = N'FK_DogfoodReceipt_ToolRequest'" }
            ,@{ Name = "FK_DogfoodReceipt_ToolGateDecision"; Sql = "SELECT COUNT(*) FROM sys.foreign_keys WHERE parent_object_id = OBJECT_ID(N'governance.DogfoodReceipt') AND referenced_object_id = OBJECT_ID(N'governance.ToolGateDecision') AND name = N'FK_DogfoodReceipt_ToolGateDecision'" }
            ,@{ Name = "FK_DogfoodReceipt_ApprovalDecision"; Sql = "SELECT COUNT(*) FROM sys.foreign_keys WHERE parent_object_id = OBJECT_ID(N'governance.DogfoodReceipt') AND referenced_object_id = OBJECT_ID(N'governance.ApprovalDecision') AND name = N'FK_DogfoodReceipt_ApprovalDecision'" }
            ,@{ Name = "FK_DogfoodReceipt_PolicyDecisionEvent"; Sql = "SELECT COUNT(*) FROM sys.foreign_keys WHERE parent_object_id = OBJECT_ID(N'governance.DogfoodReceipt') AND referenced_object_id = OBJECT_ID(N'governance.PolicyDecisionEvent') AND name = N'FK_DogfoodReceipt_PolicyDecisionEvent'" }
            ,@{ Name = "DogfoodReceipt outcome allowed check constraint"; Sql = "SELECT COUNT(*) FROM sys.check_constraints WHERE parent_object_id = OBJECT_ID(N'governance.DogfoodReceipt') AND name = N'CK_DogfoodReceipt_Outcome_Allowed'" }
            ,@{ Name = "DogfoodReceipt outcome not authority check constraint"; Sql = "SELECT COUNT(*) FROM sys.check_constraints WHERE parent_object_id = OBJECT_ID(N'governance.DogfoodReceipt') AND name = N'CK_DogfoodReceipt_Outcome_NotAuthority'" }
            ,@{ Name = "DogfoodReceipt evidence JSON check constraint"; Sql = "SELECT COUNT(*) FROM sys.check_constraints WHERE parent_object_id = OBJECT_ID(N'governance.DogfoodReceipt') AND name = N'CK_DogfoodReceipt_EvidenceJson_IsJson'" }
            ,@{ Name = "DogfoodReceipt evidence JSON versioned check constraint"; Sql = "SELECT COUNT(*) FROM sys.check_constraints WHERE parent_object_id = OBJECT_ID(N'governance.DogfoodReceipt') AND name = N'CK_DogfoodReceipt_EvidenceJson_Versioned'" }
            ,@{ Name = "governance.ThoughtLedgerGovernanceEventReference table"; Sql = "SELECT CASE WHEN OBJECT_ID(N'governance.ThoughtLedgerGovernanceEventReference', N'U') IS NULL THEN 0 ELSE 1 END" }
            ,@{ Name = "governance.usp_ThoughtLedgerGovernanceEventReference_Record procedure"; Sql = "SELECT CASE WHEN OBJECT_ID(N'governance.usp_ThoughtLedgerGovernanceEventReference_Record', N'P') IS NULL THEN 0 ELSE 1 END" }
            ,@{ Name = "governance.usp_ThoughtLedgerGovernanceEventReference_GetById procedure"; Sql = "SELECT CASE WHEN OBJECT_ID(N'governance.usp_ThoughtLedgerGovernanceEventReference_GetById', N'P') IS NULL THEN 0 ELSE 1 END" }
            ,@{ Name = "governance.usp_ThoughtLedgerGovernanceEventReference_ListForThoughtLedgerEntry procedure"; Sql = "SELECT CASE WHEN OBJECT_ID(N'governance.usp_ThoughtLedgerGovernanceEventReference_ListForThoughtLedgerEntry', N'P') IS NULL THEN 0 ELSE 1 END" }
            ,@{ Name = "governance.usp_ThoughtLedgerGovernanceEventReference_ListForGovernanceEvent procedure"; Sql = "SELECT CASE WHEN OBJECT_ID(N'governance.usp_ThoughtLedgerGovernanceEventReference_ListForGovernanceEvent', N'P') IS NULL THEN 0 ELSE 1 END" }
            ,@{ Name = "governance.usp_ThoughtLedgerGovernanceEventReference_ListForCorrelation procedure"; Sql = "SELECT CASE WHEN OBJECT_ID(N'governance.usp_ThoughtLedgerGovernanceEventReference_ListForCorrelation', N'P') IS NULL THEN 0 ELSE 1 END" }
            ,@{ Name = "governance.TR_ThoughtLedgerGovernanceEventReference_ValidateInsert trigger"; Sql = "SELECT CASE WHEN OBJECT_ID(N'governance.TR_ThoughtLedgerGovernanceEventReference_ValidateInsert', N'TR') IS NULL THEN 0 ELSE 1 END" }
            ,@{ Name = "governance.TR_ThoughtLedgerGovernanceEventReference_BlockUpdateDelete trigger"; Sql = "SELECT CASE WHEN OBJECT_ID(N'governance.TR_ThoughtLedgerGovernanceEventReference_BlockUpdateDelete', N'TR') IS NULL THEN 0 ELSE 1 END" }
            ,@{ Name = "FK_ThoughtLedgerGovernanceEventReference_GovernanceEvent"; Sql = "SELECT COUNT(*) FROM sys.foreign_keys WHERE parent_object_id = OBJECT_ID(N'governance.ThoughtLedgerGovernanceEventReference') AND referenced_object_id = OBJECT_ID(N'governance.GovernanceEvent') AND name = N'FK_ThoughtLedgerGovernanceEventReference_GovernanceEvent'" }
            ,@{ Name = "ThoughtLedgerGovernanceEventReference reference type allowed check constraint"; Sql = "SELECT COUNT(*) FROM sys.check_constraints WHERE parent_object_id = OBJECT_ID(N'governance.ThoughtLedgerGovernanceEventReference') AND name = N'CK_ThoughtLedgerGovernanceEventReference_ReferenceType_Allowed'" }
            ,@{ Name = "ThoughtLedgerGovernanceEventReference metadata JSON check constraint"; Sql = "SELECT COUNT(*) FROM sys.check_constraints WHERE parent_object_id = OBJECT_ID(N'governance.ThoughtLedgerGovernanceEventReference') AND name = N'CK_ThoughtLedgerGovernanceEventReference_MetadataJson_IsJson'" }
            ,@{ Name = "ThoughtLedgerGovernanceEventReference metadata JSON versioned check constraint"; Sql = "SELECT COUNT(*) FROM sys.check_constraints WHERE parent_object_id = OBJECT_ID(N'governance.ThoughtLedgerGovernanceEventReference') AND name = N'CK_ThoughtLedgerGovernanceEventReference_MetadataJson_Versioned'" }
            ,@{ Name = "ThoughtLedgerGovernanceEventReference metadata version check constraint"; Sql = "SELECT COUNT(*) FROM sys.check_constraints WHERE parent_object_id = OBJECT_ID(N'governance.ThoughtLedgerGovernanceEventReference') AND name = N'CK_ThoughtLedgerGovernanceEventReference_MetadataVersion_Positive'" }
            ,@{ Name = "a2a schema"; Sql = "SELECT CASE WHEN SCHEMA_ID(N'a2a') IS NULL THEN 0 ELSE 1 END" }
            ,@{ Name = "a2a.AgentHandoff table"; Sql = "SELECT CASE WHEN OBJECT_ID(N'a2a.AgentHandoff', N'U') IS NULL THEN 0 ELSE 1 END" }
            ,@{ Name = "a2a.AgentHandoffEvidenceReference table"; Sql = "SELECT CASE WHEN OBJECT_ID(N'a2a.AgentHandoffEvidenceReference', N'U') IS NULL THEN 0 ELSE 1 END" }
            ,@{ Name = "a2a.AgentHandoffEvidenceAllowedUse table"; Sql = "SELECT CASE WHEN OBJECT_ID(N'a2a.AgentHandoffEvidenceAllowedUse', N'U') IS NULL THEN 0 ELSE 1 END" }
            ,@{ Name = "a2a.AgentHandoffConstraint table"; Sql = "SELECT CASE WHEN OBJECT_ID(N'a2a.AgentHandoffConstraint', N'U') IS NULL THEN 0 ELSE 1 END" }
            ,@{ Name = "a2a.usp_AgentHandoff_Create procedure"; Sql = "SELECT CASE WHEN OBJECT_ID(N'a2a.usp_AgentHandoff_Create', N'P') IS NULL THEN 0 ELSE 1 END" }
            ,@{ Name = "a2a.usp_AgentHandoff_Get procedure"; Sql = "SELECT CASE WHEN OBJECT_ID(N'a2a.usp_AgentHandoff_Get', N'P') IS NULL THEN 0 ELSE 1 END" }
            ,@{ Name = "a2a.usp_AgentHandoff_ListByProject procedure"; Sql = "SELECT CASE WHEN OBJECT_ID(N'a2a.usp_AgentHandoff_ListByProject', N'P') IS NULL THEN 0 ELSE 1 END" }
            ,@{ Name = "a2a.usp_AgentHandoff_ListByCorrelation procedure"; Sql = "SELECT CASE WHEN OBJECT_ID(N'a2a.usp_AgentHandoff_ListByCorrelation', N'P') IS NULL THEN 0 ELSE 1 END" }
            ,@{ Name = "a2a.usp_AgentHandoff_ListBySubject procedure"; Sql = "SELECT CASE WHEN OBJECT_ID(N'a2a.usp_AgentHandoff_ListBySubject', N'P') IS NULL THEN 0 ELSE 1 END" }
            ,@{ Name = "a2a.TR_AgentHandoff_ValidateInsert trigger"; Sql = "SELECT CASE WHEN OBJECT_ID(N'a2a.TR_AgentHandoff_ValidateInsert', N'TR') IS NULL THEN 0 ELSE 1 END" }
            ,@{ Name = "a2a.TR_AgentHandoff_BlockUpdateDelete trigger"; Sql = "SELECT CASE WHEN OBJECT_ID(N'a2a.TR_AgentHandoff_BlockUpdateDelete', N'TR') IS NULL THEN 0 ELSE 1 END" }
            ,@{ Name = "AgentHandoff metadata JSON check constraint"; Sql = "SELECT COUNT(*) FROM sys.check_constraints WHERE parent_object_id = OBJECT_ID(N'a2a.AgentHandoff') AND name = N'CK_AgentHandoff_MetadataJson_IsJson'" }
            ,@{ Name = "AgentHandoff no authority transfer check constraint"; Sql = "SELECT COUNT(*) FROM sys.check_constraints WHERE parent_object_id = OBJECT_ID(N'a2a.AgentHandoff') AND name = N'CK_AgentHandoff_NoAuthorityTransfer'" }
            ,@{ Name = "AgentHandoff evidence allowed-use check constraint"; Sql = "SELECT COUNT(*) FROM sys.check_constraints WHERE parent_object_id = OBJECT_ID(N'a2a.AgentHandoffEvidenceAllowedUse') AND name = N'CK_AgentHandoffEvidenceAllowedUse_Allowed'" }
            ,@{ Name = "workflow schema"; Sql = "SELECT CASE WHEN SCHEMA_ID(N'workflow') IS NULL THEN 0 ELSE 1 END" }
            ,@{ Name = "workflow.WorkflowRun table"; Sql = "SELECT CASE WHEN OBJECT_ID(N'workflow.WorkflowRun', N'U') IS NULL THEN 0 ELSE 1 END" }
            ,@{ Name = "workflow.WorkflowRunStep table"; Sql = "SELECT CASE WHEN OBJECT_ID(N'workflow.WorkflowRunStep', N'U') IS NULL THEN 0 ELSE 1 END" }
            ,@{ Name = "workflow.WorkflowRunEvidenceReference table"; Sql = "SELECT CASE WHEN OBJECT_ID(N'workflow.WorkflowRunEvidenceReference', N'U') IS NULL THEN 0 ELSE 1 END" }
            ,@{ Name = "workflow.WorkflowRunGroundingReference table"; Sql = "SELECT CASE WHEN OBJECT_ID(N'workflow.WorkflowRunGroundingReference', N'U') IS NULL THEN 0 ELSE 1 END" }
            ,@{ Name = "workflow.usp_WorkflowRun_Create procedure"; Sql = "SELECT CASE WHEN OBJECT_ID(N'workflow.usp_WorkflowRun_Create', N'P') IS NULL THEN 0 ELSE 1 END" }
            ,@{ Name = "workflow.usp_WorkflowRun_Get procedure"; Sql = "SELECT CASE WHEN OBJECT_ID(N'workflow.usp_WorkflowRun_Get', N'P') IS NULL THEN 0 ELSE 1 END" }
            ,@{ Name = "workflow.usp_WorkflowRun_ListByProject procedure"; Sql = "SELECT CASE WHEN OBJECT_ID(N'workflow.usp_WorkflowRun_ListByProject', N'P') IS NULL THEN 0 ELSE 1 END" }
            ,@{ Name = "workflow.usp_WorkflowRun_ListByCorrelation procedure"; Sql = "SELECT CASE WHEN OBJECT_ID(N'workflow.usp_WorkflowRun_ListByCorrelation', N'P') IS NULL THEN 0 ELSE 1 END" }
            ,@{ Name = "workflow.usp_WorkflowRun_ListBySubject procedure"; Sql = "SELECT CASE WHEN OBJECT_ID(N'workflow.usp_WorkflowRun_ListBySubject', N'P') IS NULL THEN 0 ELSE 1 END" }
            ,@{ Name = "workflow.usp_WorkflowStep_Create procedure"; Sql = "SELECT CASE WHEN OBJECT_ID(N'workflow.usp_WorkflowStep_Create', N'P') IS NULL THEN 0 ELSE 1 END" }
            ,@{ Name = "workflow.usp_WorkflowStep_Get procedure"; Sql = "SELECT CASE WHEN OBJECT_ID(N'workflow.usp_WorkflowStep_Get', N'P') IS NULL THEN 0 ELSE 1 END" }
            ,@{ Name = "workflow.usp_WorkflowStep_ListByRun procedure"; Sql = "SELECT CASE WHEN OBJECT_ID(N'workflow.usp_WorkflowStep_ListByRun', N'P') IS NULL THEN 0 ELSE 1 END" }
            ,@{ Name = "workflow.usp_WorkflowStep_ListByCorrelation procedure"; Sql = "SELECT CASE WHEN OBJECT_ID(N'workflow.usp_WorkflowStep_ListByCorrelation', N'P') IS NULL THEN 0 ELSE 1 END" }
            ,@{ Name = "workflow.usp_WorkflowStep_ListBySubject procedure"; Sql = "SELECT CASE WHEN OBJECT_ID(N'workflow.usp_WorkflowStep_ListBySubject', N'P') IS NULL THEN 0 ELSE 1 END" }
            ,@{ Name = "workflow.TR_WorkflowRun_ValidateInsert trigger"; Sql = "SELECT CASE WHEN OBJECT_ID(N'workflow.TR_WorkflowRun_ValidateInsert', N'TR') IS NULL THEN 0 ELSE 1 END" }
            ,@{ Name = "workflow.TR_WorkflowRun_BlockUpdateDelete trigger"; Sql = "SELECT CASE WHEN OBJECT_ID(N'workflow.TR_WorkflowRun_BlockUpdateDelete', N'TR') IS NULL THEN 0 ELSE 1 END" }
            ,@{ Name = "WorkflowRun no workflow continuation check constraint"; Sql = "SELECT COUNT(*) FROM sys.check_constraints WHERE parent_object_id = OBJECT_ID(N'workflow.WorkflowRun') AND name = N'CK_WorkflowRun_NoWorkflowContinuation'" }
            ,@{ Name = "WorkflowRun no authority transfer check constraint"; Sql = "SELECT COUNT(*) FROM sys.check_constraints WHERE parent_object_id = OBJECT_ID(N'workflow.WorkflowRun') AND name = N'CK_WorkflowRun_NoAuthorityTransfer'" }
            ,@{ Name = "WorkflowRunStep sequence number column"; Sql = "SELECT CASE WHEN COL_LENGTH(N'workflow.WorkflowRunStep', N'SequenceNumber') IS NULL THEN 0 ELSE 1 END" }
            ,@{ Name = "WorkflowRunStep sequence number check constraint"; Sql = "SELECT COUNT(*) FROM sys.check_constraints WHERE parent_object_id = OBJECT_ID(N'workflow.WorkflowRunStep') AND name = N'CK_WorkflowRunStep_SequenceNumber_Positive'" }
            ,@{ Name = "WorkflowRunStep no execution grant check constraint"; Sql = "SELECT COUNT(*) FROM sys.check_constraints WHERE parent_object_id = OBJECT_ID(N'workflow.WorkflowRunStep') AND name = N'CK_WorkflowRunStep_NoExecutionGrant'" }
            ,@{ Name = "WorkflowRunEvidenceReference allowed-use check constraint"; Sql = "SELECT COUNT(*) FROM sys.check_constraints WHERE parent_object_id = OBJECT_ID(N'workflow.WorkflowRunEvidenceReference') AND name = N'CK_WorkflowRunEvidenceReference_AllowedUse_Allowed'" }
            ,@{ Name = "WorkflowRunGroundingReference claim-type check constraint"; Sql = "SELECT COUNT(*) FROM sys.check_constraints WHERE parent_object_id = OBJECT_ID(N'workflow.WorkflowRunGroundingReference') AND name = N'CK_WorkflowRunGroundingReference_ClaimType_Allowed'" }
            ,@{ Name = "workflow.WorkflowCheckpoint table"; Sql = "SELECT CASE WHEN OBJECT_ID(N'workflow.WorkflowCheckpoint', N'U') IS NULL THEN 0 ELSE 1 END" }
            ,@{ Name = "workflow.WorkflowCheckpointEvidenceReference table"; Sql = "SELECT CASE WHEN OBJECT_ID(N'workflow.WorkflowCheckpointEvidenceReference', N'U') IS NULL THEN 0 ELSE 1 END" }
            ,@{ Name = "workflow.WorkflowCheckpointGroundingReference table"; Sql = "SELECT CASE WHEN OBJECT_ID(N'workflow.WorkflowCheckpointGroundingReference', N'U') IS NULL THEN 0 ELSE 1 END" }
            ,@{ Name = "workflow.usp_WorkflowCheckpoint_Create procedure"; Sql = "SELECT CASE WHEN OBJECT_ID(N'workflow.usp_WorkflowCheckpoint_Create', N'P') IS NULL THEN 0 ELSE 1 END" }
            ,@{ Name = "workflow.usp_WorkflowCheckpoint_Get procedure"; Sql = "SELECT CASE WHEN OBJECT_ID(N'workflow.usp_WorkflowCheckpoint_Get', N'P') IS NULL THEN 0 ELSE 1 END" }
            ,@{ Name = "workflow.usp_WorkflowCheckpoint_ListByRun procedure"; Sql = "SELECT CASE WHEN OBJECT_ID(N'workflow.usp_WorkflowCheckpoint_ListByRun', N'P') IS NULL THEN 0 ELSE 1 END" }
            ,@{ Name = "workflow.usp_WorkflowCheckpoint_ListByStep procedure"; Sql = "SELECT CASE WHEN OBJECT_ID(N'workflow.usp_WorkflowCheckpoint_ListByStep', N'P') IS NULL THEN 0 ELSE 1 END" }
            ,@{ Name = "workflow.usp_WorkflowCheckpoint_ListByCorrelation procedure"; Sql = "SELECT CASE WHEN OBJECT_ID(N'workflow.usp_WorkflowCheckpoint_ListByCorrelation', N'P') IS NULL THEN 0 ELSE 1 END" }
            ,@{ Name = "workflow.usp_WorkflowCheckpoint_ListBySubject procedure"; Sql = "SELECT CASE WHEN OBJECT_ID(N'workflow.usp_WorkflowCheckpoint_ListBySubject', N'P') IS NULL THEN 0 ELSE 1 END" }
            ,@{ Name = "workflow.TR_WorkflowCheckpoint_ValidateInsert trigger"; Sql = "SELECT CASE WHEN OBJECT_ID(N'workflow.TR_WorkflowCheckpoint_ValidateInsert', N'TR') IS NULL THEN 0 ELSE 1 END" }
            ,@{ Name = "workflow.TR_WorkflowCheckpoint_BlockUpdateDelete trigger"; Sql = "SELECT CASE WHEN OBJECT_ID(N'workflow.TR_WorkflowCheckpoint_BlockUpdateDelete', N'TR') IS NULL THEN 0 ELSE 1 END" }
            ,@{ Name = "WorkflowCheckpoint no workflow resume check constraint"; Sql = "SELECT COUNT(*) FROM sys.check_constraints WHERE parent_object_id = OBJECT_ID(N'workflow.WorkflowCheckpoint') AND name = N'CK_WorkflowCheckpoint_NoWorkflowResume'" }
            ,@{ Name = "WorkflowCheckpoint no execution check constraint"; Sql = "SELECT COUNT(*) FROM sys.check_constraints WHERE parent_object_id = OBJECT_ID(N'workflow.WorkflowCheckpoint') AND name = N'CK_WorkflowCheckpoint_NoExecution'" }
            ,@{ Name = "WorkflowCheckpointEvidenceReference allowed-use check constraint"; Sql = "SELECT COUNT(*) FROM sys.check_constraints WHERE parent_object_id = OBJECT_ID(N'workflow.WorkflowCheckpointEvidenceReference') AND name = N'CK_WorkflowCheckpointEvidenceReference_AllowedUse_Allowed'" }
            ,@{ Name = "WorkflowCheckpointGroundingReference claim-type check constraint"; Sql = "SELECT COUNT(*) FROM sys.check_constraints WHERE parent_object_id = OBJECT_ID(N'workflow.WorkflowCheckpointGroundingReference') AND name = N'CK_WorkflowCheckpointGroundingReference_ClaimType_Allowed'" }
            ,@{ Name = "workflow.ApplyDryRunRecord table"; Sql = "SELECT CASE WHEN OBJECT_ID(N'workflow.ApplyDryRunRecord', N'U') IS NULL THEN 0 ELSE 1 END" }
            ,@{ Name = "workflow.usp_ApplyDryRun_Create procedure"; Sql = "SELECT CASE WHEN OBJECT_ID(N'workflow.usp_ApplyDryRun_Create', N'P') IS NULL THEN 0 ELSE 1 END" }
            ,@{ Name = "workflow.usp_ApplyDryRun_Get procedure"; Sql = "SELECT CASE WHEN OBJECT_ID(N'workflow.usp_ApplyDryRun_Get', N'P') IS NULL THEN 0 ELSE 1 END" }
            ,@{ Name = "workflow.usp_ApplyDryRun_ListByWorkflowRun procedure"; Sql = "SELECT CASE WHEN OBJECT_ID(N'workflow.usp_ApplyDryRun_ListByWorkflowRun', N'P') IS NULL THEN 0 ELSE 1 END" }
            ,@{ Name = "workflow.usp_ApplyDryRun_ListByControlledApplyPlan procedure"; Sql = "SELECT CASE WHEN OBJECT_ID(N'workflow.usp_ApplyDryRun_ListByControlledApplyPlan', N'P') IS NULL THEN 0 ELSE 1 END" }
            ,@{ Name = "workflow.TR_ApplyDryRunRecord_ValidateInsert trigger"; Sql = "SELECT CASE WHEN OBJECT_ID(N'workflow.TR_ApplyDryRunRecord_ValidateInsert', N'TR') IS NULL THEN 0 ELSE 1 END" }
            ,@{ Name = "workflow.TR_ApplyDryRunRecord_BlockUpdateDelete trigger"; Sql = "SELECT CASE WHEN OBJECT_ID(N'workflow.TR_ApplyDryRunRecord_BlockUpdateDelete', N'TR') IS NULL THEN 0 ELSE 1 END" }
            ,@{ Name = "ApplyDryRunRecord no action check constraint"; Sql = "SELECT COUNT(*) FROM sys.check_constraints WHERE parent_object_id = OBJECT_ID(N'workflow.ApplyDryRunRecord') AND name = N'CK_ApplyDryRunRecord_NoDryRunAction'" }
            ,@{ Name = "ApplyDryRunRecord no source apply check constraint"; Sql = "SELECT COUNT(*) FROM sys.check_constraints WHERE parent_object_id = OBJECT_ID(N'workflow.ApplyDryRunRecord') AND name = N'CK_ApplyDryRunRecord_NoSourceApply'" }
            ,@{ Name = "ApplyDryRunRecord no memory promotion check constraint"; Sql = "SELECT COUNT(*) FROM sys.check_constraints WHERE parent_object_id = OBJECT_ID(N'workflow.ApplyDryRunRecord') AND name = N'CK_ApplyDryRunRecord_NoMemoryPromotion'" }
            ,@{ Name = "ApplyDryRunRecord metadata JSON check constraint"; Sql = "SELECT COUNT(*) FROM sys.check_constraints WHERE parent_object_id = OBJECT_ID(N'workflow.ApplyDryRunRecord') AND name = N'CK_ApplyDryRunRecord_MetadataJson_IsJson'" }
            ,@{ Name = "governance.ControlledDryRunReceipt table"; Sql = "SELECT CASE WHEN OBJECT_ID(N'governance.ControlledDryRunReceipt', N'U') IS NULL THEN 0 ELSE 1 END" }
            ,@{ Name = "governance.usp_ControlledDryRunReceipt_Save procedure"; Sql = "SELECT CASE WHEN OBJECT_ID(N'governance.usp_ControlledDryRunReceipt_Save', N'P') IS NULL THEN 0 ELSE 1 END" }
            ,@{ Name = "governance.usp_ControlledDryRunReceipt_Get procedure"; Sql = "SELECT CASE WHEN OBJECT_ID(N'governance.usp_ControlledDryRunReceipt_Get', N'P') IS NULL THEN 0 ELSE 1 END" }
            ,@{ Name = "governance.usp_ControlledDryRunReceipt_ListByRequest procedure"; Sql = "SELECT CASE WHEN OBJECT_ID(N'governance.usp_ControlledDryRunReceipt_ListByRequest', N'P') IS NULL THEN 0 ELSE 1 END" }
            ,@{ Name = "governance.usp_ControlledDryRunReceipt_ListByPolicySatisfaction procedure"; Sql = "SELECT CASE WHEN OBJECT_ID(N'governance.usp_ControlledDryRunReceipt_ListByPolicySatisfaction', N'P') IS NULL THEN 0 ELSE 1 END" }
            ,@{ Name = "governance.usp_ControlledDryRunReceipt_ListBySubject procedure"; Sql = "SELECT CASE WHEN OBJECT_ID(N'governance.usp_ControlledDryRunReceipt_ListBySubject', N'P') IS NULL THEN 0 ELSE 1 END" }
            ,@{ Name = "governance.usp_ControlledDryRunReceipt_ListByAuditHash procedure"; Sql = "SELECT CASE WHEN OBJECT_ID(N'governance.usp_ControlledDryRunReceipt_ListByAuditHash', N'P') IS NULL THEN 0 ELSE 1 END" }
            ,@{ Name = "governance.TR_ControlledDryRunReceipt_ValidateInsert trigger"; Sql = "SELECT CASE WHEN OBJECT_ID(N'governance.TR_ControlledDryRunReceipt_ValidateInsert', N'TR') IS NULL THEN 0 ELSE 1 END" }
            ,@{ Name = "governance.TR_ControlledDryRunReceipt_BlockUpdateDelete trigger"; Sql = "SELECT CASE WHEN OBJECT_ID(N'governance.TR_ControlledDryRunReceipt_BlockUpdateDelete', N'TR') IS NULL THEN 0 ELSE 1 END" }
            ,@{ Name = "ControlledDryRunReceipt command audits JSON check constraint"; Sql = "SELECT COUNT(*) FROM sys.check_constraints WHERE parent_object_id = OBJECT_ID(N'governance.ControlledDryRunReceipt') AND name = N'CK_ControlledDryRunReceipt_CommandAuditsJson_IsJson'" }
            ,@{ Name = "ControlledDryRunReceipt completed-after-started check constraint"; Sql = "SELECT COUNT(*) FROM sys.check_constraints WHERE parent_object_id = OBJECT_ID(N'governance.ControlledDryRunReceipt') AND name = N'CK_ControlledDryRunReceipt_CompletedAfterStarted'" }
            ,@{ Name = "governance.PatchArtifact table"; Sql = "SELECT CASE WHEN OBJECT_ID(N'governance.PatchArtifact', N'U') IS NULL THEN 0 ELSE 1 END" }
            ,@{ Name = "governance.usp_PatchArtifact_Save procedure"; Sql = "SELECT CASE WHEN OBJECT_ID(N'governance.usp_PatchArtifact_Save', N'P') IS NULL THEN 0 ELSE 1 END" }
            ,@{ Name = "governance.usp_PatchArtifact_Get procedure"; Sql = "SELECT CASE WHEN OBJECT_ID(N'governance.usp_PatchArtifact_Get', N'P') IS NULL THEN 0 ELSE 1 END" }
            ,@{ Name = "governance.usp_PatchArtifact_ListByDryRunReceiptHash procedure"; Sql = "SELECT CASE WHEN OBJECT_ID(N'governance.usp_PatchArtifact_ListByDryRunReceiptHash', N'P') IS NULL THEN 0 ELSE 1 END" }
            ,@{ Name = "governance.usp_PatchArtifact_ListByDryRunAuditHash procedure"; Sql = "SELECT CASE WHEN OBJECT_ID(N'governance.usp_PatchArtifact_ListByDryRunAuditHash', N'P') IS NULL THEN 0 ELSE 1 END" }
            ,@{ Name = "governance.usp_PatchArtifact_ListByControlledDryRunRequest procedure"; Sql = "SELECT CASE WHEN OBJECT_ID(N'governance.usp_PatchArtifact_ListByControlledDryRunRequest', N'P') IS NULL THEN 0 ELSE 1 END" }
            ,@{ Name = "governance.usp_PatchArtifact_ListBySubject procedure"; Sql = "SELECT CASE WHEN OBJECT_ID(N'governance.usp_PatchArtifact_ListBySubject', N'P') IS NULL THEN 0 ELSE 1 END" }
            ,@{ Name = "governance.usp_PatchArtifact_ListByPatchHash procedure"; Sql = "SELECT CASE WHEN OBJECT_ID(N'governance.usp_PatchArtifact_ListByPatchHash', N'P') IS NULL THEN 0 ELSE 1 END" }
            ,@{ Name = "governance.usp_PatchArtifact_ListBySourceBaselineHash procedure"; Sql = "SELECT CASE WHEN OBJECT_ID(N'governance.usp_PatchArtifact_ListBySourceBaselineHash', N'P') IS NULL THEN 0 ELSE 1 END" }
            ,@{ Name = "governance.TR_PatchArtifact_ValidateInsert trigger"; Sql = "SELECT CASE WHEN OBJECT_ID(N'governance.TR_PatchArtifact_ValidateInsert', N'TR') IS NULL THEN 0 ELSE 1 END" }
            ,@{ Name = "governance.TR_PatchArtifact_BlockUpdateDelete trigger"; Sql = "SELECT CASE WHEN OBJECT_ID(N'governance.TR_PatchArtifact_BlockUpdateDelete', N'TR') IS NULL THEN 0 ELSE 1 END" }
            ,@{ Name = "PatchArtifact file changes JSON check constraint"; Sql = "SELECT COUNT(*) FROM sys.check_constraints WHERE parent_object_id = OBJECT_ID(N'governance.PatchArtifact') AND name = N'CK_PatchArtifact_FileChangesJson_IsJson'" }
            ,@{ Name = "PatchArtifact expiry check constraint"; Sql = "SELECT COUNT(*) FROM sys.check_constraints WHERE parent_object_id = OBJECT_ID(N'governance.PatchArtifact') AND name = N'CK_PatchArtifact_ExpiresAfterCreated'" }
            ,@{ Name = "governance.RollbackSupportReceipt table"; Sql = "SELECT CASE WHEN OBJECT_ID(N'governance.RollbackSupportReceipt', N'U') IS NULL THEN 0 ELSE 1 END" }
            ,@{ Name = "governance.usp_RollbackSupportReceipt_Save procedure"; Sql = "SELECT CASE WHEN OBJECT_ID(N'governance.usp_RollbackSupportReceipt_Save', N'P') IS NULL THEN 0 ELSE 1 END" }
            ,@{ Name = "governance.usp_RollbackSupportReceipt_Get procedure"; Sql = "SELECT CASE WHEN OBJECT_ID(N'governance.usp_RollbackSupportReceipt_Get', N'P') IS NULL THEN 0 ELSE 1 END" }
            ,@{ Name = "governance.usp_RollbackSupportReceipt_GetByReceiptHash procedure"; Sql = "SELECT CASE WHEN OBJECT_ID(N'governance.usp_RollbackSupportReceipt_GetByReceiptHash', N'P') IS NULL THEN 0 ELSE 1 END" }
            ,@{ Name = "governance.usp_RollbackSupportReceipt_ListByPatchArtifact procedure"; Sql = "SELECT CASE WHEN OBJECT_ID(N'governance.usp_RollbackSupportReceipt_ListByPatchArtifact', N'P') IS NULL THEN 0 ELSE 1 END" }
            ,@{ Name = "governance.usp_RollbackSupportReceipt_ListByPatchHash procedure"; Sql = "SELECT CASE WHEN OBJECT_ID(N'governance.usp_RollbackSupportReceipt_ListByPatchHash', N'P') IS NULL THEN 0 ELSE 1 END" }
            ,@{ Name = "governance.usp_RollbackSupportReceipt_ListByRollbackPlan procedure"; Sql = "SELECT CASE WHEN OBJECT_ID(N'governance.usp_RollbackSupportReceipt_ListByRollbackPlan', N'P') IS NULL THEN 0 ELSE 1 END" }
            ,@{ Name = "governance.usp_RollbackSupportReceipt_ListBySourceBaselineHash procedure"; Sql = "SELECT CASE WHEN OBJECT_ID(N'governance.usp_RollbackSupportReceipt_ListBySourceBaselineHash', N'P') IS NULL THEN 0 ELSE 1 END" }
            ,@{ Name = "governance.TR_RollbackSupportReceipt_ValidateInsert trigger"; Sql = "SELECT CASE WHEN OBJECT_ID(N'governance.TR_RollbackSupportReceipt_ValidateInsert', N'TR') IS NULL THEN 0 ELSE 1 END" }
            ,@{ Name = "governance.TR_RollbackSupportReceipt_BlockUpdateDelete trigger"; Sql = "SELECT CASE WHEN OBJECT_ID(N'governance.TR_RollbackSupportReceipt_BlockUpdateDelete', N'TR') IS NULL THEN 0 ELSE 1 END" }
            ,@{ Name = "RollbackSupportReceipt gate satisfied check constraint"; Sql = "SELECT COUNT(*) FROM sys.check_constraints WHERE parent_object_id = OBJECT_ID(N'governance.RollbackSupportReceipt') AND name = N'CK_RollbackSupportReceipt_GateSatisfied'" }
            ,@{ Name = "RollbackSupportReceipt expiry check constraint"; Sql = "SELECT COUNT(*) FROM sys.check_constraints WHERE parent_object_id = OBJECT_ID(N'governance.RollbackSupportReceipt') AND name = N'CK_RollbackSupportReceipt_ExpiresAfterCreated'" }
            ,@{ Name = "RollbackSupportReceipt evidence JSON check constraint"; Sql = "SELECT COUNT(*) FROM sys.check_constraints WHERE parent_object_id = OBJECT_ID(N'governance.RollbackSupportReceipt') AND name = N'CK_RollbackSupportReceipt_EvidenceReferencesJson_IsJson'" }
            ,@{ Name = "governance.SourceApplyDryRunReceipt table"; Sql = "SELECT CASE WHEN OBJECT_ID(N'governance.SourceApplyDryRunReceipt', N'U') IS NULL THEN 0 ELSE 1 END" }
            ,@{ Name = "governance.usp_SourceApplyDryRunReceipt_Save procedure"; Sql = "SELECT CASE WHEN OBJECT_ID(N'governance.usp_SourceApplyDryRunReceipt_Save', N'P') IS NULL THEN 0 ELSE 1 END" }
            ,@{ Name = "governance.usp_SourceApplyDryRunReceipt_Get procedure"; Sql = "SELECT CASE WHEN OBJECT_ID(N'governance.usp_SourceApplyDryRunReceipt_Get', N'P') IS NULL THEN 0 ELSE 1 END" }
            ,@{ Name = "governance.usp_SourceApplyDryRunReceipt_GetByReceiptHash procedure"; Sql = "SELECT CASE WHEN OBJECT_ID(N'governance.usp_SourceApplyDryRunReceipt_GetByReceiptHash', N'P') IS NULL THEN 0 ELSE 1 END" }
            ,@{ Name = "governance.usp_SourceApplyDryRunReceipt_ListBySourceApplyRequest procedure"; Sql = "SELECT CASE WHEN OBJECT_ID(N'governance.usp_SourceApplyDryRunReceipt_ListBySourceApplyRequest', N'P') IS NULL THEN 0 ELSE 1 END" }
            ,@{ Name = "governance.usp_SourceApplyDryRunReceipt_ListBySourceApplyGateEvaluation procedure"; Sql = "SELECT CASE WHEN OBJECT_ID(N'governance.usp_SourceApplyDryRunReceipt_ListBySourceApplyGateEvaluation', N'P') IS NULL THEN 0 ELSE 1 END" }
            ,@{ Name = "governance.usp_SourceApplyDryRunReceipt_ListByPatchArtifact procedure"; Sql = "SELECT CASE WHEN OBJECT_ID(N'governance.usp_SourceApplyDryRunReceipt_ListByPatchArtifact', N'P') IS NULL THEN 0 ELSE 1 END" }
            ,@{ Name = "governance.usp_SourceApplyDryRunReceipt_ListByRollbackSupportReceipt procedure"; Sql = "SELECT CASE WHEN OBJECT_ID(N'governance.usp_SourceApplyDryRunReceipt_ListByRollbackSupportReceipt', N'P') IS NULL THEN 0 ELSE 1 END" }
            ,@{ Name = "governance.TR_SourceApplyDryRunReceipt_ValidateInsert trigger"; Sql = "SELECT CASE WHEN OBJECT_ID(N'governance.TR_SourceApplyDryRunReceipt_ValidateInsert', N'TR') IS NULL THEN 0 ELSE 1 END" }
            ,@{ Name = "governance.TR_SourceApplyDryRunReceipt_BlockUpdateDelete trigger"; Sql = "SELECT CASE WHEN OBJECT_ID(N'governance.TR_SourceApplyDryRunReceipt_BlockUpdateDelete', N'TR') IS NULL THEN 0 ELSE 1 END" }
            ,@{ Name = "SourceApplyDryRunReceipt file results JSON check constraint"; Sql = "SELECT COUNT(*) FROM sys.check_constraints WHERE parent_object_id = OBJECT_ID(N'governance.SourceApplyDryRunReceipt') AND name = N'CK_SourceApplyDryRunReceipt_FileResultsJson_IsJson'" }
            ,@{ Name = "SourceApplyDryRunReceipt expiry check constraint"; Sql = "SELECT COUNT(*) FROM sys.check_constraints WHERE parent_object_id = OBJECT_ID(N'governance.SourceApplyDryRunReceipt') AND name = N'CK_SourceApplyDryRunReceipt_ExpiresAfterCreated'" }
            ,@{ Name = "SourceApplyDryRunReceipt evidence JSON check constraint"; Sql = "SELECT COUNT(*) FROM sys.check_constraints WHERE parent_object_id = OBJECT_ID(N'governance.SourceApplyDryRunReceipt') AND name = N'CK_SourceApplyDryRunReceipt_EvidenceReferencesJson_IsJson'" }
            ,@{ Name = "governance.SourceApplyReceipt table"; Sql = "SELECT CASE WHEN OBJECT_ID(N'governance.SourceApplyReceipt', N'U') IS NULL THEN 0 ELSE 1 END" }
            ,@{ Name = "governance.usp_SourceApplyReceipt_Save procedure"; Sql = "SELECT CASE WHEN OBJECT_ID(N'governance.usp_SourceApplyReceipt_Save', N'P') IS NULL THEN 0 ELSE 1 END" }
            ,@{ Name = "governance.usp_SourceApplyReceipt_Get procedure"; Sql = "SELECT CASE WHEN OBJECT_ID(N'governance.usp_SourceApplyReceipt_Get', N'P') IS NULL THEN 0 ELSE 1 END" }
            ,@{ Name = "governance.usp_SourceApplyReceipt_GetByReceiptHash procedure"; Sql = "SELECT CASE WHEN OBJECT_ID(N'governance.usp_SourceApplyReceipt_GetByReceiptHash', N'P') IS NULL THEN 0 ELSE 1 END" }
            ,@{ Name = "governance.usp_SourceApplyReceipt_ListBySourceApplyRequest procedure"; Sql = "SELECT CASE WHEN OBJECT_ID(N'governance.usp_SourceApplyReceipt_ListBySourceApplyRequest', N'P') IS NULL THEN 0 ELSE 1 END" }
            ,@{ Name = "governance.usp_SourceApplyReceipt_ListBySourceApplyDryRunReceipt procedure"; Sql = "SELECT CASE WHEN OBJECT_ID(N'governance.usp_SourceApplyReceipt_ListBySourceApplyDryRunReceipt', N'P') IS NULL THEN 0 ELSE 1 END" }
            ,@{ Name = "governance.usp_SourceApplyReceipt_ListByPatchArtifact procedure"; Sql = "SELECT CASE WHEN OBJECT_ID(N'governance.usp_SourceApplyReceipt_ListByPatchArtifact', N'P') IS NULL THEN 0 ELSE 1 END" }
            ,@{ Name = "governance.usp_SourceApplyReceipt_ListByRollbackSupportReceipt procedure"; Sql = "SELECT CASE WHEN OBJECT_ID(N'governance.usp_SourceApplyReceipt_ListByRollbackSupportReceipt', N'P') IS NULL THEN 0 ELSE 1 END" }
            ,@{ Name = "governance.TR_SourceApplyReceipt_ValidateInsert trigger"; Sql = "SELECT CASE WHEN OBJECT_ID(N'governance.TR_SourceApplyReceipt_ValidateInsert', N'TR') IS NULL THEN 0 ELSE 1 END" }
            ,@{ Name = "governance.TR_SourceApplyReceipt_BlockUpdateDelete trigger"; Sql = "SELECT CASE WHEN OBJECT_ID(N'governance.TR_SourceApplyReceipt_BlockUpdateDelete', N'TR') IS NULL THEN 0 ELSE 1 END" }
            ,@{ Name = "SourceApplyReceipt file results JSON check constraint"; Sql = "SELECT COUNT(*) FROM sys.check_constraints WHERE parent_object_id = OBJECT_ID(N'governance.SourceApplyReceipt') AND name = N'CK_SourceApplyReceipt_FileResultsJson_IsJson'" }
            ,@{ Name = "SourceApplyReceipt evidence JSON check constraint"; Sql = "SELECT COUNT(*) FROM sys.check_constraints WHERE parent_object_id = OBJECT_ID(N'governance.SourceApplyReceipt') AND name = N'CK_SourceApplyReceipt_EvidenceReferencesJson_IsJson'" }
            ,@{ Name = "SourceApplyReceipt partial not succeeded check constraint"; Sql = "SELECT COUNT(*) FROM sys.check_constraints WHERE parent_object_id = OBJECT_ID(N'governance.SourceApplyReceipt') AND name = N'CK_SourceApplyReceipt_PartialNotSucceeded'" }
            ,@{ Name = "governance.RollbackExecutionReceipt table"; Sql = "SELECT CASE WHEN OBJECT_ID(N'governance.RollbackExecutionReceipt', N'U') IS NULL THEN 0 ELSE 1 END" }
            ,@{ Name = "governance.usp_RollbackExecutionReceipt_Save procedure"; Sql = "SELECT CASE WHEN OBJECT_ID(N'governance.usp_RollbackExecutionReceipt_Save', N'P') IS NULL THEN 0 ELSE 1 END" }
            ,@{ Name = "governance.usp_RollbackExecutionReceipt_Get procedure"; Sql = "SELECT CASE WHEN OBJECT_ID(N'governance.usp_RollbackExecutionReceipt_Get', N'P') IS NULL THEN 0 ELSE 1 END" }
            ,@{ Name = "governance.usp_RollbackExecutionReceipt_GetByReceiptHash procedure"; Sql = "SELECT CASE WHEN OBJECT_ID(N'governance.usp_RollbackExecutionReceipt_GetByReceiptHash', N'P') IS NULL THEN 0 ELSE 1 END" }
            ,@{ Name = "governance.usp_RollbackExecutionReceipt_ListBySourceApplyReceipt procedure"; Sql = "SELECT CASE WHEN OBJECT_ID(N'governance.usp_RollbackExecutionReceipt_ListBySourceApplyReceipt', N'P') IS NULL THEN 0 ELSE 1 END" }
            ,@{ Name = "governance.usp_RollbackExecutionReceipt_ListByRollbackPlan procedure"; Sql = "SELECT CASE WHEN OBJECT_ID(N'governance.usp_RollbackExecutionReceipt_ListByRollbackPlan', N'P') IS NULL THEN 0 ELSE 1 END" }
            ,@{ Name = "governance.usp_RollbackExecutionReceipt_ListByRollbackSupportReceipt procedure"; Sql = "SELECT CASE WHEN OBJECT_ID(N'governance.usp_RollbackExecutionReceipt_ListByRollbackSupportReceipt', N'P') IS NULL THEN 0 ELSE 1 END" }
            ,@{ Name = "governance.usp_RollbackExecutionReceipt_ListByPatchArtifact procedure"; Sql = "SELECT CASE WHEN OBJECT_ID(N'governance.usp_RollbackExecutionReceipt_ListByPatchArtifact', N'P') IS NULL THEN 0 ELSE 1 END" }
            ,@{ Name = "governance.TR_RollbackExecutionReceipt_ValidateInsert trigger"; Sql = "SELECT CASE WHEN OBJECT_ID(N'governance.TR_RollbackExecutionReceipt_ValidateInsert', N'TR') IS NULL THEN 0 ELSE 1 END" }
            ,@{ Name = "governance.TR_RollbackExecutionReceipt_BlockUpdateDelete trigger"; Sql = "SELECT CASE WHEN OBJECT_ID(N'governance.TR_RollbackExecutionReceipt_BlockUpdateDelete', N'TR') IS NULL THEN 0 ELSE 1 END" }
            ,@{ Name = "RollbackExecutionReceipt file results JSON check constraint"; Sql = "SELECT COUNT(*) FROM sys.check_constraints WHERE parent_object_id = OBJECT_ID(N'governance.RollbackExecutionReceipt') AND name = N'CK_RollbackExecutionReceipt_FileResultsJson_IsJson'" }
            ,@{ Name = "RollbackExecutionReceipt evidence JSON check constraint"; Sql = "SELECT COUNT(*) FROM sys.check_constraints WHERE parent_object_id = OBJECT_ID(N'governance.RollbackExecutionReceipt') AND name = N'CK_RollbackExecutionReceipt_EvidenceReferencesJson_IsJson'" }
            ,@{ Name = "RollbackExecutionReceipt partial not succeeded check constraint"; Sql = "SELECT COUNT(*) FROM sys.check_constraints WHERE parent_object_id = OBJECT_ID(N'governance.RollbackExecutionReceipt') AND name = N'CK_RollbackExecutionReceipt_PartialNotSucceeded'" }
            ,@{ Name = "governance.WorkflowTransitionRecord table"; Sql = "SELECT CASE WHEN OBJECT_ID(N'governance.WorkflowTransitionRecord', N'U') IS NULL THEN 0 ELSE 1 END" }
            ,@{ Name = "governance.usp_WorkflowTransitionRecord_Save procedure"; Sql = "SELECT CASE WHEN OBJECT_ID(N'governance.usp_WorkflowTransitionRecord_Save', N'P') IS NULL THEN 0 ELSE 1 END" }
            ,@{ Name = "governance.usp_WorkflowTransitionRecord_Get procedure"; Sql = "SELECT CASE WHEN OBJECT_ID(N'governance.usp_WorkflowTransitionRecord_Get', N'P') IS NULL THEN 0 ELSE 1 END" }
            ,@{ Name = "governance.usp_WorkflowTransitionRecord_GetByRecordHash procedure"; Sql = "SELECT CASE WHEN OBJECT_ID(N'governance.usp_WorkflowTransitionRecord_GetByRecordHash', N'P') IS NULL THEN 0 ELSE 1 END" }
            ,@{ Name = "governance.usp_WorkflowTransitionRecord_ListByWorkflowRun procedure"; Sql = "SELECT CASE WHEN OBJECT_ID(N'governance.usp_WorkflowTransitionRecord_ListByWorkflowRun', N'P') IS NULL THEN 0 ELSE 1 END" }
            ,@{ Name = "governance.usp_WorkflowTransitionRecord_ListByWorkflowStep procedure"; Sql = "SELECT CASE WHEN OBJECT_ID(N'governance.usp_WorkflowTransitionRecord_ListByWorkflowStep', N'P') IS NULL THEN 0 ELSE 1 END" }
            ,@{ Name = "governance.usp_WorkflowTransitionRecord_ListByContinuationGateEvaluation procedure"; Sql = "SELECT CASE WHEN OBJECT_ID(N'governance.usp_WorkflowTransitionRecord_ListByContinuationGateEvaluation', N'P') IS NULL THEN 0 ELSE 1 END" }
            ,@{ Name = "governance.usp_WorkflowTransitionRecord_ListBySourceApplyReceipt procedure"; Sql = "SELECT CASE WHEN OBJECT_ID(N'governance.usp_WorkflowTransitionRecord_ListBySourceApplyReceipt', N'P') IS NULL THEN 0 ELSE 1 END" }
            ,@{ Name = "governance.usp_WorkflowTransitionRecord_ListByRollbackExecutionReceipt procedure"; Sql = "SELECT CASE WHEN OBJECT_ID(N'governance.usp_WorkflowTransitionRecord_ListByRollbackExecutionReceipt', N'P') IS NULL THEN 0 ELSE 1 END" }
            ,@{ Name = "governance.TR_WorkflowTransitionRecord_ValidateInsert trigger"; Sql = "SELECT CASE WHEN OBJECT_ID(N'governance.TR_WorkflowTransitionRecord_ValidateInsert', N'TR') IS NULL THEN 0 ELSE 1 END" }
            ,@{ Name = "governance.TR_WorkflowTransitionRecord_BlockUpdateDelete trigger"; Sql = "SELECT CASE WHEN OBJECT_ID(N'governance.TR_WorkflowTransitionRecord_BlockUpdateDelete', N'TR') IS NULL THEN 0 ELSE 1 END" }
            ,@{ Name = "WorkflowTransitionRecord no release approval check constraint"; Sql = "SELECT COUNT(*) FROM sys.check_constraints WHERE parent_object_id = OBJECT_ID(N'governance.WorkflowTransitionRecord') AND name = N'CK_WorkflowTransitionRecord_NoReleaseApproval'" }
            ,@{ Name = "WorkflowTransitionRecord no release readiness check constraint"; Sql = "SELECT COUNT(*) FROM sys.check_constraints WHERE parent_object_id = OBJECT_ID(N'governance.WorkflowTransitionRecord') AND name = N'CK_WorkflowTransitionRecord_NoReleaseReadiness'" }
            ,@{ Name = "WorkflowTransitionRecord truth table check constraint"; Sql = "SELECT COUNT(*) FROM sys.check_constraints WHERE parent_object_id = OBJECT_ID(N'governance.WorkflowTransitionRecord') AND name = N'CK_WorkflowTransitionRecord_ContinueTruth'" }
            ,@{ Name = "governance.ReleaseReadinessDecisionRecord table"; Sql = "SELECT CASE WHEN OBJECT_ID(N'governance.ReleaseReadinessDecisionRecord', N'U') IS NULL THEN 0 ELSE 1 END" }
            ,@{ Name = "governance.usp_ReleaseReadinessDecisionRecord_Save procedure"; Sql = "SELECT CASE WHEN OBJECT_ID(N'governance.usp_ReleaseReadinessDecisionRecord_Save', N'P') IS NULL THEN 0 ELSE 1 END" }
            ,@{ Name = "governance.usp_ReleaseReadinessDecisionRecord_Get procedure"; Sql = "SELECT CASE WHEN OBJECT_ID(N'governance.usp_ReleaseReadinessDecisionRecord_Get', N'P') IS NULL THEN 0 ELSE 1 END" }
            ,@{ Name = "governance.usp_ReleaseReadinessDecisionRecord_GetByHash procedure"; Sql = "SELECT CASE WHEN OBJECT_ID(N'governance.usp_ReleaseReadinessDecisionRecord_GetByHash', N'P') IS NULL THEN 0 ELSE 1 END" }
            ,@{ Name = "governance.usp_ReleaseReadinessDecisionRecord_ListByReleaseReadinessReport procedure"; Sql = "SELECT CASE WHEN OBJECT_ID(N'governance.usp_ReleaseReadinessDecisionRecord_ListByReleaseReadinessReport', N'P') IS NULL THEN 0 ELSE 1 END" }
            ,@{ Name = "governance.usp_ReleaseReadinessDecisionRecord_ListByWorkflowRun procedure"; Sql = "SELECT CASE WHEN OBJECT_ID(N'governance.usp_ReleaseReadinessDecisionRecord_ListByWorkflowRun', N'P') IS NULL THEN 0 ELSE 1 END" }
            ,@{ Name = "governance.usp_ReleaseReadinessDecisionRecord_ListBySubject procedure"; Sql = "SELECT CASE WHEN OBJECT_ID(N'governance.usp_ReleaseReadinessDecisionRecord_ListBySubject', N'P') IS NULL THEN 0 ELSE 1 END" }
            ,@{ Name = "governance.TR_ReleaseReadinessDecisionRecord_ValidateInsert trigger"; Sql = "SELECT CASE WHEN OBJECT_ID(N'governance.TR_ReleaseReadinessDecisionRecord_ValidateInsert', N'TR') IS NULL THEN 0 ELSE 1 END" }
            ,@{ Name = "governance.TR_ReleaseReadinessDecisionRecord_BlockUpdateDelete trigger"; Sql = "SELECT CASE WHEN OBJECT_ID(N'governance.TR_ReleaseReadinessDecisionRecord_BlockUpdateDelete', N'TR') IS NULL THEN 0 ELSE 1 END" }
            ,@{ Name = "ReleaseReadinessDecisionRecord no release approval check constraint"; Sql = "SELECT COUNT(*) FROM sys.check_constraints WHERE parent_object_id = OBJECT_ID(N'governance.ReleaseReadinessDecisionRecord') AND name = N'CK_ReleaseReadinessDecisionRecord_NoReleaseApproval'" }
            ,@{ Name = "ReleaseReadinessDecisionRecord no release execution check constraint"; Sql = "SELECT COUNT(*) FROM sys.check_constraints WHERE parent_object_id = OBJECT_ID(N'governance.ReleaseReadinessDecisionRecord') AND name = N'CK_ReleaseReadinessDecisionRecord_NoReleaseExecution'" }
            ,@{ Name = "ReleaseReadinessDecisionRecord human review check constraint"; Sql = "SELECT COUNT(*) FROM sys.check_constraints WHERE parent_object_id = OBJECT_ID(N'governance.ReleaseReadinessDecisionRecord') AND name = N'CK_ReleaseReadinessDecisionRecord_HumanReviewRelease'" }
            ,@{ Name = "ReleaseReadinessDecisionRecord ready truth check constraint"; Sql = "SELECT COUNT(*) FROM sys.check_constraints WHERE parent_object_id = OBJECT_ID(N'governance.ReleaseReadinessDecisionRecord') AND name = N'CK_ReleaseReadinessDecisionRecord_ReadyTruth'" }
            ,@{ Name = "memory.MemoryProposal table"; Sql = "SELECT CASE WHEN OBJECT_ID(N'memory.MemoryProposal', N'U') IS NULL THEN 0 ELSE 1 END" }
            ,@{ Name = "memory.MemoryProposalEvidenceReference table"; Sql = "SELECT CASE WHEN OBJECT_ID(N'memory.MemoryProposalEvidenceReference', N'U') IS NULL THEN 0 ELSE 1 END" }
            ,@{ Name = "memory.MemoryProposalGroundingReference table"; Sql = "SELECT CASE WHEN OBJECT_ID(N'memory.MemoryProposalGroundingReference', N'U') IS NULL THEN 0 ELSE 1 END" }
            ,@{ Name = "memory.MemoryProposalWorkflowReference table"; Sql = "SELECT CASE WHEN OBJECT_ID(N'memory.MemoryProposalWorkflowReference', N'U') IS NULL THEN 0 ELSE 1 END" }
            ,@{ Name = "memory.usp_MemoryProposal_Create procedure"; Sql = "SELECT CASE WHEN OBJECT_ID(N'memory.usp_MemoryProposal_Create', N'P') IS NULL THEN 0 ELSE 1 END" }
            ,@{ Name = "memory.usp_MemoryProposal_Get procedure"; Sql = "SELECT CASE WHEN OBJECT_ID(N'memory.usp_MemoryProposal_Get', N'P') IS NULL THEN 0 ELSE 1 END" }
            ,@{ Name = "memory.usp_MemoryProposal_ListByProject procedure"; Sql = "SELECT CASE WHEN OBJECT_ID(N'memory.usp_MemoryProposal_ListByProject', N'P') IS NULL THEN 0 ELSE 1 END" }
            ,@{ Name = "memory.usp_MemoryProposal_ListByStatus procedure"; Sql = "SELECT CASE WHEN OBJECT_ID(N'memory.usp_MemoryProposal_ListByStatus', N'P') IS NULL THEN 0 ELSE 1 END" }
            ,@{ Name = "memory.usp_MemoryProposal_ListByWorkflowRun procedure"; Sql = "SELECT CASE WHEN OBJECT_ID(N'memory.usp_MemoryProposal_ListByWorkflowRun', N'P') IS NULL THEN 0 ELSE 1 END" }
            ,@{ Name = "memory.usp_MemoryProposal_ListBySource procedure"; Sql = "SELECT CASE WHEN OBJECT_ID(N'memory.usp_MemoryProposal_ListBySource', N'P') IS NULL THEN 0 ELSE 1 END" }
            ,@{ Name = "memory.TR_MemoryProposal_ValidateInsert trigger"; Sql = "SELECT CASE WHEN OBJECT_ID(N'memory.TR_MemoryProposal_ValidateInsert', N'TR') IS NULL THEN 0 ELSE 1 END" }
            ,@{ Name = "memory.TR_MemoryProposal_BlockUpdateDelete trigger"; Sql = "SELECT CASE WHEN OBJECT_ID(N'memory.TR_MemoryProposal_BlockUpdateDelete', N'TR') IS NULL THEN 0 ELSE 1 END" }
            ,@{ Name = "MemoryProposal no memory promotion check constraint"; Sql = "SELECT COUNT(*) FROM sys.check_constraints WHERE parent_object_id = OBJECT_ID(N'memory.MemoryProposal') AND name = N'CK_MemoryProposal_NoMemoryPromotion'" }
            ,@{ Name = "MemoryProposal no retrieval authority check constraint"; Sql = "SELECT COUNT(*) FROM sys.check_constraints WHERE parent_object_id = OBJECT_ID(N'memory.MemoryProposal') AND name = N'CK_MemoryProposal_NoRetrievalAuthority'" }
            ,@{ Name = "MemoryProposal no vector index write check constraint"; Sql = "SELECT COUNT(*) FROM sys.check_constraints WHERE parent_object_id = OBJECT_ID(N'memory.MemoryProposal') AND name = N'CK_MemoryProposal_NoVectorIndexWrite'" }
            ,@{ Name = "MemoryProposal status staging-only check constraint"; Sql = "SELECT COUNT(*) FROM sys.check_constraints WHERE parent_object_id = OBJECT_ID(N'memory.MemoryProposal') AND name = N'CK_MemoryProposal_Status_StagingOnly'" }
            ,@{ Name = "MemoryProposalEvidenceReference evidence type check constraint"; Sql = "SELECT COUNT(*) FROM sys.check_constraints WHERE parent_object_id = OBJECT_ID(N'memory.MemoryProposalEvidenceReference') AND name = N'CK_MemoryProposalEvidenceReference_EvidenceType_Allowed'" }
            ,@{ Name = "MemoryProposalWorkflowReference target check constraint"; Sql = "SELECT COUNT(*) FROM sys.check_constraints WHERE parent_object_id = OBJECT_ID(N'memory.MemoryProposalWorkflowReference') AND name = N'CK_MemoryProposalWorkflowReference_Target'" }
            ,@{ Name = "dbo.ProjectDocuments table"; Sql = "SELECT CASE WHEN OBJECT_ID(N'dbo.ProjectDocuments', N'U') IS NULL THEN 0 ELSE 1 END" }
            ,@{ Name = "dbo.UserMutationAttribution table"; Sql = "SELECT CASE WHEN OBJECT_ID(N'dbo.UserMutationAttribution', N'U') IS NULL THEN 0 ELSE 1 END" }
            ,@{ Name = "UserMutationAttribution append-only trigger"; Sql = "SELECT CASE WHEN OBJECT_ID(N'dbo.TR_UserMutationAttribution_BlockUpdateDelete', N'TR') IS NULL THEN 0 ELSE 1 END" }
            ,@{ Name = "dbo.ProjectDocumentVersions table"; Sql = "SELECT CASE WHEN OBJECT_ID(N'dbo.ProjectDocumentVersions', N'U') IS NULL THEN 0 ELSE 1 END" }
            ,@{ Name = "dbo.ProjectDocumentLinks table"; Sql = "SELECT CASE WHEN OBJECT_ID(N'dbo.ProjectDocumentLinks', N'U') IS NULL THEN 0 ELSE 1 END" }
            ,@{ Name = "ChatMessages ReplyToMessageId column"; Sql = "SELECT CASE WHEN COL_LENGTH(N'dbo.ChatMessages', N'ReplyToMessageId') IS NULL THEN 0 ELSE 1 END" }
            ,@{ Name = "ChatMessages reply foreign key"; Sql = "SELECT COUNT(*) FROM sys.foreign_keys WHERE parent_object_id = OBJECT_ID(N'dbo.ChatMessages') AND name = N'FK_ChatMessages_ReplyToMessage'" }
            ,@{ Name = "ChatMessages reply index"; Sql = "SELECT COUNT(*) FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.ChatMessages') AND name = N'IX_ChatMessages_ReplyToMessageId'" }
            ,@{ Name = "ProjectDocuments Origin column"; Sql = "SELECT CASE WHEN COL_LENGTH(N'dbo.ProjectDocuments', N'Origin') IS NULL THEN 0 ELSE 1 END" }
            ,@{ Name = "ProjectDocuments ProcessingStatus column"; Sql = "SELECT CASE WHEN COL_LENGTH(N'dbo.ProjectDocuments', N'ProcessingStatus') IS NULL THEN 0 ELSE 1 END" }
            ,@{ Name = "ProjectDocuments Visibility column"; Sql = "SELECT CASE WHEN COL_LENGTH(N'dbo.ProjectDocuments', N'Visibility') IS NULL THEN 0 ELSE 1 END" }
            ,@{ Name = "ProjectDocuments upload metadata columns"; Sql = "SELECT CASE WHEN COL_LENGTH(N'dbo.ProjectDocuments', N'OriginalFileName') IS NULL OR COL_LENGTH(N'dbo.ProjectDocuments', N'MediaType') IS NULL OR COL_LENGTH(N'dbo.ProjectDocuments', N'ByteSize') IS NULL THEN 0 ELSE 1 END" }
            ,@{ Name = "ProjectDocuments processing evidence columns"; Sql = "SELECT CASE WHEN COL_LENGTH(N'dbo.ProjectDocuments', N'ProcessingFailureReason') IS NULL OR COL_LENGTH(N'dbo.ProjectDocuments', N'ProcessingStartedAtUtc') IS NULL OR COL_LENGTH(N'dbo.ProjectDocuments', N'ProcessingCompletedAtUtc') IS NULL THEN 0 ELSE 1 END" }
            ,@{ Name = "ProjectDocuments origin check constraint"; Sql = "SELECT COUNT(*) FROM sys.check_constraints WHERE parent_object_id = OBJECT_ID(N'dbo.ProjectDocuments') AND name = N'CK_ProjectDocuments_Origin_Allowed'" }
            ,@{ Name = "ProjectDocuments processing status check constraint"; Sql = "SELECT COUNT(*) FROM sys.check_constraints WHERE parent_object_id = OBJECT_ID(N'dbo.ProjectDocuments') AND name = N'CK_ProjectDocuments_ProcessingStatus_Allowed'" }
            ,@{ Name = "ProjectDocuments visibility check constraint"; Sql = "SELECT COUNT(*) FROM sys.check_constraints WHERE parent_object_id = OBJECT_ID(N'dbo.ProjectDocuments') AND name = N'CK_ProjectDocuments_Visibility_Allowed'" }
            ,@{ Name = "ProjectDocuments byte size check constraint"; Sql = "SELECT COUNT(*) FROM sys.check_constraints WHERE parent_object_id = OBJECT_ID(N'dbo.ProjectDocuments') AND name = N'CK_ProjectDocuments_ByteSize_NonNegative'" }
            ,@{ Name = "ProjectDocuments processing timeline check constraint"; Sql = "SELECT COUNT(*) FROM sys.check_constraints WHERE parent_object_id = OBJECT_ID(N'dbo.ProjectDocuments') AND name = N'CK_ProjectDocuments_ProcessingTimeline_Ordered'" }
            ,@{ Name = "ProjectDocuments processing failure state constraint"; Sql = "SELECT COUNT(*) FROM sys.check_constraints WHERE parent_object_id = OBJECT_ID(N'dbo.ProjectDocuments') AND name = N'CK_ProjectDocuments_ProcessingFailure_State'" }
            ,@{ Name = "dbo.ProjectProfiles table"; Sql = "SELECT CASE WHEN OBJECT_ID(N'dbo.ProjectProfiles', N'U') IS NULL THEN 0 ELSE 1 END" }
            ,@{ Name = "dbo.ProjectCommands table"; Sql = "SELECT CASE WHEN OBJECT_ID(N'dbo.ProjectCommands', N'U') IS NULL THEN 0 ELSE 1 END" }
            ,@{ Name = "dbo.ProjectProfileOptions table"; Sql = "SELECT CASE WHEN OBJECT_ID(N'dbo.ProjectProfileOptions', N'U') IS NULL THEN 0 ELSE 1 END" }
            ,@{ Name = "dbo.ProjectFiles table"; Sql = "SELECT CASE WHEN OBJECT_ID(N'dbo.ProjectFiles', N'U') IS NULL THEN 0 ELSE 1 END" }
            ,@{ Name = "Projects LocalPath column"; Sql = "SELECT CASE WHEN COL_LENGTH(N'dbo.Projects', N'LocalPath') IS NULL THEN 0 ELSE 1 END" }
            ,@{ Name = "Projects LastIndexedUtc column"; Sql = "SELECT CASE WHEN COL_LENGTH(N'dbo.Projects', N'LastIndexedUtc') IS NULL THEN 0 ELSE 1 END" }
            ,@{ Name = "Projects IndexingStatus column"; Sql = "SELECT CASE WHEN COL_LENGTH(N'dbo.Projects', N'IndexingStatus') IS NULL THEN 0 ELSE 1 END" }
            ,@{ Name = "agent.AgentRunAuditEnvelope table"; Sql = "SELECT CASE WHEN OBJECT_ID(N'agent.AgentRunAuditEnvelope', N'U') IS NULL THEN 0 ELSE 1 END" }
            ,@{ Name = "AgentRunAuditEnvelope no private reasoning constraint"; Sql = "SELECT COUNT(*) FROM sys.check_constraints WHERE parent_object_id = OBJECT_ID(N'agent.AgentRunAuditEnvelope') AND name = N'CK_AgentRunAuditEnvelope_NoRawPrivateReasoning'" }
            ,@{ Name = "dbo.ProjectChannels table"; Sql = "SELECT CASE WHEN OBJECT_ID(N'dbo.ProjectChannels', N'U') IS NULL THEN 0 ELSE 1 END" }
            ,@{ Name = "dbo.ProjectChannelMembers table"; Sql = "SELECT CASE WHEN OBJECT_ID(N'dbo.ProjectChannelMembers', N'U') IS NULL THEN 0 ELSE 1 END" }
            ,@{ Name = "ProjectChannels active slug unique index"; Sql = "SELECT COUNT(*) FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.ProjectChannels') AND name = N'UX_ProjectChannels_TenantProjectSlug_Active'" }
            ,@{ Name = "ProjectChannelMembers active user unique index"; Sql = "SELECT COUNT(*) FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.ProjectChannelMembers') AND name = N'UX_ProjectChannelMembers_ChannelUser_Active'" }
            ,@{ Name = "ProjectChannelMembers scoped channel foreign key"; Sql = "SELECT COUNT(*) FROM sys.foreign_keys WHERE parent_object_id = OBJECT_ID(N'dbo.ProjectChannelMembers') AND name = N'FK_ProjectChannelMembers_Channels'" }
            ,@{ Name = "ProjectChannelMembers role check constraint"; Sql = "SELECT COUNT(*) FROM sys.check_constraints WHERE parent_object_id = OBJECT_ID(N'dbo.ProjectChannelMembers') AND name = N'CK_ProjectChannelMembers_ChannelRole'" }
            ,@{ Name = "ProjectChannelMembers notification check constraint"; Sql = "SELECT COUNT(*) FROM sys.check_constraints WHERE parent_object_id = OBJECT_ID(N'dbo.ProjectChannelMembers') AND name = N'CK_ProjectChannelMembers_NotificationLevel'" }
        )

        foreach ($check in $checks) {
            Test-ScalarExists -Connection $connection -Name $check.Name -Sql $check.Sql
        }
    }
    finally {
        $connection.Dispose()
    }

    Write-Host "Database migration verification passed."
    exit 0
}
catch {
    Write-Error $_.Exception.Message
    exit 1
}

