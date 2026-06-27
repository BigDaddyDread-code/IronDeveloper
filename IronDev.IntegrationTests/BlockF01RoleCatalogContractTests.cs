using IronDev.Core.Governance;

namespace IronDev.IntegrationTests;

[TestClass]
public sealed class BlockF01RoleCatalogContractTests
{
    [TestMethod]
    public void DefaultRoleCatalogIsValid()
    {
        var result = Service().ValidateDefaultCatalog();

        Assert.IsTrue(result.IsValid, string.Join("; ", result.Issues));
        AssertAuthorityWarnings(result);
    }

    [TestMethod]
    public void DefaultRoleCatalogHasStableCatalogIdAndVersion()
    {
        var catalog = Service().BuildDefaultCatalog();

        Assert.AreEqual(RoleCatalogService.DefaultCatalogId, catalog.CatalogId);
        Assert.AreEqual(RoleCatalogService.DefaultCatalogVersion, catalog.CatalogVersion);
    }

    [DataTestMethod]
    [DataRow(GovernanceRoleKind.Requester)]
    [DataRow(GovernanceRoleKind.Planner)]
    [DataRow(GovernanceRoleKind.Reviewer)]
    [DataRow(GovernanceRoleKind.ApproverCandidate)]
    [DataRow(GovernanceRoleKind.PolicyOwnerCandidate)]
    [DataRow(GovernanceRoleKind.SecurityReviewer)]
    [DataRow(GovernanceRoleKind.ReleaseReviewer)]
    [DataRow(GovernanceRoleKind.OperationsReviewer)]
    [DataRow(GovernanceRoleKind.ExecutorOperatorCandidate)]
    [DataRow(GovernanceRoleKind.RollbackReviewer)]
    [DataRow(GovernanceRoleKind.RecoveryReviewer)]
    [DataRow(GovernanceRoleKind.Auditor)]
    [DataRow(GovernanceRoleKind.Observer)]
    [DataRow(GovernanceRoleKind.AutomationAgent)]
    [DataRow(GovernanceRoleKind.SystemReadOnly)]
    public void DefaultRoleCatalogContainsCanonicalRole(GovernanceRoleKind kind)
    {
        var entries = Service().ListByKind(Service().BuildDefaultCatalog(), kind);

        Assert.AreEqual(1, entries.Count);
        Assert.AreEqual(kind, entries[0].RoleKind);
    }

    [TestMethod]
    public void DefaultRoleCatalogRoleIdsAreUnique()
    {
        var catalog = Service().BuildDefaultCatalog();
        var unique = catalog.Entries.Select(static entry => entry.RoleId).Distinct(StringComparer.OrdinalIgnoreCase).Count();

        Assert.AreEqual(catalog.Entries.Count, unique);
    }

    [TestMethod]
    public void DefaultRoleCatalogDisplayNamesAreUniqueCaseInsensitive()
    {
        var catalog = Service().BuildDefaultCatalog();
        var unique = catalog.Entries.Select(static entry => entry.DisplayName).Distinct(StringComparer.OrdinalIgnoreCase).Count();

        Assert.AreEqual(catalog.Entries.Count, unique);
    }

    [TestMethod]
    public void DefaultRoleCatalogEntriesHaveNonEmptySurfaces()
    {
        foreach (var entry in Service().BuildDefaultCatalog().Entries)
        {
            Assert.IsTrue(entry.Surfaces.Count > 0, entry.RoleId);
            Assert.IsFalse(entry.Surfaces.Contains(GovernanceRoleSurface.Unknown), entry.RoleId);
        }
    }

    [TestMethod]
    public void DefaultRoleCatalogEntriesHaveAuthorityDenyingBoundaryStatements()
    {
        foreach (var entry in Service().BuildDefaultCatalog().Entries)
        {
            StringAssert.Contains(entry.BoundaryStatement, "does not grant authority");
        }
    }

    [TestMethod]
    public void NullRoleCatalogEntryFailsClosed()
    {
        var result = RoleCatalogValidator.ValidateEntry(null);

        Assert.IsFalse(result.IsValid);
        CollectionAssert.Contains(result.Issues.ToList(), "RoleCatalogEntryRequired");
        AssertAuthorityWarnings(result);
    }

