using IronDev.Core.Models;

namespace IronDev.Core.Interfaces;

/// <summary>
/// Resolves short chat follow-ups against structured conversation state before
/// route judgement decides which Context Agent stages may run.
/// </summary>
public interface IConversationContextResolver
{
    ConversationContextResolution Resolve(ContextAgentRouteRequest request);
}
