using System.Reflection;
using System.Text.Json;
using IronDev.Api.Controllers;
using IronDev.Core.Governance;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;

namespace IronDev.IntegrationTests;

[TestClass]
public sealed class BlockA11FrontendReadinessOpenApiContractLockTests
{
    private static readonly JsonNamingPolicy JsonNames = JsonNamingPolicy.CamelCase;

    private static readonly IReadOnlyList<ReadEndpointContract> ReadEndpoints =
    [
        Endpoint(
            nameof(FrontendReadinessController.GetOperationStatus),
            "/api/frontend-readiness/operations/{operationId}/status",
            "operationId",
            typeof(FrontendOperationStatusReadModel)),
        Endpoint(
            nameof(FrontendReadinessController.GetOperationTimeline),
            "/api/frontend-readiness/operations/{operationId}/timeline",
            "operationId",
            typeof(FrontendOperationTimelineReadModel)),
        Endpoint(
            nameof(FrontendReadinessController.GetPatchPackageMetadata),
            "/api/frontend-readiness/patch-packages/{packageId}/metadata",
            "packageId",
            typeof(FrontendPatchPackageMetadataReadModel)),
        Endpoint(
            nameof(FrontendReadinessController.GetPatchPackageArtifacts),
            "/api/frontend-readiness/patch-packages/{packageId}/artifacts",
            "packageId",
            typeof(FrontendPatchPackageArtifactsReadModel)),
        Endpoint(
            nameof(FrontendReadinessController.GetValidationResultMetadata),
            "/api/frontend-readiness/validation-results/{validationResultId}/metadata",
            "validationResultId",
            typeof(FrontendValidationResultMetadataReadModel)),
        Endpoint(
            nameof(FrontendReadinessController.GetEvidenceMetadata),
            "/api/frontend-readiness/evidence/{evidenceRef}/metadata",
            "evidenceRef",
            typeof(FrontendEvidenceMetadataReadModel)),
        Endpoint(
            nameof(FrontendReadinessController.GetReceiptMetadata),
            "/api/frontend-readiness/receipts/{receiptRef}/metadata",
            "receiptRef",
            typeof(FrontendReceiptMetadataReadModel))
    ];

    [DataTestMethod]
    [DynamicData(nameof(EndpointCases), DynamicDataSourceType.Method)]
    public void OpenApi_FrontendReadiness_ReadEndpointExists(string methodName, string expectedPath)
    {
        var endpoint = ReadEndpoint(methodName);

        Assert.AreEqual(expectedPath, endpoint.DocumentedPath);
    }

    [DataTestMethod]
    [DynamicData(nameof(EndpointCases), DynamicDataSourceType.Method)]
    public void OpenApi_FrontendReadiness_ReadEndpointsUseGet(string methodName, string _)
    {
        var endpoint = ReadEndpoint(methodName);
        var verbs = endpoint.HttpMethods;

        CollectionAssert.AreEquivalent(new[] { "GET" }, verbs.ToArray());
    }

    [DataTestMethod]
    [DynamicData(nameof(EndpointCases), DynamicDataSourceType.Method)]
    public void OpenApi_FrontendReadiness_ReadEndpointsDoNotUsePostPutPatchDelete(string methodName, string _)
    {
        var endpoint = ReadEndpoint(methodName);
        var verbs = endpoint.HttpMethods;

        foreach (var forbidden in new[] { "POST", "PUT", "PATCH", "DELETE" })
            Assert.IsFalse(verbs.Contains(forbidden), $"{endpoint.MethodName} exposed {forbidden}.");
    }

    [DataTestMethod]
    [DynamicData(nameof(EndpointCases), DynamicDataSourceType.Method)]
    public void OpenApi_FrontendReadiness_ReadEndpointsExposeCompactQueryParameter(string methodName, string _)
    {
        var compact = ReadEndpoint(methodName)
            .Method
            .GetParameters()
            .SingleOrDefault(parameter => string.Equals(parameter.Name, "compact", StringComparison.Ordinal));

        Assert.IsNotNull(compact, "compact query parameter is missing.");
        Assert.AreEqual(typeof(bool), compact!.ParameterType);
        Assert.IsNotNull(compact.GetCustomAttribute<FromQueryAttribute>());
        Assert.IsTrue(compact.HasDefaultValue);
        Assert.AreEqual(false, compact.DefaultValue);
    }

