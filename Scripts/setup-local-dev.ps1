param(
    [string]$SqlServerInstance = "(localdb)\MSSQLLocalDB",
    [string]$DatabaseName = "IronDeveloper",
    [switch]$RunDatabaseSetup,
    [switch]$SkipWeaviate,
    [switch]$SkipBuild,
    [switch]$SkipTests
)

$ErrorActionPreference = "Stop"

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
$weaviateScript = Join-Path $repoRoot "Scripts\weaviate-dev.ps1"
$dbSetupScript = Join-Path $repoRoot "Database\local_dev_setup.sql"
$solutionFile = Join-Path $repoRoot "IronDev.slnx"
$apiProject = Join-Path $repoRoot "IronDev.Api\IronDev.Api.csproj"
$cliProject = Join-Path $repoRoot "tools\IronDev.Cli\IronDev.Cli.csproj"
$integrationTests = Join-Path $repoRoot "IronDev.IntegrationTests\IronDev.IntegrationTests.csproj"

function Write-Step {
    param([string]$Message)
    Write-Host ""
    Write-Host "== $Message ==" -ForegroundColor Cyan
}

function Write-Ok {
    param([string]$Message)
    Write-Host "[OK] $Message" -ForegroundColor Green
}

function Write-Warn {
    param([string]$Message)
    Write-Host "[WARN] $Message" -ForegroundColor Yellow
}

function Test-CommandAvailable {
    param([string]$Name)
    return [bool](Get-Command $Name -ErrorAction SilentlyContinue)
}

Write-Step "IronDev local setup"
Write-Host "Repository: $repoRoot"

Write-Step "Checking required tools"
if (-not (Test-CommandAvailable "dotnet")) {
    throw ".NET SDK is required. Install .NET 10 SDK, then rerun this script."
}
dotnet --version
Write-Ok ".NET SDK found"

if (-not $SkipWeaviate) {
    if (-not (Test-CommandAvailable "docker")) {
        Write-Warn "Docker CLI not found. Skipping Weaviate. Install Docker Desktop to enable semantic-memory dogfooding."
        $SkipWeaviate = $true
    }
    else {
        docker version --format "{{.Server.Version}}" | Out-Null
        Write-Ok "Docker is available"
    }
}

Write-Step "Restoring .NET packages"
dotnet restore $solutionFile
Write-Ok "Restore complete"

if (-not $SkipWeaviate) {
    Write-Step "Starting Weaviate"
    powershell -ExecutionPolicy Bypass -File $weaviateScript up
    powershell -ExecutionPolicy Bypass -File $weaviateScript smoke
    Write-Ok "Weaviate is ready at http://localhost:8080 and localhost:50051"
}
else {
    Write-Warn "Weaviate skipped. IronDev can still start with Weaviate disabled or degraded."
}

if ($RunDatabaseSetup) {
    Write-Step "Running local database setup"

    if (-not (Test-CommandAvailable "sqlcmd")) {
        throw "sqlcmd is required for -RunDatabaseSetup. Install SQL Server command-line tools or run Database/local_dev_setup.sql manually."
    }

    if (-not (Test-Path $dbSetupScript)) {
        throw "Database setup script not found: $dbSetupScript"
    }

    sqlcmd -S $SqlServerInstance -d master -Q "IF DB_ID(N'$DatabaseName') IS NULL CREATE DATABASE [$DatabaseName];"
    sqlcmd -S $SqlServerInstance -d $DatabaseName -i $dbSetupScript
    Write-Ok "Database setup complete: $DatabaseName on $SqlServerInstance"
}
else {
    Write-Warn "Database setup not run. Pass -RunDatabaseSetup to create/seed the local database via Database/local_dev_setup.sql."
}

if (-not $SkipBuild) {
    Write-Step "Building IronDev API and product CLI"
    dotnet build $apiProject --no-restore -p:UseSharedCompilation=false -nr:false
    dotnet build $cliProject --no-restore -p:UseSharedCompilation=false -nr:false
    Write-Ok "API and CLI build complete"
}
else {
    Write-Warn "Build skipped."
}

if (-not $SkipTests) {
    Write-Step "Running focused smoke tests"
    dotnet test $integrationTests `
        --no-restore `
        --filter "ApiBoundaryTests|IronDevCliTests" `
        -p:UseSharedCompilation=false `
        -nr:false
    Write-Ok "Focused smoke tests passed"
}
else {
    Write-Warn "Tests skipped."
}

Write-Step "Next steps"
Write-Host "1. Confirm IronDev.Api/appsettings.Development.json has the right SQL connection string."
Write-Host "2. Start the API:"
Write-Host "   dotnet run --project IronDev.Api/IronDev.Api.csproj"
Write-Host "3. Use the product CLI:"
Write-Host "   dotnet run --project tools/IronDev.Cli/IronDev.Cli.csproj -- --help"
Write-Host "4. Login seed:"
Write-Host "   Email:    bob@irondev.local"
Write-Host "   Password: change-me-local-only"
Write-Host "5. Use TauriShell only through API/OpenAPI once the API is running."
