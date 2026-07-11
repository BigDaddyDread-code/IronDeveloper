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

function Start-LocalDbIfNeeded {
    param([Parameter(Mandatory = $true)][string]$DataSource)

    if ($DataSource -notmatch '^\(localdb\)\\(?<instance>.+)$') {
        return
    }

    $localDb = Get-Command sqllocaldb -ErrorAction SilentlyContinue
    if ($null -eq $localDb) {
        Write-Warning "sqllocaldb was not found; continuing and relying on sqlcmd to reach '$DataSource'."
        return
    }

    $instance = $Matches.instance
    $startExitCode = 0
    $startOutput = @()
    try {
        $startOutput = & $localDb.Source start $instance 2>&1
        $startExitCode = $LASTEXITCODE
    }
    catch {
        $startOutput += $_.Exception.Message
        $startExitCode = if ($null -ne $LASTEXITCODE) { $LASTEXITCODE } else { 1 }
    }
    if ($startExitCode -ne 0) {
        Write-Warning "Could not start LocalDB instance '$instance' through sqllocaldb; attempting to resolve an existing named pipe."
        if ($startOutput.Count -gt 0) {
            Write-Warning (($startOutput | Out-String).Trim())
        }
    }
}

function Resolve-SqlCmdDataSource {
    param([Parameter(Mandatory = $true)][string]$DataSource)

    if ($DataSource -notmatch '^\(localdb\)\\(?<instance>.+)$') {
        return $DataSource
    }

    $instance = $Matches.instance
    $localDb = Get-Command sqllocaldb -ErrorAction SilentlyContinue
    if ($null -ne $localDb) {
        $info = @()
        try {
            $info = & $localDb.Source info $instance 2>$null
        }
        catch {
            $info = @()
        }
        foreach ($line in $info) {
            $match = [regex]::Match($line, '^\s*Instance pipe name:\s*(?<pipe>.+?)\s*$')
            if ($match.Success -and -not [string]::IsNullOrWhiteSpace($match.Groups['pipe'].Value)) {
                $pipe = $match.Groups['pipe'].Value.Trim()
                if ($pipe.StartsWith("np:", [StringComparison]::OrdinalIgnoreCase)) {
                    return $pipe
                }
                return "np:$pipe"
            }
        }
    }

    $errorLog = Join-Path $env:LOCALAPPDATA "Microsoft\Microsoft SQL Server Local DB\Instances\$instance\error.log"
    if (Test-Path -LiteralPath $errorLog -PathType Leaf) {
        $pipeAnnouncement = Select-String -LiteralPath $errorLog -Pattern 'Server local connection provider is ready to accept connection on' |
            Select-Object -Last 1
        if ($null -ne $pipeAnnouncement) {
            $match = [regex]::Match($pipeAnnouncement.Line, 'Server local connection provider is ready to accept connection on \[(?<pipe>[^\]]+)\]')
            if ($match.Success) {
                $pipe = $match.Groups['pipe'].Value.Trim()
                if ($pipe -like '\\.\pipe\*\tsql\query') {
                    if ($pipe.StartsWith("np:", [StringComparison]::OrdinalIgnoreCase)) {
                        return $pipe
                    }
                    return "np:$pipe"
                }
            }
        }
    }

    throw "Could not resolve LocalDB instance '$instance' to a sqlcmd named pipe."
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

function Reset-FixtureDirectory {
    param(
        [Parameter(Mandatory = $true)][string]$Root,
        [Parameter(Mandatory = $true)][string]$Path
    )

    $resolvedRoot = [System.IO.Path]::GetFullPath($Root).TrimEnd('\')
    $resolvedPath = [System.IO.Path]::GetFullPath($Path).TrimEnd('\')
    if (-not $resolvedPath.StartsWith($resolvedRoot + '\', [StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to reset fixture path '$resolvedPath' outside LocalTest workspace root '$resolvedRoot'."
    }

    if (Test-Path $resolvedPath) {
        Remove-Item -LiteralPath $resolvedPath -Recurse -Force
    }

    New-Item -ItemType Directory -Force -Path $resolvedPath | Out-Null
}

function Initialize-FixtureGitRepository {
    param([Parameter(Mandatory = $true)][string]$Path)

    $git = Get-Command git -ErrorAction SilentlyContinue
    if ($null -eq $git) {
        throw "git was not found. LocalTest fixtures require git for deterministic readiness."
    }

    & $git.Source -C $Path init -q
    if ($LASTEXITCODE -ne 0) {
        throw "git init failed for '$Path'."
    }

    & $git.Source -C $Path add -A
    if ($LASTEXITCODE -ne 0) {
        throw "git add failed for '$Path'."
    }

    & $git.Source -C $Path -c user.email=bob@irondev.local -c user.name="LocalTest Seed" commit -m "Seed LocalTest fixture" -q
    if ($LASTEXITCODE -ne 0) {
        throw "git commit failed for '$Path'."
    }
}

$localTestProjectPath = Join-Path $workspaceRoot "IronDevLocalTestProject"
Reset-FixtureDirectory -Root $workspaceRoot -Path $localTestProjectPath
@"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
  </PropertyGroup>
</Project>
"@ | Set-Content -Path (Join-Path $localTestProjectPath "IronDevLocalTestProject.csproj") -Encoding UTF8

@"
namespace IronDevLocalTestProject;

public static class LocalTestMarker
{
    public static string Describe() => "LocalTest disposable build marker";
}
"@ | Set-Content -Path (Join-Path $localTestProjectPath "LocalTestMarker.cs") -Encoding UTF8

Initialize-FixtureGitRepository -Path $localTestProjectPath

$setupProjectPath = Join-Path $workspaceRoot "IronDevSetupTestProject"
Reset-FixtureDirectory -Root $workspaceRoot -Path $setupProjectPath
@"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
  </PropertyGroup>
</Project>
"@ | Set-Content -Path (Join-Path $setupProjectPath "IronDevSetupTestProject.csproj") -Encoding UTF8

@"
namespace IronDevSetupTestProject;

public static class SetupMarker
{
    public static string Describe() => "LocalTest guided setup marker";
}
"@ | Set-Content -Path (Join-Path $setupProjectPath "SetupMarker.cs") -Encoding UTF8

Initialize-FixtureGitRepository -Path $setupProjectPath

$bookSellerPath = Join-Path $workspaceRoot "BookSellerTestFixture"
Reset-FixtureDirectory -Root $workspaceRoot -Path $bookSellerPath
@"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
  </PropertyGroup>
</Project>
"@ | Set-Content -Path (Join-Path $bookSellerPath "BookSeller.TestFixture.csproj") -Encoding UTF8

@'
if (args.Contains("--self-test", StringComparer.OrdinalIgnoreCase))
{
    BookSellerSelfTest.Run();
    Console.WriteLine("BookSeller self-test passed.");
    return;
}

var catalog = new CatalogService();
foreach (var book in catalog.SearchByAuthor("Le Guin"))
{
    Console.WriteLine($"{book.Title} by {book.Author} - {book.Price:C}");
}

public sealed record Book(string Isbn, string Title, string Author, decimal Price);

public sealed class CatalogService
{
    private readonly Book[] books =
    [
        new("9780441478125", "The Left Hand of Darkness", "Ursula K. Le Guin", 9.99m),
        new("9780547928227", "The Hobbit", "J. R. R. Tolkien", 12.50m),
        new("9780143111597", "Parable of the Sower", "Octavia E. Butler", 11.25m)
    ];

    public IReadOnlyList<Book> SearchByAuthor(string authorFragment)
    {
        if (string.IsNullOrWhiteSpace(authorFragment))
        {
            return [];
        }

        return books
            .Where(book => book.Author.Contains(authorFragment, StringComparison.OrdinalIgnoreCase))
            .OrderBy(book => book.Title, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }
}

public static class BookSellerSelfTest
{
    public static void Run()
    {
        var results = new CatalogService().SearchByAuthor("Le Guin");
        if (results.Count != 1 || results[0].Title != "The Left Hand of Darkness")
        {
            throw new InvalidOperationException("Search by author returned the wrong result.");
        }
    }
}
'@ | Set-Content -Path (Join-Path $bookSellerPath "Program.cs") -Encoding UTF8

@'
# BookSeller Test Fixture

Realistic LocalTest fixture for provisioning, build, review, and apply journeys.

Commands:

```powershell
dotnet build .\BookSeller.TestFixture.csproj
dotnet run --project .\BookSeller.TestFixture.csproj -- --self-test
```
'@ | Set-Content -Path (Join-Path $bookSellerPath "README.md") -Encoding UTF8

Initialize-FixtureGitRepository -Path $bookSellerPath

$sqlcmd = Get-Command sqlcmd -ErrorAction SilentlyContinue
if ($null -eq $sqlcmd) {
    throw "sqlcmd was not found. Install SQL Server command-line tools, then rerun this LocalTest reset."
}

Start-LocalDbIfNeeded -DataSource $SqlServer
$sqlCmdServer = Resolve-SqlCmdDataSource -DataSource $SqlServer

function Invoke-SqlFile {
    param(
        [Parameter(Mandatory = $true)][string]$DatabaseName,
        [Parameter(Mandatory = $true)][string]$Path
    )

    $args = @("-S", $sqlCmdServer, "-d", $DatabaseName, "-b", "-I", "-i", $Path)

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

function Invoke-MigrationManifest {
    $migrationScript = Join-Path $repoRoot "Database\apply-migrations.ps1"
    if (-not (Test-Path -LiteralPath $migrationScript -PathType Leaf)) {
        throw "Migration script was not found at '$migrationScript'."
    }

    $migrationConnection = [System.Data.SqlClient.SqlConnectionStringBuilder]::new($builder.ConnectionString)
    $migrationConnection["Data Source"] = $sqlCmdServer
    $migrationConnection["Initial Catalog"] = $database

    if ($sqlCmdServer.StartsWith("np:", [StringComparison]::OrdinalIgnoreCase) -or $SqlServer -match '^\(localdb\)\\') {
        $migrationConnection.Encrypt = $false
        $migrationConnection.TrustServerCertificate = $false
    }

    & $migrationScript -ConnectionString $migrationConnection.ConnectionString
    if ($LASTEXITCODE -ne 0) {
        throw "Database migration manifest failed for LocalTest database '$database'."
    }
}

Write-Host "Resetting LocalTest database '$database' on '$SqlServer'."

if (-not $SkipSchema) {
    $tempDir = Join-Path ([System.IO.Path]::GetTempPath()) ("irondev-localtest-" + [Guid]::NewGuid().ToString("N"))
    New-Item -ItemType Directory -Force -Path $tempDir | Out-Null

    try {
        $schemaPath = Join-Path $tempDir "localtest-schema.sql"
        $documentMigrationPath = Join-Path $tempDir "localtest-documents.sql"
        $profileMigrationPath = Join-Path $tempDir "localtest-project-profiles.sql"

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

        (Get-Content -Path (Join-Path $repoRoot "Database\migrate_project_profiles.sql") -Raw).
            Replace("[IronDeveloper]", "[$database]") |
            Set-Content -Path $profileMigrationPath -Encoding UTF8

        Invoke-SqlFile -DatabaseName "master" -Path $schemaPath
        Invoke-SqlFile -DatabaseName $database -Path $documentMigrationPath
        Invoke-SqlFile -DatabaseName $database -Path $profileMigrationPath
        Invoke-SqlFile -DatabaseName $database -Path (Join-Path $repoRoot "Database\migrate_chat_document_sources.sql")
        Invoke-SqlFile -DatabaseName $database -Path (Join-Path $repoRoot "Database\migrate_project_channels.sql")
        Invoke-MigrationManifest
    }
    finally {
        Remove-Item -LiteralPath $tempDir -Recurse -Force -ErrorAction SilentlyContinue
    }
}

Invoke-SqlFile -DatabaseName $database -Path (Join-Path $PSScriptRoot "localtest-seed.sql")
Invoke-SqlFile -DatabaseName $database -Path (Join-Path $repoRoot "Database\migrate_work_item_identity.sql")

Write-Host "LocalTest reset complete."
Write-Host "Database: $database"
Write-Host "Workspace root: $workspaceRoot"
Write-Host "Logs root: $logsRoot"
Write-Host "Ready fixture: IronDev Local Test Project"
Write-Host "Setup fixture: IronDev Setup Test Project"
Write-Host "Login: bob@irondev.local / change-me-local-only"