    [DataTestMethod]
    [DynamicData(nameof(EndpointCases), DynamicDataSourceType.Method)]
    public void OpenApi_FrontendReadiness_ReadEndpointsDocumentStatusEnvelopes(string methodName, string _)
    {
        var endpoint = ReadEndpoint(methodName);

        foreach (var statusCode in new[]
        {
            StatusCodes.Status200OK,
            StatusCodes.Status404NotFound,
            StatusCodes.Status503ServiceUnavailable
        })
        {
            var response = endpoint.Method
                .GetCustomAttributes<ProducesResponseTypeAttribute>()
                .SingleOrDefault(attribute => attribute.StatusCode == statusCode);

            Assert.IsNotNull(response, $"{endpoint.MethodName} does not document HTTP {statusCode}.");
            Assert.AreEqual(endpoint.EnvelopeType, response!.Type);
        }
    }

    [DataTestMethod]
    [DynamicData(nameof(EndpointEnvelopeFieldCases), DynamicDataSourceType.Method)]
    public void OpenApi_FrontendReadiness_EnvelopeIncludesRequiredFields(string methodName, string fieldName)
    {
        var endpoint = ReadEndpoint(methodName);

        AssertSchemaIncludesField(endpoint.EnvelopeType, fieldName);
    }

    [DataTestMethod]
    [DynamicData(nameof(SchemaFieldCases), DynamicDataSourceType.Method)]
    public void OpenApi_FrontendReadiness_SchemaIncludesRequiredField(Type schemaType, string fieldName)
    {
        AssertSchemaIncludesField(schemaType, fieldName);
    }

    [TestMethod]
    public void OpenApi_FrontendReadiness_EnvelopeMutationOccurredIsDocumentedAsBoolean()
    {
        var property = typeof(FrontendReadinessApiEnvelope<FrontendOperationStatusReadModel>)
            .GetProperty(nameof(FrontendReadinessApiEnvelope<FrontendOperationStatusReadModel>.MutationOccurred));

        Assert.IsNotNull(property);
        Assert.AreEqual(typeof(bool), property!.PropertyType);
    }

    [TestMethod]
    public void OpenApi_FrontendReadiness_ReadStateEnumIncludesAllKnownValues() =>
        AssertEnumValues<FrontendReadinessReadStateKind>(
            "Available",
            "NotFound",
            "Empty",
            "Redacted",
            "Unavailable",
            "Invalid",
            "Expired",
            "Stale",
            "NotVisible",
            "Unknown");

    [TestMethod]
    public void OpenApi_FrontendReadiness_FreshnessEnumIncludesAllKnownValues() =>
        AssertEnumValues<FrontendReadinessFreshnessKind>(
            "Current",
            "Stale",
            "Expired",
            "Unknown",
            "NotApplicable");

    [TestMethod]
    public void OpenApi_FrontendReadiness_ReadStateEnumUsesStringOpenApiValues() =>
        Assert.IsNotNull(typeof(FrontendReadinessReadStateKind).GetCustomAttribute<System.Text.Json.Serialization.JsonConverterAttribute>());

    [TestMethod]
    public void OpenApi_FrontendReadiness_FreshnessEnumUsesStringOpenApiValues() =>
        Assert.IsNotNull(typeof(FrontendReadinessFreshnessKind).GetCustomAttribute<System.Text.Json.Serialization.JsonConverterAttribute>());

    [DataTestMethod]
    [DynamicData(nameof(EndpointCases), DynamicDataSourceType.Method)]
    public void OpenApi_FrontendReadiness_CompactModeDoesNotUseReducedEnvelopeSchema(string methodName, string _)
    {
        var endpoint = ReadEndpoint(methodName);

        Assert.AreEqual(endpoint.EnvelopeType, endpoint.ActionResultEnvelopeType);
        AssertSchemaIncludesField(endpoint.EnvelopeType, "readState");
        AssertSchemaIncludesField(endpoint.EnvelopeType, "freshness");
        AssertSchemaIncludesField(endpoint.EnvelopeType, "boundary");
        AssertSchemaIncludesField(endpoint.EnvelopeType, "warnings");
        AssertSchemaIncludesField(endpoint.EnvelopeType, "errors");
    }

