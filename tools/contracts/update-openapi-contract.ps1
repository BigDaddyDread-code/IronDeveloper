param(
    [int]$Port = 5017,
    [switch]$Check,
    [switch]$VerifyDeterminism
)

$ErrorActionPreference = "Stop"

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..\..")
$apiProject = Join-Path $repoRoot "IronDev.Api\IronDev.Api.csproj"
$frontendRoot = Join-Path $repoRoot "IronDev.TauriShell"
$openApiSnapshot = Join-Path $frontendRoot "openapi\irondev-api.openapi.json"
$generatedTypes = Join-Path $frontendRoot "src\api\generated\ironDevApiTypes.ts"
$apiBaseUrl = "http://127.0.0.1:$Port"
$tempRoot = Join-Path ([IO.Path]::GetTempPath()) ("irondev-contract-source-" + [guid]::NewGuid().ToString("N"))
$apiOutput = Join-Path $tempRoot "api"
$apiStdOut = Join-Path $tempRoot "api.out.log"
$apiStdErr = Join-Path $tempRoot "api.err.log"
$apiProcess = $null

$environmentVariables = @{
    ASPNETCORE_ENVIRONMENT = "Test"
    ConnectionStrings__IronDeveloperDb = "Server=127.0.0.1,1;Database=IronDev_Contract_Test;Integrated Security=True;Encrypt=False;Connection Timeout=1;"
    IRONDEV_JWT_KEY = "irondev-contract-generation-key-32chars"
    Ai__Provider = "fake"
}
$previousEnvironment = @{}

function Invoke-Native {
    param(
        [Parameter(Mandatory = $true)][string]$Name,
        [Parameter(Mandatory = $true)][scriptblock]$Command
    )

    Write-Host "== $Name =="
    & $Command
    if ($LASTEXITCODE -ne 0) {
        throw "$Name failed with exit code $LASTEXITCODE."
    }
}

function Wait-ForSwagger {
    param([int]$TimeoutSeconds = 60)

    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    do {
        if ($null -ne $apiProcess -and $apiProcess.HasExited) {
            throw "Contract-source API exited before Swagger became available."
        }

        try {
            $response = Invoke-WebRequest -Uri "$apiBaseUrl/swagger/v1/swagger.json" -UseBasicParsing -TimeoutSec 2
            if ($response.StatusCode -eq 200) {
                return
            }
        }
        catch {
        }

        Start-Sleep -Milliseconds 500
    } while ((Get-Date) -lt $deadline)

    throw "Timed out waiting for contract-source Swagger at $apiBaseUrl."
}

function Invoke-ContractGeneration {
    param([Parameter(Mandatory = $true)][string]$Name)

    Push-Location $frontendRoot
    try {
        $previousApiBaseUrl = $env:IRONDEV_API_BASE_URL
        $env:IRONDEV_API_BASE_URL = $apiBaseUrl
        try {
            Invoke-Native -Name $Name -Command {
                npm run api:generate
            }
        }
        finally {
            $env:IRONDEV_API_BASE_URL = $previousApiBaseUrl
        }
    }
    finally {
        Pop-Location
    }
}

if (Get-NetTCPConnection -State Listen -LocalPort $Port -ErrorAction SilentlyContinue) {
    throw "Port $Port is already in use. Refusing to stop or reuse an unrelated process."
}

$beforeOpenApiHash = (Get-FileHash -LiteralPath $openApiSnapshot -Algorithm SHA256).Hash
$beforeTypesHash = (Get-FileHash -LiteralPath $generatedTypes -Algorithm SHA256).Hash

try {
    New-Item -ItemType Directory -Path $apiOutput -Force | Out-Null

    Invoke-Native -Name "Build isolated contract-source API" -Command {
        dotnet build $apiProject --configuration Release --nologo "-p:OutDir=$apiOutput$([IO.Path]::DirectorySeparatorChar)"
    }

    foreach ($entry in $environmentVariables.GetEnumerator()) {
        $previousEnvironment[$entry.Key] = [Environment]::GetEnvironmentVariable($entry.Key)
        [Environment]::SetEnvironmentVariable($entry.Key, $entry.Value)
    }

    $dotnet = (Get-Command dotnet -ErrorAction Stop).Source
    $apiDll = Join-Path $apiOutput "IronDev.Api.dll"
    $apiProcess = Start-Process `
        -FilePath $dotnet `
        -ArgumentList @("`"$apiDll`"", "--urls", $apiBaseUrl) `
        -RedirectStandardOutput $apiStdOut `
        -RedirectStandardError $apiStdErr `
        -WindowStyle Hidden `
        -PassThru

    Wait-ForSwagger

    Invoke-ContractGeneration -Name "Regenerate OpenAPI and TypeScript contracts"

    $firstOpenApiHash = (Get-FileHash -LiteralPath $openApiSnapshot -Algorithm SHA256).Hash
    $firstTypesHash = (Get-FileHash -LiteralPath $generatedTypes -Algorithm SHA256).Hash

    if ($VerifyDeterminism) {
        Invoke-ContractGeneration -Name "Regenerate OpenAPI and TypeScript contracts again"
        $secondOpenApiHash = (Get-FileHash -LiteralPath $openApiSnapshot -Algorithm SHA256).Hash
        $secondTypesHash = (Get-FileHash -LiteralPath $generatedTypes -Algorithm SHA256).Hash

        if ($firstOpenApiHash -ne $secondOpenApiHash -or $firstTypesHash -ne $secondTypesHash) {
            throw "Generated API contracts are nondeterministic. OpenAPI: $firstOpenApiHash / $secondOpenApiHash; TypeScript: $firstTypesHash / $secondTypesHash."
        }

        Write-Host "Determinism verified: two OpenAPI and TypeScript generations produced identical SHA-256 hashes."
    }

    $afterOpenApiHash = (Get-FileHash -LiteralPath $openApiSnapshot -Algorithm SHA256).Hash
    $afterTypesHash = (Get-FileHash -LiteralPath $generatedTypes -Algorithm SHA256).Hash

    if ($Check -and ($beforeOpenApiHash -ne $afterOpenApiHash -or $beforeTypesHash -ne $afterTypesHash)) {
        throw "Generated API contract drift detected. Run tools/contracts/update-openapi-contract.ps1 and review both generated artifacts."
    }

    Write-Host "Contract generation completed from $apiBaseUrl."
    if ($Check) {
        Write-Host "Checked-in OpenAPI and TypeScript contracts match the running API."
    }
}
catch {
    if (Test-Path -LiteralPath $apiStdOut) {
        Write-Host "--- contract-source API stdout ---"
        Get-Content -LiteralPath $apiStdOut
    }
    if (Test-Path -LiteralPath $apiStdErr) {
        Write-Host "--- contract-source API stderr ---"
        Get-Content -LiteralPath $apiStdErr
    }
    throw
}
finally {
    if ($null -ne $apiProcess -and -not $apiProcess.HasExited) {
        Stop-Process -Id $apiProcess.Id -Force -ErrorAction SilentlyContinue
        [void]$apiProcess.WaitForExit(5000)
    }

    foreach ($entry in $previousEnvironment.GetEnumerator()) {
        [Environment]::SetEnvironmentVariable($entry.Key, $entry.Value)
    }

    if (Test-Path -LiteralPath $tempRoot) {
        Remove-Item -LiteralPath $tempRoot -Recurse -Force
    }
}