    [DataTestMethod]
    [DataRow("missing-role-id", "RoleCatalogEntryRoleIdRequired")]
    [DataRow("malformed-role-id", "RoleCatalogEntryRoleIdInvalid")]
    [DataRow("missing-catalog-version", "RoleCatalogEntryCatalogVersionRequired")]
    [DataRow("unknown-role-kind", "RoleCatalogEntryRoleKindUnknown")]
    [DataRow("unknown-scope-kind", "RoleCatalogEntryScopeKindUnknown")]
    [DataRow("missing-display-name", "RoleCatalogEntryDisplayNameRequired")]
    [DataRow("missing-description", "RoleCatalogEntryDescriptionRequired")]
    [DataRow("missing-responsibility", "RoleCatalogEntryResponsibilitySummaryRequired")]
    [DataRow("missing-created-reason", "RoleCatalogEntryCreatedReasonRequired")]
    [DataRow("missing-boundary", "RoleCatalogEntryBoundaryStatementRequired")]
    [DataRow("boundary-without-denial", "RoleCatalogEntryBoundaryStatementMustDenyAuthority")]
    [DataRow("empty-surfaces", "RoleCatalogEntrySurfacesRequired")]
    [DataRow("unknown-surface", "RoleCatalogEntrySurfaceUnknown")]
    [DataRow("deprecated-without-replacement", "RoleCatalogEntryDeprecatedReplacementOrTerminalReasonRequired")]
    [DataRow("malformed-replacement", "RoleCatalogEntryReplacementRoleIdInvalid")]
    [DataRow("replacement-on-active", "RoleCatalogEntryReplacementRoleIdOnlyForDeprecatedEntry")]
    [DataRow("unsafe-text", "RoleCatalogEntryDescriptionUnsafe")]
    public void InvalidRoleCatalogEntryFailsClosed(string caseName, string expectedIssue)
    {
        var result = RoleCatalogValidator.ValidateEntry(InvalidEntry(caseName));

        Assert.IsFalse(result.IsValid);
        CollectionAssert.Contains(result.Issues.ToList(), expectedIssue);
        AssertAuthorityWarnings(result);
    }

    [TestMethod]
    public void NullRoleCatalogFailsClosed()
    {
        var result = RoleCatalogValidator.ValidateCatalog(null);

        Assert.IsFalse(result.IsValid);
        CollectionAssert.Contains(result.Issues.ToList(), "RoleCatalogRequired");
        AssertAuthorityWarnings(result);
    }

    [DataTestMethod]
    [DataRow("empty", "RoleCatalogEntriesRequired")]
    [DataRow("duplicate-role-id", "RoleCatalogDuplicateRoleId:role:f01:reviewer")]
    [DataRow("duplicate-display-name", "RoleCatalogDuplicateDisplayName:Reviewer")]
    [DataRow("invalid-entry", "Entry::RoleCatalogEntryRoleIdRequired")]
    [DataRow("boundary-without-denial", "RoleCatalogBoundaryStatementMustDenyAuthority")]
    public void InvalidCatalogFailsClosed(string caseName, string expectedIssue)
    {
        var result = RoleCatalogValidator.ValidateCatalog(InvalidCatalog(caseName));

        Assert.IsFalse(result.IsValid);
        CollectionAssert.Contains(result.Issues.ToList(), expectedIssue);
        AssertAuthorityWarnings(result);
    }

    [TestMethod]
    public void DuplicateRoleIdsAreReported()
    {
        var result = RoleCatalogValidator.ValidateCatalog(InvalidCatalog("duplicate-role-id"));

        CollectionAssert.Contains(result.DuplicateRoleIds.ToList(), "role:f01:reviewer");
    }

    [TestMethod]
    public void UnsafeRoleTextReportsUnsafeRoleId()
    {
        var result = RoleCatalogValidator.ValidateEntry(Entry() with { Description = "This can execute changes." });

        Assert.IsFalse(result.IsValid);
        CollectionAssert.Contains(result.UnsafeRoleIds.ToList(), "role:f01:reviewer");
    }

