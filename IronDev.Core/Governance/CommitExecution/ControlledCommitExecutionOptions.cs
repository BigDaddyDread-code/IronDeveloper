namespace IronDev.Core.Governance.CommitExecution;

public sealed record ControlledCommitExecutionOptions
{
    public IReadOnlyCollection<string> ForbiddenFileGlobs { get; init; } =
    [
        ".git/**",
        "**/obj/**",
        "**/bin/**",
        "**/project.assets.json",
        "**/*.nupkg"
    ];

    public bool RequireHooksDisabled { get; init; } = true;
}
