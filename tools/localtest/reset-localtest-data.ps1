param(
    [string]$ConfigPath,
    [string]$SqlServer,
    [switch]$SkipSchema
)

$ErrorActionPreference = "Stop"

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..\..")
if ([string]::IsNullOrWhiteSpace($ConfigPath)) {
    $ConfigPath = Join-Path $repoRoot "IronDev.Api\appsettings.LocalTest.json"
}

if (-not (Test-Path $ConfigPath)) {
    throw "LocalTest config was not found at '$ConfigPath'."
}

$config = Get-Content -Path $ConfigPath -Raw | ConvertFrom-Json
$connectionString = $config.ConnectionStrings.IronDeveloperDb
if ([string]::IsNullOrWhiteSpace($connectionString)) {
    throw "ConnectionStrings:IronDeveloperDb is missing from '$ConfigPath'."
}

$builder = [System.Data.SqlClient.SqlConnectionStringBuilder]::new($connectionString)
$database = $builder.InitialCatalog
if ([string]::IsNullOrWhiteSpace($database) -or $database -notmatch "Test") {
    throw "Refusing to reset database '$database'. LocalTest database name must contain 'Test'."
}

if ([string]::IsNullOrWhiteSpace($SqlServer)) {
    $SqlServer = $builder.DataSource
}

if ([string]::IsNullOrWhiteSpace($SqlServer)) {
    throw "SQL Server data source could not be resolved from '$ConfigPath'."
}

$workspaceRoot = $config.LocalTest.WorkspaceRoot
$logsRoot = $config.LocalTest.LogsRoot
if ([string]::IsNullOrWhiteSpace($workspaceRoot) -or $workspaceRoot -notmatch "Test") {
    throw "Refusing to use workspace root '$workspaceRoot'. LocalTest workspace root must contain 'Test'."
}

if ([string]::IsNullOrWhiteSpace($logsRoot) -or $logsRoot -notmatch "Test") {
    throw "Refusing to use logs root '$logsRoot'. LocalTest logs root must contain 'Test'."
}

New-Item -ItemType Directory -Force -Path $workspaceRoot, $logsRoot | Out-Null

$sqlcmd = Get-Command sqlcmd -ErrorAction SilentlyContinue
if ($null -eq $sqlcmd) {
    throw "sqlcmd was not found. Install SQL Server command-line tools, then rerun this LocalTest reset."
}

function Invoke-SqlFile {
    param(
        [Parameter(Mandatory = $true)][string]$DatabaseName,
        [Parameter(Mandatory = $true)][string]$Path
    )

    $args = @("-S", $SqlServer, "-d", $DatabaseName, "-b", "-i", $Path)

    if ($builder.IntegratedSecurity) {
        $args += "-E"
    }
    else {
        $args += @("-U", $builder.UserID, "-P", $builder.Password)
    }

    & sqlcmd @args
    if ($LASTEXITCODE -ne 0) {
        throw "sqlcmd failed while running '$Path'."
    }
}

Write-Host "Resetting LocalTest database '$database' on '$SqlServer'."

if (-not $SkipSchema) {
    $tempDir = Join-Path ([System.IO.Path]::GetTempPath()) ("irondev-localtest-" + [Guid]::NewGuid().ToString("N"))
    New-Item -ItemType Directory -Force -Path $tempDir | Out-Null

    try {
        $schemaPath = Join-Path $tempDir "localtest-schema.sql"
        $documentMigrationPath = Join-Path $tempDir "localtest-documents.sql"

        $schemaPreamble = @"
IF NOT EXISTS (SELECT name FROM master.sys.databases WHERE name = N'$database')
BEGIN
    CREATE DATABASE [$database];
END
GO

USE [$database];
GO

DECLARE @dropForeignKeys NVARCHAR(MAX) = N'';
SELECT @dropForeignKeys +=
    N'ALTER TABLE ' + QUOTENAME(OBJECT_SCHEMA_NAME(parent_object_id)) +
    N'.' + QUOTENAME(OBJECT_NAME(parent_object_id)) +
    N' DROP CONSTRAINT ' + QUOTENAME(name) + N';' + CHAR(13)
FROM sys.foreign_keys;

IF LEN(@dropForeignKeys) > 0
    EXEC sp_executesql @dropForeignKeys;

DECLARE @dropTables NVARCHAR(MAX) = N'';
SELECT @dropTables +=
    N'DROP TABLE ' + QUOTENAME(SCHEMA_NAME(schema_id)) +
    N'.' + QUOTENAME(name) + N';' + CHAR(13)
FROM sys.tables
WHERE is_ms_shipped = 0;

IF LEN(@dropTables) > 0
    EXEC sp_executesql @dropTables;
GO

"@

        $schemaBody = (Get-Content -Path (Join-Path $repoRoot "Database\rebuild_db.sql") -Raw).
            Replace("[IronDeveloper]", "[$database]").
            Replace("name = N'IronDeveloper'", "name = N'$database'").
            Replace("CREATE DATABASE [IronDeveloper]", "CREATE DATABASE [$database]")

        ($schemaPreamble + $schemaBody) | Set-Content -Path $schemaPath -Encoding UTF8

        (Get-Content -Path (Join-Path $repoRoot "Database\migrate_project_documents.sql") -Raw).
            Replace("[IronDeveloper]", "[$database]") |
            Set-Content -Path $documentMigrationPath -Encoding UTF8

        Invoke-SqlFile -DatabaseName "master" -Path $schemaPath
        Invoke-SqlFile -DatabaseName $database -Path $documentMigrationPath
    }
    finally {
        Remove-Item -LiteralPath $tempDir -Recurse -Force -ErrorAction SilentlyContinue
    }
}

Invoke-SqlFile -DatabaseName $database -Path (Join-Path $PSScriptRoot "localtest-seed.sql")

Write-Host "LocalTest reset complete."
Write-Host "Database: $database"
Write-Host "Workspace root: $workspaceRoot"
Write-Host "Logs root: $logsRoot"
Write-Host "Login: localtest@irondev.local / change-me-local-only"
