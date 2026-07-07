[CmdletBinding()]
param(
    [string]$Server,
    [string]$Database,
    [string]$ConnectionString,
    [switch]$TrustServerCertificate,
    [switch]$ResolveConnectionStringOnly,
    [int]$CommandTimeoutSeconds = 120
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

function Test-LocalDeveloperSqlTarget {
    param([Parameter(Mandatory = $true)][string]$ServerName)

    if ($ServerName -like "(localdb)\*") {
        return $true
    }

    return $ServerName -match "^(localhost|127\.0\.0\.1|\.)(,|\\|$)"
}

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
    if (Test-LocalDeveloperSqlTarget -ServerName $Server) {
        # DEMO-REHEARSAL-001 residual R2 (review-narrowed): legacy
        # System.Data.SqlClient cannot open an Encrypt=true connection to LocalDB,
        # so explicit LOCAL developer targets (LocalDB, localhost, 127.0.0.1, .)
        # run unencrypted. This branch must never widen beyond local targets.
        $builder["Encrypt"] = $false
        $builder["TrustServerCertificate"] = $false
    }
    else {
        # Every non-local generated connection stays encrypted by default.
        # -TrustServerCertificate keeps encryption ON and trusts the server
        # certificate (the self-signed remote case). Fully custom needs go
        # through -ConnectionString.
        $builder["Encrypt"] = $true
        $builder["TrustServerCertificate"] = [bool]$TrustServerCertificate
    }
    return $builder.ConnectionString
}

function Split-SqlBatches {
    param([Parameter(Mandatory = $true)][string]$Sql)

    return [System.Text.RegularExpressions.Regex]::Split(
        $Sql.Replace("`r`n", "`n"),
        "(?im)^\s*GO\s*$") |
        Where-Object { -not [string]::IsNullOrWhiteSpace($_) } |
        ForEach-Object { $_.Trim() }
}

function Invoke-SqlBatch {
    param(
        [Parameter(Mandatory = $true)][System.Data.SqlClient.SqlConnection]$Connection,
        [Parameter(Mandatory = $true)][string]$Batch,
        [Parameter(Mandatory = $true)][string]$MigrationId,
        [Parameter(Mandatory = $true)][int]$BatchNumber
    )

    $command = $Connection.CreateCommand()
    $command.CommandText = $Batch
    $command.CommandTimeout = $CommandTimeoutSeconds
    try {
        [void]$command.ExecuteNonQuery()
    }
    catch {
        throw "Migration '$MigrationId' failed at batch $BatchNumber. $($_.Exception.Message)"
    }
    finally {
        $command.Dispose()
    }
}

try {
    $root = Get-RepositoryRoot
    $manifestPath = Join-Path $root "Database/migrations.json"
    if (-not (Test-Path -LiteralPath $manifestPath)) {
        throw "Migration manifest not found: $manifestPath"
    }

    $manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
    if ($null -eq $manifest.migrations -or $manifest.migrations.Count -eq 0) {
        throw "Migration manifest contains no migrations."
    }

    $sqlConnectionString = New-ConnectionString
    if ($ResolveConnectionStringOnly) {
        # Test seam: print the resolved connection string and stop before any
        # connection is opened. ApplyMigrationsScriptContractTests pins the
        # encryption defaults through this switch.
        Write-Output $sqlConnectionString
        exit 0
    }

    $connection = New-Object System.Data.SqlClient.SqlConnection $sqlConnectionString
    $connection.Open()

    try {
        foreach ($migration in $manifest.migrations) {
            if ([string]::IsNullOrWhiteSpace($migration.id)) {
                throw "Migration entry is missing id."
            }

            if ([string]::IsNullOrWhiteSpace($migration.path)) {
                throw "Migration '$($migration.id)' is missing path."
            }

            $migrationPath = Join-Path $root ([string]$migration.path)
            if (-not (Test-Path -LiteralPath $migrationPath)) {
                throw "Migration file not found for '$($migration.id)': $migrationPath"
            }

            Write-Host "Applying migration $($migration.id) from $($migration.path)"
            $sql = Get-Content -LiteralPath $migrationPath -Raw
            $batches = @(Split-SqlBatches -Sql $sql)
            for ($i = 0; $i -lt $batches.Count; $i++) {
                Invoke-SqlBatch -Connection $connection -Batch $batches[$i] -MigrationId $migration.id -BatchNumber ($i + 1)
            }
        }
    }
    finally {
        $connection.Dispose()
    }

    Write-Host "Database migrations applied successfully."
    exit 0
}
catch {
    Write-Error $_.Exception.Message
    exit 1
}
