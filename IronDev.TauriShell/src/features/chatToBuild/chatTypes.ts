import type { ChatCompletionResponse } from '../../api/types';

export type ChatMessageRole = 'user' | 'assistant';
export type ChatSendMode = 'projectStateReview';

export interface ChatWorkspaceMessage {
  id: string;
  role: ChatMessageRole;
  content: string;
  createdUtc: string;
  canContinueInBuild?: boolean;
  response?: ChatCompletionResponse | null;
}

export interface ChatSendRequest {
  prompt: string;
  displayText?: string;
  mode?: ChatSendMode;
  canContinueInBuild?: boolean;
}
