import type { TicketCreateStatus } from '../api/types';
import { CommandButton } from './CommandButton';
import { MetadataRow } from './MetadataRow';
import { StatusBadge } from './StatusBadge';
import { SurfacePanel } from './SurfacePanel';

export interface CreateTicketDraft {
  title: string;
  summary: string;
  type: string;
  priority: string;
  acceptanceCriteria: string;
}

interface CreateTicketPanelProps {
  projectId: number | null;
  projectName: string | null;
  draft: CreateTicketDraft;
  status: TicketCreateStatus;
  message: string;
  createdTicketId: number | null;
  onChange: (draft: CreateTicketDraft) => void;
  onSubmit: () => void;
  onCancel: () => void;
}

export function CreateTicketPanel({
  projectId,
  projectName,
  draft,
  status,
  message,
  createdTicketId,
  onChange,
  onSubmit,
  onCancel
}: CreateTicketPanelProps) {
  const isSubmitting = status === 'submitting';

  return (
    <SurfacePanel className="ticket-create-panel" testId="ticket.create.panel">
      <div className="ticket-create-panel__header">
        <div className="section-heading">
          <p className="eyebrow">New IronDev work item</p>
          <h2>Create Ticket</h2>
        </div>
        <StatusBadge status={status === 'success' ? 'ready' : status === 'error' ? 'warning' : 'info'}>
          {statusLabel(status)}
        </StatusBadge>
      </div>

      <div className="ticket-create-panel__metadata">
        <MetadataRow label="Project" value={projectName ?? (projectId ? `Project ${projectId}` : 'Project required')} />
        <MetadataRow label="Write path" value="Tauri UI -> IronDev.Api -> ticket database" />
      </div>

      {status === 'success' ? (
        <div className="ticket-create-panel__state ticket-create-panel__state--success" data-testid="ticket.create.success">
          <h3>Ticket created</h3>
          <p>{message}</p>
          {createdTicketId ? <MetadataRow label="Ticket" value={`#${createdTicketId}`} /> : null}
        </div>
      ) : null}

      {status === 'error' ? (
        <div className="ticket-create-panel__state ticket-create-panel__state--error" data-testid="ticket.create.error">
          <h3>Ticket was not created</h3>
          <p>{message}</p>
        </div>
      ) : null}

      <div className="ticket-create-form">
        <label>
          Title
          <input
            data-testid="ticket.create.title"
            value={draft.title}
            disabled={isSubmitting}
            onChange={(event) => onChange({ ...draft, title: event.target.value })}
            placeholder="Short, specific work item"
          />
        </label>

        <label>
          Summary
          <textarea
            data-testid="ticket.create.summary"
            value={draft.summary}
            disabled={isSubmitting}
            onChange={(event) => onChange({ ...draft, summary: event.target.value })}
            placeholder="What needs to change, and why?"
            rows={4}
          />
        </label>

        <div className="ticket-create-form__grid">
          <label>
            Type
            <input
              data-testid="ticket.create.type"
              value={draft.type}
              disabled={isSubmitting}
              onChange={(event) => onChange({ ...draft, type: event.target.value })}
              placeholder="UI / Workflow"
            />
          </label>

          <label>
            Priority
            <select
              data-testid="ticket.create.priority"
              value={draft.priority}
              disabled={isSubmitting}
              onChange={(event) => onChange({ ...draft, priority: event.target.value })}
            >
              <option value="Critical">Critical</option>
              <option value="High">High</option>
              <option value="Medium">Medium</option>
              <option value="Low">Low</option>
            </select>
          </label>
        </div>

        <label>
          Acceptance criteria
          <textarea
            data-testid="ticket.create.acceptanceCriteria"
            value={draft.acceptanceCriteria}
            disabled={isSubmitting}
            onChange={(event) => onChange({ ...draft, acceptanceCriteria: event.target.value })}
            placeholder="One criterion per line"
            rows={5}
          />
        </label>
      </div>

      <div className="ticket-create-panel__actions">
        <CommandButton type="button" variant="subtle" testId="ticket.create.cancel" disabled={isSubmitting} onClick={onCancel}>
          Cancel
        </CommandButton>
        <CommandButton type="button" variant="primary" testId="ticket.create.submit" disabled={isSubmitting} onClick={onSubmit}>
          {isSubmitting ? 'Creating ticket' : 'Create'}
        </CommandButton>
      </div>
    </SurfacePanel>
  );
}

function statusLabel(status: TicketCreateStatus) {
  switch (status) {
    case 'submitting':
      return 'Creating';
    case 'success':
      return 'Created';
    case 'error':
      return 'Needs attention';
    case 'validating':
      return 'Validating';
    default:
      return 'Draft';
  }
}
