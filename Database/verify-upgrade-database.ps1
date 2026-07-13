[CmdletBinding()]
param(
    [string]$ConnectionString,
    [string]$Server = "(localdb)\MSSQLLocalDB",
    [string]$Database,
    [switch]$KeepDatabase,
    [int]$CommandTimeoutSeconds = 180
)

$ErrorActionPreference = "Stop"
$baselineCommit = "7f0e1058"
$baselineMigrationId = "2026-07-cln-12-user-mutation-attribution"

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

function Open-SqlConnection {
    param([Parameter(Mandatory = $true)][System.Data.SqlClient.SqlConnection]$Connection)

    for ($attempt = 1; $attempt -le 10; $attempt++) {
        try {
            $Connection.Open()
            return
        }
        catch {
            if ($attempt -ge 10) {
                throw
            }
            Start-Sleep -Milliseconds 500
        }
    }
}

function Invoke-DatabaseCommand {
    param(
        [Parameter(Mandatory = $true)][System.Data.SqlClient.SqlConnectionStringBuilder]$MasterBuilder,
        [Parameter(Mandatory = $true)][string]$Sql
    )

    $connection = [System.Data.SqlClient.SqlConnection]::new($MasterBuilder.ConnectionString)
    try {
        Open-SqlConnection -Connection $connection
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

function Invoke-SqlFile {
    param(
        [Parameter(Mandatory = $true)][System.Data.SqlClient.SqlConnectionStringBuilder]$TargetBuilder,
        [Parameter(Mandatory = $true)][string]$Path,
        [switch]$SkipDatabaseBootstrapBatches
    )

    $connection = [System.Data.SqlClient.SqlConnection]::new($TargetBuilder.ConnectionString)
    try {
        Open-SqlConnection -Connection $connection
        $batchNumber = 0
        foreach ($batch in @(Split-SqlBatches -Sql (Get-Content -LiteralPath $Path -Raw))) {
            if ($SkipDatabaseBootstrapBatches -and
                ($batch.IndexOf("master.sys.databases", [StringComparison]::OrdinalIgnoreCase) -ge 0 -or
                 $batch -match "(?im)^\s*USE\s+\[IronDeveloper\]")) {
                continue
            }

            $batchNumber++
            $command = $connection.CreateCommand()
            $command.CommandText = $batch
            $command.CommandTimeout = $CommandTimeoutSeconds
            try {
                [void]$command.ExecuteNonQuery()
            }
            catch {
                throw "SQL file '$Path' failed at batch $batchNumber. $($_.Exception.Message)"
            }
            finally {
                $command.Dispose()
            }
        }
    }
    finally {
        $connection.Dispose()
    }
}

function Invoke-MigrationTool {
    param(
        [Parameter(Mandatory = $true)][string]$ScriptName,
        [string[]]$Arguments = @()
    )

    $pwsh = (Get-Process -Id $PID -ErrorAction Stop).Path
    & $pwsh -NoProfile -File (Join-Path $PSScriptRoot $ScriptName) @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "$ScriptName failed during CLN-21 upgrade verification."
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
    $Database = "IronDev_Upgrade_$([Guid]::NewGuid().ToString('N').Substring(0, 12))_Test"
}
if (-not (Test-TestDatabaseName -Name $Database)) {
    throw "Refusing upgrade verification against '$Database'. The database must end in '_Test' or start with 'IronDev_CI_'."
}

$masterBuilder = [System.Data.SqlClient.SqlConnectionStringBuilder]::new($baseBuilder.ConnectionString)
$masterBuilder["Initial Catalog"] = "master"
$targetBuilder = [System.Data.SqlClient.SqlConnectionStringBuilder]::new($baseBuilder.ConnectionString)
$targetBuilder["Initial Catalog"] = $Database
$quotedDatabase = Quote-SqlIdentifier -Value $Database
$databaseLiteral = $Database.Replace("'", "''")
$databaseCreated = $false
$previousMigrationConnection = $env:IRONDEV_MIGRATION_CONNECTION_STRING

try {
    Write-Host "Recreating isolated upgrade database '$Database'."
    Invoke-DatabaseCommand -MasterBuilder $masterBuilder -Sql "
        IF DB_ID(N'$databaseLiteral') IS NOT NULL
        BEGIN
            ALTER DATABASE $quotedDatabase SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
            DROP DATABASE $quotedDatabase;
        END;
        CREATE DATABASE $quotedDatabase;"
    $databaseCreated = $true

    $baselineSchema = Join-Path $root "Database\baselines\7f0e1058_rebuild_db.sql"
    $baselineFixture = Join-Path $root "Database\baselines\cln_21_pre_cln_19_runtime_fixture.sql"
    $preservationVerifier = Join-Path $root "Database\verify_upgrade_preservation.sql"

    Invoke-SqlFile -TargetBuilder $targetBuilder -Path $baselineSchema -SkipDatabaseBootstrapBatches

    $env:IRONDEV_MIGRATION_CONNECTION_STRING = $targetBuilder.ConnectionString
    Invoke-MigrationTool -ScriptName "apply-migrations.ps1" -Arguments @("-ThroughMigrationId", $baselineMigrationId)
    Invoke-SqlFile -TargetBuilder $targetBuilder -Path $baselineFixture

    Invoke-MigrationTool -ScriptName "apply-migrations.ps1"
    Invoke-MigrationTool -ScriptName "verify-migrations.ps1"
    Invoke-SqlFile -TargetBuilder $targetBuilder -Path $preservationVerifier

    Write-Host "PASS upgrade migration verification"
    Write-Host "  Starting schema commit: $baselineCommit"
    Write-Host "  Baseline manifest through: $baselineMigrationId"
    Write-Host "  Current migration manifest: applied"
    Write-Host "  Schema verifier: passed"
    Write-Host "  Data-preservation assertions: passed"
}
finally {
    $env:IRONDEV_MIGRATION_CONNECTION_STRING = $previousMigrationConnection
    if ($databaseCreated -and -not $KeepDatabase) {
        Invoke-DatabaseCommand -MasterBuilder $masterBuilder -Sql "
            IF DB_ID(N'$databaseLiteral') IS NOT NULL
            BEGIN
                ALTER DATABASE $quotedDatabase SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
                DROP DATABASE $quotedDatabase;
            END;"
        Write-Host "Removed isolated upgrade database '$Database'."
    }
}