    [TestMethod]
    public void OpenApi_FrontendReadiness_OperationStatusCompactSchemaStillDocumentsAuthorityFields()
    {
        AssertSchemaIncludesField(typeof(FrontendOperationStatusReadModel), "forbiddenActions");
        AssertSchemaIncludesField(typeof(FrontendOperationStatusReadModel), "missingEvidence");
        AssertSchemaIncludesField(typeof(FrontendOperationStatusReadModel), "authorityWarnings");
    }

    [TestMethod]
    public void OpenApi_FrontendReadiness_PatchPackageArtifactsSchemaStillDocumentsAuthorityFields()
    {
        AssertSchemaIncludesField(typeof(FrontendPatchPackageArtifactsReadModel), "authorityWarnings");
        AssertSchemaIncludesField(typeof(FrontendPatchPackageArtifactsReadModel), "validationIsStale");
        AssertSchemaIncludesField(typeof(FrontendPatchPackageArtifactsReadModel), "boundary");
    }

    [TestMethod]
    public void StaticScan_A11AddsNoFrontendFiles()
    {
        var source = A11Source();

        AssertNoMarkers(source, ["IronDev." + "TauriShell", ".t" + "sx", "/src/" + "app/"]);
    }

    [TestMethod]
    public void StaticScan_A11AddsNoMutationEndpoint()
    {
        foreach (var endpoint in ReadEndpoints)
            CollectionAssert.AreEquivalent(new[] { "GET" }, endpoint.HttpMethods.ToArray());

        AssertNoMarkers(A11Source(), ["[Http" + "Post]", "[Http" + "Put]", "[Http" + "Patch]", "[Http" + "Delete]"]);
    }

    [TestMethod]
    public void StaticScan_A11AddsNoExecutorOrProviderMutationPath()
    {
        AssertNoMarkers(
            A11Source(),
            [
                "SourceApply" + "Executor",
                "Rollback" + "Executor",
                "Commit" + "Executor",
                "Push" + "Executor",
                "DraftPullRequest" + "Executor",
                "Merge" + "Executor",
                "Release" + "Executor",
                "Deployment" + "Executor",
                "MemoryPromotion" + "Executor",
                "Workflow" + "ContinuationExecutor",
                "Continue" + "WorkflowAsync",
                "Apply" + "Patch",
                "Apply" + "Source",
                "Process" + "StartInfo",
                "Run" + "ProcessAsync",
                "git " + "apply",
                "git " + "commit",
                "git " + "push",
                "gh " + "pr create"
            ]);
    }

    [TestMethod]
    public void StaticScan_A11DoesNotReadRawPayloads()
    {
        AssertNoMarkers(
            A11Source(),
            [
                "ReadValidation" + "LogAsync",
                "ReadValidation" + "OutputAsync",
                "ReadCommand" + "OutputAsync",
                "ReadBuild" + "OutputAsync",
                "ReadTest" + "OutputAsync",
                "ReadPatch" + "PayloadAsync",
                "ReadPatch" + "TextAsync",
                "ReadDiff" + "TextAsync",
                "ReadTimeline" + "PayloadAsync",
                "ReadEvent" + "PayloadAsync",
                "ReadReceipt" + "TextAsync",
                "ReadEvidence" + "TextAsync",
                "raw" + "Prompt",
                "raw" + "Completion",
                "raw" + "ToolOutput",
                "raw" + "Patch",
                "raw" + "Log",
                "full " + "diff"
            ]);
    }

    [TestMethod]
    public void StaticScan_A11DoesNotGenerateFrontendClient()
    {
        AssertNoMarkers(
            A11Source(),
            [
                "OpenApi" + "Client",
                "TypeScript" + "Client",
                "generated " + "frontend client",
                "generate " + "client"
            ]);
    }

    [TestMethod]
    public void StaticScan_A11DoesNotCreateActionRequests()
    {
        AssertNoMarkers(
            A11Source(),
            [
                "ControlledAction" + "RequestCreate(",
                "CreateAction" + "Request(",
                "ActionRequest" + "Mutation"
            ]);
    }

    [TestMethod]
    public void StaticScan_A11DoesNotRefreshOrRunValidation()
    {
        AssertNoMarkers(
            A11Source(),
            [
                "Refresh" + "Validation",
                "Run" + "ValidationAsync",
                "Retry" + "Validation",
                "Repair" + "Validation",
                "Validation" + "Runner"
            ]);
    }

    public static IEnumerable<object[]> EndpointCases() =>
        ReadEndpoints.Select(endpoint => new object[] { endpoint.MethodName, endpoint.ExpectedPath });

