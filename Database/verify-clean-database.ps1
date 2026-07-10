[CmdletBinding()]
param(
    [string]$ConnectionString,
    [string]$Server = "(localdb)\MSSQLLocalDB",
    [string]$Database,
    [switch]$KeepDatabase,
    [int]$CommandTimeoutSeconds = 180
)

$ErrorActionPreference = "Stop"

function Get-RepositoryRoot {
    $directory = Get-Item -LiteralPath $PSScriptRoot
    while ($null -ne $directory) {
        if (Test-Path -LiteralPath (Join-Path $directory.FullName "IronDev.slnx")) {
            return $directory.FullName
        }
        $directory = $directory.Parent
    }

    throw "Could not find repository root from $PSScriptRoot."
}

function Test-TestDatabaseName {
    param([string]$Name)

    return -not [string]::IsNullOrWhiteSpace($Name) -and
        ($Name.EndsWith("_Test", [StringComparison]::OrdinalIgnoreCase) -or
         $Name.StartsWith("IronDev_CI_", [StringComparison]::OrdinalIgnoreCase))
}

function Get-DefaultVerificationDatabase {
    param([string]$SourceDatabase)

    if ($SourceDatabase.StartsWith("IronDev_CI_", [StringComparison]::OrdinalIgnoreCase)) {
        return "$($SourceDatabase)_Migration"
    }

    if ($SourceDatabase.EndsWith("_Test", [StringComparison]::OrdinalIgnoreCase)) {
        return "$($SourceDatabase.Substring(0, $SourceDatabase.Length - 5))_Migration_Test"
    }

    return "IronDev_Platform_Migration_Test"
}

function Quote-SqlIdentifier {
    param([Parameter(Mandatory = $true)][string]$Value)

    return "[" + $Value.Replace("]", "]]") + "]"
}

function Split-SqlBatches {
    param([Parameter(Mandatory = $true)][string]$Sql)

    return [Regex]::Split($Sql.Replace("`r`n", "`n"), "(?im)^\s*GO\s*$") |
        Where-Object { -not [string]::IsNullOrWhiteSpace($_) } |
        ForEach-Object { $_.Trim() }
}

function Invoke-SqlBatch {
    param(
        [Parameter(Mandatory = $true)][System.Data.SqlClient.SqlConnection]$Connection,
        [Parameter(Mandatory = $true)][string]$Batch,
        [Parameter(Mandatory = $true)][int]$BatchNumber
    )

    $command = $Connection.CreateCommand()
    $command.CommandText = $Batch
    $command.CommandTimeout = $CommandTimeoutSeconds
    try {
        [void]$command.ExecuteNonQuery()
    }
    catch {
        throw "Base schema failed at batch $BatchNumber. $($_.Exception.Message)"
    }
    finally {
        $command.Dispose()
    }
}

function Invoke-DatabaseCommand {
    param(
        [Parameter(Mandatory = $true)][System.Data.SqlClient.SqlConnectionStringBuilder]$MasterBuilder,
        [Parameter(Mandatory = $true)][string]$Sql
    )

    $connection = [System.Data.SqlClient.SqlConnection]::new($MasterBuilder.ConnectionString)
    try {
        $connection.Open()
        $command = $connection.CreateCommand()
        $command.CommandText = $Sql
        $command.CommandTimeout = $CommandTimeoutSeconds
        try {
            [void]$command.ExecuteNonQuery()
        }
        finally {
            $command.Dispose()
        }
    }
    finally {
        $connection.Dispose()
    }
}

$root = Get-RepositoryRoot
$baseBuilder = if (-not [string]::IsNullOrWhiteSpace($ConnectionString)) {
    [System.Data.SqlClient.SqlConnectionStringBuilder]::new($ConnectionString)
}
else {
    $builder = [System.Data.SqlClient.SqlConnectionStringBuilder]::new()
    $builder["Data Source"] = $Server
    $builder["Initial Catalog"] = "master"
    $builder["Integrated Security"] = $true
    $builder["Encrypt"] = $false
    $builder
}

if ([string]::IsNullOrWhiteSpace($Database)) {
    $Database = Get-DefaultVerificationDatabase -SourceDatabase $baseBuilder.InitialCatalog
}

if (-not (Test-TestDatabaseName -Name $Database)) {
    throw "Refusing clean migration verification against '$Database'. The database must end in '_Test' or start with 'IronDev_CI_'."
}

$masterBuilder = [System.Data.SqlClient.SqlConnectionStringBuilder]::new($baseBuilder.ConnectionString)
$masterBuilder["Initial Catalog"] = "master"
$targetBuilder = [System.Data.SqlClient.SqlConnectionStringBuilder]::new($baseBuilder.ConnectionString)
$targetBuilder["Initial Catalog"] = $Database
$quotedDatabase = Quote-SqlIdentifier -Value $Database
$databaseLiteral = $Database.Replace("'", "''")
$created = $false
$previousMigrationConnection = $env:IRONDEV_MIGRATION_CONNECTION_STRING

try {
    Write-Host "Recreating isolated migration database '$Database'."
    Invoke-DatabaseCommand -MasterBuilder $masterBuilder -Sql "
        IF DB_ID(N'$databaseLiteral') IS NOT NULL
        BEGIN
            ALTER DATABASE $quotedDatabase SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
            DROP DATABASE $quotedDatabase;
        END;
        CREATE DATABASE $quotedDatabase;"
    $created = $true

    $schemaPath = Join-Path $root "Database\rebuild_db.sql"
    $schemaBatches = @(Split-SqlBatches -Sql (Get-Content -LiteralPath $schemaPath -Raw))
    $targetConnection = [System.Data.SqlClient.SqlConnection]::new($targetBuilder.ConnectionString)
    try {
        $targetConnection.Open()
        $batchNumber = 0
        foreach ($batch in $schemaBatches) {
            if ($batch.IndexOf("master.sys.databases", [StringComparison]::OrdinalIgnoreCase) -ge 0 -or
                $batch -match "(?im)^\s*USE\s+\[IronDeveloper\]") {
                continue
            }

            $batchNumber++
            Invoke-SqlBatch -Connection $targetConnection -Batch $batch -BatchNumber $batchNumber
        }
    }
    finally {
        $targetConnection.Dispose()
    }

    $env:IRONDEV_MIGRATION_CONNECTION_STRING = $targetBuilder.ConnectionString
    $pwsh = (Get-Process -Id $PID -ErrorAction Stop).Path
    foreach ($scriptName in @("apply-migrations.ps1", "apply-migrations.ps1", "verify-migrations.ps1")) {
        & $pwsh -NoProfile -File (Join-Path $PSScriptRoot $scriptName)
        if ($LASTEXITCODE -ne 0) {
            throw "$scriptName failed against the isolated clean database."
        }
    }

    Write-Host "PASS clean database migration verification"
    Write-Host "  Database: $Database"
    Write-Host "  Base schema: Database/rebuild_db.sql"
    Write-Host "  Migrations: applied twice"
    Write-Host "  Verifier: passed"
}
finally {
    $env:IRONDEV_MIGRATION_CONNECTION_STRING = $previousMigrationConnection
    if ($created -and -not $KeepDatabase) {
        Invoke-DatabaseCommand -MasterBuilder $masterBuilder -Sql "
            IF DB_ID(N'$databaseLiteral') IS NOT NULL
            BEGIN
                ALTER DATABASE $quotedDatabase SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
                DROP DATABASE $quotedDatabase;
            END;"
        Write-Host "Removed isolated migration database '$Database'."
    }
}
