import type { ChatCompletionResponse, ChatDocumentSource } from '../../api/types';

export type ChatMessageRole = 'user' | 'assistant';
export type ChatSendMode = 'projectQuestion' | 'projectStateReview';
export type ChatResponseMode = 'Exploration' | 'Formalization' | 'Confirmation';

export interface ChatWorkspaceMessage {
  id: string;
  role: ChatMessageRole;
  content: string;
  createdUtc: string;
  canContinueInBuild?: boolean;
  response?: ChatCompletionResponse | null;
  discussionSaveStatus?: 'idle' | 'saving' | 'saved' | 'error';
  discussionSaveError?: string | null;
  savedDiscussion?: {
    documentId: number;
    documentVersionId: number;
  } | null;
  documentSources?: ChatDocumentSource[];
}

export interface ChatSendRequest {
  prompt: string;
  displayText?: string;
  mode?: ChatSendMode;
  canContinueInBuild?: boolean;
}