    public static IEnumerable<object[]> EndpointEnvelopeFieldCases()
    {
        foreach (var endpoint in ReadEndpoints)
        foreach (var fieldName in new[]
        {
            "status",
            "data",
            "readState",
            "freshness",
            "boundary",
            "mutationOccurred",
            "warnings",
            "errors"
        })
        {
            yield return [endpoint.MethodName, fieldName];
        }
    }

    public static IEnumerable<object[]> SchemaFieldCases()
    {
        foreach (var fieldName in new[]
        {
            "kind",
            "hasData",
            "isFinal",
            "isFallback",
            "isRedacted",
            "isStale",
            "isExpired",
            "isAuthorityGrant",
            "allowsMutation",
            "freshness",
            "reasons",
            "missingRefs",
            "warnings",
            "nextSafeActions",
            "boundary"
        })
        {
            yield return [typeof(FrontendReadinessReadState), fieldName];
        }

        foreach (var fieldName in new[]
        {
            "kind",
            "freshnessKnown",
            "isStale",
            "isExpired",
            "observedAtUtc",
            "expiresAtUtc",
            "evaluatedAtUtc",
            "reasons",
            "warnings"
        })
        {
            yield return [typeof(FrontendReadinessFreshnessState), fieldName];
        }

        foreach (var fieldName in new[]
        {
            "readOnly",
            "statusOnly",
            "canCreateApproval",
            "canAcceptApproval",
            "canSatisfyPolicy",
            "canExecute",
            "canMutateSource",
            "canRollback",
            "canCommit",
            "canPush",
            "canCreatePullRequest",
            "canMarkReadyForReview",
            "canMerge",
            "canRelease",
            "canDeploy",
            "canPromoteMemory",
            "canContinueWorkflow"
        })
        {
            yield return [typeof(FrontendReadBoundary), fieldName];
        }

        foreach (var fieldName in new[]
        {
            "operationId",
            "operationKind",
            "subject",
            "state",
            "blockedReasons",
            "missingEvidence",
            "nextSafeActions",
            "forbiddenActions",
            "evidenceRefs",
            "receiptRefs",
            "authorityWarnings",
            "boundary",
            "observedAtUtc",
            "expiresAtUtc"
        })
        {
            yield return [typeof(FrontendOperationStatusReadModel), fieldName];
        }

        foreach (var fieldName in new[]
        {
            "evidenceRef",
            "evidenceKind",
            "summary",
            "referenceOnly",
            "containsRawPayload",
            "warnings",
            "boundary",
            "observedAtUtc",
            "expiresAtUtc",
            "freshnessKnown"
        })
        {
            yield return [typeof(FrontendEvidenceMetadataReadModel), fieldName];
        }

        foreach (var fieldName in new[]
        {
            "receiptRef",
            "receiptKind",
            "summary",
            "referenceOnly",
            "grantsAuthority",
            "continuesWorkflow",
            "warnings",
            "boundary",
            "observedAtUtc",
            "expiresAtUtc",
            "freshnessKnown"
        })
        {
            yield return [typeof(FrontendReceiptMetadataReadModel), fieldName];
        }

        foreach (var fieldName in new[]
        {
            "operationId",
            "entries",
            "boundary",
            "observedAtUtc",
            "expiresAtUtc",
            "freshnessKnown"
        })
        {
            yield return [typeof(FrontendOperationTimelineReadModel), fieldName];
        }

        foreach (var fieldName in new[]
        {
            "entryId",
            "eventKind",
            "summary",
            "evidenceRefs",
            "receiptRefs",
            "observedAtUtc"
        })
        {
            yield return [typeof(FrontendTimelineEntry), fieldName];
        }

        foreach (var fieldName in new[]
        {
            "packageId",
            "repository",
            "branch",
            "runId",
            "patchHash",
            "proposedFilePaths",
            "artifactRefs",
            "evidenceRefs",
            "receiptRefs",
            "reviewSummaryRef",
            "knownRisksRef",
            "boundary",
            "observedAtUtc",
            "expiresAtUtc",
            "freshnessKnown"
        })
        {
            yield return [typeof(FrontendPatchPackageMetadataReadModel), fieldName];
        }

        foreach (var fieldName in new[]
        {
            "packageId",
            "repository",
            "branch",
            "runId",
            "patchHash",
            "patchDiffText",
            "reviewSummaryText",
            "knownRisksText",
            "validationSummaryText",
            "validationOutcome",
            "whatRan",
            "whatPassed",
            "whatFailed",
            "whatWasSkipped",
            "validationIsStale",
            "proposedFilePaths",
            "artifactRefs",
            "evidenceRefs",
            "receiptRefs",
            "authorityWarnings",
            "boundary",
            "observedAtUtc",
            "expiresAtUtc",
            "freshnessKnown"
        })
        {
            yield return [typeof(FrontendPatchPackageArtifactsReadModel), fieldName];
        }

        foreach (var fieldName in new[]
        {
            "validationResultId",
            "repository",
            "branch",
            "runId",
            "patchHash",
            "outcome",
            "whatRan",
            "whatPassed",
            "whatFailed",
            "whatWasSkipped",
            "isStale",
            "evidenceRefs",
            "receiptRefs",
            "boundary",
            "observedAtUtc",
            "expiresAtUtc",
            "freshnessKnown"
        })
        {
            yield return [typeof(FrontendValidationResultMetadataReadModel), fieldName];
        }
    }

