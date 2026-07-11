import type { TicketSaveStatus } from '../api/types';
import { CommandButton } from './CommandButton';
import { MetadataRow } from './MetadataRow';
import { StatusBadge } from './StatusBadge';

export interface TicketEditDraft {
  title: string;
  summary: string;
  problem: string;
  proposedChange: string;
  type: string;
  priority: string;
  acceptanceCriteria: string;
  technicalNotes: string;
  unitTests: string;
  integrationTests: string;
  manualTests: string;
  regressionTests: string;
  buildValidation: string;
}

interface TicketEditFormProps {
  draft: TicketEditDraft;
  status: TicketSaveStatus;
  message: string;
  isDirty: boolean;
  validationMessage: string | null;
  blockedReason: string | null;
  onChange: (draft: TicketEditDraft) => void;
  onSave: () => void;
  onCancel: () => void;
  onReloadAndCompare: () => void;
}

export function TicketEditForm({
  draft,
  status,
  message,
  isDirty,
  validationMessage,
  blockedReason,
  onChange,
  onSave,
  onCancel,
  onReloadAndCompare
}: TicketEditFormProps) {
  const isSaving = status === 'saving';
  const isBlocked = Boolean(blockedReason);
  const canSave = isDirty && !isSaving && !isBlocked && !validationMessage;

  return (
    <div className="ticket-edit" data-testid="ticket.edit.form">
      <div className="ticket-edit__header">
        <div className="section-heading">
          <p className="eyebrow">Edit selected ticket</p>
          <h2>{draft.title.trim() || 'Untitled ticket'}</h2>
        </div>
        <div className="ticket-edit__commands">
          <StatusBadge status={isDirty ? 'warning' : status === 'saved' ? 'ready' : 'neutral'} data-testid="ticket.edit.dirtyState">
            {isDirty ? 'Unsaved changes' : status === 'saved' ? 'Saved' : 'Clean'}
          </StatusBadge>
          <CommandButton type="button" variant="subtle" testId="ticket.command.cancel" disabled={isSaving} onClick={onCancel}>
            Cancel
          </CommandButton>
          <CommandButton
            type="button"
            variant="primary"
            testId="ticket.command.save"
            disabled={!canSave}
            title={blockedReason ?? validationMessage ?? (!isDirty ? 'No changes to save.' : undefined)}
            onClick={onSave}
          >
            {isSaving ? 'Saving' : 'Save'}
          </CommandButton>
        </div>
      </div>

      <div className="ticket-edit__state">
        {blockedReason ? (
          <p className="state-error" data-testid="ticket.edit.validation">
            {blockedReason}
          </p>
        ) : validationMessage ? (
          <p className="state-error" data-testid="ticket.edit.validation">
            {validationMessage}
          </p>
        ) : status === 'validation' ? (
          <p className="state-error" data-testid="ticket.edit.validation">
            {message}
          </p>
        ) : (
          <MetadataRow label="State" value={message} />
        )}
        {status === 'saved' ? <p className="state-success" data-testid="ticket.edit.success">{message}</p> : null}
        {status === 'error' ? <p className="state-error" data-testid="ticket.edit.error">{message}</p> : null}
        {status === 'error' && message.startsWith('Stale write refused') ? (
          <CommandButton type="button" variant="subtle" testId="ticket.edit.reloadConflict" onClick={onReloadAndCompare}>Reload and compare</CommandButton>
        ) : null}
      </div>

      <div className="ticket-edit__form">
        <label>
          Title
          <input
            data-testid="ticket.edit.title"
            value={draft.title}
            disabled={isSaving || isBlocked}
            onChange={(event) => onChange({ ...draft, title: event.target.value })}
          />
        </label>

        <label>
          Summary
          <textarea
            data-testid="ticket.edit.summary"
            value={draft.summary}
            disabled={isSaving || isBlocked}
            rows={3}
            onChange={(event) => onChange({ ...draft, summary: event.target.value })}
          />
        </label>

        <div className="ticket-edit__grid">
          <label>
            Type
            <input
              data-testid="ticket.edit.type"
              value={draft.type}
              disabled={isSaving || isBlocked}
              onChange={(event) => onChange({ ...draft, type: event.target.value })}
            />
          </label>

          <label>
            Priority
            <select
              data-testid="ticket.edit.priority"
              value={draft.priority}
              disabled={isSaving || isBlocked}
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
          Problem
          <textarea
            data-testid="ticket.edit.problem"
            value={draft.problem}
            disabled={isSaving || isBlocked}
            rows={3}
            onChange={(event) => onChange({ ...draft, problem: event.target.value })}
          />
        </label>

        <label>
          Proposed change
          <textarea
            data-testid="ticket.edit.proposedChange"
            value={draft.proposedChange}
            disabled={isSaving || isBlocked}
            rows={4}
            onChange={(event) => onChange({ ...draft, proposedChange: event.target.value })}
          />
        </label>

        <label>
          Acceptance criteria
          <textarea
            data-testid="ticket.edit.acceptanceCriteria"
            value={draft.acceptanceCriteria}
            disabled={isSaving || isBlocked}
            rows={5}
            onChange={(event) => onChange({ ...draft, acceptanceCriteria: event.target.value })}
          />
        </label>

        <label>
          Technical notes
          <textarea
            data-testid="ticket.edit.technicalNotes"
            value={draft.technicalNotes}
            disabled={isSaving || isBlocked}
            rows={4}
            onChange={(event) => onChange({ ...draft, technicalNotes: event.target.value })}
          />
        </label>

        <div className="ticket-edit__grid">
          <label>
            Unit tests
            <textarea
              data-testid="ticket.edit.unitTests"
              value={draft.unitTests}
              disabled={isSaving || isBlocked}
              rows={3}
              onChange={(event) => onChange({ ...draft, unitTests: event.target.value })}
            />
          </label>

          <label>
            Integration tests
            <textarea
              data-testid="ticket.edit.integrationTests"
              value={draft.integrationTests}
              disabled={isSaving || isBlocked}
              rows={3}
              onChange={(event) => onChange({ ...draft, integrationTests: event.target.value })}
            />
          </label>

          <label>
            Manual tests
            <textarea
              data-testid="ticket.edit.manualTests"
              value={draft.manualTests}
              disabled={isSaving || isBlocked}
              rows={3}
              onChange={(event) => onChange({ ...draft, manualTests: event.target.value })}
            />
          </label>

          <label>
            Regression tests
            <textarea
              data-testid="ticket.edit.regressionTests"
              value={draft.regressionTests}
              disabled={isSaving || isBlocked}
              rows={3}
              onChange={(event) => onChange({ ...draft, regressionTests: event.target.value })}
            />
          </label>
        </div>

        <label>
          Build validation
          <textarea
            data-testid="ticket.edit.buildValidation"
            value={draft.buildValidation}
            disabled={isSaving || isBlocked}
            rows={3}
            onChange={(event) => onChange({ ...draft, buildValidation: event.target.value })}
          />
        </label>
      </div>
    </div>
  );
}
