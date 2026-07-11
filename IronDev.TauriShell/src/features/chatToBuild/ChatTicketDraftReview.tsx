import { useState } from 'react';
import type { BaWorkingDraft, ProjectTicket } from '../../api/types';
import { CommandButton } from '../../components/CommandButton';
import { StatusBadge } from '../../components/StatusBadge';

interface ChatTicketDraftReviewProps {
  draft: BaWorkingDraft;
  projectLabel: string;
  sourceSessionId: number | null;
  onClose: () => void;
  onCreateTicket: (draft: BaWorkingDraft) => Promise<ProjectTicket>;
  onOpenWorkItem: (ticket: ProjectTicket) => void;
}

type CreateState = 'idle' | 'creating' | 'error' | 'success';

export function ChatTicketDraftReview({
  draft,
  projectLabel,
  sourceSessionId,
  onClose,
  onCreateTicket,
  onOpenWorkItem
}: ChatTicketDraftReviewProps) {
  const [createState, setCreateState] = useState<CreateState>('idle');
  const [errorMessage, setErrorMessage] = useState<string | null>(null);
  const [createdTicket, setCreatedTicket] = useState<ProjectTicket | null>(null);
  const rules = cleanList(draft.businessRules);
  const criteria = cleanList(draft.acceptanceCriteria);
  const assumptions = cleanList(draft.assumptions);
  const openQuestions = cleanList(draft.openQuestions);
  const conflicts = cleanList(draft.potentialConflicts);
  const sourceMessageIds = cleanList(draft.sourceMessageIds);
  const blockers = ticketCreationBlockers(draft, sourceSessionId, sourceMessageIds, conflicts);
  const canCreate = blockers.length === 0;

  const createTicket = async () => {
    if (!canCreate || createState === 'creating') return;

    setCreateState('creating');
    setErrorMessage(null);
    try {
      const ticket = await onCreateTicket(draft);
      setCreatedTicket(ticket);
      setCreateState('success');
    } catch (error) {
      setErrorMessage(error instanceof Error ? error.message : 'Create ticket failed.');
      setCreateState('error');
    }
  };

  return (
    <div className="chat-ticket-review__backdrop" data-testid="chat.ticketDraft.backdrop">
      <section
        className="chat-ticket-review"
        role="dialog"
        aria-modal="true"
        aria-labelledby="chat-ticket-review-title"
        data-testid="chat.ticketDraft.review"
        onKeyDown={(event) => {
          if (event.key === 'Escape' && createState !== 'creating') onClose();
        }}
      >
        {createState === 'success' && createdTicket ? (
          <div className="chat-ticket-review__success" data-testid="chat.ticketDraft.success">
            <p className="eyebrow">Ticket created</p>
            <h2 id="chat-ticket-review-title">Ticket #{createdTicket.id ?? 'created'}</h2>
            <p>{createdTicket.title ?? draft.candidateTitle ?? 'Untitled ticket'}</p>
            <p className="chat-ticket-review__boundary">
              The backend created this Work Item. Creation does not imply readiness, approval, execution, or source mutation.
            </p>
            <div className="chat-ticket-review__footer">
              <CommandButton type="button" variant="subtle" onClick={onClose}>
                Back to conversation
              </CommandButton>
              <CommandButton
                type="button"
                variant="primary"
                testId="chat.ticketDraft.openWorkItem"
                onClick={() => onOpenWorkItem(createdTicket)}
              >
                Open work item
              </CommandButton>
            </div>
          </div>
        ) : (
          <>
            <header className="chat-ticket-review__header">
              <div>
                <p className="eyebrow">Ticket draft</p>
                <h2 id="chat-ticket-review-title">Review ticket draft</h2>
                <p>{draft.candidateTitle?.trim() || 'Untitled draft'}</p>
              </div>
              <CommandButton
                type="button"
                variant="subtle"
                autoFocus
                testId="chat.ticketDraft.close"
                disabled={createState === 'creating'}
                onClick={onClose}
              >
                Close
              </CommandButton>
            </header>

            <div className="chat-ticket-review__body">
              <section className="chat-ticket-review__section chat-ticket-review__decision" data-testid="chat.ticketDraft.decision">
                <div className="chat-ticket-review__section-heading">
                  <h3>Decision</h3>
                  <StatusBadge status={canCreate ? 'ready' : 'warning'}>
                    {canCreate ? 'Ready to create' : 'Needs attention'}
                  </StatusBadge>
                </div>
                {blockers.length > 0 ? (
                  <ul data-testid="chat.ticketDraft.blockers">
                    {blockers.map((blocker) => <li key={blocker}>{blocker}</li>)}
                  </ul>
                ) : (
                  <p>The backend draft is ready and its Workshop provenance is present.</p>
                )}
                <ReviewList title="Open questions" items={openQuestions} testId="chat.ticketDraft.questions" />
                <ReviewList title="Potential conflicts" items={conflicts} testId="chat.ticketDraft.conflicts" />
              </section>

              <section className="chat-ticket-review__section">
                <h3>Draft</h3>
                {draft.problem ? <ReviewText label="Problem or outcome" value={draft.problem} /> : null}
                {draft.proposedChange ? <ReviewText label="Proposed change" value={draft.proposedChange} /> : null}
                <ReviewList title="Business rules" items={rules} testId="chat.ticketDraft.rules" />
                <ReviewList title="Acceptance criteria" items={criteria} ordered testId="chat.ticketDraft.criteria" />
                <ReviewList title="Assumptions" items={assumptions} testId="chat.ticketDraft.assumptions" />
              </section>

              <section className="chat-ticket-review__section" data-testid="chat.ticketDraft.provenance">
                <h3>Provenance</h3>
                <dl>
                  <div><dt>Project</dt><dd>{projectLabel}</dd></div>
                  <div><dt>Conversation</dt><dd>{sourceSessionId ? `Session ${sourceSessionId}` : 'Unavailable'}</dd></div>
                  <div><dt>Source messages</dt><dd>{sourceMessageIds.length > 0 ? sourceMessageIds.join(', ') : 'Unavailable'}</dd></div>
                  {typeof draft.confidence === 'number' ? (
                    <div><dt>Advisory confidence</dt><dd>{Math.round(draft.confidence * 100)}%</dd></div>
                  ) : null}
                </dl>
              </section>
            </div>

            {errorMessage ? (
              <p className="chat-ticket-review__error" role="alert" data-testid="chat.ticketDraft.error">
                {errorMessage}
              </p>
            ) : null}

            <footer className="chat-ticket-review__footer">
              <p className="chat-ticket-review__boundary">
                {draft.boundary ?? 'A ticket draft is shaped evidence, not approval or execution authority.'}
              </p>
              <CommandButton
                type="button"
                variant="primary"
                testId="chat.ticketDraft.create"
                disabled={!canCreate || createState === 'creating'}
                title={blockers[0]}
                onClick={() => void createTicket()}
              >
                {createState === 'creating' ? 'Creating ticket...' : 'Create ticket'}
              </CommandButton>
            </footer>
          </>
        )}
      </section>
    </div>
  );
}

