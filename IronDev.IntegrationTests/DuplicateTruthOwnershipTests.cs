using IronDev.Api.Filters;
using IronDev.Api.Middleware;
using IronDev.Core.Audit;
using IronDev.Core.Chat;
using IronDev.Core.Governance;
using IronDev.Core.Provisioning;
using IronDev.Infrastructure.Builder;
using IronDev.Infrastructure.Services;

namespace IronDev.IntegrationTests;

[TestClass]
public sealed class DuplicateTruthOwnershipTests
{
    [TestMethod]
    public void CleanupTruthRules_HaveOneNamedAuthoritativeOwner()
    {
        var owners = new Dictionary<string, Type>(StringComparer.Ordinal)
        {
            ["project-provisioning-readiness"] = typeof(ProvisioningReadinessEvaluator),
            ["ticket-build-readiness"] = typeof(BuilderReadinessService),
            ["chat-route-classification"] = typeof(LlmChatModeClassifier),
            ["governed-action-authority"] = typeof(ConscienceDecisionService),
            ["tenant-token-scope"] = typeof(TenantTokenScopeMiddleware),
            ["route-body-project-binding"] = typeof(RouteBodyScopeBindingFilter),
            ["project-artifact-access"] = typeof(ProjectArtifactAccessService),
            ["memory-authority-ranking"] = typeof(MemoryAuthorityNormalizer),
            ["audit-link-safety"] = typeof(AuditEvidenceLinkSafety),
            ["refusal-formatting"] = typeof(GovernedRefusal)
        };

        Assert.AreEqual(10, owners.Count);
        Assert.AreEqual(owners.Count, owners.Values.Distinct().Count());
    }

    [TestMethod]
    public void DeletedOrPrivateDuplicateOwners_DoNotReappear()
    {
        var root = FindRepoRoot();
        Assert.IsFalse(File.Exists(Path.Combine(root, "IronDev.Infrastructure", "Services", "ChatModeClassifierService.cs")));

        var auditProjector = File.ReadAllText(Path.Combine(root, "IronDev.Core", "Audit", "ProjectAuditExportModels.cs"));
        Assert.IsFalse(auditProjector.Contains("private static bool IsSafeEvidenceLink", StringComparison.Ordinal));
        StringAssert.Contains(auditProjector, "AuditEvidenceLinkSafety.IsSafeForProject");

        var productRoots = new[] { "IronDev.Core", "IronDev.Infrastructure", "IronDev.Api", "IronDev.Client" };
        var refusalFiles = productRoots
            .SelectMany(directory => Directory.GetFiles(Path.Combine(root, directory), "*.cs", SearchOption.AllDirectories))
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        var refusalRecordDefinitions = refusalFiles.Count(path =>
            File.ReadAllText(path).Contains("record GovernedRefusalEnvelope", StringComparison.Ordinal));
        Assert.AreEqual(1, refusalRecordDefinitions);
    }

    private static string FindRepoRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null && !File.Exists(Path.Combine(current.FullName, "IronDev.slnx")))
            current = current.Parent;

        return current?.FullName ?? throw new DirectoryNotFoundException("Could not locate IronDev.slnx.");
    }
}
