import type { ChatCompletionResponse } from '../../api/types';
import type { ChatClarificationKind, ChatClarificationState } from '../../api/types';
import { coerceChatGovernanceMode, getChatModeGate } from './chatGovernanceGate';
import type { ChatModeGate } from './chatGovernanceGate';
import type { ChatResponseMode } from './chatTypes';

const chatMessageTagVersion = 1;

export interface AssistantTagMetadata {
  mode?: ChatResponseMode | null;
  modeConfidence?: number | null;
  modeReason?: string | null;
  clarification?: ChatClarificationState | null;
  gate?: ChatModeGate | null;
  reasoningTrace?: string[];
  disambiguationQuestion?: string | null;
  reasoningSummary?: string | null;
  dogfoodTraceId?: string | null;
  dogfoodTracePath?: string | null;
  traceId?: number | null;
  contextSummary?: string | null;
  linkedFilePaths?: string | null;
  linkedSymbols?: string | null;
}

export function buildAssistantTagEnvelope(response: ChatCompletionResponse, mode: ChatResponseMode | null) {
  const gate = getChatModeGate({ ...response, mode });

  return JSON.stringify({
    v: chatMessageTagVersion,
    mode: gate.mode,
    modeConfidence: gate.confidence,
    modeReason: gate.reason,
    clarification: normalizeClarification(response.clarification),
    gate: {
      mode: gate.mode,
      canSaveDiscussion: gate.canSaveDiscussion,
      canCreateTicket: gate.canCreateTicket,
      canViewSources: gate.canViewSources,
      canCopyMarkdown: gate.canCopyMarkdown,
      reason: gate.reason,
      confidence: gate.confidence,
      governanceActions: gate.governanceActions
    },
    reasoningTrace: response.reasoningTrace ?? [],
    disambiguationQuestion: response.disambiguationQuestion,
    reasoningSummary: response.reasoningSummary,
    dogfoodTraceId: response.dogfoodTraceId,
    dogfoodTracePath: response.dogfoodTracePath,
    traceId: response.traceId,
    contextSummary: response.contextSummary,
    linkedFilePaths: response.linkedFilePaths,
    linkedSymbols: response.linkedSymbols
  });
}

export function parseAssistantTagMetadata(rawTags: string | null | undefined) {
  if (!rawTags) {
    return {} as AssistantTagMetadata;
  }

  try {
    const parsed = JSON.parse(rawTags) as unknown;
    if (!parsed || typeof parsed !== 'object' || Array.isArray(parsed)) {
      return {} as AssistantTagMetadata;
    }

    const record = parsed as Record<string, unknown>;
    const version = toNumber(record.v);
    if (version !== chatMessageTagVersion) {
      return {} as AssistantTagMetadata;
    }

    return {
      mode: coerceChatGovernanceMode(typeof record.mode === 'string' ? record.mode : undefined),
      modeConfidence: toNumber(record.modeConfidence),
      modeReason: typeof record.modeReason === 'string' ? record.modeReason : null,
      clarification: parseClarification(record.clarification),
      gate: parseGate(record.gate),
      reasoningTrace: toStringList(record.reasoningTrace),
      disambiguationQuestion: typeof record.disambiguationQuestion === 'string' ? record.disambiguationQuestion : null,
      reasoningSummary: typeof record.reasoningSummary === 'string' ? record.reasoningSummary : null,
      dogfoodTraceId: typeof record.dogfoodTraceId === 'string' ? record.dogfoodTraceId : null,
      dogfoodTracePath: typeof record.dogfoodTracePath === 'string' ? record.dogfoodTracePath : null,
      traceId: toNumber(record.traceId),
      contextSummary: typeof record.contextSummary === 'string' ? record.contextSummary : null,
      linkedFilePaths: typeof record.linkedFilePaths === 'string' ? record.linkedFilePaths : null,
      linkedSymbols: typeof record.linkedSymbols === 'string' ? record.linkedSymbols : null
    } as AssistantTagMetadata;
  } catch {
    return {} as AssistantTagMetadata;
  }
}

function normalizeClarification(value: ChatClarificationState | null | undefined): ChatClarificationState {
  if (!value?.required) {
    return {
      required: false,
      kind: 'None',
      questions: [],
      reason: null
    };
  }

  return {
    required: true,
    kind: normalizeClarificationKind(value.kind),
    questions: value.questions?.filter(Boolean) ?? [],
    reason: value.reason ?? null
  };
}

function parseClarification(value: unknown): ChatClarificationState | null {
  if (!value || typeof value !== 'object' || Array.isArray(value)) {
    return null;
  }

  const record = value as Record<string, unknown>;
  const required = record.required === true;
  return {
    required,
    kind: required ? normalizeClarificationKind(typeof record.kind === 'string' ? record.kind : null) : 'None',
    questions: toStringList(record.questions),
    reason: typeof record.reason === 'string' ? record.reason : null
  };
}

function parseGate(value: unknown): ChatModeGate | null {
  if (!value || typeof value !== 'object' || Array.isArray(value)) {
    return null;
  }

  const record = value as Record<string, unknown>;
  const mode = coerceChatGovernanceMode(typeof record.mode === 'string' ? record.mode : undefined);
  return {
    mode,
    canSaveDiscussion: record.canSaveDiscussion === true,
    canCreateTicket: record.canCreateTicket === true,
    canViewSources: record.canViewSources === true,
    canCopyMarkdown: record.canCopyMarkdown === true,
    reason: typeof record.reason === 'string' ? record.reason : null,
    confidence: toNumber(record.confidence),
    governanceActions: toStringList(record.governanceActions),
    showGovernanceActions:
      record.canSaveDiscussion === true ||
      record.canCreateTicket === true ||
      record.canViewSources === true ||
      record.canCopyMarkdown === true,
    modeBadgeStatus: mode === 'Formalization'
      ? 'ready'
      : mode === 'Confirmation'
        ? 'warning'
        : 'neutral'
  };
}

function normalizeClarificationKind(value: string | null | undefined): ChatClarificationKind {
  return value === 'ProductScope' || value === 'GeneralScope' ? value : 'None';
}

function toNumber(value: unknown) {
  if (typeof value === 'number' && Number.isFinite(value)) {
    return value;
  }

  if (typeof value === 'string') {
    const parsed = Number(value);
    return Number.isFinite(parsed) ? parsed : null;
  }

  return null;
}

function toStringList(value: unknown): string[] {
  if (!Array.isArray(value)) {
    return [];
  }

  return value
    .filter((item): item is string => typeof item === 'string')
    .map((item) => item.trim())
    .filter(Boolean);
}
