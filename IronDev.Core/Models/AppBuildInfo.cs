namespace IronDev.Core.Models;

public static class AppBuildInfo
{
    public const string ProductName = "IronDev";
    public const string ReleaseName = "Alpha 0.1";
    public const string Version = "0.1.0-alpha";
    public const string WorkflowName = "Project-Aware Ticket and Proposal Workflow";
    public const string SafetyModel = "Proposal first, approval before writes, trace everything.";

    public static string DisplayName => $"{ProductName} {ReleaseName}";
    public static string WindowTitle => $"{DisplayName} - {WorkflowName}";
}
