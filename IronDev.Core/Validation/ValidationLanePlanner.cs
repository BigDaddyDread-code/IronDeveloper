using System.Security.Cryptography;
using System.Text;

namespace IronDev.Core.Validation;

public sealed class ValidationLanePlanner
{
    public static readonly ValidationLane[] KnownLanes =
    [
        new()
        {
            Name = "restore",
            Reason = "Project or solution metadata changed.",
            Timeout = TimeSpan.FromMinutes(5),
            CommandKind = ValidationCommandKind.Restore,
            Commands = ["dotnet restore IronDev.slnx"],
            SafeToParallelize = false,
            ParallelismGroup = "restore",
            CacheCategory = "restore"
        },
        new()
        {
            Name = "build",
            Reason = "Compiled source or project metadata changed.",
            Timeout = TimeSpan.FromMinutes(10),
            CommandKind = ValidationCommandKind.Build,
            Commands = ["dotnet build IronDev.slnx --no-restore -v:minimal"],
            SafeToParallelize = false,
            ParallelismGroup = "build",
            CacheCategory = "build"
        },
        new()
        {
            Name = "diff-check",
            Reason = "Changed files must pass whitespace and patch hygiene checks.",
            Timeout = TimeSpan.FromMinutes(2),
            CommandKind = ValidationCommandKind.DiffCheck,
            Commands = ["git diff --check", "git diff --cached --check"],
            SafeToParallelize = true,
            ParallelismGroup = "hygiene",
            CacheCategory = "diff"
        },
        new()
        {
            Name = "docs-receipt-check",
            Reason = "Receipt-only changes must stay aligned with documented boundaries.",
            Timeout = TimeSpan.FromMinutes(2),
            CommandKind = ValidationCommandKind.Test,
            Commands = ["receipt boundary text inspection"],
            SafeToParallelize = true,
            ParallelismGroup = "hygiene",
            CacheCategory = "docs"
        },
        new()
        {
            Name = "cli-command-surface",
            Reason = "CLI changes require command-surface and forbidden-verb coverage.",
            Timeout = TimeSpan.FromMinutes(5),
            CommandKind = ValidationCommandKind.Test,
            Commands = ["dotnet test IronDev.IntegrationTests/IronDev.IntegrationTests.csproj --filter IronDevCliTests --no-restore"],
            SafeToParallelize = false,
            ParallelismGroup = "cli",
            CacheCategory = "cli-mutation"
        },
        new()
        {
            Name = "impacted-governance-tests",
            Reason = "Governance source changes require the impacted governance regression lane.",
            Timeout = TimeSpan.FromMinutes(10),
            CommandKind = ValidationCommandKind.Test,
            Commands = ["dotnet test IronDev.IntegrationTests/IronDev.IntegrationTests.csproj --filter Governance --no-restore"],
            SafeToParallelize = false,
            ParallelismGroup = "governance",
            CacheCategory = "authority"
        },
        new()
        {
            Name = "focused-ao",
            Reason = "AO files changed, so run the AO merge/release separation lane.",
            Timeout = TimeSpan.FromMinutes(6),
            CommandKind = ValidationCommandKind.Test,
            Commands = ["dotnet test IronDev.IntegrationTests/IronDev.IntegrationTests.csproj --filter BlockAOMergeAndReleaseSeparation --no-restore"],
            SafeToParallelize = true,
            ParallelismGroup = "focused",
            CacheCategory = "authority"
        },
        new()
        {
            Name = "focused-bk0",
            Reason = "BK0 validation runtime files changed, so run the BK0 focused lane.",
            Timeout = TimeSpan.FromMinutes(6),
            CommandKind = ValidationCommandKind.Test,
            Commands = ["dotnet test IronDev.IntegrationTests/IronDev.IntegrationTests.csproj --filter BlockBK0Validation --no-restore"],
            SafeToParallelize = true,
            ParallelismGroup = "focused",
            CacheCategory = "test"
        },
        new()
        {
            Name = "fast-authority-invariants",
            Reason = "Every validation plan carries the fast authority invariant lane.",
            Timeout = TimeSpan.FromMinutes(5),
            CommandKind = ValidationCommandKind.Test,
            Commands = ["dotnet test IronDev.IntegrationTests/IronDev.IntegrationTests.csproj --filter Authority --no-restore"],
            SafeToParallelize = false,
            ParallelismGroup = "authority",
            CacheCategory = "authority"
        },
        new()
        {
            Name = "phase-gate",
            Reason = "A phase/block boundary changed and requires explicit phase-gate evidence.",
            Timeout = TimeSpan.FromMinutes(12),
            CommandKind = ValidationCommandKind.Test,
            Commands = ["dotnet test IronDev.IntegrationTests/IronDev.IntegrationTests.csproj --filter PhaseGate --no-restore"],
            SafeToParallelize = false,
            ParallelismGroup = "phase-gate",
            CacheCategory = "authority"
        }
    ];

