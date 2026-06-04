using IronDev.Core.Chat;
using IronDev.Core.Interfaces;

namespace IronDev.Infrastructure.Services;

public sealed class FileSystemChatPromptTemplateProvider : IChatPromptTemplateProvider
{
    private const string FormalizationModeInstructionFile = "FormalizationModeInstructions.md";
    private const string ExplorationModeInstructionFile = "ExplorationModeInstructions.md";
    private const string AgentInstructionDirectory = "agent-instructions";
    private const string DocsDirectory = "Docs";

    private const string FormalizationInstructionFallback =
        "You are in Formalization Mode.\n\nThe user is asking to lock work into artifacts.\n\n- Produce concise, implementation-ready output only.\n- Surface risks, trade-offs, dependencies, assumptions, and test impact.\n- Prefer explicit next actions for ticket/discussion handoff.\n- Do not drift into exploratory chatter when user intent is already committed.";

    private const string ExplorationInstructionFallback =
        "You are in Exploration Mode.\n\nThis is a normal conversation or information-gathering turn.\n\n- Answer the user directly and naturally.\n- Do not try to architect, structure, formalize, or turn the discussion into project artefacts.\n- Do not mention tickets, discussions, saving work, or governance steps unless the user explicitly asks.\n- Stay conversational. Think out loud if it helps. Ask clarifying questions.\n- Keep it lightweight and focused on the current question.\n\nOnly move toward formalization when the user clearly wants to commit something.";

    private const string ConfirmationInstructionFallback =
        "You are in Confirmation Mode.\n\nAsk the user to confirm the intended lane or missing decision. Do not expose or suggest governance actions yet.";

    public string GetTemplate(ChatPromptTemplate template) =>
        template switch
        {
            ChatPromptTemplate.Formalization => LoadInstruction(FormalizationModeInstructionFile, FormalizationInstructionFallback),
            ChatPromptTemplate.Confirmation => ConfirmationInstructionFallback,
            _ => LoadInstruction(ExplorationModeInstructionFile, ExplorationInstructionFallback)
        };

    private static string LoadInstruction(string fileName, string fallback)
    {
        var filePath = ResolveInstructionPath(fileName);
        if (string.IsNullOrWhiteSpace(filePath))
            return fallback;

        try
        {
            return File.ReadAllText(filePath);
        }
        catch
        {
            return fallback;
        }
    }

    private static string? ResolveInstructionPath(string fileName)
    {
        var roots = new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory };

        foreach (var root in roots.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(root))
                continue;

            var current = Path.GetFullPath(root);
            for (var i = 0; i < 8; i++)
            {
                var candidate = Path.Combine(current, DocsDirectory, AgentInstructionDirectory, fileName);
                if (File.Exists(candidate))
                    return candidate;

                var parent = Directory.GetParent(current);
                if (parent is null)
                    break;
                current = parent.FullName;
            }
        }

        return null;
    }
}
