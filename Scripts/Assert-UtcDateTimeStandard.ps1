$ErrorActionPreference = "Stop"

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
$sourceRoots = @(
    "IronDev.Api",
    "IronDev.Client",
    "IronDev.Core",
    "IronDev.Infrastructure",
    "IronDev.TauriShell",
    "tools"
)

$forbiddenPatterns = @(
    "DateTime.Now",
    "DateTimeOffset.Now"
)

$violations = New-Object System.Collections.Generic.List[string]

foreach ($sourceRoot in $sourceRoots) {
    $root = Join-Path $repoRoot $sourceRoot
    if (-not (Test-Path $root)) {
        continue
    }

    Get-ChildItem -Path $root -Recurse -File -Include *.cs |
        Where-Object {
            $_.FullName -notmatch "\\bin\\" -and
            $_.FullName -notmatch "\\obj\\"
        } |
        ForEach-Object {
            $relativePath = Resolve-Path -Relative $_.FullName
            $lineNumber = 0
            foreach ($line in Get-Content -LiteralPath $_.FullName) {
                $lineNumber++
                foreach ($pattern in $forbiddenPatterns) {
                    if ($line.Contains($pattern)) {
                        $violations.Add("${relativePath}:${lineNumber}: forbidden local timestamp source '$pattern'")
                    }
                }
            }
        }
}

if ($violations.Count -gt 0) {
    Write-Error ("UTC date/time guard failed:`n" + ($violations -join "`n"))
    exit 1
}

Write-Host "UTC date/time guard passed."
