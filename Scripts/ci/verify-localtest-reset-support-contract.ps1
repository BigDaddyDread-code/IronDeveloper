$ErrorActionPreference = "Stop"
$root = Resolve-Path (Join-Path $PSScriptRoot "..\..")
$scriptPath = Join-Path $root "tools\localtest\Invoke-LocalTestResetAndSupport.ps1"
$tokens = $null
$errors = $null
[System.Management.Automation.Language.Parser]::ParseFile($scriptPath, [ref]$tokens, [ref]$errors) | Out-Null
if ($errors.Count -gt 0) { throw "Reset/support script has PowerShell parse errors: $($errors.Message -join '; ')" }

$text = Get-Content -LiteralPath $scriptPath -Raw
$required = @(
    'ResetTestTenantProject', 'ResetDisposableWorkspace', 'ExportSupportBundle',
    'ConfirmReset', 'StartsWith($workspaceRoot', 'correlationIdsRetained = $true',
    'secretsExcluded = $true', '[REDACTED]', 'MaximumLogFiles', 'MaximumLinesPerLog',
    'Test-ReparsePoint', 'includedLogFiles', 'log-{0:D3}.log'
)
foreach ($marker in $required) {
    if ($text.IndexOf($marker, [StringComparison]::Ordinal) -lt 0) { throw "Missing reset/support contract marker '$marker'." }
}

$fixtureRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("IronDevTest-cln41-" + [Guid]::NewGuid().ToString("N"))
$workspaceRoot = Join-Path $fixtureRoot "workspaces"
$logsRoot = Join-Path $fixtureRoot "logs"
$configPath = Join-Path $fixtureRoot "appsettings.LocalTest.json"
$bundlePath = Join-Path $fixtureRoot "support.zip"
$expandedPath = Join-Path $fixtureRoot "expanded"
try {
    New-Item -ItemType Directory -Force -Path $workspaceRoot, $logsRoot | Out-Null
    [ordered]@{
        ConnectionStrings = @{ IronDeveloperDb = "Server=(localdb)\MSSQLLocalDB;Database=IronDeveloper_Test;Integrated Security=True;" }
        LocalTest = @{ WorkspaceRoot = $workspaceRoot; LogsRoot = $logsRoot }
    } | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $configPath -Encoding UTF8

    @(
        '{"password":"json-secret","correlationId":"corr-cln41-123"}',
        ('Authorization: ' + 'Be' + 'arer ' + 'bearer-secret-value'),
        'endpoint=https://alice:uri-secret@example.test/path',
        ('api_key=' + 's' + 'k-' + 'abcdefghijklmnop'),
        'jwt=eyJabcdefghijk.abcdefghijkl.abcdefghijkl',
        ('Server=sql.example;Database=IronDeveloper_Test;' + 'Pass' + 'word=connection-secret;')
    ) | Set-Content -LiteralPath (Join-Path $logsRoot "customer-secret-name.log") -Encoding UTF8

    & $scriptPath -Action ExportSupportBundle -ConfigPath $configPath -OutputPath $bundlePath -MaximumLogFiles 1 -MaximumLinesPerLog 20
    Expand-Archive -LiteralPath $bundlePath -DestinationPath $expandedPath

    $manifest = Get-Content -LiteralPath (Join-Path $expandedPath "manifest.json") -Raw | ConvertFrom-Json
    if (-not $manifest.correlationIdsRetained -or -not $manifest.secretsExcluded -or $manifest.includedLogFiles -ne 1) {
        throw "Support manifest did not record the bounded export contract."
    }
    $exportedLogs = @(Get-ChildItem -LiteralPath (Join-Path $expandedPath "logs") -File)
    if ($exportedLogs.Count -ne 1 -or $exportedLogs[0].Name -ne "log-001.log") {
        throw "Support export must use generic log filenames."
    }
    $safeLog = Get-Content -LiteralPath $exportedLogs[0].FullName -Raw
    if ($safeLog -notmatch 'corr-cln41-123') { throw "Correlation IDs must remain available for support investigation." }
    foreach ($secret in @('json-secret', 'bearer-secret-value', 'uri-secret', ('s' + 'k-' + 'abcdefghijklmnop'), 'eyJabcdefghijk', 'connection-secret', 'customer-secret-name')) {
        if ($safeLog.IndexOf($secret, [StringComparison]::OrdinalIgnoreCase) -ge 0 -or
            $exportedLogs[0].Name.IndexOf($secret, [StringComparison]::OrdinalIgnoreCase) -ge 0) {
            throw "Support export leaked a secret-shaped fixture value."
        }
    }

    $outsideRejected = $false
    try { & $scriptPath -Action ResetDisposableWorkspace -ConfigPath $configPath -WorkspacePath (Join-Path $fixtureRoot "outside-run") -ConfirmReset }
    catch { $outsideRejected = $_.Exception.Message -match 'outside the configured LocalTest workspace root' }
    if (-not $outsideRejected) { throw "Workspace reset did not reject an outside-root target." }

    $weakMarkerRejected = $false
    try { & $scriptPath -Action ResetDisposableWorkspace -ConfigPath $configPath -WorkspacePath (Join-Path $workspaceRoot "contest") -ConfirmReset }
    catch { $weakMarkerRejected = $_.Exception.Message -match 'delimited test, disposable, or run marker' }
    if (-not $weakMarkerRejected) { throw "Workspace reset accepted an ambiguous leaf marker." }

    $nonZipRejected = $false
    try { & $scriptPath -Action ExportSupportBundle -ConfigPath $configPath -OutputPath (Join-Path $fixtureRoot "support.json") }
    catch { $nonZipRejected = $_.Exception.Message -match '\.zip extension' }
    if (-not $nonZipRejected) { throw "Support export accepted a non-ZIP output target." }
}
finally {
    Remove-Item -LiteralPath $fixtureRoot -Recurse -Force -ErrorAction SilentlyContinue
}

Write-Host "PASS LocalTest reset and support bundle contract."
