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
