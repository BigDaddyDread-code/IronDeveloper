namespace IronDev.IntegrationTests.AgentMemory;

[TestClass]
[TestCategory("StaticBoundary")]
public sealed class MemoryWriteAuthorityLockTests
{
    [TestMethod]
    public void MemoryController_BindsRouteAndTokenScopeForAllowedWrites()
    {
        var source = Controller();
        StringAssert.Contains(source, "BindRouteScope(projectId, summary.ProjectId)");
        StringAssert.Contains(source, "BindRouteScope(projectId, document.ProjectId)");
        StringAssert.Contains(source, "BindRouteScope(projectId, plan.ProjectId)");
        StringAssert.Contains(source, "summary.ProjectId = projectId");
        StringAssert.Contains(source, "document.ProjectId = projectId");
        StringAssert.Contains(source, "plan.ProjectId = projectId");
        StringAssert.Contains(source, "CurrentUser().TenantId!.Value");
        StringAssert.Contains(source, "The route project is authoritative.");
    }

    [TestMethod]
    public void ContextObservation_CannotSelfAssertAuthorityOrLifecycle()
    {
        var source = Controller();
        StringAssert.Contains(source, "document.AuthorityLevel = \"ObservedFact\"");
        StringAssert.Contains(source, "document.Status = \"Active\"");
        StringAssert.Contains(source, "document.SupersedesDocumentId = null");
    }

    [TestMethod]
    public void DirectCanonWrites_AreRefusedPendingGovernedPromotion()
    {
        var source = Controller();
        Assert.AreEqual(2, source.Split("GovernedPromotionRequired").Length - 1);
        Assert.IsFalse(source.Contains("_memory.SaveDecisionAsync", StringComparison.Ordinal));
        Assert.IsFalse(source.Contains("_memory.SaveProjectRuleAsync", StringComparison.Ordinal));
    }

    [TestMethod]
    public void ArchiveAndReindex_RequireMaintenanceCapability()
    {
        var source = Controller();
        StringAssert.Contains(source, "CanMaintainMemoryAsync");
        StringAssert.Contains(source, "ProjectMemoryCapabilities.CanMaintainProjectMemory(role)");
        StringAssert.Contains(source, "ProjectMemoryCapabilities.MaintainProjectMemory");
        StringAssert.Contains(source, "if (!await CanMaintainMemoryAsync(ct))");
        Assert.IsFalse(source.Contains("TenantUserRoles.CanAdministerUsers(role)", StringComparison.Ordinal));
    }

    [TestMethod]
    public void Refusals_UseCanonicalGovernedEnvelope()
    {
        var source = Controller();
        StringAssert.Contains(source, "GovernedRefusal.Create(");
        StringAssert.Contains(source, "GovernedRefusalEnvelope");
        Assert.IsFalse(source.Contains("Conflict(new", StringComparison.Ordinal));
        Assert.IsFalse(source.Contains("return Forbid()", StringComparison.Ordinal));
    }

    [TestMethod]
    public void MembershipAndActorAttribution_RunForMemoryMutations()
    {
        var program = Read("IronDev.Api", "Program.cs");
        StringAssert.Contains(program, "UseMiddleware<UserMutationAttributionMiddleware>()");
        StringAssert.Contains(program, "UseMiddleware<ProjectMembershipMiddleware>()");
        var attribution = Read("IronDev.Api", "Middleware", "UserMutationAttributionMiddleware.cs");
        StringAssert.Contains(attribution, "ActorUserId = actorUserId");
        StringAssert.Contains(attribution, "ProjectId = RouteValue(context, \"projectId\")");
    }

    private static string Controller() => Read("IronDev.Api", "Controllers", "MemoryController.cs");

    private static string Read(params string[] parts) =>
        File.ReadAllText(parts.Aggregate(RepositoryRoot(), Path.Combine));

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "IronDev.slnx"))) return directory.FullName;
            directory = directory.Parent;
        }
        throw new DirectoryNotFoundException("Could not locate repository root.");
    }
}
