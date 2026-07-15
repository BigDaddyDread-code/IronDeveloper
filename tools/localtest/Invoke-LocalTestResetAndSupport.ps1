[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateSet("ResetTestTenantProject", "ResetDisposableWorkspace", "ExportSupportBundle")]
    [string]$Action,
    [string]$ConfigPath,
    [string]$WorkspacePath,
    [string]$OutputPath,
    [switch]$ConfirmReset,
    [ValidateRange(1, 20)]
    [int]$MaximumLogFiles = 20,
    [ValidateRange(1, 2000)]
    [int]$MaximumLinesPerLog = 2000
)

$ErrorActionPreference = "Stop"
$repoRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot "..\.."))
if ([string]::IsNullOrWhiteSpace($ConfigPath)) {
    $ConfigPath = Join-Path $repoRoot "IronDev.Api\appsettings.LocalTest.json"
}
if (-not (Test-Path -LiteralPath $ConfigPath -PathType Leaf)) {
    throw "LocalTest configuration was not found."
}

$config = Get-Content -LiteralPath $ConfigPath -Raw | ConvertFrom-Json
if ([string]::IsNullOrWhiteSpace([string]$config.LocalTest.WorkspaceRoot) -or
    [string]::IsNullOrWhiteSpace([string]$config.LocalTest.LogsRoot) -or
    [string]::IsNullOrWhiteSpace([string]$config.ConnectionStrings.IronDeveloperDb)) {
    throw "LocalTest configuration is missing a database, workspace root, or logs root."
}
$workspaceRoot = [System.IO.Path]::GetFullPath([string]$config.LocalTest.WorkspaceRoot).TrimEnd('\')
$logsRoot = [System.IO.Path]::GetFullPath([string]$config.LocalTest.LogsRoot).TrimEnd('\')
$databaseName = ([System.Data.SqlClient.SqlConnectionStringBuilder]::new([string]$config.ConnectionStrings.IronDeveloperDb)).InitialCatalog
if ($databaseName -notmatch '(?i)(^|[_-])test($|[_-])' -or $workspaceRoot -notmatch '(?i)test') {
    throw "Refusing operation because configuration is not an isolated LocalTest target."
}

function Assert-ResetConfirmation {
    if (-not $ConfirmReset) {
        throw "Reset actions require -ConfirmReset."
    }
}

function ConvertTo-SupportSafeText {
    param([AllowEmptyString()][string]$Text)
    $safe = $Text -replace '(?i)Bearer\s+[A-Za-z0-9._~+/-]+=*', 'Bearer [REDACTED]'
    $safe = $safe -replace '(?i)(["'']?(?:password|pwd|secret|token|api[_-]?key|authorization|clientsecret)["'']?\s*[:=]\s*)(["''][^"''\r\n]*["'']|[^\s,;]+)', '$1[REDACTED]'
    $safe = $safe -replace '(?i)(Server|Data Source)\s*=\s*[^;]+;[^\r\n]*', '[REDACTED_CONNECTION_STRING]'
    $safe = $safe -replace '(?i)://[^\s/@:]+:[^\s/@]+@', '://[REDACTED]@'
    $safe = $safe -replace '(?i)\bsk-[A-Za-z0-9_-]{12,}\b', '[REDACTED_API_KEY]'
    $safe = $safe -replace '\beyJ[A-Za-z0-9_-]{8,}\.[A-Za-z0-9_-]{8,}\.[A-Za-z0-9_-]{8,}\b', '[REDACTED_JWT]'
    return $safe
}

function Test-ReparsePoint {
    param([Parameter(Mandatory = $true)][System.IO.FileSystemInfo]$Item)
    return ($Item.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0
}

switch ($Action) {
    "ResetTestTenantProject" {
        Assert-ResetConfirmation
        & (Join-Path $PSScriptRoot "reset-localtest-data.ps1") -ConfigPath $ConfigPath
        if ($LASTEXITCODE -ne 0) { throw "LocalTest tenant/project reset failed." }
    }
    "ResetDisposableWorkspace" {
        Assert-ResetConfirmation
        if ([string]::IsNullOrWhiteSpace($WorkspacePath)) { throw "-WorkspacePath is required." }
        $target = [System.IO.Path]::GetFullPath($WorkspacePath).TrimEnd('\')
        if (-not $target.StartsWith($workspaceRoot + '\', [StringComparison]::OrdinalIgnoreCase)) {
            throw "Refusing to reset a workspace outside the configured LocalTest workspace root."
        }
        if ([System.IO.Path]::GetFileName($target) -notmatch '(?i)(^|[-_.])(test|disposable|run)([-_.]|$)') {
            throw "Disposable workspace leaf must contain a delimited test, disposable, or run marker."
        }
        $cursor = [System.IO.DirectoryInfo]::new($target)
        while ($null -ne $cursor -and $cursor.FullName.StartsWith($workspaceRoot, [StringComparison]::OrdinalIgnoreCase)) {
            if ($cursor.Exists -and (Test-ReparsePoint $cursor)) {
                throw "Refusing to reset a workspace through a reparse point."
            }
            if ($cursor.FullName.Equals($workspaceRoot, [StringComparison]::OrdinalIgnoreCase)) { break }
            $cursor = $cursor.Parent
        }
        if (Test-Path -LiteralPath $target) { Remove-Item -LiteralPath $target -Recurse -Force }
        New-Item -ItemType Directory -Path $target -Force | Out-Null
        Write-Host "Disposable LocalTest workspace reset complete."
    }
    "ExportSupportBundle" {
        if ([string]::IsNullOrWhiteSpace($OutputPath)) {
            $OutputPath = Join-Path $repoRoot ("artifacts\support\irondev-support-{0}.zip" -f (Get-Date -Format "yyyyMMdd-HHmmss"))
        }
        $resolvedOutput = [System.IO.Path]::GetFullPath($OutputPath)
        if (-not [System.IO.Path]::GetExtension($resolvedOutput).Equals('.zip', [StringComparison]::OrdinalIgnoreCase)) {
            throw "Support bundle output must use a .zip extension."
        }
        New-Item -ItemType Directory -Path (Split-Path -Parent $resolvedOutput) -Force | Out-Null
        $stage = Join-Path ([System.IO.Path]::GetTempPath()) ("irondev-support-" + [Guid]::NewGuid().ToString("N"))
        try {
            New-Item -ItemType Directory -Path $stage -Force | Out-Null
            $logStage = Join-Path $stage "logs"
            New-Item -ItemType Directory -Path $logStage -Force | Out-Null
            $includedLogCount = 0
            if (Test-Path -LiteralPath $logsRoot -PathType Container) {
                $logsRootItem = Get-Item -LiteralPath $logsRoot
                if (Test-ReparsePoint $logsRootItem) { throw "Refusing to export logs through a reparse-point root." }
                Get-ChildItem -LiteralPath $logsRoot -File -Filter '*.log' |
                    Where-Object { -not (Test-ReparsePoint $_) } |
                    Sort-Object LastWriteTimeUtc -Descending |
                    Select-Object -First $MaximumLogFiles |
                    ForEach-Object {
                        $safeLines = Get-Content -LiteralPath $_.FullName -Tail $MaximumLinesPerLog | ForEach-Object { ConvertTo-SupportSafeText $_ }
                        $includedLogCount++
                        $safeLines | Set-Content -LiteralPath (Join-Path $logStage ("log-{0:D3}.log" -f $includedLogCount)) -Encoding UTF8
                    }
            }
            [ordered]@{
                schemaVersion = 1
                createdUtc = [DateTimeOffset]::UtcNow.ToString("O")
                environment = "LocalTest"
                databaseClassification = "isolated-test"
                correlationIdsRetained = $true
                secretsExcluded = $true
                maximumLogFiles = $MaximumLogFiles
                maximumLinesPerLog = $MaximumLinesPerLog
                includedLogFiles = $includedLogCount
            } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $stage "manifest.json") -Encoding UTF8
            if (Test-Path -LiteralPath $resolvedOutput) { Remove-Item -LiteralPath $resolvedOutput -Force }
            Compress-Archive -Path (Join-Path $stage '*') -DestinationPath $resolvedOutput -CompressionLevel Optimal
            Write-Host "Bounded support bundle exported to '$resolvedOutput'."
        }
        finally {
            Remove-Item -LiteralPath $stage -Recurse -Force -ErrorAction SilentlyContinue
        }
    }
}
