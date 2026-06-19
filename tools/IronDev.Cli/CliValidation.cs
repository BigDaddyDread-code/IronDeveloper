using System.Text.Json;
using System.Text.Json.Serialization;
using IronDev.Core.Validation;

namespace IronDev.Cli;

internal static class IronDevCliValidation
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private static readonly string[] ForbiddenSubcommands =
    [
        "approve",
        "merge",
        "release",
        "deploy",
        "continue",
        "continue-workflow",
        "satisfy-policy",
        "policy-satisfy",
        "mutate-source",
        "promote-memory",
        "push",
        "commit",
        "tag",
        "publish",
        "request-reviewers",
        "ready",
        "rerun-ci",
        "apply",
        "rollback"
    ];

    public static bool IsValidationCommand(string[] args) =>
        args.Length > 0 && string.Equals(args[0], "validate", StringComparison.OrdinalIgnoreCase);

    public static async Task<int> HandleAsync(string[] args, TextWriter output, TextWriter error, CancellationToken cancellationToken)
    {
        if (args.Length < 2)
            return Usage(error, "validate requires a subcommand: plan, run, lanes, receipt, or inventory.");

        var subcommand = args[1].ToLowerInvariant();
        if (ForbiddenSubcommands.Contains(subcommand, StringComparer.OrdinalIgnoreCase))
            return Usage(error, $"validate {args[1]} is intentionally unsupported; Block BK0 records validation evidence only.");

        return subcommand switch
        {
            "plan" => await HandlePlanAsync(args, output, error, cancellationToken).ConfigureAwait(false),
            "run" => await HandleRunAsync(args, output, error, cancellationToken).ConfigureAwait(false),
            "lanes" => HandleLanes(args, output),
            "receipt" => await HandleReceiptAsync(args, output, error, cancellationToken).ConfigureAwait(false),
            "inventory" => HandleInventory(args, output),
            _ => Usage(error, $"unsupported validate subcommand: {args[1]}")
        };
    }

    private static async Task<int> HandlePlanAsync(string[] args, TextWriter output, TextWriter error, CancellationToken cancellationToken)
    {
        var changedFilesPath = GetOption(args, "--changed-files");
        if (ChangedFilesPathIsMissing(changedFilesPath))
            return InvalidLanePlan(error, $"changed-files file was not found: {changedFilesPath}");

        var changedFiles = await ReadChangedFilesAsync(changedFilesPath, cancellationToken).ConfigureAwait(false);
        var request = new ValidationLanePlanRequest
        {
            BaseRef = GetOption(args, "--base") ?? "unknown",
            HeadRef = GetOption(args, "--head") ?? "unknown",
            Phase = GetOption(args, "--phase") ?? "unknown",
            CurrentBlock = GetOption(args, "--block") ?? "unknown",
            ChangedFiles = changedFiles
        };
        var plan = new ValidationLanePlanner().Plan(request);
        var outPath = GetOption(args, "--out");
        if (!string.IsNullOrWhiteSpace(outPath))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outPath)) ?? Environment.CurrentDirectory);
            await File.WriteAllTextAsync(outPath, JsonSerializer.Serialize(plan, JsonOptions), cancellationToken).ConfigureAwait(false);
        }

        if (HasFlag(args, "--json"))
            output.WriteLine(JsonSerializer.Serialize(new { command = "validate plan", status = "succeeded", plan }, JsonOptions));
        else
        {
            output.WriteLine($"Validation plan: {plan.ValidationPlanId}");
            foreach (var lane in plan.Lanes)
                output.WriteLine($"- {lane.Name}: {lane.Reason}");
            output.WriteLine("Boundary: plan evidence does not approve, merge, release, deploy, mutate source, promote memory, satisfy policy, or continue workflow.");
        }

        return 0;
    }

    private static async Task<int> HandleRunAsync(string[] args, TextWriter output, TextWriter error, CancellationToken cancellationToken)
    {
        var laneName = GetOption(args, "--lane");
        var command = GetOption(args, "--command");
        var artifacts = GetOption(args, "--artifacts");
        var adHoc = HasFlag(args, "--ad-hoc");
        if (string.IsNullOrWhiteSpace(artifacts))
            return Usage(error, "validate run requires --artifacts <directory>.");

        var changedFilesPath = GetOption(args, "--changed-files");
        if (ChangedFilesPathIsMissing(changedFilesPath))
            return InvalidLanePlan(error, $"changed-files file was not found: {changedFilesPath}");

        var knownLane = string.IsNullOrWhiteSpace(laneName) ? null : ValidationLanePlanner.FindLane(laneName);
        if (adHoc)
        {
            if (string.IsNullOrWhiteSpace(command))
                return Usage(error, "validate run --ad-hoc requires --command <executable>.");
            if (knownLane is not null)
                return InvalidLanePlan(error, $"ad-hoc validation cannot use known lane name '{knownLane.Name}'.");
        }
        else
        {
            if (string.IsNullOrWhiteSpace(laneName))
                return Usage(error, "validate run requires --lane <name> or --ad-hoc.");
            if (knownLane is null)
                return InvalidLanePlan(error, $"unknown validation lane: {laneName}");
            if (!string.IsNullOrWhiteSpace(command) || ReadRepeatedOptions(args, "--arg").Length > 0)
                return InvalidLanePlan(error, $"known lane '{knownLane.Name}' must run its declared command manifest; use --ad-hoc for custom commands.");
        }

        var lane = adHoc
            ? CreateAdHocLane(laneName, command!, ReadTimeoutSeconds(args))
            : knownLane!;
        var changedFiles = await ReadChangedFilesAsync(changedFilesPath, cancellationToken).ConfigureAwait(false);
        var dirtyChangedFiles = ValidationGeneratedArtifactInspector.FindDirtyGeneratedArtifacts(changedFiles);
        var runRoot = Path.Combine(Path.GetFullPath(artifacts), "validation-run-" + DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmss"));
        Directory.CreateDirectory(runRoot);

        var runner = new SupervisedProcessRunner();
        var results = new List<ValidationProcessResult>();
        foreach (var spec in BuildCommandSpecs(args, lane, runRoot, command, adHoc))
        {
            var result = await runner.RunAsync(spec, cancellationToken).ConfigureAwait(false);
            results.Add(result);
            if (result.FailureClassification != ValidationFailureKind.Passed)
                break;
        }

        if (results.Count == 0)
            return InvalidLanePlan(error, $"lane '{lane.Name}' does not have an executable command manifest.");

        var plan = new ValidationLanePlan
        {
            ValidationPlanId = "validation_plan_cli_" + Guid.NewGuid().ToString("N")[..12],
            BaseRef = GetOption(args, "--base") ?? "unknown",
            HeadRef = GetOption(args, "--head") ?? "unknown",
            Phase = GetOption(args, "--phase") ?? "unknown",
            CurrentBlock = GetOption(args, "--block") ?? "unknown",
            ChangedFiles = changedFiles,
            Lanes = [lane],
            Boundary = ValidationRuntimeBoundary.Evidence
        };
        var receipt = new ValidationRunReceiptBuilder().Build(
            plan,
            results,
            GetOption(args, "--branch") ?? "unknown",
            GetOption(args, "--commit") ?? "unknown",
            worktreeCleanBefore: !HasFlag(args, "--dirty-before"),
            worktreeCleanAfter: !HasFlag(args, "--dirty-after") && dirtyChangedFiles.Length == 0,
            dirtyChangedFiles: dirtyChangedFiles);
        var written = await new ValidationReceiptWriter().WriteAsync(runRoot, receipt, cancellationToken).ConfigureAwait(false);

        if (HasFlag(args, "--json"))
            output.WriteLine(JsonSerializer.Serialize(new { command = "validate run", status = receipt.Verdict.ToString(), runRoot, results, receiptPath = written.ReceiptPath }, JsonOptions));
        else
        {
            output.WriteLine($"Validation run: {receipt.ValidationRunId}");
            output.WriteLine($"Verdict: {receipt.Verdict}");
            output.WriteLine($"Receipt: {written.ReceiptPath}");
            output.WriteLine("Boundary: run evidence does not approve, merge, release, deploy, mutate source, promote memory, satisfy policy, or continue workflow.");
        }

        return receipt.Verdict == ValidationRunVerdict.Passed ? 0 : 1;
    }

    private static int HandleLanes(string[] args, TextWriter output)
    {
        if (HasFlag(args, "--json"))
            output.WriteLine(JsonSerializer.Serialize(new { lanes = ValidationLanePlanner.KnownLanes }, JsonOptions));
        else
        {
            output.WriteLine("Validation lanes:");
            foreach (var lane in ValidationLanePlanner.KnownLanes)
                output.WriteLine($"- {lane.Name}: {lane.Reason}");
        }

        return 0;
    }

    private static int HandleInventory(string[] args, TextWriter output)
    {
        var inventory = SlowFlakyTestInventoryBuilder.Default();
        if (HasFlag(args, "--json"))
            output.WriteLine(JsonSerializer.Serialize(new { inventory }, JsonOptions));
        else
        {
            output.WriteLine("Slow/flaky validation inventory:");
            foreach (var item in inventory.Items)
                output.WriteLine($"- {item.TestNameOrFilter}: {item.Reason}");
            output.WriteLine("Boundary: inventory evidence does not approve, merge, release, deploy, mutate source, promote memory, satisfy policy, or continue workflow.");
        }

        return 0;
    }

    private static async Task<int> HandleReceiptAsync(string[] args, TextWriter output, TextWriter error, CancellationToken cancellationToken)
    {
        var path = GetOption(args, "--path");
        if (string.IsNullOrWhiteSpace(path))
        {
            var artifacts = GetOption(args, "--artifacts");
            if (string.IsNullOrWhiteSpace(artifacts))
                return Usage(error, "validate receipt requires --path <receipt.json> or --artifacts <directory>.");
            path = HasFlag(args, "--last")
                ? FindLatestReceipt(artifacts)
                : Path.Combine(artifacts, "validation-receipt.json");
        }

        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return Usage(error, "validation receipt was not found.");

        var json = await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
        var receipt = JsonSerializer.Deserialize<ValidationRunReceipt>(json, JsonOptions);
        if (receipt is null)
            return Usage(error, "validation receipt could not be parsed.");

        if (HasFlag(args, "--json"))
            output.WriteLine(JsonSerializer.Serialize(new { receipt }, JsonOptions));
        else
        {
            output.WriteLine($"Validation receipt: {receipt.ValidationRunId}");
            output.WriteLine($"Verdict: {receipt.Verdict}");
            output.WriteLine($"Required lanes: {receipt.RequiredLanes.Length}");
            output.WriteLine("Boundary: receipt evidence does not approve, merge, release, deploy, mutate source, promote memory, satisfy policy, or continue workflow.");
        }

        return receipt.Verdict == ValidationRunVerdict.Passed ? 0 : 1;
    }

    private static async Task<string[]> ReadChangedFilesAsync(string? path, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(path))
            return [];

        var lines = await File.ReadAllLinesAsync(path, cancellationToken).ConfigureAwait(false);
        return lines.Where(line => !string.IsNullOrWhiteSpace(line)).Select(line => line.Trim()).ToArray();
    }

    private static bool ChangedFilesPathIsMissing(string? path) =>
        !string.IsNullOrWhiteSpace(path) && !File.Exists(path);

    private static ValidationLane CreateAdHocLane(string? laneName, string command, int timeoutSeconds) =>
        new()
        {
            Name = string.IsNullOrWhiteSpace(laneName) ? "ad-hoc" : "ad-hoc-" + SanitizeLaneName(laneName),
            Reason = "Ad hoc validation command. This receipt cannot satisfy a required named lane.",
            Requirement = ValidationLaneRequirement.Deferred,
            Timeout = TimeSpan.FromSeconds(timeoutSeconds),
            CommandKind = ValidationCommandKind.Generic,
            Commands = [command],
            CacheCategory = "ad-hoc"
        };

    private static IEnumerable<ValidationCommandSpec> BuildCommandSpecs(string[] args, ValidationLane lane, string runRoot, string? adHocCommand, bool adHoc)
    {
        if (adHoc)
        {
            yield return CreateCommandSpec(args, lane, runRoot, index: 1, adHocCommand!, ReadRepeatedOptions(args, "--arg"));
            yield break;
        }

        for (var i = 0; i < lane.Commands.Length; i++)
        {
            var parts = SplitCommandLine(lane.Commands[i]);
            if (parts.Length == 0)
                continue;
            yield return CreateCommandSpec(args, lane, runRoot, i + 1, parts[0], parts.Skip(1).ToArray());
        }
    }

    private static ValidationCommandSpec CreateCommandSpec(string[] args, ValidationLane lane, string runRoot, int index, string command, string[] commandArgs)
    {
        var timeout = TimeSpan.FromSeconds(ReadTimeoutSeconds(args, (int)Math.Max(1, lane.Timeout.TotalSeconds)));
        return new ValidationCommandSpec
        {
            Command = command,
            Arguments = commandArgs,
            WorkingDirectory = Path.GetFullPath(GetOption(args, "--cwd") ?? Environment.CurrentDirectory),
            Environment = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["MSBUILDDISABLENODEREUSE"] = "1"
            },
            Timeout = timeout,
            StdoutPath = Path.Combine(runRoot, $"{lane.Name}.{index}.stdout.log"),
            StderrPath = Path.Combine(runRoot, $"{lane.Name}.{index}.stderr.log"),
            LaneName = lane.Name,
            CommandKind = lane.CommandKind
        };
    }

    private static string[] SplitCommandLine(string commandLine)
    {
        var values = new List<string>();
        var current = new System.Text.StringBuilder();
        var inQuote = false;
        var quote = '\0';
        foreach (var ch in commandLine)
        {
            if ((ch == '"' || ch == '\'') && (!inQuote || ch == quote))
            {
                inQuote = !inQuote;
                quote = inQuote ? ch : '\0';
                continue;
            }

            if (char.IsWhiteSpace(ch) && !inQuote)
            {
                if (current.Length > 0)
                {
                    values.Add(current.ToString());
                    current.Clear();
                }

                continue;
            }

            current.Append(ch);
        }

        if (current.Length > 0)
            values.Add(current.ToString());

        return values.ToArray();
    }

    private static string SanitizeLaneName(string value)
    {
        var chars = value.Select(ch => char.IsLetterOrDigit(ch) || ch == '-' ? ch : '-').ToArray();
        return new string(chars).Trim('-');
    }

    private static string? FindLatestReceipt(string artifacts)
    {
        var root = Path.GetFullPath(artifacts);
        if (!Directory.Exists(root))
            return null;

        return Directory.EnumerateFiles(root, "validation-receipt.json", SearchOption.AllDirectories)
            .Select(path => new FileInfo(path))
            .OrderByDescending(info => info.LastWriteTimeUtc)
            .FirstOrDefault()
            ?.FullName;
    }

    private static string[] ReadRepeatedOptions(string[] args, string name)
    {
        var values = new List<string>();
        for (var i = 0; i < args.Length - 1; i++)
        {
            if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
                values.Add(args[i + 1]);
        }

        return values.ToArray();
    }

    private static int ReadTimeoutSeconds(string[] args, int defaultValue = 30) =>
        int.TryParse(GetOption(args, "--timeout-seconds"), out var seconds) && seconds > 0
            ? seconds
            : defaultValue;

    private static string? GetOption(string[] args, string name)
    {
        for (var i = 0; i < args.Length - 1; i++)
        {
            if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
                return args[i + 1];
        }

        return null;
    }

    private static bool HasFlag(string[] args, string name) =>
        args.Any(arg => string.Equals(arg, name, StringComparison.OrdinalIgnoreCase));

    private static int InvalidLanePlan(TextWriter error, string message)
    {
        error.WriteLine($"InvalidLanePlan: {message}");
        return 2;
    }

    private static int Usage(TextWriter error, string message)
    {
        error.WriteLine(message);
        error.WriteLine("Usage:");
        error.WriteLine("  irondev validate plan [--changed-files <path>] [--base <ref>] [--head <ref>] [--phase <name>] [--block <name>] [--out <path>] [--json]");
        error.WriteLine("  irondev validate run --lane <known-lane> --artifacts <dir> [--timeout-seconds <n>] [--cwd <path>] [--json]");
        error.WriteLine("  irondev validate run --ad-hoc --artifacts <dir> --command <exe> [--arg <arg>]... [--timeout-seconds <n>] [--cwd <path>] [--json]");
        error.WriteLine("  irondev validate lanes [--json]");
        error.WriteLine("  irondev validate receipt (--path <receipt.json> | --artifacts <dir> [--last]) [--json]");
        error.WriteLine("  irondev validate inventory [--json]");
        return 2;
    }
}
