namespace IronDev.Core.Models;

public sealed record WorkbenchReleaseInfoDto
{
    public string Version { get; init; } = string.Empty;
    public string Mode { get; init; } = "V1";
    public bool V2Enabled { get; init; }
    public bool V1FallbackEnabled { get; init; } = true;
    public string PreviewId { get; init; } = "default";
    public string ApiBuildIdentity { get; init; } = string.Empty;
    public string ApiCommit { get; init; } = string.Empty;
    public bool ResetSupported { get; init; }
}