    private static ReadEndpointContract Endpoint(
        string methodName,
        string expectedPath,
        string pathParameterName,
        Type dataType) =>
        new(methodName, expectedPath, pathParameterName, dataType);

    private static ReadEndpointContract ReadEndpoint(string methodName) =>
        ReadEndpoints.Single(endpoint => string.Equals(endpoint.MethodName, methodName, StringComparison.Ordinal));

    private static void AssertSchemaIncludesField(Type schemaType, string fieldName)
    {
        var fields = schemaType
            .GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Select(property => JsonNames.ConvertName(property.Name))
            .ToHashSet(StringComparer.Ordinal);

        Assert.IsTrue(fields.Contains(fieldName), $"{schemaType.Name} does not document '{fieldName}'.");
    }

    private static void AssertEnumValues<TEnum>(params string[] expected)
        where TEnum : struct, Enum
    {
        var actual = Enum.GetNames<TEnum>();

        foreach (var value in expected)
            Assert.IsTrue(actual.Contains(value, StringComparer.Ordinal), $"{typeof(TEnum).Name} is missing {value}.");
    }

    private static void AssertNoMarkers(string source, IEnumerable<string> markers)
    {
        foreach (var marker in markers)
            Assert.IsFalse(source.Contains(marker, StringComparison.OrdinalIgnoreCase), $"Forbidden marker '{marker}' was present.");
    }

    private static string A11Source() =>
        File.ReadAllText(Path.Combine(RepoRoot(), "IronDev.IntegrationTests", "BlockA11FrontendReadinessOpenApiContractLockTests.cs"));

    private static string RepoRoot()
    {
        var directory = AppContext.BaseDirectory;
        while (!string.IsNullOrWhiteSpace(directory))
        {
            if (File.Exists(Path.Combine(directory, "IronDev.slnx")))
                return directory;

            directory = Directory.GetParent(directory)?.FullName ?? string.Empty;
        }

        Assert.Fail("Could not locate repo root.");
        return string.Empty;
    }

    private sealed record ReadEndpointContract(
        string MethodName,
        string ExpectedPath,
        string PathParameterName,
        Type DataType)
    {
        public MethodInfo Method { get; } =
            typeof(FrontendReadinessController).GetMethod(MethodName)
            ?? throw new InvalidOperationException($"Missing frontend readiness method {MethodName}.");

        public Type EnvelopeType => typeof(FrontendReadinessApiEnvelope<>).MakeGenericType(DataType);

        public Type ActionResultEnvelopeType =>
            Method.ReturnType.GetGenericArguments().Single();

        public string DocumentedPath
        {
            get
            {
                var controllerRoute = typeof(FrontendReadinessController).GetCustomAttribute<RouteAttribute>()?.Template
                    ?? throw new InvalidOperationException("Frontend readiness controller route is missing.");
                var getRoute = Method.GetCustomAttribute<HttpGetAttribute>()?.Template
                    ?? throw new InvalidOperationException($"{MethodName} GET route is missing.");

                return "/" + string.Join("/", controllerRoute.Trim('/'), getRoute.Trim('/'));
            }
        }

        public IReadOnlyCollection<string> HttpMethods =>
            Method
                .GetCustomAttributes<HttpMethodAttribute>()
                .SelectMany(attribute => attribute.HttpMethods)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
    }
}