    [TestMethod]
    public void FindByRoleIdReturnsMatchingEntry()
    {
        var entry = Service().FindByRoleId(Service().BuildDefaultCatalog(), "role:f01:reviewer");

        Assert.IsNotNull(entry);
        Assert.AreEqual(GovernanceRoleKind.Reviewer, entry.RoleKind);
    }

    [TestMethod]
    public void FindByRoleIdReturnsNullForMissingRole()
    {
        var entry = Service().FindByRoleId(Service().BuildDefaultCatalog(), "role:f01:nope");

        Assert.IsNull(entry);
    }

    [TestMethod]
    public void ListBySurfaceReturnsOnlyMatchingEntries()
    {
        var entries = Service().ListBySurface(Service().BuildDefaultCatalog(), GovernanceRoleSurface.ReleaseReadiness);

        Assert.IsTrue(entries.Count > 0);
        Assert.IsTrue(entries.All(static entry => entry.Surfaces.Contains(GovernanceRoleSurface.ReleaseReadiness)));
    }

    [TestMethod]
    public void ListByKindReturnsOnlyMatchingEntries()
    {
        var entries = Service().ListByKind(Service().BuildDefaultCatalog(), GovernanceRoleKind.Auditor);

        Assert.AreEqual(1, entries.Count);
        Assert.IsTrue(entries.All(static entry => entry.RoleKind == GovernanceRoleKind.Auditor));
    }

    [DataTestMethod]
    [DataRow(GovernanceRoleKind.ApproverCandidate, "role catalog is not approval")]
    [DataRow(GovernanceRoleKind.PolicyOwnerCandidate, "role catalog is not policy satisfaction")]
    [DataRow(GovernanceRoleKind.ExecutorOperatorCandidate, "role catalog is not execution authority")]
    [DataRow(GovernanceRoleKind.ReleaseReviewer, "role catalog is not release authority")]
    [DataRow(GovernanceRoleKind.AutomationAgent, "role catalog is not workflow continuation authority")]
    [DataRow(GovernanceRoleKind.SystemReadOnly, "role catalog is not mutation authority")]
    [DataRow(GovernanceRoleKind.Observer, "role catalog is not mutation authority")]
    [DataRow(GovernanceRoleKind.Auditor, "role catalog is not mutation authority")]
    public void CatalogRolesDoNotGrantAuthority(GovernanceRoleKind kind, string forbiddenImplication)
    {
        var result = Service().ValidateDefaultCatalog();
        var entries = Service().ListByKind(Service().BuildDefaultCatalog(), kind);

        Assert.AreEqual(1, entries.Count);
        CollectionAssert.Contains(result.ForbiddenAuthorityImplications.ToList(), forbiddenImplication);
        AssertAuthorityWarnings(result);
    }

    [DataTestMethod]
    [DataRow("approval profile")]
    [DataRow("policy review")]
    [DataRow("release readiness")]
    [DataRow("workflow continuation review")]
    [DataRow("source apply review")]
    [DataRow("merge readiness review")]
    [DataRow("deployment readiness review")]
    public void ValidGovernanceDomainTextIsNotRejected(string text)
    {
        var result = RoleCatalogValidator.ValidateEntry(Entry() with
        {
            Description = $"Names a responsibility type for {text}.",
            ResponsibilitySummary = $"May be referenced by future {text} profile contracts."
        });

        Assert.IsTrue(result.IsValid, string.Join("; ", result.Issues));
    }

    [DataTestMethod]
    [DataRow("can approve")]
    [DataRow("can execute")]
    [DataRow("can mutate")]
    [DataRow("can merge")]
    [DataRow("permission granted")]
    [DataRow("authority granted")]
    [DataRow("approval granted")]
    [DataRow("policy satisfied")]
    [DataRow("workflow continuation authorized")]
    [DataRow("user assigned")]
    [DataRow("principal")]
    [DataRow("raw policy")]
    [DataRow("raw approval")]
    [DataRow("raw command")]
    [DataRow("private reasoning")]
    public void UnsafeRoleTextFails(string unsafeText)
    {
        var result = RoleCatalogValidator.ValidateEntry(Entry() with { Description = $"Bad text says {unsafeText}." });

        Assert.IsFalse(result.IsValid);
        Assert.IsTrue(result.Issues.Any(static issue => issue.EndsWith("Unsafe", StringComparison.Ordinal)));
    }

