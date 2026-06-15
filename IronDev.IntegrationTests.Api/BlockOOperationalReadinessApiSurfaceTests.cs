using System.Text.RegularExpressions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests.Api;

[TestClass]
public sealed class BlockOOperationalReadinessApiSurfaceTests
{
    [TestMethod]
    public void BlockO_ApiSurfaces_AreGetOnly()
    {
        foreach (var file in ControllerFiles())
        {
            var text = File.ReadAllText(file);
            StringAssert.Contains(text, "HttpGet");
        }
    }

    [TestMethod]
    public void BlockO_ApiSurfaces_HaveNoPostPutPatchDelete()
    {
        foreach (var file in ControllerFiles())
        {
            var text = File.ReadAllText(file);
            AssertDoesNotContainAny(text, "HttpPost", "HttpPut", "HttpPatch", "HttpDelete");
        }
    }

    [TestMethod]
    public void BlockO_ApiSurfaces_ReturnMutationOccurredFalse()
    {
        foreach (var file in ControllerFiles())
        {
            var text = File.ReadAllText(file);
            Assert.IsTrue(
                text.Contains("mutationOccurred = false", StringComparison.Ordinal) ||
                text.Contains("MutationOccurred = false", StringComparison.Ordinal),
                $"{Path.GetFileName(file)} must explicitly return mutationOccurred false.");
        }
    }

    [TestMethod]
    public void BlockO_ApiSurfaces_ReturnNoAuthorityBoundary()
    {
        foreach (var file in ControllerFiles())
        {
            var text = File.ReadAllText(file);
            foreach (var token in DangerousTrueBoundaryAssignments())
                Assert.IsFalse(text.Contains(token, StringComparison.OrdinalIgnoreCase), $"{Path.GetFileName(file)} must not set authority boundary '{token}'.");
        }
    }

    [TestMethod] public void BlockO_ApiSurfaces_DoNotExposeRawPayloadJson() => AssertNoContentLeak("PayloadJson");
    [TestMethod] public void BlockO_ApiSurfaces_DoNotExposePrivateReasoning() => AssertNoContentLeak("PrivateReasoning");
    [TestMethod] public void BlockO_ApiSurfaces_DoNotExposeRawPrompt() => AssertNoContentLeak("RawPrompt");
    [TestMethod] public void BlockO_ApiSurfaces_DoNotExposeRawCompletion() => AssertNoContentLeak("RawCompletion");
    [TestMethod] public void BlockO_ApiSurfaces_DoNotExposeRawToolOutput() => AssertNoContentLeak("RawToolOutput");
    [TestMethod] public void BlockO_ApiSurfaces_DoNotExposeSourceContent() => AssertNoContentLeak("SourceContent");
    [TestMethod] public void BlockO_ApiSurfaces_DoNotExposePatchPayload() => AssertNoContentLeak("PatchPayload");
    [TestMethod] public void BlockO_ApiSurfaces_DoNotExposeSecrets() => AssertNoContentLeak("Secret");

    [TestMethod]
    public void BlockO_ApiRoutes_DoNotContainControlFragments()
    {
        foreach (var route in ControllerRoutes())
        {
            foreach (var fragment in ForbiddenRouteFragments())
                Assert.IsFalse(RouteContainsForbiddenFragment(route, fragment), $"Block O route must not contain control fragment '{fragment}': {route}");
        }
    }

    [TestMethod]
    public void BlockO_ApiResponses_StateObservationNotAuthority()
    {
        var text = string.Join("\n", ControllerFiles().Select(File.ReadAllText));
        StringAssert.Contains(text, "mutationOccurred = false");
        StringAssert.Contains(text, "readOnly");
        StringAssert.Contains(text, "canApprove = false");
        StringAssert.Contains(text, "canSatisfyPolicy = false");
        StringAssert.Contains(text, "canTransitionWorkflow = false");
        StringAssert.Contains(text, "canInvokeTool = false");
        StringAssert.Contains(text, "canDispatchAgent = false");
        StringAssert.Contains(text, "canCallModel = false");
        StringAssert.Contains(text, "canPromoteMemory = false");
        StringAssert.Contains(text, "canApplySource = false");
        StringAssert.Contains(text, "canApplyPatch = false");
    }

