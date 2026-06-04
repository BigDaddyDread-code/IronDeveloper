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
  modeBadgeStatus: 'ready' | 'warning' | 'neutral';
}

export function getChatModeGate(response: ChatCompletionResponse | null | undefined): ChatModeGate {
  const gate = response?.gate;
  const mode = coerceChatGovernanceMode(gate?.mode ?? response?.mode);
  const canSaveDiscussion = gate?.canSaveDiscussion === true;
  const canCreateTicket = gate?.canCreateTicket === true;
  const canViewSources = gate?.canViewSources === true;
  const canCopyMarkdown = gate?.canCopyMarkdown === true;
  const showGovernanceActions = canSaveDiscussion || canCreateTicket || canViewSources || canCopyMarkdown;

  return {
    mode,
    canSaveDiscussion,
    canCreateTicket,
    canViewSources,
    canCopyMarkdown,
    reason: gate?.reason ?? response?.modeReason ?? null,
    confidence: typeof gate?.confidence === 'number'
      ? gate.confidence
      : typeof response?.modeConfidence === 'number'
        ? response.modeConfidence
        : null,
    governanceActions: showGovernanceActions ? gate?.governanceActions?.filter(Boolean) ?? [] : [],
    showGovernanceActions,
    modeBadgeStatus: mode === 'Formalization'
      ? 'ready'
      : mode === 'Confirmation'
        ? 'warning'
        : 'neutral'
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