    public ValidationLanePlan Plan(ValidationLanePlanRequest request)
    {
        var normalizedFiles = request.ChangedFiles
            .Where(file => !string.IsNullOrWhiteSpace(file))
            .Select(NormalizePath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var selected = new Dictionary<string, ValidationLane>(StringComparer.OrdinalIgnoreCase);
        Add(selected, "fast-authority-invariants");

        foreach (var file in normalizedFiles)
            AddLanesForFile(selected, file);

        if (RequiresPhaseGate(request))
            Add(selected, "phase-gate");

        if (normalizedFiles.Length == 0)
            Add(selected, "diff-check");

        var lanes = selected.Values
            .OrderBy(lane => LaneOrder(lane.Name))
            .ThenBy(lane => lane.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return new ValidationLanePlan
        {
            ValidationPlanId = CreatePlanId(request, normalizedFiles, lanes),
            BaseRef = BlankToUnknown(request.BaseRef),
            HeadRef = BlankToUnknown(request.HeadRef),
            Phase = BlankToUnknown(request.Phase),
            CurrentBlock = BlankToUnknown(request.CurrentBlock),
            ChangedFiles = normalizedFiles,
            Lanes = lanes,
            EscalationReasons = BuildEscalationReasons(lanes),
            Boundary = ValidationRuntimeBoundary.Evidence
        };
    }

    public static ValidationLane? FindLane(string name) =>
        KnownLanes.FirstOrDefault(lane => string.Equals(lane.Name, name, StringComparison.OrdinalIgnoreCase));

    private static void AddLanesForFile(IDictionary<string, ValidationLane> selected, string file)
    {
        if (IsProjectMetadata(file))
        {
            Add(selected, "restore");
            Add(selected, "build");
            Add(selected, "impacted-governance-tests");
            return;
        }

        if (file.StartsWith("IronDev.Core/Governance/", StringComparison.OrdinalIgnoreCase))
        {
            Add(selected, "build");
            Add(selected, "impacted-governance-tests");
        }

        if (file.StartsWith("tools/IronDev.Cli/", StringComparison.OrdinalIgnoreCase))
        {
            Add(selected, "build");
            Add(selected, "cli-command-surface");
            Add(selected, "impacted-governance-tests");
        }

        if (file.StartsWith("IronDev.Core/Validation/", StringComparison.OrdinalIgnoreCase) ||
            file.StartsWith("IronDev.IntegrationTests/BlockBK0", StringComparison.OrdinalIgnoreCase))
        {
            Add(selected, "build");
            Add(selected, "focused-bk0");
        }

        if (file.StartsWith("IronDev.IntegrationTests/BlockAO", StringComparison.OrdinalIgnoreCase) ||
            file.Contains("MergeRelease", StringComparison.OrdinalIgnoreCase))
        {
            Add(selected, "build");
            Add(selected, "focused-ao");
        }

        if (file.StartsWith("Docs/receipts/", StringComparison.OrdinalIgnoreCase))
        {
            Add(selected, "diff-check");
            Add(selected, "docs-receipt-check");
        }

        if (file.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
            Add(selected, "build");
    }

    private static void Add(IDictionary<string, ValidationLane> selected, string laneName)
    {
        var lane = FindLane(laneName);
        if (lane is not null)
            selected[lane.Name] = lane;
    }

    private static bool RequiresPhaseGate(ValidationLanePlanRequest request)
    {
        if (!string.IsNullOrWhiteSpace(request.Phase) && !string.Equals(request.Phase, "unknown", StringComparison.OrdinalIgnoreCase))
            return true;

        return request.ChangedFiles.Any(file =>
            NormalizePath(file).Contains("Phase", StringComparison.OrdinalIgnoreCase) ||
            NormalizePath(file).Contains("AGENTS", StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsProjectMetadata(string file) =>
        file.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase) ||
        file.EndsWith(".slnx", StringComparison.OrdinalIgnoreCase) ||
        file.StartsWith("Directory.Build.", StringComparison.OrdinalIgnoreCase) ||
        file.EndsWith("global.json", StringComparison.OrdinalIgnoreCase);

    private static int LaneOrder(string name) =>
        name switch
        {
            "restore" => 0,
            "build" => 1,
            "diff-check" => 2,
            "docs-receipt-check" => 3,
            "fast-authority-invariants" => 4,
            "focused-bk0" => 5,
            "focused-ao" => 6,
            "cli-command-surface" => 7,
            "impacted-governance-tests" => 8,
            "phase-gate" => 9,
            _ => 100
        };

    private static string[] BuildEscalationReasons(IEnumerable<ValidationLane> lanes) =>
        lanes.Where(lane => !lane.SafeToParallelize)
            .Select(lane => $"{lane.Name}:{lane.ParallelismGroup}")
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static string CreatePlanId(ValidationLanePlanRequest request, string[] normalizedFiles, ValidationLane[] lanes)
    {
        var payload = string.Join('\n',
            BlankToUnknown(request.BaseRef),
            BlankToUnknown(request.HeadRef),
            BlankToUnknown(request.Phase),
            BlankToUnknown(request.CurrentBlock),
            string.Join('|', normalizedFiles),
            string.Join('|', lanes.Select(lane => lane.Name)));
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(payload));
        return "validation_plan_" + Convert.ToHexString(hash).ToLowerInvariant()[..16];
    }

    private static string NormalizePath(string path) =>
        path.Replace('\\', '/').Trim().TrimStart('/');

    private static string BlankToUnknown(string? value) =>
        string.IsNullOrWhiteSpace(value) ? "unknown" : value.Trim();
}

public static class ValidationGeneratedArtifactInspector
{
    public static ValidationChangedFileClassification Classify(string path)
    {
        var normalized = path.Replace('\\', '/').Trim();
        if (normalized.EndsWith("/obj/project.assets.json", StringComparison.OrdinalIgnoreCase) ||
            normalized.Contains("/obj/", StringComparison.OrdinalIgnoreCase) &&
            normalized.EndsWith(".nuget.g.props", StringComparison.OrdinalIgnoreCase) ||
            normalized.Contains("/obj/", StringComparison.OrdinalIgnoreCase) &&
            normalized.EndsWith(".nuget.g.targets", StringComparison.OrdinalIgnoreCase))
        {
            return new ValidationChangedFileClassification
            {
                Path = path,
                Kind = ValidationChangedFileKind.GeneratedRestoreArtifact,
                Reason = "Generated NuGet/restore artifact; validation must not preserve it as source evidence."
            };
        }

        if (string.Equals(Path.GetFileName(normalized), "NuGet.Config", StringComparison.OrdinalIgnoreCase))
        {
            return new ValidationChangedFileClassification
            {
                Path = path,
                Kind = ValidationChangedFileKind.TemporaryNuGetConfig,
                Reason = "Repo-local NuGet.Config can be a temporary restore workaround and needs explicit review."
            };
        }

        return new ValidationChangedFileClassification
        {
            Path = path,
            Kind = ValidationChangedFileKind.Source,
            Reason = "Source or documentation path."
        };
    }

    public static ValidationChangedFileClassification[] FindDirtyGeneratedArtifacts(IEnumerable<string> changedFiles) =>
        changedFiles.Select(Classify)
            .Where(item => item.Kind is ValidationChangedFileKind.GeneratedRestoreArtifact or ValidationChangedFileKind.TemporaryNuGetConfig)
            .ToArray();
}

public static class ValidationCachePolicyEvaluator
{
    public static bool CanAcceptCachedPassEvidence(string category, ValidationCachePolicy policy)
    {
        if (string.IsNullOrWhiteSpace(category))
            return false;

        return !policy.ProhibitedCachedEvidenceCategories.Contains(category, StringComparer.OrdinalIgnoreCase);
    }

    public static ValidationFailureKind ClassifyCachedPassEvidence(string category, ValidationCachePolicy policy) =>
        CanAcceptCachedPassEvidence(category, policy)
            ? ValidationFailureKind.Passed
            : ValidationFailureKind.CachePolicyViolation;
}

public static class SlowFlakyTestInventoryBuilder
{
    public static SlowFlakyTestInventory Default() =>
        new()
        {
            Items =
            [
                new SlowFlakyTestInventoryItem
                {
                    TestNameOrFilter = "stable Z-* validation bands",
                    Category = "stable-band",
                    Reason = "Broad governance bands are valuable but slower than focused block lanes.",
                    ExpectedDuration = TimeSpan.FromMinutes(20),
                    OwnerArea = "governance",
                    SafeToParallelize = false
                },
                new SlowFlakyTestInventoryItem
                {
                    TestNameOrFilter = "dogfood replay loops",
                    Category = "dogfood",
                    Reason = "Dogfood loops can touch workspace/run artifacts and cannot be accepted from cache as pass evidence.",
                    ExpectedDuration = TimeSpan.FromMinutes(30),
                    OwnerArea = "dogfood",
                    SafeToParallelize = false,
                    MutatesWorkspace = true,
                    UsesDogfoodPath = true
                },
                new SlowFlakyTestInventoryItem
                {
                    TestNameOrFilter = "database-backed integration lanes",
                    Category = "db",
                    Reason = "Database lanes depend on local service availability and need explicit environment reporting.",
                    ExpectedDuration = TimeSpan.FromMinutes(15),
                    OwnerArea = "data",
                    SafeToParallelize = false,
                    RequiresDatabase = true
                }
            ],
            Boundary = ValidationRuntimeBoundary.Evidence
        };
}
