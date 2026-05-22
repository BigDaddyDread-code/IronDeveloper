param(
    [Parameter(Mandatory = $true)]
    [string]$BaselinePath,

    [Parameter(Mandatory = $true)]
    [string]$TargetPath,

    [Parameter(Mandatory = $true)]
    [string]$DatabaseName,

    [Parameter(Mandatory = $true)]
    [string]$RunId,

    [string]$SqlServer = ".",

    [switch]$StopIronDev,

    [switch]$Force,

    [switch]$DryRun
)

$ErrorActionPreference = "Stop"

function Resolve-FullPath {
    param([string]$Path)
    return [System.IO.Path]::GetFullPath($Path).TrimEnd('\')
}

function Assert-SafePath {
    param(
        [string]$BaselinePath,
        [string]$TargetPath
    )

    $baselineFull = Resolve-FullPath $BaselinePath
    $targetFull = Resolve-FullPath $TargetPath

    if (-not (Test-Path -LiteralPath $baselineFull)) {
        throw "BaselinePath does not exist: $baselineFull"
    }

    if ($baselineFull -eq $targetFull) {
        throw "BaselinePath and TargetPath must not be the same."
    }

    if (-not $baselineFull.EndsWith("\BookSeller_Baseline", [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to continue: BaselinePath must end with \BookSeller_Baseline. Actual: $baselineFull"
    }

    if (-not $targetFull.EndsWith("\BookSeller", [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to continue: TargetPath must end with \BookSeller. Actual: $targetFull"
    }

    $dangerous = @(
        "C:\",
        "C:\repo",
        $env:USERPROFILE,
        (Resolve-FullPath (Join-Path $PSScriptRoot "..\.."))
    ) | Where-Object { -not [string]::IsNullOrWhiteSpace($_) }

    foreach ($path in $dangerous) {
        $dangerFull = Resolve-FullPath $path
        if ($targetFull -eq $dangerFull) {
            throw "Refusing to continue: TargetPath is dangerous: $targetFull"
        }
    }

    return @{
        BaselineFull = $baselineFull
        TargetFull = $targetFull
    }
}

function Stop-ProcessIfRunning {
    param([string[]]$Names)

    foreach ($name in $Names) {
        $processes = Get-Process -Name $name -ErrorAction SilentlyContinue
        foreach ($process in $processes) {
            Write-Host "Stopping process $($process.ProcessName) [$($process.Id)]"
            if (-not $DryRun) {
                Stop-Process -Id $process.Id -Force
            }
        }
    }
}

function Remove-DirectoryContents {
    param([string]$Path)

    $targetFull = Resolve-FullPath $Path
    if (-not $targetFull.EndsWith("\BookSeller", [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to clear unsafe target path: $targetFull"
    }

    if (-not (Test-Path -LiteralPath $targetFull)) {
        Write-Host "Creating target folder: $targetFull"
        if (-not $DryRun) {
            New-Item -ItemType Directory -Path $targetFull | Out-Null
        }
        return
    }

    Write-Host "Clearing target folder: $targetFull"

    if (-not $Force -and -not $DryRun) {
        throw "Refusing to delete target contents without -Force."
    }

    if (-not $DryRun) {
        Get-ChildItem -LiteralPath $targetFull -Force |
            Remove-Item -Recurse -Force
    }
}

function Copy-Baseline {
    param(
        [string]$Baseline,
        [string]$Target
    )

    Write-Host "Copying baseline:"
    Write-Host "  From: $Baseline"
    Write-Host "  To:   $Target"

    if (-not $DryRun) {
        robocopy $Baseline $Target /MIR /XD ".git" "bin" "obj" ".vs" | Out-Host
        $exitCode = $LASTEXITCODE
        if ($exitCode -gt 7) {
            throw "Robocopy failed with exit code $exitCode"
        }
    }
}

function Clean-BuildFolders {
    param([string]$Target)

    if (-not (Test-Path -LiteralPath $Target)) {
        return
    }

    $folders = Get-ChildItem -LiteralPath $Target -Recurse -Force -Directory |
        Where-Object { $_.Name -in @("bin", "obj", ".vs") }

    foreach ($folder in $folders) {
        Write-Host "Removing build folder: $($folder.FullName)"
        if (-not $DryRun) {
            Remove-Item -LiteralPath $folder.FullName -Recurse -Force
        }
    }
}

function Reset-Database {
    param(
        [string]$SqlServer,
        [string]$DatabaseName
    )

    Write-Host "Resetting database: $DatabaseName on $SqlServer"

    $sql = @"
IF DB_ID(N'$DatabaseName') IS NOT NULL
BEGIN
    ALTER DATABASE [$DatabaseName] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
    DROP DATABASE [$DatabaseName];
END;

CREATE DATABASE [$DatabaseName];
"@

    if (-not $DryRun) {
        $sqlcmd = Get-Command sqlcmd -ErrorAction SilentlyContinue
        if ($null -eq $sqlcmd) {
            throw "sqlcmd was not found. Install SQL Server command-line tools or run with -DryRun."
        }

        sqlcmd -S $SqlServer -Q $sql
        if ($LASTEXITCODE -ne 0) {
            throw "sqlcmd failed while resetting database."
        }
    }
}

$paths = Assert-SafePath -BaselinePath $BaselinePath -TargetPath $TargetPath
$baselineFull = $paths.BaselineFull
$targetFull = $paths.TargetFull

$runRoot = Join-Path $PSScriptRoot "runs\$RunId"
$runFile = Join-Path $runRoot "dogfood-run.json"
$resetLogFile = Join-Path $runRoot "reset-log.json"

Write-Host "Dogfood reset starting"
Write-Host "RunId: $RunId"
Write-Host "DryRun: $DryRun"

if (-not $DryRun) {
    New-Item -ItemType Directory -Force -Path $runRoot | Out-Null
    New-Item -ItemType Directory -Force -Path (Join-Path $runRoot "traces") | Out-Null
    New-Item -ItemType Directory -Force -Path (Join-Path $runRoot "screenshots") | Out-Null
    New-Item -ItemType Directory -Force -Path (Join-Path $runRoot "reports") | Out-Null
}

if ($StopIronDev) {
    Stop-ProcessIfRunning -Names @("IronDev.Agent", "IronDeveloper")
}

Stop-ProcessIfRunning -Names @("BookSeller", "BookSeller.Api", "BookSeller.Web")

Remove-DirectoryContents -Path $targetFull
Copy-Baseline -Baseline $baselineFull -Target $targetFull
Clean-BuildFolders -Target $targetFull
Reset-Database -SqlServer $SqlServer -DatabaseName $DatabaseName

$runInfo = [ordered]@{
    dogfoodRunId = $RunId
    scenario = "BookSellerMvp"
    baselinePath = $baselineFull
    targetPath = $targetFull
    databaseName = $DatabaseName
    sqlServer = $SqlServer
    createdAtUtc = [DateTimeOffset]::UtcNow.ToString("o")
    dryRun = [bool]$DryRun
}

$resetLog = [ordered]@{
    dogfoodRunId = $RunId
    resetAtUtc = [DateTimeOffset]::UtcNow.ToString("o")
    stoppedIronDev = [bool]$StopIronDev
    baselineCopied = -not [bool]$DryRun
    databaseReset = -not [bool]$DryRun
}

if (-not $DryRun) {
    $runInfo | ConvertTo-Json -Depth 10 | Set-Content -Path $runFile -Encoding UTF8
    $resetLog | ConvertTo-Json -Depth 10 | Set-Content -Path $resetLogFile -Encoding UTF8
}

Write-Host "Dogfood reset complete"
Write-Host "Run folder: $runRoot"
