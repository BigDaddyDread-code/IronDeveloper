export type ApiConnectionStatus = 'checking' | 'connected' | 'disconnected' | 'unauthenticated';

export interface ApiStatus {
  status: ApiConnectionStatus;
  baseUrl: string;
  message: string;
}

export interface ProjectTicket {
  id: number;
  projectId: number;
  title: string;
  ticketType?: string;
  priority?: string;
  status?: string;
  summary?: string | null;
  problem?: string | null;
  acceptanceCriteria?: string | null;
  contextSummary?: string | null;
  buildValidation?: string | null;
  createdDate?: string;
}

export interface TicketLoadResult {
  tickets: ProjectTicket[];
  status: ApiConnectionStatus;
  message: string;
}
