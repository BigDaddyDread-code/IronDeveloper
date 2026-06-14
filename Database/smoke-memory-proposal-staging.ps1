param(
    [Parameter(Mandatory = $true)]
    [string]$ConnectionString
)

$ErrorActionPreference = "Stop"

Add-Type -AssemblyName "System.Data"
$connection = [System.Data.SqlClient.SqlConnection]::new($ConnectionString)
$connection.Open()
try {
    $checks = @(
        "memory.MemoryProposal",
        "memory.MemoryProposalEvidenceReference",
        "memory.MemoryProposalGroundingReference",
        "memory.MemoryProposalWorkflowReference",
        "memory.usp_MemoryProposal_Create",
        "memory.usp_MemoryProposal_Get",
        "memory.usp_MemoryProposal_ListByProject",
        "memory.usp_MemoryProposal_ListByStatus",
        "memory.usp_MemoryProposal_ListByWorkflowRun",
        "memory.usp_MemoryProposal_ListBySource"
    )

    foreach ($name in $checks) {
        $command = $connection.CreateCommand()
        $command.CommandText = "SELECT CASE WHEN OBJECT_ID(@name) IS NULL THEN 0 ELSE 1 END"
        $null = $command.Parameters.AddWithValue("@name", $name)
        $exists = [int]$command.ExecuteScalar()
        if ($exists -ne 1) {
            throw "Missing SQL object: $name"
        }
    }

    Write-Host "Memory proposal staging smoke passed."
}
finally {
    $connection.Dispose()
}
