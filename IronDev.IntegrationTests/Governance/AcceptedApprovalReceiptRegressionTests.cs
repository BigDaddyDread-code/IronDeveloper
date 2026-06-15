using System.Reflection;
using IronDev.Api.Controllers;
using IronDev.Core.Governance;
using IronDev.Infrastructure.Governance;
using Microsoft.AspNetCore.Mvc;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests.Governance;

[TestClass]
[TestCategory("AcceptedApprovalReceiptRegression")]
public sealed class AcceptedApprovalReceiptRegressionTests
{
    private static readonly string[] RequiredBindingProperties =
    [
        nameof(AcceptedApprovalRecord.ApprovalTargetKind),
        nameof(AcceptedApprovalRecord.ApprovalTargetId),
        nameof(AcceptedApprovalRecord.ApprovalTargetHash),
        nameof(AcceptedApprovalRecord.ProjectId),
        nameof(AcceptedApprovalRecord.CapabilityCode),
        nameof(AcceptedApprovalRecord.ApprovalPurpose),
        nameof(AcceptedApprovalRecord.ApprovedByActorId),
        nameof(AcceptedApprovalRecord.AcceptedAtUtc),
        nameof(AcceptedApprovalRecord.CorrelationId),
        nameof(AcceptedApprovalRecord.CausationId),
        nameof(AcceptedApprovalRecord.EvidenceReferences),
        nameof(AcceptedApprovalRecord.BoundaryMaxims)
    ];

    [TestMethod]
    public void AcceptedApprovalReceiptRegression_ReceiptExists() =>
        Assert.IsTrue(File.Exists(ReceiptPath()), "PR172 accepted approval receipt must exist.");

    [TestMethod]
    public void AcceptedApprovalReceiptRegression_RecordsInstalledAcceptedApprovalChain()
    {
        var receipt = ReceiptText();

        foreach (var statement in new[]
        {
            "PR168 Accepted Approval Record Contract",
            "PR169 Accepted Approval SQL Store",
            "PR170 Accepted Approval Read API",
            "PR171 Governed Accepted Approval Create API",
            "PR168 Accepted Approval Record Contract defined the accepted approval record contract.",
            "PR169 Accepted Approval SQL Store made accepted approval records durable in SQL.",
            "PR170 Accepted Approval Read API exposed accepted approval records through read-only project-scoped API.",
            "PR171 Governed Accepted Approval Create API added the governed project-scoped create API.",
            "contract -> SQL store -> read API -> governed create API"
        })
        {
            StringAssert.Contains(receipt, statement);
        }
    }

    [TestMethod]
    public void AcceptedApprovalReceiptRegression_StatesAcceptedApprovalCanBeFiledButNotSpent()
    {
        var receipt = ReceiptText();

        StringAssert.Contains(receipt, "Accepted approval records can now be filed.");
        StringAssert.Contains(receipt, "Accepted approval records still cannot be spent.");
    }

    [TestMethod]
    public void AcceptedApprovalReceiptRegression_ContainsBoundaryMaxims()
    {
        var receipt = ReceiptText();

        foreach (var maxim in BoundaryMaxims())
        {
            StringAssert.Contains(receipt, maxim);
        }
    }

    [TestMethod]
    public void AcceptedApprovalReceiptRegression_ContractStillRequiresTargetHash()
    {
        foreach (var property in RequiredBindingProperties)
        {
            Assert.IsNotNull(typeof(AcceptedApprovalRecord).GetProperty(property), $"AcceptedApprovalRecord must keep required binding property {property}.");
        }

        var invalid = ValidRecord() with { ApprovalTargetHash = " " };
        var result = AcceptedApprovalValidation.Validate(invalid);

        Assert.IsFalse(result.IsValid, "Accepted approval without target hash must remain invalid.");
        Assert.IsTrue(result.Issues.Any(issue => issue.Code == "APPROVAL_TARGET_HASH_REQUIRED"), string.Join(", ", result.Issues.Select(issue => issue.Code)));
    }

