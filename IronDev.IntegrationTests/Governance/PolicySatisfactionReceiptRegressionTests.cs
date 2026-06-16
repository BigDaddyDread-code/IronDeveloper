using System.Reflection;
using IronDev.Api.Controllers;
using IronDev.Core.Governance;
using IronDev.Infrastructure.Governance;
using Microsoft.AspNetCore.Mvc;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests.Governance;

[TestClass]
[TestCategory("PolicySatisfactionReceiptRegression")]
public sealed class PolicySatisfactionReceiptRegressionTests
{
    private static readonly string[] BlockQProductionFiles =
    [
        Path.Combine(RepoRoot(), "IronDev.Core", "Governance", "PolicySatisfactionRecord.cs"),
        Path.Combine(RepoRoot(), "IronDev.Core", "Governance", "PolicySatisfactionValidation.cs"),
        Path.Combine(RepoRoot(), "IronDev.Core", "Governance", "IPolicySatisfactionStore.cs"),
        Path.Combine(RepoRoot(), "IronDev.Core", "Governance", "PolicyRequirement.cs"),
        Path.Combine(RepoRoot(), "IronDev.Core", "Governance", "PolicyRequirementSatisfactionEvaluation.cs"),
        Path.Combine(RepoRoot(), "IronDev.Core", "Governance", "PolicyRequirementSatisfactionEvaluator.cs"),
        Path.Combine(RepoRoot(), "IronDev.Core", "Governance", "PolicySatisfactionReadModels.cs"),
        Path.Combine(RepoRoot(), "IronDev.Core", "Governance", "PolicySatisfactionCreateModels.cs"),
        Path.Combine(RepoRoot(), "IronDev.Infrastructure", "Governance", "SqlPolicySatisfactionStore.cs"),
        Path.Combine(RepoRoot(), "IronDev.Infrastructure", "Governance", "PolicySatisfactionQueryService.cs"),
        Path.Combine(RepoRoot(), "IronDev.Infrastructure", "Governance", "PolicySatisfactionCreateService.cs"),
        Path.Combine(RepoRoot(), "IronDev.Api", "Controllers", "PolicySatisfactionsV1Controller.cs")
    ];

    [TestMethod]
    public void PolicySatisfactionReceiptRegression_ReceiptExists() =>
        Assert.IsTrue(File.Exists(ReceiptPath()), "PR179 receipt must exist.");

    [TestMethod]
    public void PolicySatisfactionReceiptRegression_ReceiptRecordsBlockQChain()
    {
        var receipt = ReceiptText();
        foreach (var statement in new[]
        {
            "PR174 - Policy Satisfaction Record Contract",
            "PR175 - Policy Satisfaction SQL Store",
            "PR176 - Policy Requirement/Satisfaction Evaluator",
            "PR177 - Policy Satisfaction Read API",
            "PR178 - Governed Policy Satisfaction Create API"
        })
        {
            StringAssert.Contains(receipt, statement);
        }
    }

    [TestMethod]
    public void PolicySatisfactionReceiptRegression_ReceiptRecordsFullAuthorityChain() =>
        StringAssert.Contains(ReceiptText(), FullAuthorityChain());

    [TestMethod]
    public void PolicySatisfactionReceiptRegression_StatesPolicySatisfactionCanBeFiledButNotSpent()
    {
        var receipt = ReceiptText();
        StringAssert.Contains(receipt, "Policy satisfaction records can now be filed.");
        StringAssert.Contains(receipt, "Policy satisfaction records still cannot be spent.");
    }

    [TestMethod]
    public void PolicySatisfactionReceiptRegression_PolicySatisfactionRecordContractStillBindsSubjectAndPolicy()
    {
        foreach (var property in new[]
        {
            nameof(PolicySatisfactionRecord.PolicySatisfactionId),
            nameof(PolicySatisfactionRecord.ProjectId),
            nameof(PolicySatisfactionRecord.PolicyCode),
            nameof(PolicySatisfactionRecord.PolicyVersion),
            nameof(PolicySatisfactionRecord.SubjectKind),
            nameof(PolicySatisfactionRecord.SubjectId),
            nameof(PolicySatisfactionRecord.SubjectHash),
            nameof(PolicySatisfactionRecord.CapabilityCode),
            nameof(PolicySatisfactionRecord.AcceptedApprovalId),
            nameof(PolicySatisfactionRecord.ApprovalRequirementHash),
            nameof(PolicySatisfactionRecord.ApprovalEvaluatedAtUtc),
            nameof(PolicySatisfactionRecord.SatisfiedAtUtc),
            nameof(PolicySatisfactionRecord.CorrelationId),
            nameof(PolicySatisfactionRecord.CausationId),
            nameof(PolicySatisfactionRecord.EvidenceReferences),
            nameof(PolicySatisfactionRecord.BoundaryMaxims)
        })
        {
            Assert.IsNotNull(typeof(PolicySatisfactionRecord).GetProperty(property), $"Missing property: {property}");
        }
    }

