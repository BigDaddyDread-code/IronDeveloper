using System.Text;
using IronDev.Core.Agents;
using IronDev.Core.Workbench;

namespace IronDev.UnitTests;

[TestClass]
public sealed class WorkbenchBusinessAnalystProviderContractsTests
{
    [TestMethod]
    public void ContextBudget_MeasuresCompleteUtf8RequestAndReservesOutputAndSafetyMargin()
    {
        var context = Context("Shape a café scheduling idea.");
        var envelope = Envelope(
            immutablePolicy: "IMMUTABLE",
            profile: "ADVISORY",
            snapshot: "UNTRUSTED café SNAPSHOT");

        var measurement = WorkbenchBusinessAnalystContextBudget.MeasureAndValidate(
            context,
            envelope);

        Assert.AreEqual(
            WorkbenchBusinessAnalystProviderContract.ContextBudgetPolicyVersion,
            measurement.PolicyVersion);
        Assert.AreEqual(
            Encoding.UTF8.GetByteCount(envelope.UntrustedSnapshot),
            measurement.SnapshotUtf8Bytes);
        Assert.IsTrue(
            measurement.CompleteRequestUtf8Bytes >
            measurement.ImmutablePolicyUtf8Bytes +
            measurement.AnalystProfileUtf8Bytes +
            measurement.SnapshotUtf8Bytes);
        Assert.IsTrue(
            measurement.EstimatedInputTokens +
            measurement.ReservedOutputTokens +
            measurement.SafetyMarginTokens <=
            WorkbenchBusinessAnalystProviderContract.MaximumContextWindowTokens);
    }

    [TestMethod]
    public void ContextBudget_RejectsOversizedSnapshotWithStableSafeDimension()
    {
        var envelope = Envelope(
            snapshot: new string(
                'x',
                WorkbenchBusinessAnalystProviderContract.MaximumSnapshotUtf8Bytes + 1));

        var exception = Assert.ThrowsException<WorkbenchBusinessAnalystContextTooLargeException>(() =>
            WorkbenchBusinessAnalystContextBudget.MeasureAndValidate(
                Context("Shape this"),
                envelope));

        Assert.AreEqual(WorkbenchBusinessAnalystContextTooLargeException.ErrorCode, "agent_context_too_large");
        Assert.AreEqual("snapshot_utf8_bytes", exception.Dimension);
        Assert.IsFalse(exception.Message.Contains(envelope.UntrustedSnapshot, StringComparison.Ordinal));
    }

    [TestMethod]
    public void ContextBudget_CountsEncodedEscapingInCompleteRequest()
    {
        var envelope = Envelope(snapshot: string.Concat(
            Enumerable.Repeat("\"\\\n", 1_000)));

        var measurement = WorkbenchBusinessAnalystContextBudget.MeasureAndValidate(
            Context("Shape this"),
            envelope);

        Assert.IsTrue(
            measurement.CompleteRequestUtf8Bytes >
            measurement.ImmutablePolicyUtf8Bytes +
            measurement.AnalystProfileUtf8Bytes +
            measurement.SnapshotUtf8Bytes + 1_000,
            "The aggregate budget must include encoded JSON escaping, not a fixed framing estimate.");
    }

    [TestMethod]
    public void ProviderMapper_UsesRealHierarchyOrSafeDemotionWithoutConcatenation()
    {
        var envelope = Envelope();

        var openAi = WorkbenchBusinessAnalystProviderMessageMapper.ForOpenAi(envelope);
        var systemUser = WorkbenchBusinessAnalystProviderMessageMapper
            .ForSystemUserProvider(envelope);

        CollectionAssert.AreEqual(
            new[] { AgentModelRole.System, AgentModelRole.Developer, AgentModelRole.User },
            openAi.Select(message => message.Role).ToArray());
        CollectionAssert.AreEqual(
            new[] { AgentModelRole.System, AgentModelRole.User, AgentModelRole.User },
            systemUser.Select(message => message.Role).ToArray());
        Assert.AreEqual("IMMUTABLE", openAi[0].Content);
        Assert.AreEqual("ADVISORY", openAi[1].Content);
        Assert.AreEqual("UNTRUSTED", openAi[2].Content);
        Assert.IsFalse(openAi.Any(message =>
            message.Content.Contains("IMMUTABLEADVISORY", StringComparison.Ordinal)));
    }