    [TestMethod]
    public void AcceptedApprovalReceiptRegression_StoreStillAppendOnly()
    {
        var forbidden = new[] { "Update", "Delete", "Remove", "Overwrite", "Upsert" };
        var methods = typeof(IAcceptedApprovalStore)
            .GetMethods()
            .Concat(typeof(SqlAcceptedApprovalStore).GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            .ToArray();

        foreach (var method in methods)
        foreach (var token in forbidden)
        {
            Assert.IsFalse(method.Name.Contains(token, StringComparison.OrdinalIgnoreCase), $"Accepted approval store must not expose {token}: {method.Name}");
        }

        StringAssert.Contains(SqlMigrationText(), "TR_AcceptedApproval_BlockUpdateDelete");
    }

    [TestMethod]
    public void AcceptedApprovalReceiptRegression_StoreStillValidatesBeforeSave() =>
        StringAssert.Contains(FileText("IronDev.Infrastructure", "Governance", "SqlAcceptedApprovalStore.cs"), "AcceptedApprovalValidation.Validate");

    [TestMethod]
    public void AcceptedApprovalReceiptRegression_ReadApiStillGetOnly()
    {
        foreach (var methodName in new[] { "Get", "ListByTarget", "ListByCorrelation" })
        {
            var method = ControllerMethod(methodName);
            Assert.IsTrue(method.GetCustomAttributes(typeof(HttpGetAttribute), inherit: false).Any(), $"{methodName} must remain GET.");
            Assert.IsFalse(method.GetCustomAttributes(typeof(HttpPostAttribute), inherit: false).Any(), $"{methodName} must not be POST.");
            Assert.IsFalse(method.GetCustomAttributes(typeof(HttpPutAttribute), inherit: false).Any(), $"{methodName} must not be PUT.");
            Assert.IsFalse(method.GetCustomAttributes(typeof(HttpPatchAttribute), inherit: false).Any(), $"{methodName} must not be PATCH.");
            Assert.IsFalse(method.GetCustomAttributes(typeof(HttpDeleteAttribute), inherit: false).Any(), $"{methodName} must not be DELETE.");
        }

        var controller = ControllerText();
        StringAssert.Contains(controller, "[HttpGet(\"{acceptedApprovalId:guid}\")]");
        StringAssert.Contains(controller, "[HttpGet(\"by-target/{approvalTargetKind}/{approvalTargetId}\")]");
        StringAssert.Contains(controller, "[HttpGet(\"by-correlation/{correlationId}\")]");
    }

    [TestMethod]
    public void AcceptedApprovalReceiptRegression_CreateApiStillSinglePostOnly()
    {
        var methods = typeof(AcceptedApprovalsV1Controller).GetMethods()
            .Where(method => method.DeclaringType == typeof(AcceptedApprovalsV1Controller))
            .ToArray();
        var create = ControllerMethod("Create");

        Assert.AreEqual(1, methods.Count(method => method.GetCustomAttributes(typeof(HttpPostAttribute), inherit: false).Any()), "Accepted approvals should expose exactly one POST route.");
        Assert.IsTrue(create.GetCustomAttributes(typeof(HttpPostAttribute), inherit: false).Any(), "Create must remain POST.");
        Assert.IsFalse(methods.Any(method => method.GetCustomAttributes(typeof(HttpPutAttribute), inherit: false).Any()), "Accepted approvals must not expose PUT.");
        Assert.IsFalse(methods.Any(method => method.GetCustomAttributes(typeof(HttpPatchAttribute), inherit: false).Any()), "Accepted approvals must not expose PATCH.");
        Assert.IsFalse(methods.Any(method => method.GetCustomAttributes(typeof(HttpDeleteAttribute), inherit: false).Any()), "Accepted approvals must not expose DELETE.");

        var controller = ControllerText();
        foreach (var forbidden in new[] { "approve-and-apply", "approve-and-release", "approve-and-satisfy-policy", "approve-and-continue" })
        {
            Assert.IsFalse(controller.Contains(forbidden, StringComparison.OrdinalIgnoreCase), $"Forbidden shortcut route found: {forbidden}");
        }
    }

    [TestMethod]
    public void AcceptedApprovalReceiptRegression_CreateApiStillRejectsClientOwnedFields()
    {
        var controller = ControllerText();

        foreach (var field in new[]
        {
            "acceptedApprovalId",
            "projectId",
            "approvedByActorId",
            "approvedByActorDisplayName",
            "acceptedAtUtc",
            "createdAtUtc",
            "isPolicySatisfied",
            "canApplySource",
            "canContinueWorkflow",
            "canApproveRelease"
        })
        {
            StringAssert.Contains(controller, $"\"{field}\"");
        }

        StringAssert.Contains(controller, "Field is server-owned and must not be supplied by the client.");
    }

    [TestMethod]
    public void AcceptedApprovalReceiptRegression_CreateApiStillDerivesServerOwnedFields()
    {
        var service = FileText("IronDev.Infrastructure", "Governance", "AcceptedApprovalCreateService.cs");

        foreach (var statement in new[]
        {
            "AcceptedApprovalId = Guid.NewGuid()",
            "ProjectId = projectId",
            "ApprovedByActorId = actor.ActorId",
            "AcceptedAtUtc = acceptedAtUtc",
            "_store.SaveAsync(record, cancellationToken)",
            "_query.GetAsync(record.ProjectId, record.AcceptedApprovalId, cancellationToken)"
        })
        {
            StringAssert.Contains(service, statement);
        }
    }

    [TestMethod]
    public void AcceptedApprovalReceiptRegression_ProjectScopingStillHolds()
    {
        var readTests = FileText("IronDev.IntegrationTests.Api", "AcceptedApprovalReadApiTests.cs");
        var createTests = FileText("IronDev.IntegrationTests.Api", "AcceptedApprovalCreateApiTests.cs");
        var store = FileText("IronDev.Infrastructure", "Governance", "SqlAcceptedApprovalStore.cs");
        var sql = SqlMigrationText();

        foreach (var statement in new[]
        {
            "AcceptedApprovalReadApi_DoesNotLeakAcrossProjects",
            "AcceptedApprovalReadApi_CanListByTargetWithinProject",
            "AcceptedApprovalReadApi_CanListByCorrelationWithinProject"
        })
        {
            StringAssert.Contains(readTests, statement);
        }

        StringAssert.Contains(createTests, "AcceptedApprovalCreateApi_CrossProjectRouteOwnsProjectId");
        StringAssert.Contains(store, "ListByProjectAndCorrelationAsync");
        StringAssert.Contains(sql, "WHERE ProjectId = @ProjectId");
    }

    [TestMethod]
    public void AcceptedApprovalReceiptRegression_PrivateMaterialStillRejected()
    {
        var service = FileText("IronDev.Infrastructure", "Governance", "AcceptedApprovalCreateService.cs");

        foreach (var marker in new[]
        {
            "chain-of-thought",
            "hidden reasoning",
            "private reasoning",
            "scratchpad",
            "raw prompt",
            "raw completion",
            "raw tool output",
            "system prompt",
            "developer prompt",
            "secret",
            "private key",
            "bearer "
        })
        {
            StringAssert.Contains(service, marker);
        }

        StringAssert.Contains(service, "RAW_PRIVATE_REASONING_REJECTED");
        StringAssert.Contains(service, "SECRET_MATERIAL_REJECTED");
    }

    [TestMethod]
    public void AcceptedApprovalReceiptRegression_AuthorityEscalationStillRejected()
    {
        var service = FileText("IronDev.Infrastructure", "Governance", "AcceptedApprovalCreateService.cs");

        foreach (var marker in new[]
        {
            "policy satisfied",
            "dry-run executed",
            "patch artifact created",
            "source applied",
            "workflow continued",
            "release ready",
            "release approved",
            "ready to ship",
            "grants execution"
        })
        {
            StringAssert.Contains(service, marker);
        }

        StringAssert.Contains(service, "AUTHORITY_ESCALATION_REJECTED");
    }

    [TestMethod]
    public void AcceptedApprovalReceiptRegression_ResponseStillCarriesBoundary()
    {
        Assert.AreEqual(
            "Accepted approval is necessary but not sufficient for policy satisfaction, source apply, workflow continuation, or release readiness.",
            AcceptedApprovalReadBoundaryText.AuthorityBoundary);
        Assert.AreEqual(AcceptedApprovalReadBoundaryText.AuthorityBoundary, AcceptedApprovalCreateBoundaryText.AuthorityBoundary);
    }

    [TestMethod]
    public void AcceptedApprovalReceiptRegression_CreateDoesNotCreatePolicySatisfaction()
    {
        var createTests = FileText("IronDev.IntegrationTests.Api", "AcceptedApprovalCreateApiTests.cs");

        StringAssert.Contains(createTests, "AcceptedApprovalCreateApi_DoesNotSatisfyPolicy");
        StringAssert.Contains(createTests, "governance.PolicyDecisionEvent");
        AssertNoForbiddenImplementationTokens("SatisfyPolicy", "PolicySatisfied = true");
    }

    [TestMethod]
    public void AcceptedApprovalReceiptRegression_CreateDoesNotCreateDryRunPatchApplyWorkflowOrReleaseEffects()
    {
        var createTests = FileText("IronDev.IntegrationTests.Api", "AcceptedApprovalCreateApiTests.cs");

        StringAssert.Contains(createTests, "AcceptedApprovalCreateApi_DoesNotApplySource");
        AssertNoForbiddenImplementationTokens(
            "RunDryRunAsync",
            "CreatePatchArtifactAsync",
            "ApplySourceAsync",
            "ContinueWorkflowAsync",
            "ApproveReleaseAsync",
            "ReleaseReady = true",
            "SourceApplyExecutor",
            "WorkflowContinuationExecutor",
            "ReleaseReadinessDecisionStore");
    }

    [TestMethod]
    public void AcceptedApprovalReceiptRegression_NoUiOrCliAcceptedApprovalCreatePath()
    {
        foreach (var file in UiAndCliSourceFiles())
        {
            var text = File.ReadAllText(file);
            foreach (var forbidden in new[]
            {
                "accepted-approvals create",
                "CreateAcceptedApprovalCommand",
                "ApproveAcceptedApprovalButton"
            })
            {
                Assert.IsFalse(text.Contains(forbidden, StringComparison.OrdinalIgnoreCase), $"UI/CLI file must not expose accepted approval creation: {file} contained {forbidden}");
            }
        }
    }

    [TestMethod]
    public void AcceptedApprovalReceiptRegression_NoReleaseReadinessShortcut() =>
        AssertNoForbiddenImplementationTokens(
            "ReleaseReady = true",
            "ApproveReleaseAsync",
            "ReleaseReadinessDecisionStore",
            "CanApproveRelease = true");

    [TestMethod]
    public void AcceptedApprovalReceiptRegression_NoSourceApplyShortcut() =>
        AssertNoForbiddenImplementationTokens(
            "CanApplySource = true",
            "ApplySourceAsync",
            "SourceApplyExecutor",
            "ControlledSourceApply");

    private static AcceptedApprovalRecord ValidRecord() =>
        new()
        {
            AcceptedApprovalId = Guid.NewGuid(),
            ProjectId = Guid.NewGuid(),
            ApprovalTargetKind = AcceptedApprovalTargetKinds.PatchArtifact,
            ApprovalTargetId = "patch-artifact-pr172",
            ApprovalTargetHash = "sha256:eeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeee",
            CapabilityCode = "L4_ACCEPTED_APPROVAL_RECORD",
            ApprovalPurpose = AcceptedApprovalPurposes.PolicySatisfactionInput,
            ApprovedByActorId = "human-operator-pr172",
            ApprovedByActorDisplayName = "Human Operator",
            AcceptedAtUtc = DateTimeOffset.UtcNow,
            ExpiresAtUtc = DateTimeOffset.UtcNow.AddDays(7),
            CorrelationId = "correlation-pr172",
            CausationId = "approval-package-pr172",
            EvidenceReferences = ["approval-package:approval-package-pr172"],
            BoundaryMaxims = BoundaryMaxims()
        };

    private static IReadOnlyList<string> BoundaryMaxims() =>
    [
        "Accepted approval record is not policy satisfaction.",
        "Accepted approval record is not dry-run execution.",
        "Accepted approval record is not patch artifact creation.",
        "Accepted approval record is not source apply.",
        "Accepted approval record is not rollback.",
        "Accepted approval record is not workflow continuation.",
        "Accepted approval record is not release readiness.",
        "Creating accepted approval does not authorize execution.",
        "Reading accepted approval does not authorize execution.",
        "Persisting accepted approval does not authorize execution.",
        "Approval package is not accepted approval.",
        "Human-looking approval text is not accepted approval.",
        "UI review is not accepted approval."
    ];

    private static MethodInfo ControllerMethod(string name) =>
        typeof(AcceptedApprovalsV1Controller).GetMethods()
            .Single(method => method.DeclaringType == typeof(AcceptedApprovalsV1Controller) && method.Name == name);

    private static string ReceiptText() => File.ReadAllText(ReceiptPath());

    private static string ReceiptPath() =>
        Path.Combine(RepoRoot(), "Docs", "receipts", "PR172_ACCEPTED_APPROVAL_RECEIPT_AND_REGRESSION_TESTS.md");

    private static string SqlMigrationText() => FileText("Database", "migrate_accepted_approval.sql");

    private static string ControllerText() => FileText("IronDev.Api", "Controllers", "AcceptedApprovalsV1Controller.cs");

    private static string FileText(params string[] pathParts) =>
        File.ReadAllText(Path.Combine(RepoRoot(), Path.Combine(pathParts)));

    private static void AssertNoForbiddenImplementationTokens(params string[] tokens)
    {
        foreach (var file in AcceptedApprovalProductionFiles())
        {
            var text = File.ReadAllText(file);
            foreach (var token in tokens)
            {
                Assert.IsFalse(text.Contains(token, StringComparison.Ordinal), $"Forbidden implementation token found in {file}: {token}");
            }
        }
    }

    private static IReadOnlyList<string> AcceptedApprovalProductionFiles()
    {
        var root = RepoRoot();
        return
        [
            Path.Combine(root, "IronDev.Core", "Governance", "AcceptedApprovalRecord.cs"),
            Path.Combine(root, "IronDev.Core", "Governance", "AcceptedApprovalReadModels.cs"),
            Path.Combine(root, "IronDev.Core", "Governance", "AcceptedApprovalCreateModels.cs"),
            Path.Combine(root, "IronDev.Core", "Governance", "IAcceptedApprovalStore.cs"),
            Path.Combine(root, "IronDev.Infrastructure", "Governance", "SqlAcceptedApprovalStore.cs"),
            Path.Combine(root, "IronDev.Infrastructure", "Governance", "AcceptedApprovalQueryService.cs"),
            Path.Combine(root, "IronDev.Infrastructure", "Governance", "AcceptedApprovalCreateService.cs"),
            Path.Combine(root, "IronDev.Api", "Controllers", "AcceptedApprovalsV1Controller.cs")
        ];
    }

    private static IEnumerable<string> UiAndCliSourceFiles()
    {
        var root = RepoRoot();
        foreach (var directory in new[] { Path.Combine(root, "IronDev.TauriShell"), Path.Combine(root, "tools", "IronDev.Cli") })
        {
            if (!Directory.Exists(directory))
            {
                continue;
            }

            foreach (var file in Directory.EnumerateFiles(directory, "*.*", SearchOption.AllDirectories)
                .Where(file => file.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)
                               || file.EndsWith(".ts", StringComparison.OrdinalIgnoreCase)
                               || file.EndsWith(".tsx", StringComparison.OrdinalIgnoreCase)
                               || file.EndsWith(".js", StringComparison.OrdinalIgnoreCase)
                               || file.EndsWith(".jsx", StringComparison.OrdinalIgnoreCase)
                               || file.EndsWith(".svelte", StringComparison.OrdinalIgnoreCase)))
            {
                yield return file;
            }
        }
    }

    private static string RepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "IronDev.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not locate repository root.");
    }
}