    [TestMethod]
    public void LookupDoesNotGrantAuthorityOrSatisfyPolicyOrApproveWork()
    {
        var catalog = Service().BuildDefaultCatalog();
        var entry = Service().FindByRoleId(catalog, "role:f01:approver-candidate");
        var validation = RoleCatalogValidator.ValidateCatalog(catalog);

        Assert.IsNotNull(entry);
        CollectionAssert.Contains(validation.Warnings.ToList(), "role catalog does not approve work");
        CollectionAssert.Contains(validation.Warnings.ToList(), "role catalog does not satisfy policy");
        CollectionAssert.Contains(validation.Warnings.ToList(), "role catalog does not authorize execution");
        AssertNoAuthorityFields(typeof(GovernanceRoleCatalogEntry));
    }

    [TestMethod]
    public void F01AddsNoApiCliUiWorkerOrOpenApiSurface()
    {
        var changedNames = F01AllowedFileNames();

        Assert.IsTrue(changedNames.All(static path =>
            path.StartsWith("IronDev.Core/Governance/", StringComparison.Ordinal) ||
            path.StartsWith("IronDev.IntegrationTests/", StringComparison.Ordinal) ||
            path.StartsWith("Docs/receipts/", StringComparison.Ordinal)));
        Assert.IsFalse(changedNames.Any(static path =>
            path.StartsWith("IronDev.Api/", StringComparison.Ordinal) ||
            path.StartsWith("IronDev.Cli/", StringComparison.Ordinal) ||
            path.StartsWith("IronDev.Infrastructure/", StringComparison.Ordinal) ||
            path.StartsWith("OpenApi/", StringComparison.Ordinal)));
    }

    [TestMethod]
    public void F01AddsNoPersistenceSqlIdentityPrincipalPermissionOrExecutorSurface()
    {
        var source = StripStrings(F01CoreSource());

        AssertDoesNotContain(source, "SqlConnection");
        AssertDoesNotContain(source, "DbContext");
        AssertDoesNotContain(source, "IRepository");
        AssertDoesNotContain(source, "Repository<");
        AssertDoesNotContain(source, "Repository(");
        AssertDoesNotContain(source, "Store<");
        AssertDoesNotContain(source, "Store(");
        AssertDoesNotContain(source, "ClaimsPrincipal");
        AssertDoesNotContain(source, "IPrincipal");
        AssertDoesNotContain(source, "UserManager");
        AssertDoesNotContain(source, "RoleManager");
        AssertDoesNotContain(source, "Authorize");
        AssertDoesNotContain(source, "Permission");
        AssertDoesNotContain(source, "ExecuteAsync");
        AssertDoesNotContain(source, "ProcessStartInfo");
        AssertDoesNotContain(source, "HttpClient");
        AssertDoesNotContain(source, "GitHub");
        AssertDoesNotContain(source, "Octokit");
    }

    [TestMethod]
    public void F01DoesNotIntroduceAuthorityShapedRoleFields()
    {
        var fieldNames = typeof(GovernanceRoleCatalogEntry)
            .GetProperties()
            .Select(static property => property.Name)
            .Concat(typeof(GovernanceRoleCatalog).GetProperties().Select(static property => property.Name))
            .ToArray();

        AssertNoAuthorityNames(fieldNames);
    }

    [TestMethod]
    public void F01DoesNotIntroduceAuthorityShapedDecisionNames()
    {
        var enumNames = Enum.GetNames<GovernanceRoleKind>()
            .Concat(Enum.GetNames<GovernanceRoleScopeKind>())
            .Concat(Enum.GetNames<GovernanceRoleSurface>())
            .ToArray();

        AssertNoAuthorityNames(enumNames);
    }