function ticketCreationBlockers(
  draft: BaWorkingDraft,
  sourceSessionId: number | null,
  sourceMessageIds: string[],
  conflicts: string[]
) {
  const blockers: string[] = [];
  if (!draft.candidateTitle?.trim()) blockers.push('The draft needs a title.');
  if (draft.readyForConfirmation !== true) blockers.push('The backend draft is not ready for confirmation.');
  if (!sourceSessionId) blockers.push('A source Workshop session is required.');
  if (sourceMessageIds.length === 0) blockers.push('At least one source Workshop message is required.');
  if (conflicts.length > 0) blockers.push('Resolve every potential conflict before creating the ticket.');
  return blockers;
}

function ReviewText({ label, value }: { label: string; value: string }) {
  return (
    <div className="chat-ticket-review__text">
      <h4>{label}</h4>
      <p>{value}</p>
    </div>
  );
}

function ReviewList({
  title,
  items,
  ordered = false,
  testId
}: {
  title: string;
  items: string[];
  ordered?: boolean;
  testId: string;
}) {
  if (items.length === 0) return null;
  const List = ordered ? 'ol' : 'ul';
  return (
    <div className="chat-ticket-review__list" data-testid={testId}>
      <h4>{title}</h4>
      <List>{items.map((item) => <li key={item}>{item}</li>)}</List>
    </div>
  );
}

function cleanList(items: string[] | null | undefined) {
  return (items ?? []).map((item) => item.trim()).filter(Boolean);
}
