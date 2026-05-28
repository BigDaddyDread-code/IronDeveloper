import type { ChatCompletionResponse } from '../../api/types';

export type ChatMessageRole = 'user' | 'assistant';
export type ChatSendMode = 'message' | 'project-review';

export interface ChatWorkspaceMessage {
  id: string;
  role: ChatMessageRole;
  content: string;
  createdUtc: string;
  response?: ChatCompletionResponse | null;
}

export interface ChatSendRequest {
  prompt: string;
  displayText?: string;
  mode?: ChatSendMode;
}
