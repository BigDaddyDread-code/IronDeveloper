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
            @{ Name = "ToolGateDecision no memory promotion check constraint"; Sql = "SELECT COUNT(*) FROM sys.check_constraints WHERE parent_object_id = OBJECT_ID(N'governance.ToolGateDecision') AND name = N'CK_ToolGateDecision_NoMemoryPromotion'" }
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
