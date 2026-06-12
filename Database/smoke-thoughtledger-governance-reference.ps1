param(
    [string]$Server = ".\SQLEXPRESS",
    [string]$Database = "IronDeveloper_Test",
    [string]$ConnectionString,
    [switch]$TrustServerCertificate
)

$ErrorActionPreference = "Stop"

function New-ConnectionString {
    if (-not [string]::IsNullOrWhiteSpace($ConnectionString)) {
        return $ConnectionString
    }

    $builder = New-Object Microsoft.Data.SqlClient.SqlConnectionStringBuilder
    $builder["Data Source"] = $Server
    $builder["Initial Catalog"] = $Database
    $builder["Integrated Security"] = $true
    $builder["Encrypt"] = $true
    if ($TrustServerCertificate) {
        $builder["Trust Server Certificate"] = $true
    }

    return $builder.ConnectionString
}

function Invoke-Scalar {
    param(
        [Microsoft.Data.SqlClient.SqlConnection]$Connection,
        [string]$Sql,
        [hashtable]$Parameters = @{}
    )

    $command = $Connection.CreateCommand()
    $command.CommandText = $Sql
    foreach ($key in $Parameters.Keys) {
        $parameter = $command.Parameters.Add("@$key", [Microsoft.Data.SqlDbType]::NVarChar, -1)
        $parameter.Value = $Parameters[$key]
    }

    return $command.ExecuteScalar()
}

Add-Type -AssemblyName "Microsoft.Data.SqlClient" | Out-Null

$connection = [Microsoft.Data.SqlClient.SqlConnection]::new((New-ConnectionString))
$connection.Open()
try {
    $projectId = [guid]::NewGuid()
    $eventId = [guid]::NewGuid()
    $referenceId = [guid]::NewGuid()
    $entryId = "thought-smoke-$referenceId"
    $correlationId = [guid]::NewGuid()

    $eventCommand = $connection.CreateCommand()
    $eventCommand.CommandType = [System.Data.CommandType]::StoredProcedure
    $eventCommand.CommandText = "governance.AppendGovernanceEvent"
    $eventCommand.Parameters.Add("@EventId", [Microsoft.Data.SqlDbType]::UniqueIdentifier).Value = $eventId
    $eventCommand.Parameters.Add("@ProjectId", [Microsoft.Data.SqlDbType]::UniqueIdentifier).Value = $projectId
    $eventCommand.Parameters.Add("@EventType", [Microsoft.Data.SqlDbType]::NVarChar, 200).Value = "thoughtledger.reference.smoke.event"
    $eventCommand.Parameters.Add("@ActorType", [Microsoft.Data.SqlDbType]::NVarChar, 100).Value = "system_test_fixture"
    $eventCommand.Parameters.Add("@ActorId", [Microsoft.Data.SqlDbType]::NVarChar, 200).Value = "smoke"
    $eventCommand.Parameters.Add("@CorrelationId", [Microsoft.Data.SqlDbType]::UniqueIdentifier).Value = $correlationId
    $eventCommand.Parameters.Add("@CausationId", [Microsoft.Data.SqlDbType]::UniqueIdentifier).Value = [DBNull]::Value
    $eventCommand.Parameters.Add("@SubjectType", [Microsoft.Data.SqlDbType]::NVarChar, 100).Value = "thought_ledger_entry"
    $eventCommand.Parameters.Add("@SubjectId", [Microsoft.Data.SqlDbType]::NVarChar, 200).Value = $entryId
    $eventCommand.Parameters.Add("@PayloadVersion", [Microsoft.Data.SqlDbType]::Int).Value = 1
    $eventCommand.Parameters.Add("@PayloadJson", [Microsoft.Data.SqlDbType]::NVarChar, -1).Value = '{"schema":"thoughtledger.reference.smoke.event.v1","schemaVersion":1}'
    [void]$eventCommand.ExecuteScalar()

    $referenceCommand = $connection.CreateCommand()
    $referenceCommand.CommandType = [System.Data.CommandType]::StoredProcedure
    $referenceCommand.CommandText = "governance.usp_ThoughtLedgerGovernanceEventReference_Record"
    $referenceCommand.Parameters.Add("@ThoughtLedgerGovernanceEventReferenceId", [Microsoft.Data.SqlDbType]::UniqueIdentifier).Value = $referenceId
    $referenceCommand.Parameters.Add("@ProjectId", [Microsoft.Data.SqlDbType]::UniqueIdentifier).Value = $projectId
    $referenceCommand.Parameters.Add("@ThoughtLedgerEntryId", [Microsoft.Data.SqlDbType]::NVarChar, 200).Value = $entryId
    $referenceCommand.Parameters.Add("@GovernanceEventId", [Microsoft.Data.SqlDbType]::UniqueIdentifier).Value = $eventId
    $referenceCommand.Parameters.Add("@ReferenceType", [Microsoft.Data.SqlDbType]::NVarChar, 50).Value = "Observed"
    $referenceCommand.Parameters.Add("@ReasonCode", [Microsoft.Data.SqlDbType]::NVarChar, 100).Value = "SMOKE_REFERENCE"
    $referenceCommand.Parameters.Add("@Reason", [Microsoft.Data.SqlDbType]::NVarChar, 1000).Value = "Smoke reference cites governance event as evidence only."
    $referenceCommand.Parameters.Add("@CorrelationId", [Microsoft.Data.SqlDbType]::UniqueIdentifier).Value = $correlationId
    $referenceCommand.Parameters.Add("@CausationId", [Microsoft.Data.SqlDbType]::UniqueIdentifier).Value = [DBNull]::Value
    $referenceCommand.Parameters.Add("@CreatedByActorType", [Microsoft.Data.SqlDbType]::NVarChar, 100).Value = "system_test_fixture"
    $referenceCommand.Parameters.Add("@CreatedByActorId", [Microsoft.Data.SqlDbType]::NVarChar, 200).Value = "smoke"
    $referenceCommand.Parameters.Add("@MetadataVersion", [Microsoft.Data.SqlDbType]::Int).Value = 1
    $referenceCommand.Parameters.Add("@MetadataJson", [Microsoft.Data.SqlDbType]::NVarChar, -1).Value = '{"schema":"thoughtledger.governance_event_reference.v1","schemaVersion":1,"source":"smoke","grantsApproval":false,"grantsExecution":false,"mutatesSource":false,"promotesMemory":false,"startsWorkflow":false,"satisfiesPolicy":false}'
    $referenceCommand.Parameters.Add("@CreatedUtc", [Microsoft.Data.SqlDbType]::DateTime2).Value = [DBNull]::Value
    [void]$referenceCommand.ExecuteScalar()

    $count = Invoke-Scalar -Connection $connection -Sql "SELECT COUNT(1) FROM governance.ThoughtLedgerGovernanceEventReference WHERE ThoughtLedgerGovernanceEventReferenceId = @id" -Parameters @{ id = $referenceId.ToString() }
    if ([int]$count -ne 1) {
        throw "ThoughtLedger governance reference smoke failed: reference row was not recorded."
    }

    Write-Host "ThoughtLedger governance event reference smoke passed: $referenceId"
}
finally {
    $connection.Dispose()
}