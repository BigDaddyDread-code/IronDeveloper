using IronDev.Core.Interfaces;
using IronDev.Core.Models;

namespace IronDev.Infrastructure.Services;

public sealed class CodeProposalValidator : ICodeProposalValidator
{
    private readonly ICodeRunProfileCatalog _profiles;

    public CodeProposalValidator(ICodeRunProfileCatalog profiles)
    {
        _profiles = profiles;
    }

    public CodeProposalValidationResult Validate(CodeProposal proposal)
    {
        var errors = new List<string>();
        var warnings = new List<string>();
        var profile = _profiles.GetProfile(proposal.RunProfile.RuntimeProfileId);
        if (profile is null)
        {
            errors.Add($"Runtime profile '{proposal.RunProfile.RuntimeProfileId}' is not allow-listed.");
        }

        if (proposal.ProjectId <= 0)
            errors.Add("ProjectId must be set.");
        if (proposal.TicketId <= 0)
            errors.Add("TicketId must be set.");
        if (string.IsNullOrWhiteSpace(proposal.ReviewId))
            errors.Add("ReviewId must be set.");
        if (proposal.Files.Count == 0)
            errors.Add("Code proposal must include at least one generated file.");

        if (profile is not null && proposal.Files.Count > profile.MaxFileCount)
            errors.Add($"Code proposal has {proposal.Files.Count} files; profile '{profile.RuntimeProfileId}' allows {profile.MaxFileCount}.");

        ValidateRelativePath(proposal.RunProfile.WorkingDirectory, "WorkingDirectory", errors);
        if (Path.HasExtension(proposal.RunProfile.WorkingDirectory))
            errors.Add("WorkingDirectory must be a relative directory, not a file path.");

        if (profile is not null)
        {
            if (!string.Equals(proposal.RunProfile.BuildCommand, profile.BuildCommand, StringComparison.Ordinal))
                errors.Add("BuildCommand must match the backend-owned runtime profile.");
            if (!string.Equals(proposal.RunProfile.RunCommand, profile.RunCommand, StringComparison.Ordinal))
                errors.Add("RunCommand must match the backend-owned runtime profile.");
        }

        foreach (var file in proposal.Files)
        {
            ValidateRelativePath(file.RelativePath, $"Generated file '{file.RelativePath}'", errors);

            if (profile is not null && System.Text.Encoding.UTF8.GetByteCount(file.Content) > profile.MaxFileBytes)
                errors.Add($"Generated file '{file.RelativePath}' exceeds the profile file size limit.");

            if (string.IsNullOrWhiteSpace(file.Content))
                warnings.Add($"Generated file '{file.RelativePath}' is empty.");
            if (!string.Equals(file.Sha256, ComputeSha256(file.Content), StringComparison.OrdinalIgnoreCase))
                errors.Add($"Generated file '{file.RelativePath}' has an invalid SHA-256 hash.");
        }

        var duplicates = proposal.Files
            .GroupBy(file => file.RelativePath, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToArray();
        foreach (var duplicate in duplicates)
            errors.Add($"Generated file path '{duplicate}' is duplicated.");

        foreach (var verification in proposal.Verifications)
        {
            if (profile is not null && !profile.AllowedVerificationKinds.Contains(verification.Kind, StringComparer.OrdinalIgnoreCase))
            {
                errors.Add($"Verification kind '{verification.Kind}' is not allowed for runtime profile '{profile.RuntimeProfileId}'.");
            }

            foreach (var parameter in verification.Parameters)
            {
                if (parameter.Value.Length > 2_000)
                    errors.Add($"Verification parameter '{parameter.Key}' is too large.");
            }
        }

        return new CodeProposalValidationResult
        {
            Errors = errors,
            Warnings = warnings,
            RuntimeProfile = profile
        };
    }

    private static void ValidateRelativePath(string value, string label, ICollection<string> errors)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            errors.Add($"{label} is required.");
            return;
        }

        if (Path.IsPathRooted(value))
        {
            errors.Add($"{label} must be relative.");
            return;
        }

        var normalized = value.Replace('\\', '/');
        if (normalized.Split('/', StringSplitOptions.RemoveEmptyEntries).Any(part => part == ".."))
            errors.Add($"{label} must not contain '..'.");
    }

    private static string ComputeSha256(string value)
    {
        var bytes = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
