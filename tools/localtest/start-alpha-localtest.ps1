param(
    [string]$ApiBaseUrl = "http://localhost:5000",
    [int]$ProjectId = 1,
    [int]$UiPort = 5173,
    [switch]$Reset,
    [switch]$BrowserOnly
)

$ErrorActionPreference = "Stop"

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..\..")
$shellRoot = Join-Path $repoRoot "IronDev.TauriShell"
$apiOut = Join-Path $env:TEMP "irondev-localtest-api.out.log"
$apiErr = Join-Path $env:TEMP "irondev-localtest-api.err.log"
$uiOut = Join-Path $env:TEMP "irondev-localtest-ui.out.log"
$uiErr = Join-Path $env:TEMP "irondev-localtest-ui.err.log"

function Stop-Listener {
    param([int]$Port)

    $listeners = Get-NetTCPConnection -LocalPort $Port -State Listen -ErrorAction SilentlyContinue |
        Select-Object -ExpandProperty OwningProcess -Unique

    foreach ($processId in $listeners) {
        if ($processId -and $processId -ne 0) {
            Stop-Process -Id $processId -Force -ErrorAction SilentlyContinue
        }
    }
}

function Wait-HttpOk {
    param(
        [string]$Uri,
        [int]$TimeoutSeconds = 45
    )

    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    do {
        Start-Sleep -Milliseconds 700
        try {
            $response = Invoke-WebRequest -Uri $Uri -UseBasicParsing -TimeoutSec 2
            if ($response.StatusCode -ge 200 -and $response.StatusCode -lt 300) {
                return
            }
        }
        catch {
        }
    } while ((Get-Date) -lt $deadline)

    throw "Timed out waiting for $Uri."
}

function Get-NodeCommand {
    $bundled = "C:\Users\bob\.cache\codex-runtimes\codex-primary-runtime\dependencies\node\bin\node.exe"
    if (Test-Path $bundled) {
        return $bundled
    }

    $node = Get-Command node -ErrorAction SilentlyContinue
    if ($null -eq $node) {
        throw "node was not found. Install Node.js or use the bundled Codex runtime."
    }

    return $node.Source
}

function Start-BrowserShell {
    param([int]$Port)

    Stop-Listener -Port $Port
    $node = Get-NodeCommand

    Start-Process -FilePath $node `
        -ArgumentList @("node_modules/vite/bin/vite.js", "--host", "127.0.0.1", "--port", "$Port", "--mode", "localtest") `
        -WorkingDirectory $shellRoot `
        -PassThru `
        -WindowStyle Hidden `
        -RedirectStandardOutput $uiOut `
        -RedirectStandardError $uiErr | Out-Null

    Wait-HttpOk -Uri "http://127.0.0.1:$Port/"
}

function Get-TauriDesktopProcesses {
    @(Get-CimInstance Win32_Process -ErrorAction SilentlyContinue |
        Where-Object { $_.Name -eq "irondev-tauri-shell.exe" })
}

function Stop-TauriDesktopProcesses {
    $desktopProcesses = Get-TauriDesktopProcesses
    if ($desktopProcesses.Count -eq 0) {
        return
    }

    Write-Host "Stopping existing LocalTest Tauri desktop shell"
    Write-Host "  ProcessId: $($desktopProcesses.ProcessId -join ', ')"
    foreach ($process in $desktopProcesses) {
        Stop-Process -Id $process.ProcessId -Force -ErrorAction SilentlyContinue
    }

    $deadline = (Get-Date).AddSeconds(10)
    do {
        Start-Sleep -Milliseconds 500
        $remaining = Get-TauriDesktopProcesses
        if ($remaining.Count -eq 0) {
            return
        }
    } while ((Get-Date) -lt $deadline)

    throw "Existing Tauri desktop shell is still running. Close it and rerun LocalTest startup."
}

if ($Reset) {
    & (Join-Path $PSScriptRoot "reset-localtest-data.ps1")
}

