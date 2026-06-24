using System.Text.RegularExpressions;

namespace IronDev.Core.Governance;

public static partial class EvidencePayloadRedactor
{
    public const int MaxPreviewLength = 512;

    public static RedactedEvidencePreview Redact(SuppliedEvidencePayloadForRedaction payload)
    {
        var reasons = new List<EvidenceRedactionReasonKind>();
        var text = payload.PayloadText ?? string.Empty;

        if (text.Any(IsUnsafeControlCharacter))
        {
            return Suppressed(
                payload,
                EvidenceRedactionReasonKind.UnsafeControlCharacters);
        }

        if (PrivateKeyPattern().IsMatch(text))
        {
            return Suppressed(
                payload,
                EvidenceRedactionReasonKind.PrivateKeyDetected);
        }

        if (PrivateReasoningPattern().IsMatch(text))
        {
            reasons.Add(EvidenceRedactionReasonKind.PrivateReasoningDetected);
            text = PrivateReasoningPattern().Replace(text, "[REDACTED_PRIVATE_REASONING]");
        }

        if (PromptOrModelPattern().IsMatch(text))
        {
            reasons.Add(EvidenceRedactionReasonKind.PromptOrModelTextDetected);
            text = PromptOrModelPattern().Replace(text, "[REDACTED_PROMPT_OR_MODEL_TEXT]");
        }

        if (PatchPattern().IsMatch(text))
        {
            reasons.Add(EvidenceRedactionReasonKind.PatchOrDiffContentDetected);
            text = PatchPattern().Replace(text, "[REDACTED_PATCH_CONTENT]");
        }

        if (RawPayloadPattern().IsMatch(text))
        {
            reasons.Add(EvidenceRedactionReasonKind.RawPayloadMarkerDetected);
            text = RawPayloadPattern().Replace(text, "[REDACTED_RAW_PAYLOAD]");
        }

        if (ValidationLogPattern().IsMatch(text))
        {
            reasons.Add(EvidenceRedactionReasonKind.ValidationLogContentDetected);
            text = ValidationLogPattern().Replace(text, "[REDACTED_RAW_PAYLOAD]");
        }

        if (RequestResponseBodyPattern().IsMatch(text))
        {
            reasons.Add(EvidenceRedactionReasonKind.RequestResponseBodyDetected);
            text = RequestResponseBodyPattern().Replace(text, "[REDACTED_RAW_PAYLOAD]");
        }

        if (AuthorizationPattern().IsMatch(text))
        {
            reasons.Add(EvidenceRedactionReasonKind.AuthorizationHeaderDetected);
            text = AuthorizationPattern().Replace(text, "[REDACTED_AUTHORIZATION]");
        }

        if (BearerPattern().IsMatch(text))
        {
            reasons.Add(EvidenceRedactionReasonKind.TokenDetected);
            text = BearerPattern().Replace(text, "[REDACTED_SECRET]");
        }

        if (ApiKeyPattern().IsMatch(text))
        {
            reasons.Add(EvidenceRedactionReasonKind.SecretDetected);
            text = ApiKeyPattern().Replace(text, "[REDACTED_SECRET]");
        }

        if (PasswordPattern().IsMatch(text))
        {
            reasons.Add(EvidenceRedactionReasonKind.SecretDetected);
            text = PasswordPattern().Replace(text, "[REDACTED_SECRET]");
        }

        if (TokenAssignmentPattern().IsMatch(text))
        {
            reasons.Add(EvidenceRedactionReasonKind.TokenDetected);
            text = TokenAssignmentPattern().Replace(text, "[REDACTED_SECRET]");
        }

        if (ConnectionStringPattern().IsMatch(text))
        {
            reasons.Add(EvidenceRedactionReasonKind.ConnectionStringDetected);
            text = ConnectionStringPattern().Replace(text, "[REDACTED_SECRET]");
        }

        var truncated = false;
        if (text.Length > MaxPreviewLength)
        {
            reasons.Add(EvidenceRedactionReasonKind.PayloadTooLarge);
            text = text[..MaxPreviewLength];
            truncated = true;
        }

        if (LooksUnsafeAfterRedaction(text))
        {
            return Suppressed(
                payload,
                EvidenceRedactionReasonKind.UnhandledUnsafeContent);
        }

        var distinctReasons = reasons
            .Distinct()
            .OrderBy(static reason => reason)
            .ToArray();

        return new RedactedEvidencePreview
        {
            EvidenceId = payload.EvidenceId,
            PreviewText = text,
            PayloadState = EvidencePayloadState.RedactedPreviewAvailable,
            WasRedacted = distinctReasons.Length > 0,
            WasSuppressed = false,
            RedactionReasons = distinctReasons,
            PreviewTruncated = truncated,
            Source = payload.Source
        };
    }

