import type { ChatCompletionResponse } from '../../api/types';
import type { ChatResponseMode } from './chatTypes';

export interface ChatModeGate {
  mode: ChatResponseMode | null;
  canSaveDiscussion: boolean;
  canCreateTicket: boolean;
  canViewSources: boolean;
  canCopyMarkdown: boolean;
  reason: string | null;
  confidence: number | null;
  governanceActions: string[];
  showGovernanceActions: boolean;
}

export function getChatModeGate(response: ChatCompletionResponse | null | undefined): ChatModeGate {
  const mode = coerceChatGovernanceMode(response?.mode);
  const isFormalization = mode === 'Formalization';

  return {
    mode,
    canSaveDiscussion: isFormalization,
    canCreateTicket: isFormalization,
    canViewSources: isFormalization,
    canCopyMarkdown: isFormalization,
    reason: response?.modeReason ?? null,
    confidence: typeof response?.modeConfidence === 'number' ? response.modeConfidence : null,
    governanceActions: isFormalization ? response?.governanceActions?.filter(Boolean) ?? [] : [],
    showGovernanceActions: isFormalization
  };
}

export function coerceChatGovernanceMode(value?: string | null): ChatResponseMode | null {
  if (!value) {
    return null;
  }

  const normalized = value.trim().toLowerCase();
  if (normalized === 'exploration') {
    return 'Exploration';
  }

  if (normalized === 'formalization') {
    return 'Formalization';
  }

  if (normalized === 'confirmation') {
    return 'Confirmation';
  }

  return null;
}
