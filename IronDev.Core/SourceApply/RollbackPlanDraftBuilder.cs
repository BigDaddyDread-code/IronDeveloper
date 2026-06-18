using System.Text;

namespace IronDev.Core.SourceApply;

public static class RollbackPlanDraftBuilder
{
    public static async Task<RollbackPlanDraft> WriteDraftAsync(SourceApplyRequest request, string runPath, CancellationToken cancellationToken)
    {
        var reversePatchPath = Path.Combine(runPath, "reverse-patch.diff");
        var instructionsPath = Path.Combine(runPath, "rollback-plan-draft.md");
        var patchText = File.Exists(request.PatchPath)
            ? await File.ReadAllTextAsync(request.PatchPath, cancellationToken).ConfigureAwait(false)
            : string.Empty;

        await File.WriteAllTextAsync(reversePatchPath, patchText, cancellationToken).ConfigureAwait(false);
        await File.WriteAllTextAsync(instructionsPath, RenderInstructions(request), cancellationToken).ConfigureAwait(false);

        return new RollbackPlanDraft
        {
            RollbackPlanDraftId = $"rollback_plan_draft_{Guid.NewGuid():N}",
            RunId = request.RunId,
            PatchSha256 = request.PatchSha256,
            ChangedFiles = request.ChangedFiles,
            ReversePatchPath = reversePatchPath,
            RevertInstructionsPath = instructionsPath,
            RiskNotes = [
                "Draft only; rollback was not executed.",
                "Use git apply --reverse only after separate human review.",
                "This draft does not prove rollback safety."
            ],
            CreatedAtUtc = DateTimeOffset.UtcNow,
            Boundary = SourceApplyBoundary.None
        };
    }

    private static string RenderInstructions(SourceApplyRequest request)
    {
        var builder = new StringBuilder();
        builder.AppendLine("# Rollback Plan Draft");
        builder.AppendLine();
        builder.AppendLine($"Run: `{request.RunId}`");
        builder.AppendLine($"Patch SHA-256: `{request.PatchSha256}`");
        builder.AppendLine();
        builder.AppendLine("Boundary: this is a rollback draft only. IronDev did not execute rollback.");
        builder.AppendLine();
        builder.AppendLine("If a future controlled source apply happens and a human approves rollback, the likely manual command shape is:");
        builder.AppendLine();
        builder.AppendLine("```powershell");
        builder.AppendLine($"git apply --reverse \"{request.PatchPath}\"");
        builder.AppendLine("```");
        builder.AppendLine();
        builder.AppendLine("Changed files:");
        foreach (var file in request.ChangedFiles)
            builder.AppendLine($"- `{file}`");
        builder.AppendLine();
        builder.AppendLine("This draft is not rollback approval, not rollback execution, not source apply, and not release readiness.");
        return builder.ToString();
    }
}