    [TestMethod]
    public void BlockO_RetentionRules_HaveNoApiSurface()
    {
        var controllerNames = Directory.GetFiles(Path.Combine(RepositoryRoot(), "IronDev.Api", "Controllers"), "*.cs")
            .Select(Path.GetFileName)
            .ToArray();

        Assert.IsFalse(controllerNames.Any(name => name.Contains("Retention", StringComparison.OrdinalIgnoreCase)), "PR150 must not add a retention API controller.");

        var controllerText = string.Join("\n", Directory.GetFiles(Path.Combine(RepositoryRoot(), "IronDev.Api", "Controllers"), "*.cs").Select(File.ReadAllText));
        Assert.IsFalse(controllerText.Contains("retention-rules", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(controllerText.Contains("cleanup-rules", StringComparison.OrdinalIgnoreCase));
    }

    private static void AssertNoContentLeak(string token)
    {
        foreach (var file in ControllerFiles())
        {
            var text = ScrubAllowedBoundaryPropertyNames(File.ReadAllText(file));
            Assert.IsFalse(text.Contains(token, StringComparison.OrdinalIgnoreCase), $"{Path.GetFileName(file)} must not expose content token '{token}'.");
        }
    }

    private static string ScrubAllowedBoundaryPropertyNames(string text)
    {
        foreach (var allowed in new[]
        {
            "exposesRawPayloadJson",
            "ExposesRawPayloadJson",
            "exposesRawPayloads",
            "ExposesRawPayloads",
            "exposesRawPrompt",
            "ExposesRawPrompt",
            "exposesRawCompletion",
            "ExposesRawCompletion",
            "exposesRawToolOutput",
            "ExposesRawToolOutput",
            "exposesSourceContent",
            "ExposesSourceContent",
            "exposesPatchPayload",
            "ExposesPatchPayload",
            "exposesPrivateReasoning",
            "ExposesPrivateReasoning",
            "exposesSensitiveValues",
            "ExposesSensitiveValues"
        })
        {
            text = text.Replace(allowed, string.Empty, StringComparison.OrdinalIgnoreCase);
        }

        return text;
    }

    private static IReadOnlyList<string> ControllerRoutes()
    {
        var routes = new List<string>();
        var routePattern = new Regex(@"\[(?:Route|HttpGet)\(""([^""]*)""\)\]", RegexOptions.Compiled);
        foreach (var file in ControllerFiles())
        {
            var text = File.ReadAllText(file);
            routes.AddRange(routePattern.Matches(text).Select(match => match.Groups[1].Value));
        }

        return routes;
    }

    private static bool RouteContainsForbiddenFragment(string route, string fragment)
    {
        var routeTokens = RouteTokens(route);
        var fragmentTokens = RouteTokens(fragment);
        if (fragmentTokens.Count == 0 || routeTokens.Count < fragmentTokens.Count)
            return false;

        for (var start = 0; start <= routeTokens.Count - fragmentTokens.Count; start++)
        {
            var matches = true;
            for (var offset = 0; offset < fragmentTokens.Count; offset++)
            {
                if (!string.Equals(routeTokens[start + offset], fragmentTokens[offset], StringComparison.OrdinalIgnoreCase))
                {
                    matches = false;
                    break;
                }
            }

            if (matches)
                return true;
        }

        return false;
    }

    private static IReadOnlyList<string> RouteTokens(string routeOrFragment)
    {
        var withoutRouteParameters = Regex.Replace(routeOrFragment, @"\{[^}]+\}", string.Empty);
        return Regex.Split(withoutRouteParameters, @"[^A-Za-z0-9]+")
            .Where(token => !string.IsNullOrWhiteSpace(token))
            .ToArray();
    }

    private static IReadOnlyList<string> ControllerFiles()
    {
        var root = RepositoryRoot();
        return
        [
            Path.Combine(root, "IronDev.Api", "Controllers", "GovernanceTraceExplorerController.cs"),
            Path.Combine(root, "IronDev.Api", "Controllers", "FailedWorkflowDiagnosisReportController.cs"),
            Path.Combine(root, "IronDev.Api", "Controllers", "ApprovalGateDogfoodCorrelationReportController.cs"),
            Path.Combine(root, "IronDev.Api", "Controllers", "AgentRunHealthSummaryController.cs"),
            Path.Combine(root, "IronDev.Api", "Controllers", "BackendOperationalHealthController.cs")
        ];
    }

    private static IReadOnlyList<string> DangerousTrueBoundaryAssignments() =>
    [
        "canApprove = true",
        "canReject = true",
        "canSatisfyPolicy = true",
        "canTransitionWorkflow = true",
        "canInvokeTool = true",
        "canDispatchAgent = true",
        "canCallModel = true",
        "canBuildPrompt = true",
        "canCreateTicket = true",
        "canPromoteMemory = true",
        "canActivateRetrieval = true",
        "canApplySource = true",
        "canApplyPatch = true",
        "canRestartBackend = true",
        "canRepairBackend = true",
        "canRunMigration = true",
        "createsGovernanceEvent = true",
        "createsApprovalDecision = true",
        "createsPolicyDecision = true",
        "createsToolRequest = true",
        "createsDogfoodReceipt = true",
        "toolInvoked = true",
        "agentDispatched = true",
        "modelCalled = true",
        "promptBuilt = true",
        "memoryPromoted = true",
        "retrievalActivated = true",
        "sourceApplied = true",
        "patchApplied = true"
    ];

    private static IReadOnlyList<string> ForbiddenRouteFragments() =>
    [
        "approve",
        "reject",
        "grant",
        "satisfy",
        "transition",
        "continue",
        "execute",
        "invoke",
        "dispatch",
        "replay",
        "rerun",
        "retry",
        "resume",
        "restart",
        "repair",
        "fix",
        "heal",
        "self-heal",
        "migrate",
        "migration",
        "cleanup",
        "delete",
        "purge",
        "archive",
        "redact",
        "release-approve",
        "mark-passed",
        "dogfood-pass",
        "gate-open",
        "gate-reopen",
        "apply-source",
        "patch-apply",
        "promote-memory",
        "activate-retrieval",
        "call-model",
        "build-prompt",
        "create-ticket"
    ];

    private static void AssertDoesNotContainAny(string text, params string[] markers)
    {
        foreach (var marker in markers)
            Assert.IsFalse(text.Contains(marker, StringComparison.OrdinalIgnoreCase), $"Unexpected marker: {marker}");
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "IronDev.slnx")))
                return directory.FullName;

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not locate repository root.");
    }
}

