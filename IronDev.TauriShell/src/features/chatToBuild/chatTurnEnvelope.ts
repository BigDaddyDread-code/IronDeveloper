import type { ChatCompletionResponse } from '../../api/types';
import { coerceChatGovernanceMode, getChatModeGate } from './chatGovernanceGate';
import type { ChatResponseMode } from './chatTypes';

const chatMessageTagVersion = 1;

export interface AssistantTagMetadata {
  mode?: ChatResponseMode | null;
  modeConfidence?: number | null;
  modeReason?: string | null;
  showGovernanceActions?: boolean;
  governanceActions?: string[];
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
    showGovernanceActions: gate.showGovernanceActions,
    governanceActions: gate.governanceActions,
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
      showGovernanceActions: typeof record.showGovernanceActions === 'boolean' ? record.showGovernanceActions : undefined,
      reasoningTrace: toStringList(record.reasoningTrace),
      governanceActions: toStringList(record.governanceActions),
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
