param(
    [Parameter(Mandatory = $true)]
    [string]$Run,

    [Parameter(Mandatory = $true)]
    [string]$Project,

    [Parameter(Mandatory = $true)]
    [string]$Task,

    [switch]$Json
)

$ErrorActionPreference = "Stop"
$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..\..")
$cliProject = Join-Path $repoRoot "tools\IronDev.Cli\IronDev.Cli.csproj"
$args = @("run", "--project", $cliProject, "--", "product-hardening", "dogfood", "--run", $Run, "--project", $Project, "--task", $Task)
if ($Json) {
    $args += "--json"
}

dotnet @args
