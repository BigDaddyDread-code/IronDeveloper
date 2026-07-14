[CmdletBinding()]
param(
    [string]$ConnectionString,
    [switch]$SkipFrontend,
    [switch]$KeepDatabase
)

$ErrorActionPreference = "Stop"
$repoRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot "..\.."))
$configPath = Join-Path $repoRoot "IronDev.Api\appsettings.LocalTest.json"
$runId = [Guid]::NewGuid().ToString("N")
$database = "IronDev_Platform_$($runId.Substring(0, 12))_Test"
$artifactRoot = Join-Path $repoRoot "artifacts\platform-baseline\$runId"
$previousConnectionString = $env:ConnectionStrings__IronDeveloperDb
$validationPassed = $false

function Resolve-LocalDbDataSource {
    param([Parameter(Mandatory = $true)][string]$DataSource)

    if ($DataSource -notmatch '^\(localdb\)\\(?<instance>.+)$') {
        return $DataSource
    }

    $instance = $Matches.instance
    $localDb = Get-Command sqllocaldb -ErrorAction SilentlyContinue
    if ($null -ne $localDb) {
        $startOutput = @()
        $startExitCode = 0
        try {
            $startOutput = & $localDb.Source start $instance 2>&1
            $startExitCode = $LASTEXITCODE
        }
        catch {
            $startOutput += $_.Exception.Message
            $startExitCode = 1
        }
        if ($startExitCode -ne 0) {
            Write-Warning "LocalDB start reported failure; checking the registered and last live named pipes."
            if ($startOutput.Count -gt 0) {
                Write-Warning (($startOutput | Out-String).Trim())
            }
        }
        $info = & $localDb.Source info $instance 2>$null
        foreach ($line in $info) {
            $match = [Regex]::Match($line, '^\s*Instance pipe name:\s*(?<pipe>.+?)\s*$')
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
        $announcement = Select-String -LiteralPath $errorLog -Pattern 'Server local connection provider is ready to accept connection on' |
            Select-Object -Last 1
        if ($null -ne $announcement) {
            $match = [Regex]::Match($announcement.Line, 'Server local connection provider is ready to accept connection on \[(?<pipe>[^\]]+)\]')
            if ($match.Success -and $match.Groups['pipe'].Value.Trim() -like '\\.\pipe\*\tsql\query') {
                return "np:$($match.Groups['pipe'].Value.Trim())"
            }
        }
    }

    throw "Could not resolve LocalDB instance '$instance'."
}

function Remove-TestDatabase {
    param(
        [Parameter(Mandatory = $true)][System.Data.SqlClient.SqlConnectionStringBuilder]$TargetBuilder,
        [Parameter(Mandatory = $true)][string]$Name
    )

    if (-not $Name.EndsWith("_Test", [StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to remove non-test database '$Name'."
    }

    $masterBuilder = [System.Data.SqlClient.SqlConnectionStringBuilder]::new($TargetBuilder.ConnectionString)
    $masterBuilder["Initial Catalog"] = "master"
    $quoted = "[" + $Name.Replace("]", "]]") + "]"
    $literal = $Name.Replace("'", "''")
    $connection = [System.Data.SqlClient.SqlConnection]::new($masterBuilder.ConnectionString)
    try {
        $connection.Open()
        $command = $connection.CreateCommand()
        $command.CommandText = "IF DB_ID(N'$literal') IS NOT NULL BEGIN ALTER DATABASE $quoted SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE $quoted; END;"
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

if ([string]::IsNullOrWhiteSpace($ConnectionString)) {
    $ConnectionString = $env:ConnectionStrings__IronDeveloperDb
}
if ([string]::IsNullOrWhiteSpace($ConnectionString)) {
    if (-not (Test-Path -LiteralPath $configPath -PathType Leaf)) {
        throw "Provide -ConnectionString or restore $configPath."
    }
    $ConnectionString = (Get-Content -LiteralPath $configPath -Raw | ConvertFrom-Json).ConnectionStrings.IronDeveloperDb
}

$baseBuilder = [System.Data.SqlClient.SqlConnectionStringBuilder]::new($ConnectionString)
$localDbConnection = $baseBuilder.DataSource -like "(localdb)\*"
$baseBuilder["Data Source"] = Resolve-LocalDbDataSource -DataSource $baseBuilder.DataSource
if ($localDbConnection) {
    $baseBuilder["Encrypt"] = $false
    $baseBuilder["TrustServerCertificate"] = $false
}
$targetBuilder = [System.Data.SqlClient.SqlConnectionStringBuilder]::new($baseBuilder.ConnectionString)
$targetBuilder["Initial Catalog"] = $database

try {
    Write-Host "== Platform baseline =="
    Write-Host "Isolated database: $database"
    Write-Host "Build output: $artifactRoot"

    & (Join-Path $repoRoot "Database\verify-fresh-install.ps1") `
        -ConnectionString $baseBuilder.ConnectionString `
        -Database $database `
        -KeepDatabase

    $env:ConnectionStrings__IronDeveloperDb = $targetBuilder.ConnectionString
    New-Item -ItemType Directory -Path $artifactRoot -Force | Out-Null

    dotnet test (Join-Path $repoRoot "IronDev.IntegrationTests.Api\IronDev.IntegrationTests.Api.csproj") `
        --artifacts-path $artifactRoot `
        --logger "console;verbosity=minimal" `
        --filter "(FullyQualifiedName~EndpointContractTests&TestCategory!=ProcessExecution)|FullyQualifiedName~ApiTestBaseCatalogGuardContractTests"
    if ($LASTEXITCODE -ne 0) {
        throw "In-process API contract tests failed."
    }

    if (-not $SkipFrontend) {
        & (Join-Path $repoRoot "Scripts\ci\run-frontend-contract-ci.ps1")
    }

    Write-Host "PASS platform baseline"
    Write-Host "  Fresh install: migrations, seed, API, login, project load, and Board smoke passed"
    Write-Host "  In-process API contract: passed"
    Write-Host "  Frontend/API contract: $(if ($SkipFrontend) { 'skipped by request' } else { 'passed' })"
    $validationPassed = $true
}
finally {
    $env:ConnectionStrings__IronDeveloperDb = $previousConnectionString

    if (-not $KeepDatabase) {
        try {
            Remove-TestDatabase -TargetBuilder $targetBuilder -Name $database
        }
        catch {
            if ($validationPassed) {
                throw
            }

            Write-Warning "Could not remove the isolated database after validation failed: $($_.Exception.Message)"
        }
    }

    $resolvedArtifactRoot = [System.IO.Path]::GetFullPath($artifactRoot)
    $allowedArtifactRoot = [System.IO.Path]::GetFullPath((Join-Path $repoRoot "artifacts\platform-baseline"))
    if ($resolvedArtifactRoot.StartsWith($allowedArtifactRoot + [System.IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase) -and
        (Test-Path -LiteralPath $resolvedArtifactRoot)) {
        Remove-Item -LiteralPath $resolvedArtifactRoot -Recurse -Force
    }
}
