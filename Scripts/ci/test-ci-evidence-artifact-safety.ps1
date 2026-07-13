param(
    [Parameter(Mandatory = $true)]
    [string]$ArtifactDirectory
)

$ErrorActionPreference = "Stop"

$forbiddenDirectoryNames = @(
    ".git",
    "bin",
    "obj",
    "node_modules"
)

$forbiddenMarkers = @(
    ("Password" + "="),
    ("Pwd" + "="),
    "Bearer ",
    "Authorization:",
    "Jwt:Key",
    "Weaviate:ApiKey",
    "OPENAI_API_KEY=",
    "IRONDEV_JWT_KEY=",
    "IRONDEV_WEAVIATE_API_KEY=",
    "client_secret=",
    "access_token=",
    "refresh_token=",
    "sk-",
    "ghp_",
    "github_pat_",
    ("BEGIN " + "PRIVATE KEY")
)

$forbiddenPatterns = @(
    @{ Name = "JWT-shaped credential"; Pattern = "\beyJ[A-Za-z0-9_-]+\.[A-Za-z0-9_-]+\.[A-Za-z0-9_-]+\b" },
    @{ Name = "credential assignment"; Pattern = "(?i)\b(password|pwd|secret|token|api[_-]?key|client[_-]?secret|access[_-]?token|refresh[_-]?token|authorization)\s*[:=]\s*[^\s,;]+" }
)

if (-not (Test-Path -LiteralPath $ArtifactDirectory -PathType Container)) {
    throw "CI evidence artifact directory does not exist: $ArtifactDirectory"
}

$resolvedArtifactDirectory = [System.IO.Path]::GetFullPath($ArtifactDirectory)
$normalizedArtifactDirectory = $resolvedArtifactDirectory.Replace('\', '/')
if (-not $normalizedArtifactDirectory.Contains("/artifacts/ci/")) {
    throw "CI evidence artifact directory must be bounded under artifacts/ci: $resolvedArtifactDirectory"
}

foreach ($directory in Get-ChildItem -LiteralPath $resolvedArtifactDirectory -Directory -Recurse -Force) {
    if ($forbiddenDirectoryNames -contains $directory.Name) {
        throw "Forbidden directory in CI evidence artifact: $($directory.FullName)"
    }
}

$textExtensions = @(
    ".md",
    ".txt",
    ".trx",
    ".xml",
    ".json",
    ".log"
)

foreach ($file in Get-ChildItem -LiteralPath $resolvedArtifactDirectory -File -Recurse -Force) {
    if ($textExtensions -notcontains $file.Extension) {
        continue
    }

    $content = Get-Content -LiteralPath $file.FullName -Raw
    foreach ($marker in $forbiddenMarkers) {
        if ($content.IndexOf($marker, [StringComparison]::OrdinalIgnoreCase) -ge 0) {
            throw "Forbidden marker '$marker' found in CI evidence artifact file: $($file.FullName)"
        }
    }

    foreach ($entry in $forbiddenPatterns) {
        if ([regex]::IsMatch($content, $entry.Pattern)) {
            throw "Forbidden $($entry.Name) found in CI evidence artifact file: $($file.FullName)"
        }
    }
}

Write-Host "CI evidence artifact safety scan passed: $resolvedArtifactDirectory"
