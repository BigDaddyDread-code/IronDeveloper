param(
    [string]$RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
)

$wpfRoot = Join-Path $RepoRoot "IronDeveloper"

$forbidden = @(
    "IronDev.Infrastructure",
    "global::IronDev.Infrastructure",
    "IronDev.Services",
    "global::IronDev.Services",
    "IUserService",
    "IProjectService",
    "ITicketService",
    "IChatHistoryService",
    "IProjectMemoryService",
    "ICodeIndexService",
    "IDraftTicketService",
    "ICodebaseTicketGeneratorService",
    "ITicketBuildOrchestrator",
    "IArtifactSourceReferenceService"
)

$files = Get-ChildItem -LiteralPath $wpfRoot -Recurse -Include *.cs,*.xaml,*.csproj |
    Where-Object {
        -not $_.PSIsContainer -and
        $_.FullName -notmatch '\\bin\\' -and
        $_.FullName -notmatch '\\obj\\' -and
        $_.Name -notmatch '_wpftmp\.csproj$'
    }

$hits = foreach ($file in $files) {
    foreach ($term in $forbidden) {
        Select-String -LiteralPath $file.FullName -Pattern $term -SimpleMatch |
            ForEach-Object {
                [pscustomobject]@{
                    Path = $_.Path
                    Line = $_.LineNumber
                    Term = $term
                    Text = $_.Line.Trim()
                }
            }
    }
}

if ($hits) {
    $hits | Format-Table -AutoSize | Out-String | Write-Error
    exit 1
}

Write-Host "WPF API boundary guard passed."