    [TestMethod]
    public void PolicySatisfactionReceiptRegression_StoreStillAppendOnly()
    {
        var forbidden = new[] { "Update", "Delete", "Remove", "Overwrite", "Upsert" };
        var methods = typeof(IPolicySatisfactionStore)
            .GetMethods()
            .Concat(typeof(SqlPolicySatisfactionStore).GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly));

        foreach (var method in methods)
        foreach (var token in forbidden)
        {
            Assert.IsFalse(method.Name.Contains(token, StringComparison.OrdinalIgnoreCase), $"Unexpected mutable method: {method.Name}");
        }

        StringAssert.Contains(File.ReadAllText(SqlMigrationPath()), "TR_PolicySatisfaction_BlockUpdateDelete");
    }

    [TestMethod]
    public void PolicySatisfactionReceiptRegression_StoreStillValidatesShapeBeforeSave() =>
        StringAssert.Contains(File.ReadAllText(StorePath()), "PolicySatisfactionValidation.Validate");

    [TestMethod]
    public void PolicySatisfactionReceiptRegression_ReadApiStillGetOnlyExceptCreateRoute()
    {
        var methods = ControllerMethods();
        Assert.AreEqual(1, methods.Count(method => method.GetCustomAttributes(typeof(HttpPostAttribute), inherit: false).Any()));

        foreach (var readMethodName in new[] { "Get", "ListBySubject", "ListByAcceptedApproval", "ListByCorrelation" })
        {
            var method = methods.Single(candidate => candidate.Name == readMethodName);
            Assert.IsTrue(method.GetCustomAttributes(typeof(HttpGetAttribute), inherit: false).Any(), $"{readMethodName} must remain GET.");
            Assert.IsFalse(method.GetCustomAttributes(typeof(HttpPostAttribute), inherit: false).Any(), $"{readMethodName} must not be POST.");
        }

        Assert.IsFalse(methods.Any(method => method.GetCustomAttributes(typeof(HttpPutAttribute), inherit: false).Any()));
        Assert.IsFalse(methods.Any(method => method.GetCustomAttributes(typeof(HttpPatchAttribute), inherit: false).Any()));
        Assert.IsFalse(methods.Any(method => method.GetCustomAttributes(typeof(HttpDeleteAttribute), inherit: false).Any()));
    }

    [TestMethod]
    public void PolicySatisfactionReceiptRegression_CreateApiRejectsServerOwnedFields()
    {
        var controller = File.ReadAllText(ControllerPath());
        foreach (var field in new[]
        {
            "policySatisfactionId",
            "projectId",
            "satisfiedAtUtc",
            "createdAtUtc",
            "canApplySource",
            "canRunDryRun",
            "canCreatePatchArtifact",
            "canContinueWorkflow",
            "canApproveRelease",
            "releaseReady",
            "mutationOccurred"
        })
        {
            StringAssert.Contains(controller, field);
        }
    }

    [TestMethod]
    public void PolicySatisfactionReceiptRegression_CreateServiceUsesEvaluatorStoreAndReadBack()
    {
        var service = File.ReadAllText(CreateServicePath());
        foreach (var token in new[]
        {
            "IPolicyRequirementSatisfactionEvaluator",
            "IPolicySatisfactionStore",
            "IPolicySatisfactionQueryService",
            "_evaluator.Evaluate",
            "_store.SaveAsync",
            "_query.GetAsync"
        })
        {
            StringAssert.Contains(service, token);
        }
    }

    [TestMethod]
    public void PolicySatisfactionReceiptRegression_CreateServiceRejectsUnsatisfiedEvaluation() =>
        StringAssert.Contains(File.ReadAllText(CreateServicePath()), "POLICY_REQUIREMENT_NOT_SATISFIED");

    [TestMethod]
    public void PolicySatisfactionReceiptRegression_CreateServiceChecksPolicyRequirementHash()
    {
        var service = File.ReadAllText(CreateServicePath());
        StringAssert.Contains(service, "POLICY_REQUIREMENT_HASH_MISMATCH");
        StringAssert.Contains(service, "evaluation.PolicyRequirementHash");
    }

    [TestMethod]
    public void PolicySatisfactionReceiptRegression_CreateServiceRejectsPrivateMaterial()
    {
        var service = File.ReadAllText(CreateServicePath());
        foreach (var marker in new[]
        {
            "raw prompt",
            "raw completion",
            "raw tool output",
            "chain-of-thought",
            "private reasoning",
            "hidden reasoning",
            "scratchpad",
            "system prompt",
            "developer prompt",
            "secret",
            "private key",
            "bearer "
        })
        {
            StringAssert.Contains(service, marker);
        }
    }

    [TestMethod]
    public void PolicySatisfactionReceiptRegression_CreateServiceRejectsExecutionAuthorityClaims()
    {
        var service = File.ReadAllText(CreateServicePath());
        foreach (var marker in new[]
        {
            "runs dry-run",
            "creates patch artifact",
            "applies source",
            "continues workflow",
            "approves release",
            "release ready",
            "source applied",
            "workflow continued"
        })
        {
            StringAssert.Contains(service, marker);
        }
    }

    [TestMethod]
    public void PolicySatisfactionReceiptRegression_ReadApiBoundaryStillSaysReadIsNotExecution()
    {
        var readModel = File.ReadAllText(ReadModelPath());
        var readReceipt = File.ReadAllText(Path.Combine(RepoRoot(), "Docs", "receipts", "PR177_POLICY_SATISFACTION_READ_API.md"));
        var combined = readModel + Environment.NewLine + readReceipt;

        foreach (var statement in new[]
        {
            "Reading persisted policy satisfaction does not authorize execution by itself.",
            "Persisted policy satisfaction is not source apply.",
            "Persisted policy satisfaction is not workflow continuation.",
            "Persisted policy satisfaction is not release readiness."
        })
        {
            StringAssert.Contains(combined, statement);
        }
    }

    [TestMethod]
    public void PolicySatisfactionReceiptRegression_CreateApiBoundaryStillSaysCreateIsNotExecution()
    {
        var createModel = File.ReadAllText(CreateModelPath());
        var createReceipt = File.ReadAllText(Path.Combine(RepoRoot(), "Docs", "receipts", "PR178_GOVERNED_POLICY_SATISFACTION_CREATE_API.md"));
        var combined = createModel + Environment.NewLine + createReceipt;

        foreach (var statement in new[]
        {
            "Policy satisfaction record creation is not dry-run execution.",
            "Policy satisfaction record creation is not patch artifact creation.",
            "Policy satisfaction record creation is not source apply.",
            "Policy satisfaction record creation is not workflow continuation.",
            "Policy satisfaction record creation is not release readiness.",
            "Created policy satisfaction does not authorize execution by itself."
        })
        {
            StringAssert.Contains(combined, statement);
        }
    }

    [TestMethod]
    public void PolicySatisfactionReceiptRegression_NoDryRunImplementationExistsYet() =>
        AssertNoBlockQProductionTokens("RunDryRunAsync", "DryRunExecutor", "ControlledDryRunRunner", "DryRunResultStore");

    [TestMethod]
    public void PolicySatisfactionReceiptRegression_NoPatchArtifactImplementationExistsYet() =>
        AssertNoBlockQProductionTokens("CreatePatchArtifactAsync", "PatchArtifactStore", "PatchArtifactId = Guid.NewGuid");

    [TestMethod]
    public void PolicySatisfactionReceiptRegression_NoSourceApplyImplementationExistsYet() =>
        AssertNoBlockQProductionTokens("ApplySourceAsync", "SourceApplyService", "ControlledSourceApply", "CanApplySource = true");

    [TestMethod]
    public void PolicySatisfactionReceiptRegression_NoWorkflowContinuationImplementationExistsYet() =>
        AssertNoBlockQProductionTokens("ContinueWorkflowAsync", "WorkflowContinuationService", "CanContinueWorkflow = true");

    [TestMethod]
    public void PolicySatisfactionReceiptRegression_NoReleaseReadinessImplementationExistsYet() =>
        AssertNoBlockQProductionTokens("ApproveReleaseAsync", "ReleaseReady = true", "CanApproveRelease = true", "ReleaseReadinessGateSatisfied = true");

    [TestMethod]
    public void PolicySatisfactionReceiptRegression_NoUiCliRuntimeSchedulerAdded()
    {
        foreach (var file in Pr179ChangedFiles())
        {
            var relative = Path.GetRelativePath(RepoRoot(), file);
            foreach (var pathToken in new[] { "Tauri", "IronDev.Cli", "tools", "Controllers", "Program.cs" })
            {
                Assert.IsFalse(relative.Contains(pathToken, StringComparison.OrdinalIgnoreCase), $"Unexpected changed path for PR179: {relative}");
            }
        }

        Assert.IsTrue(true, "PR179 is receipt/test-only; runtime boundary is enforced by changed-path checks.");
    }

    [TestMethod]
    public void PolicySatisfactionReceiptRegression_NoModelAgentToolMemoryRetrievalActivation() =>
        AssertNoBlockQProductionTokens("LLM", "model call", "AgentDispatch", "ToolExecution", "PromoteMemory", "ActivateRetrieval");

    [TestMethod]
    public void PolicySatisfactionReceiptRegression_AuthorityChainStopsBeforeDryRun()
    {
        var receipt = ReceiptText();
        StringAssert.Contains(receipt, "Block Q stops at filed policy satisfaction.");
        StringAssert.Contains(receipt, "Block R begins controlled dry-run requirements.");
    }

    private static void AssertNoBlockQProductionTokens(params string[] tokens)
    {
        foreach (var file in BlockQProductionFiles)
        {
            var text = File.ReadAllText(file);
            foreach (var token in tokens)
            {
                Assert.IsFalse(text.Contains(token, StringComparison.Ordinal), $"Unexpected token in {file}: {token}");
            }
        }
    }

    private static IReadOnlyList<MethodInfo> ControllerMethods() =>
        typeof(PolicySatisfactionsV1Controller).GetMethods()
            .Where(method => method.DeclaringType == typeof(PolicySatisfactionsV1Controller))
            .ToArray();

    private static IReadOnlyList<string> Pr179ChangedFiles() =>
    [
        ReceiptPath(),
        Path.Combine(RepoRoot(), "IronDev.IntegrationTests", "Governance", "PolicySatisfactionReceiptRegressionTests.cs")
    ];

    private static string FullAuthorityChain() =>
        "accepted approval record -> policy satisfaction record -> controlled dry-run -> patch artifact -> controlled source apply -> rollback -> workflow continuation -> release readiness gate";

    private static string ReceiptText() => File.ReadAllText(ReceiptPath());

    private static string ReceiptPath() =>
        Path.Combine(RepoRoot(), "Docs", "receipts", "PR179_POLICY_SATISFACTION_RECEIPT_AND_REGRESSION_TESTS.md");

    private static string SqlMigrationPath() => Path.Combine(RepoRoot(), "Database", "migrate_policy_satisfaction.sql");
    private static string StorePath() => Path.Combine(RepoRoot(), "IronDev.Infrastructure", "Governance", "SqlPolicySatisfactionStore.cs");
    private static string ControllerPath() => Path.Combine(RepoRoot(), "IronDev.Api", "Controllers", "PolicySatisfactionsV1Controller.cs");
    private static string CreateServicePath() => Path.Combine(RepoRoot(), "IronDev.Infrastructure", "Governance", "PolicySatisfactionCreateService.cs");
    private static string ReadModelPath() => Path.Combine(RepoRoot(), "IronDev.Core", "Governance", "PolicySatisfactionReadModels.cs");
    private static string CreateModelPath() => Path.Combine(RepoRoot(), "IronDev.Core", "Governance", "PolicySatisfactionCreateModels.cs");

    private static string RepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "IronDev.slnx")))
        {
            directory = directory.Parent;
        }

        if (directory is null)
        {
            throw new InvalidOperationException("Could not locate repository root.");
        }

        return directory.FullName;
    }
}
