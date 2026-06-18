using System.Text.Json;
using System.Text.Json.Serialization;
using IronDev.Core.Governance;

namespace IronDev.Cli;

internal static class IronDevCliGovernanceInspection
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public static bool IsGovernanceCommand(string[] args) =>
        args.Length > 0 && string.Equals(args[0], "governance", StringComparison.OrdinalIgnoreCase);

    public static Task<int> HandleAsync(string[] args, TextWriter output, TextWriter error, CancellationToken cancellationToken)
    {
        _ = cancellationToken;
        if (args.Length < 2)
            return Task.FromResult(WriteUsage(error));

        return args[1].ToLowerInvariant() switch
        {
            "inventory" => Task.FromResult(HandleInventory(args, output, error)),
            "classify" => Task.FromResult(HandleClassify(args, output, error)),
            _ => Task.FromResult(WriteFailure(error, $"unsupported governance command: {args[1]}"))
        };
    }

    private static int HandleInventory(string[] args, TextWriter output, TextWriter error)
    {
        var json = HasFlag(args, "--json") || HasOutputJson(args);
        if (json)
        {
            output.WriteLine(JsonSerializer.Serialize(new
            {
                command = "governance inventory",
                status = "succeeded",
                entries = AuthorityActionInventory.All
            }, JsonOptions));
            return 0;
        }

        output.WriteLine("Governed action inventory");
        output.WriteLine();
        foreach (var entry in AuthorityActionInventory.All)
        {
            output.WriteLine($"- {entry.ActionKind}: {entry.Classification}; allowedInCurrentBlock={entry.AllowedInCurrentBlock}; requiresConscience={entry.RequiresConscience}; requiresThoughtLedger={entry.RequiresThoughtLedger}; status={entry.CurrentImplementationStatus}");
        }

        output.WriteLine();
        output.WriteLine("Authority-bearing actions are registered only in Block AB and are not executable in this block.");
        return 0;
    }

    private static int HandleClassify(string[] args, TextWriter output, TextWriter error)
    {
        var json = HasFlag(args, "--json") || HasOutputJson(args);
        var action = GetOption(args, "--action");
        if (string.IsNullOrWhiteSpace(action))
            return WriteFailure(error, "missing required option: --action <action-kind>");

        var entry = AuthorityActionInventory.Get(action);
        var data = new
        {
            command = "governance classify",
            status = "succeeded",
            requestedActionKind = action,
            registeredActionKind = entry.ActionKind,
            entry.Classification,
            entry.AllowedInCurrentBlock,
            entry.RequiresConscience,
            entry.RequiresThoughtLedger,
            entry.CurrentImplementationStatus,
            executableInCurrentBlock = entry.AllowedInCurrentBlock
        };

        if (json)
        {
            output.WriteLine(JsonSerializer.Serialize(data, JsonOptions));
            return 0;
        }

        output.WriteLine($"{entry.ActionKind}: {entry.Classification}");
        output.WriteLine($"Allowed in current block: {entry.AllowedInCurrentBlock}");
        output.WriteLine($"Requires Conscience: {entry.RequiresConscience}");
        output.WriteLine($"Requires ThoughtLedger: {entry.RequiresThoughtLedger}");
        output.WriteLine($"Implementation status: {entry.CurrentImplementationStatus}");
        output.WriteLine("Executable in current block: false");
        return 0;
    }

    private static int WriteUsage(TextWriter error)
    {
        error.WriteLine("Usage:");
        error.WriteLine("  irondev governance inventory [--json]");
        error.WriteLine("  irondev governance classify --action <action-kind> [--json]");
        return 2;
    }

    private static int WriteFailure(TextWriter error, string message)
    {
        error.WriteLine(message);
        return 2;
    }

    private static bool HasFlag(string[] args, string name) =>
        args.Any(arg => string.Equals(arg, name, StringComparison.OrdinalIgnoreCase));

    private static bool HasOutputJson(string[] args)
    {
        for (var index = 0; index < args.Length - 1; index++)
        {
            if (string.Equals(args[index], "--output", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(args[index + 1], "json", StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private static string? GetOption(string[] args, string name)
    {
        for (var index = 0; index < args.Length - 1; index++)
        {
            if (string.Equals(args[index], name, StringComparison.OrdinalIgnoreCase))
                return args[index + 1];
        }

        return null;
    }
}