    [TestMethod]
    public void InvocationAudit_CanonicalizesOnlySafeMetadata()
    {
        var audit = new WorkbenchBusinessAnalystInvocationAudit
        {
            AgentRunId = Guid.Parse("11111111-1111-1111-1111-111111111111"),
            ClaimToken = Guid.Parse("22222222-2222-2222-2222-222222222222"),
            AttemptNumber = 1,
            SafeRequestId = " ba-safe_1 ",
            ProviderRequestId = " req_safe_2 ",
            UsageReported = true,
            Usage = new AgentModelUsage { InputTokens = 20, OutputTokens = 4 },
            DurationMilliseconds = 15,
            Outcome = WorkbenchBusinessAnalystInvocationOutcome.Succeeded,
            CompletedAtUtc = DateTimeOffset.UnixEpoch
        };

        var normalized = WorkbenchBusinessAnalystInvocationAuditCanonicalizer
            .NormalizeAndValidate(audit);
        var hash = WorkbenchBusinessAnalystInvocationAuditCanonicalizer.ComputeHash(audit);

        Assert.AreEqual("ba-safe_1", normalized.SafeRequestId);
        Assert.AreEqual("req_safe_2", normalized.ProviderRequestId);
        Assert.AreEqual(64, hash.Length);
        Assert.IsTrue(hash.All(Uri.IsHexDigit));
    }

    [TestMethod]
    public void InvocationAudit_RejectsUnreportedUsageAndUnsafeRequestIdentifier()
    {
        var audit = new WorkbenchBusinessAnalystInvocationAudit
        {
            AgentRunId = Guid.Parse("11111111-1111-1111-1111-111111111111"),
            ClaimToken = Guid.Parse("22222222-2222-2222-2222-222222222222"),
            AttemptNumber = 1,
            SafeRequestId = "ba-safe_1",
            UsageReported = false,
            Usage = new AgentModelUsage { InputTokens = 1 },
            DurationMilliseconds = 15,
            Outcome = WorkbenchBusinessAnalystInvocationOutcome.Failed,
            FailureCategory = "agent_provider_failed",
            CompletedAtUtc = DateTimeOffset.UnixEpoch
        };

        Assert.ThrowsException<WorkbenchBusinessAnalystInvocationAuditValidationException>(() =>
            WorkbenchBusinessAnalystInvocationAuditCanonicalizer.NormalizeAndValidate(audit));
        Assert.ThrowsException<WorkbenchBusinessAnalystInvocationAuditValidationException>(() =>
            WorkbenchBusinessAnalystInvocationAuditCanonicalizer.NormalizeAndValidate(
                audit with
                {
                    Usage = new AgentModelUsage(),
                    ProviderRequestId = "unsafe\r\nheader"
                }));
    }

    [TestMethod]
    public void OutputValidator_RejectsAssistantMessageBeyondReservedOutputBoundary()
    {
        var context = Context("Shape this");
        var output = new WorkbenchBusinessAnalystOutput(
            WorkbenchBusinessAnalystContract.OutputSchemaVersion,
            context.ContextHash,
            context.UnderstandingRevision,
            WorkbenchAgentRunStates.Completed,
            new string(
                'x',
                WorkbenchBusinessAnalystProviderContract.MaximumAssistantMessageCharacters + 1));

        Assert.ThrowsException<WorkbenchAgentOutputValidationException>(() =>
            WorkbenchBusinessAnalystOutputValidator.Validate(output, context));
        Assert.ThrowsException<WorkbenchAgentOutputValidationException>(() =>
            WorkbenchBusinessAnalystOutputValidator.Validate(
                output with
                {
                    AssistantMessage = new string(
                        '\u20ac',
                        WorkbenchBusinessAnalystProviderContract.MaximumOutputUtf8Bytes / 2)
                },
                context));
    }

    private static WorkbenchBusinessAnalystProviderEnvelope Envelope(
        string immutablePolicy = "IMMUTABLE",
        string profile = "ADVISORY",
        string snapshot = "UNTRUSTED") =>
        new()
        {
            EnvelopeVersion = WorkbenchBusinessAnalystProviderContract.EnvelopeVersion,
            SafeRequestId = "ba-11111111111111111111111111111111",
            ImmutableCodePolicy = immutablePolicy,
            ConstrainedAnalystProfile = profile,
            UntrustedSnapshot = snapshot,
            ContextBudgetPolicyVersion =
                WorkbenchBusinessAnalystProviderContract.ContextBudgetPolicyVersion,
            ReservedOutputTokens = WorkbenchBusinessAnalystProviderContract.ReservedOutputTokens
        };

    private static WorkbenchBusinessAnalystContext Context(string message) =>
        new(
            Guid.Parse("11111111-1111-1111-1111-111111111111"),
            1,
            7,
            "An idea",
            70,
            1,
            700,
            7000,
            3,
            "{}",
            [new WorkbenchAgentContextMessage(7000, "user", message, DateTime.UnixEpoch)],
            WorkbenchBusinessAnalystContract.AgentVersion,
            WorkbenchBusinessAnalystContract.PromptVersion,
            WorkbenchBusinessAnalystContract.ToolPolicyVersion,
            WorkbenchBusinessAnalystContract.ContextSchemaVersion,
            WorkbenchBusinessAnalystContract.ContextCanonicalizationVersion,
            WorkbenchBusinessAnalystContract.OutputSchemaVersion,
            new string('a', 64));
}