    private static RedactedEvidencePreview Suppressed(
        SuppliedEvidencePayloadForRedaction payload,
        EvidenceRedactionReasonKind reason) =>
        new()
        {
            EvidenceId = payload.EvidenceId,
            PreviewText = "[SUPPRESSED_UNSAFE_PAYLOAD]",
            PayloadState = EvidencePayloadState.PayloadSuppressed,
            WasRedacted = true,
            WasSuppressed = true,
            RedactionReasons = [reason],
            PreviewTruncated = false,
            Source = payload.Source
        };

    private static bool IsUnsafeControlCharacter(char value) =>
        char.IsControl(value) && value is not '\r' and not '\n' and not '\t';

    private static bool LooksUnsafeAfterRedaction(string text) =>
        PrivateKeyPattern().IsMatch(text) ||
        AuthorizationPattern().IsMatch(text) ||
        BearerPattern().IsMatch(text) ||
        ApiKeyPattern().IsMatch(text) ||
        PasswordPattern().IsMatch(text) ||
        TokenAssignmentPattern().IsMatch(text) ||
        ConnectionStringPattern().IsMatch(text);

    [GeneratedRegex("(?is)-----BEGIN [A-Z ]*PRIVATE KEY-----.*?-----END [A-Z ]*PRIVATE KEY-----")]
    private static partial Regex PrivateKeyPattern();

    [GeneratedRegex("(?i)\\b(hidden chain-of-thought|chain of thought|private reasoning|scratchpad)\\b[^\\r\\n]*")]
    private static partial Regex PrivateReasoningPattern();

    [GeneratedRegex("(?i)\\b(prompt text|model response text|system prompt|developer prompt|user prompt)\\b[^\\r\\n]*")]
    private static partial Regex PromptOrModelPattern();

    [GeneratedRegex("(?im)^(diff --git .*|@@.*)$")]
    private static partial Regex PatchPattern();

    [GeneratedRegex("(?i)\\b(raw evidence payload|raw receipt payload|raw payload)\\b[^\\r\\n]*")]
    private static partial Regex RawPayloadPattern();

    [GeneratedRegex("(?i)\\b(raw validation log|validation log)\\b[^\\r\\n]*")]
    private static partial Regex ValidationLogPattern();

    [GeneratedRegex("(?i)\\b(raw request body|raw response body|request body|response body)\\b[^\\r\\n]*")]
    private static partial Regex RequestResponseBodyPattern();

    [GeneratedRegex("(?im)^\\s*authorization\\s*:\\s*[^\\r\\n]+")]
    private static partial Regex AuthorizationPattern();

    [GeneratedRegex("(?i)\\bbearer\\s+[A-Za-z0-9._~+/=-]+")]
    private static partial Regex BearerPattern();

    [GeneratedRegex("(?i)\\b(api[_ -]?key|apikey)\\s*[:=]\\s*[^\\s;]+")]
    private static partial Regex ApiKeyPattern();

    [GeneratedRegex("(?i)\\bpassword\\s*[:=]\\s*[^\\s;]+")]
    private static partial Regex PasswordPattern();

    [GeneratedRegex("(?i)\\b(token|access_token|refresh_token)\\s*[:=]\\s*[^\\s;]+")]
    private static partial Regex TokenAssignmentPattern();

    [GeneratedRegex("(?i)\\b(connection string|server=|data source=|user id=|uid=|pwd=)[^\\r\\n]*")]
    private static partial Regex ConnectionStringPattern();
}