    [DataTestMethod]
    [DataRow("A role catalog names responsibility types. It does not grant authority.")]
    [DataRow("A role name is not permission.")]
    [DataRow("catalog-only")]
    [DataRow("identity assignment")]
    [DataRow("user assignment")]
    [DataRow("group assignment")]
    [DataRow("permission grant")]
    [DataRow("approval")]
    [DataRow("policy satisfaction")]
    [DataRow("execution authority")]
    [DataRow("mutation authority")]
    [DataRow("source apply authority")]
    [DataRow("commit authority")]
    [DataRow("push authority")]
    [DataRow("pull request authority")]
    [DataRow("ready-for-review authority")]
    [DataRow("merge authority")]
    [DataRow("release authority")]
    [DataRow("deployment authority")]
    [DataRow("workflow continuation")]
    public void ReceiptContainsBoundaryLines(string phrase)
    {
        var receipt = File.ReadAllText(Path.Combine(RepositoryRoot(), "Docs", "receipts", "F01_ROLE_CATALOG_CONTRACT.md"));

        StringAssert.Contains(receipt, phrase);
    }

    private static RoleCatalogService Service() => new();

    private static GovernanceRoleCatalogEntry Entry() =>
        new()
        {
            RoleId = "role:f01:reviewer",
            CatalogVersion = "f01",
            RoleKind = GovernanceRoleKind.Reviewer,
            ScopeKind = GovernanceRoleScopeKind.ProjectScoped,
            DisplayName = "Reviewer",
            Description = "Names a responsibility type for proposal review.",
            ResponsibilitySummary = "May be referenced by future review profile contracts.",
            Surfaces = [GovernanceRoleSurface.Proposal],
            IsDeprecated = false,
            ReplacementRoleId = null,
            CreatedReason = "Block F01 canonical role vocabulary.",
            BoundaryStatement = "This role does not grant authority."
        };

    private static GovernanceRoleCatalog Catalog(params GovernanceRoleCatalogEntry[] entries) =>
        new()
        {
            CatalogId = RoleCatalogService.DefaultCatalogId,
            CatalogVersion = RoleCatalogService.DefaultCatalogVersion,
            Entries = entries,
            CreatedReason = "Block F01 canonical role catalog contract.",
            BoundaryStatement = "The role catalog does not grant authority."
        };

    private static GovernanceRoleCatalogEntry InvalidEntry(string caseName) =>
        caseName switch
        {
            "missing-role-id" => Entry() with { RoleId = "" },
            "malformed-role-id" => Entry() with { RoleId = "reviewer" },
            "missing-catalog-version" => Entry() with { CatalogVersion = "" },
            "unknown-role-kind" => Entry() with { RoleKind = GovernanceRoleKind.Unknown },
            "unknown-scope-kind" => Entry() with { ScopeKind = GovernanceRoleScopeKind.Unknown },
            "missing-display-name" => Entry() with { DisplayName = "" },
            "missing-description" => Entry() with { Description = "" },
            "missing-responsibility" => Entry() with { ResponsibilitySummary = "" },
            "missing-created-reason" => Entry() with { CreatedReason = "" },
            "missing-boundary" => Entry() with { BoundaryStatement = "" },
            "boundary-without-denial" => Entry() with { BoundaryStatement = "This role is descriptive." },
            "empty-surfaces" => Entry() with { Surfaces = [] },
            "unknown-surface" => Entry() with { Surfaces = [GovernanceRoleSurface.Unknown] },
            "deprecated-without-replacement" => Entry() with { IsDeprecated = true, ReplacementRoleId = null, CreatedReason = "Block F01 deprecated role." },
            "malformed-replacement" => Entry() with { IsDeprecated = true, ReplacementRoleId = "role:f01" },
            "replacement-on-active" => Entry() with { ReplacementRoleId = "role:f01:planner" },
            "unsafe-text" => Entry() with { Description = "This role can execute." },
            _ => throw new ArgumentOutOfRangeException(nameof(caseName), caseName, null)
        };

