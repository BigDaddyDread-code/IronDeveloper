using System.Reflection;
using System.Text.RegularExpressions;
using IronDev.Core.Models;

namespace IronDev.Api.Services;

public static partial class WorkbenchReleaseInfoFactory
{
    public const string DefaultVersion = "0.1.0-preview.7";
    public const string DefaultPreviewId = "default";

    public static WorkbenchReleaseInfoDto Create(
        IConfiguration configuration,
        IHostEnvironment environment,
        Assembly? apiAssembly = null)
    {
        var version = configuration["WorkbenchV2:Version"]?.Trim();
        if (string.IsNullOrWhiteSpace(version))
        {
            version = DefaultVersion;
        }

        var previewId = NormalizePreviewId(configuration["WorkbenchV2:PreviewId"]);
        var v2Enabled = configuration.GetValue("WorkbenchV2:Enabled", false);
        var buildIdentity = (apiAssembly ?? typeof(WorkbenchReleaseInfoFactory).Assembly)
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion ?? "unknown";

        return new WorkbenchReleaseInfoDto
        {
            Version = version,
            Mode = v2Enabled ? "V2" : "V1",
            V2Enabled = v2Enabled,
            ConversationAuthorityEnabled = v2Enabled &&
                configuration.GetValue("WorkbenchV2:ConversationAuthorityEnabled", false),
            V1FallbackEnabled = configuration.GetValue("WorkbenchV2:V1FallbackEnabled", true),
            PreviewId = previewId,
            ApiBuildIdentity = buildIdentity,
            ApiCommit = ResolveCommit(buildIdentity),
            ResetSupported = environment.IsEnvironment("LocalTest") &&
                configuration.GetValue("LocalTest:ResetAllowed", true)
        };
    }

    public static string NormalizePreviewId(string? value)
    {
        var previewId = string.IsNullOrWhiteSpace(value) ? DefaultPreviewId : value.Trim().ToLowerInvariant();
        if (!PreviewIdPattern().IsMatch(previewId))
        {
            throw new InvalidOperationException(
                "WorkbenchV2:PreviewId must contain 1-32 lowercase letters, numbers, or hyphens and must start with a letter or number.");
        }

        return previewId;
    }

    private static string ResolveCommit(string informationalVersion)
    {
        var separator = informationalVersion.IndexOf('+');
        return separator >= 0 && separator < informationalVersion.Length - 1
            ? informationalVersion[(separator + 1)..]
            : informationalVersion;
    }

    [GeneratedRegex("^[a-z0-9][a-z0-9-]{0,31}$", RegexOptions.CultureInvariant)]
    private static partial Regex PreviewIdPattern();
}
