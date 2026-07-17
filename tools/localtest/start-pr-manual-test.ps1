[CmdletBinding()]
param(
    [string]$ApiBaseUrl = "http://localhost:5000",
    [int]$ProjectId = 0,
    [int]$UiPort = 5173,
    [string]$PreviewId = "default",
    [switch]$Reset,
    [switch]$FreshSession,
    [switch]$BrowserOnly,
    [switch]$EnableSandboxApply,
    [switch]$UseV1
)

$ErrorActionPreference = "Stop"

# The established launcher remains the implementation for compatibility with
# historical automation. This neutral entry point is the supported product and
# PR-manual-test command; callers no longer need a milestone-named script.
$implementation = Join-Path $PSScriptRoot "start-alpha-localtest.ps1"
$pwsh = (Get-Process -Id $PID -ErrorAction Stop).Path
$arguments = @(
    "-NoProfile",
    "-File", $implementation,
    "-ApiBaseUrl", $ApiBaseUrl,
    "-ProjectId", "$ProjectId",
    "-UiPort", "$UiPort",
    "-PreviewId", $PreviewId
)

if ($Reset) { $arguments += "-Reset" }
if ($FreshSession) { $arguments += "-FreshSession" }
if ($BrowserOnly) { $arguments += "-BrowserOnly" }
if ($EnableSandboxApply) { $arguments += "-EnableSandboxApply" }
if ($UseV1) { $arguments += "-UseV1" }

& $pwsh @arguments
if ($LASTEXITCODE -ne 0) {
    throw "LocalTest startup failed with exit code $LASTEXITCODE."
}