    private static GovernanceRoleCatalog InvalidCatalog(string caseName) =>
        caseName switch
        {
            "empty" => Catalog(),
            "duplicate-role-id" => Catalog(
                Entry(),
                Entry() with { DisplayName = "Second Reviewer" }),
            "duplicate-display-name" => Catalog(
                Entry(),
                Entry() with { RoleId = "role:f01:planner", RoleKind = GovernanceRoleKind.Planner }),
            "invalid-entry" => Catalog(Entry() with { RoleId = "" }),
            "boundary-without-denial" => Catalog(Entry()) with { BoundaryStatement = "The role catalog names things." },
            _ => throw new ArgumentOutOfRangeException(nameof(caseName), caseName, null)
        };

    private static void AssertAuthorityWarnings(GovernanceRoleCatalogValidationResult result)
    {
        CollectionAssert.Contains(result.Warnings.ToList(), "role catalog is descriptive only");
        CollectionAssert.Contains(result.Warnings.ToList(), "role catalog does not assign users");
        CollectionAssert.Contains(result.Warnings.ToList(), "role catalog does not grant permissions");
        CollectionAssert.Contains(result.Warnings.ToList(), "role catalog does not approve work");
        CollectionAssert.Contains(result.Warnings.ToList(), "role catalog does not satisfy policy");
        CollectionAssert.Contains(result.Warnings.ToList(), "role catalog does not authorize execution");
        CollectionAssert.Contains(result.Warnings.ToList(), "role catalog does not authorize mutation");
        CollectionAssert.Contains(result.Warnings.ToList(), "role catalog does not continue workflow");
        CollectionAssert.Contains(result.ForbiddenAuthorityImplications.ToList(), "role catalog is not identity");
        CollectionAssert.Contains(result.ForbiddenAuthorityImplications.ToList(), "role catalog is not authorization");
        CollectionAssert.Contains(result.ForbiddenAuthorityImplications.ToList(), "role catalog is not approval");
        CollectionAssert.Contains(result.ForbiddenAuthorityImplications.ToList(), "role catalog is not policy satisfaction");
        CollectionAssert.Contains(result.ForbiddenAuthorityImplications.ToList(), "role catalog is not execution authority");
        CollectionAssert.Contains(result.ForbiddenAuthorityImplications.ToList(), "role catalog is not mutation authority");
        CollectionAssert.Contains(result.ForbiddenAuthorityImplications.ToList(), "role catalog is not workflow continuation authority");
    }

    private static void AssertNoAuthorityFields(Type type) =>
        AssertNoAuthorityNames(type.GetProperties().Select(static property => property.Name));

    private static void AssertNoAuthorityNames(IEnumerable<string> names)
    {
        var forbidden = new[]
        {
            "CanApprove",
            "CanExecute",
            "CanMutate",
            "CanMerge",
            "CanRelease",
            "CanDeploy",
            "PolicySatisfied",
            "ApprovalGranted",
            "AuthorityGranted"
        };

        foreach (var name in names)
        {
            CollectionAssert.DoesNotContain(forbidden, name);
        }
    }

    private static string F01CoreSource()
    {
        var root = RepositoryRoot();
        var files = Directory.GetFiles(Path.Combine(root, "IronDev.Core", "Governance"), "RoleCatalog*.cs");
        return string.Join(Environment.NewLine, files.Select(File.ReadAllText));
    }

    private static IReadOnlyList<string> F01AllowedFileNames() =>
    [
        "IronDev.Core/Governance/RoleCatalogModels.cs",
        "IronDev.Core/Governance/RoleCatalogValidator.cs",
        "IronDev.Core/Governance/RoleCatalogService.cs",
        "IronDev.IntegrationTests/BlockF01RoleCatalogContractTests.cs",
        "Docs/receipts/F01_ROLE_CATALOG_CONTRACT.md"
    ];

    private static string StripStrings(string source) =>
        System.Text.RegularExpressions.Regex.Replace(source, "\"(?:\\\\.|[^\"])*\"", "\"\"");

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "IronDev.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }

    private static void AssertDoesNotContain(string source, string forbidden) =>
        Assert.IsFalse(
            source.Contains(forbidden, StringComparison.Ordinal),
            $"Unexpected forbidden marker found in F01 source: {forbidden}");
}