Stop-Listener -Port 5000
Start-Process -FilePath dotnet `
    -ArgumentList @("run", "--launch-profile", "LocalTest", "--project", "IronDev.Api\IronDev.Api.csproj") `
    -WorkingDirectory $repoRoot `
    -PassThru `
    -WindowStyle Hidden `
    -RedirectStandardOutput $apiOut `
    -RedirectStandardError $apiErr | Out-Null

Wait-HttpOk -Uri "$ApiBaseUrl/health"
$environment = Invoke-RestMethod -Uri "$ApiBaseUrl/api/environment" -TimeoutSec 5

if ($environment.environment -ne "LocalTest" -or $environment.database -notmatch "Test" -or -not $environment.isTestEnvironment) {
    throw "API environment check failed. Refusing to start UI against $($environment.environment)/$($environment.database)."
}

$env:VITE_IRONDEV_API_BASE_URL = $ApiBaseUrl
$env:VITE_IRONDEV_PROJECT_ID = "$ProjectId"
$env:IRONDEV_API_PROXY_TARGET = $ApiBaseUrl

Write-Host ""
Write-Host "PASS LocalTest API started"
Write-Host "  API: $ApiBaseUrl"
Write-Host "  Environment: $($environment.environment)"
Write-Host "  Database: $($environment.database)"
Write-Host "  Workspace: $($environment.workspaceRoot)"
Write-Host ""

if ($BrowserOnly) {
    Start-BrowserShell -Port $UiPort
    Write-Host "PASS LocalTest browser shell started"
    Write-Host "  UI: http://127.0.0.1:$UiPort/"
    Write-Host ""
    Write-Host "Processes are left running. Stop ports 5000/$UiPort when finished."
    exit 0
}

Write-Host "Starting Tauri desktop shell. Close the desktop window or stop this command when finished."
$node = Get-NodeCommand
$tauriCli = Join-Path $shellRoot "node_modules\@tauri-apps\cli\tauri.js"
if (-not (Test-Path $tauriCli)) {
    throw "Tauri CLI was not found at $tauriCli. Restore IronDev.TauriShell dependencies first."
}

$tauriLocalTestConfig = Join-Path $env:TEMP "irondev-tauri-localtest.conf.json"
@{
    build = @{
        beforeDevCommand = ""
        devUrl = "http://127.0.0.1:$UiPort"
    }
} | ConvertTo-Json -Depth 4 | Set-Content -Path $tauriLocalTestConfig -Encoding UTF8

Stop-TauriDesktopProcesses
Start-BrowserShell -Port $UiPort
Write-Host "PASS LocalTest browser shell started"
Write-Host "  UI: http://127.0.0.1:$UiPort/"
Write-Host ""

Push-Location $shellRoot
try {
    $existingDesktopProcessIds = @(Get-TauriDesktopProcesses | Select-Object -ExpandProperty ProcessId)

    $tauriProcess = Start-Process -FilePath $node `
        -ArgumentList @($tauriCli, "dev", "--config", $tauriLocalTestConfig, "--no-dev-server-wait") `
        -WorkingDirectory $shellRoot `
        -NoNewWindow `
        -PassThru `
        -Wait

    if ($tauriProcess.ExitCode -ne 0) {
        $desktopProcess = Get-TauriDesktopProcesses |
            Where-Object { $existingDesktopProcessIds -notcontains $_.ProcessId } |
            Select-Object -First 1

        if ($desktopProcess) {
            Write-Host "PASS LocalTest Tauri desktop shell started"
            Write-Host "  ProcessId: $($desktopProcess.ProcessId)"
            Write-Host "  Tauri CLI exited with code $($tauriProcess.ExitCode) after launching the desktop process."
            Write-Host "  Waiting for the desktop shell to close."
            Wait-Process -Id $desktopProcess.ProcessId
            return
        }

        throw "Tauri desktop shell exited with code $($tauriProcess.ExitCode)."
    }
}
finally {
    Pop-Location
    Stop-Listener -Port $UiPort
}
