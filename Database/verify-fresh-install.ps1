[CmdletBinding()]
param(
    [string]$ConnectionString,
    [string]$Server = "(localdb)\MSSQLLocalDB",
    [string]$Database,
    [switch]$KeepDatabase,
    [int]$ApiPort,
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

function Invoke-SqlScript {
    param(
        [Parameter(Mandatory = $true)][System.Data.SqlClient.SqlConnectionStringBuilder]$TargetBuilder,
        [Parameter(Mandatory = $true)][string]$Path
    )

    $connection = [System.Data.SqlClient.SqlConnection]::new($TargetBuilder.ConnectionString)
    try {
        $connection.Open()
        $batchNumber = 0
        foreach ($batch in @(Split-SqlBatches -Sql (Get-Content -LiteralPath $Path -Raw))) {
            $batchNumber++
            $command = $connection.CreateCommand()
            $command.CommandText = $batch
            $command.CommandTimeout = $CommandTimeoutSeconds
            try {
                [void]$command.ExecuteNonQuery()
            }
            catch {
                throw "Seed script '$Path' failed at batch $batchNumber. $($_.Exception.Message)"
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

function Remove-TestDatabase {
    param(
        [Parameter(Mandatory = $true)][System.Data.SqlClient.SqlConnectionStringBuilder]$TargetBuilder,
        [Parameter(Mandatory = $true)][string]$Name
    )

    if (-not (Test-TestDatabaseName -Name $Name)) {
        throw "Refusing to remove non-test database '$Name'."
    }

    $masterBuilder = [System.Data.SqlClient.SqlConnectionStringBuilder]::new($TargetBuilder.ConnectionString)
    $masterBuilder["Initial Catalog"] = "master"
    $quoted = Quote-SqlIdentifier -Value $Name
    $literal = $Name.Replace("'", "''")
    $connection = [System.Data.SqlClient.SqlConnection]::new($masterBuilder.ConnectionString)
    try {
        $connection.Open()
        $command = $connection.CreateCommand()
        $command.CommandText = "IF DB_ID(N'$literal') IS NOT NULL BEGIN ALTER DATABASE $quoted SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE $quoted; END;"
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

function Get-FreeTcpPort {
    $listener = [System.Net.Sockets.TcpListener]::new([System.Net.IPAddress]::Loopback, 0)
    try {
        $listener.Start()
        return ([System.Net.IPEndPoint]$listener.LocalEndpoint).Port
    }
    finally {
        $listener.Stop()
    }
}

function New-JwtKey {
    $bytes = New-Object byte[] 48
    $rng = [System.Security.Cryptography.RandomNumberGenerator]::Create()
    try {
        $rng.GetBytes($bytes)
        return [Convert]::ToBase64String($bytes)
    }
    finally {
        $rng.Dispose()
    }
}

function Wait-ApiReady {
    param(
        [Parameter(Mandatory = $true)][string]$BaseUrl,
        [Parameter(Mandatory = $true)][System.Diagnostics.Process]$Process,
        [Parameter(Mandatory = $true)][string]$ErrorLog
    )

    $deadline = [DateTimeOffset]::UtcNow.AddSeconds(90)
    do {
        if ($Process.HasExited) {
            $errorText = if (Test-Path -LiteralPath $ErrorLog) { Get-Content -LiteralPath $ErrorLog -Raw } else { "No API error log was produced." }
            throw "Fresh-install API exited before becoming ready. $errorText"
        }

        try {
            $response = Invoke-WebRequest -Uri "$BaseUrl/health" -UseBasicParsing -TimeoutSec 2
            if ($response.StatusCode -eq 200) {
                return
            }
        }
        catch {
        }

        Start-Sleep -Milliseconds 500
    } while ([DateTimeOffset]::UtcNow -lt $deadline)

    throw "Timed out waiting for the fresh-install API at $BaseUrl."
}

function Invoke-ProductProof {
    param([Parameter(Mandatory = $true)][string]$BaseUrl)

    $login = Invoke-RestMethod -Method Post -Uri "$BaseUrl/api/auth/login" -ContentType "application/json" -Body (@{
        email = "bob@irondev.local"
        password = "change-me-local-only"
    } | ConvertTo-Json -Compress)
    if ([string]::IsNullOrWhiteSpace($login.token)) {
        throw "Fresh-install login returned no base token."
    }

    $baseHeaders = @{ Authorization = "Bearer $($login.token)" }
    $selected = Invoke-RestMethod -Method Post -Uri "$BaseUrl/api/tenants/select" -Headers $baseHeaders -ContentType "application/json" -Body '{"tenantId":1}'
    if ([string]::IsNullOrWhiteSpace($selected.token)) {
        throw "Fresh-install tenant selection returned no tenant token."
    }

    $tenantHeaders = @{ Authorization = "Bearer $($selected.token)" }
    $project = Invoke-RestMethod -Method Get -Uri "$BaseUrl/api/projects/1" -Headers $tenantHeaders
    if ($project.id -ne 1 -or $project.tenantId -ne 1 -or $project.name -ne "IronDev Local Test Project") {
        throw "Fresh-install seeded project did not load with the expected identity and tenant scope."
    }

    $board = Invoke-RestMethod -Method Get -Uri "$BaseUrl/api/projects/1/board" -Headers $tenantHeaders
    if ($null -eq $board -or $board.projectId -ne 1) {
        throw "Fresh-install core Board smoke did not return project 1."
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
    $Database = "IronDev_FreshInstall_$([Guid]::NewGuid().ToString('N').Substring(0, 12))_Test"
}
if (-not (Test-TestDatabaseName -Name $Database)) {
    throw "Refusing fresh-install proof against '$Database'. The database must end in '_Test' or start with 'IronDev_CI_'."
}
if ($ApiPort -le 0) {
    $ApiPort = Get-FreeTcpPort
}

$targetBuilder = [System.Data.SqlClient.SqlConnectionStringBuilder]::new($baseBuilder.ConnectionString)
$targetBuilder["Initial Catalog"] = $Database
$baseUrl = "http://127.0.0.1:$ApiPort"
$logRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("irondev-fresh-install-" + [Guid]::NewGuid().ToString("N"))
$apiOut = Join-Path $logRoot "api.out.log"
$apiError = Join-Path $logRoot "api.err.log"
$apiProcess = $null
$databaseCreated = $false
$previousEnvironment = $env:ASPNETCORE_ENVIRONMENT
$previousJwtKey = $env:IRONDEV_JWT_KEY
$previousConnection = $env:ConnectionStrings__IronDeveloperDb

try {
    New-Item -ItemType Directory -Path $logRoot -Force | Out-Null

    # The guarded cleanup is safe even if the nested verifier fails after it
    # creates the database but before it returns control to this script.
    $databaseCreated = $true
    & (Join-Path $PSScriptRoot "verify-clean-database.ps1") `
        -ConnectionString $baseBuilder.ConnectionString `
        -Database $Database `
        -KeepDatabase `
        -CommandTimeoutSeconds $CommandTimeoutSeconds
    if ($LASTEXITCODE -ne 0) {
        throw "Clean migration verification failed for fresh-install database '$Database'."
    }
    Invoke-SqlScript -TargetBuilder $targetBuilder -Path (Join-Path $root "tools\localtest\localtest-seed.sql")
    Invoke-SqlScript -TargetBuilder $targetBuilder -Path (Join-Path $root "Database\migrate_work_item_identity.sql")

    dotnet build (Join-Path $root "IronDev.Api\IronDev.Api.csproj") -c Release --nologo
    if ($LASTEXITCODE -ne 0) {
        throw "Fresh-install API build failed."
    }

    $apiDll = Join-Path $root "IronDev.Api\bin\Release\net10.0\IronDev.Api.dll"
    if (-not (Test-Path -LiteralPath $apiDll -PathType Leaf)) {
        throw "Fresh-install API output was not found at '$apiDll'."
    }

    $env:ASPNETCORE_ENVIRONMENT = "LocalTest"
    $env:IRONDEV_JWT_KEY = New-JwtKey
    $env:ConnectionStrings__IronDeveloperDb = $targetBuilder.ConnectionString
    $processArguments = @{
        FilePath = "dotnet"
        ArgumentList = @($apiDll, "--urls", $baseUrl)
        WorkingDirectory = (Join-Path $root "IronDev.Api")
        PassThru = $true
        RedirectStandardOutput = $apiOut
        RedirectStandardError = $apiError
    }
    if ([System.Environment]::OSVersion.Platform -eq [System.PlatformID]::Win32NT) {
        $processArguments.WindowStyle = "Hidden"
    }
    $apiProcess = Start-Process @processArguments

    $env:ASPNETCORE_ENVIRONMENT = $previousEnvironment
    $env:IRONDEV_JWT_KEY = $previousJwtKey
    $env:ConnectionStrings__IronDeveloperDb = $previousConnection

    Wait-ApiReady -BaseUrl $baseUrl -Process $apiProcess -ErrorLog $apiError
    Invoke-ProductProof -BaseUrl $baseUrl

    Write-Host "PASS fresh-install migration proof"
    Write-Host "  Database: $Database"
    Write-Host "  Migrations: applied and verified"
    Write-Host "  Seed: applied"
    Write-Host "  API: started"
    Write-Host "  Login and tenant selection: passed"
    Write-Host "  Project load and Board smoke: passed"
}
finally {
    $env:ASPNETCORE_ENVIRONMENT = $previousEnvironment
    $env:IRONDEV_JWT_KEY = $previousJwtKey
    $env:ConnectionStrings__IronDeveloperDb = $previousConnection

    if ($null -ne $apiProcess -and -not $apiProcess.HasExited) {
        Stop-Process -Id $apiProcess.Id -Force -ErrorAction SilentlyContinue
        [void]$apiProcess.WaitForExit(10000)
    }

    if ($databaseCreated -and -not $KeepDatabase) {
        Remove-TestDatabase -TargetBuilder $targetBuilder -Name $Database
        Write-Host "Removed isolated fresh-install database '$Database'."
    }

    Remove-Item -LiteralPath $logRoot -Recurse -Force -ErrorAction SilentlyContinue
}
