import { useState } from 'react';
import type {
  ProjectUnderstandingConflict,
  ProjectUnderstandingFact,
  ProjectUnderstandingReadModel
} from '../../api/types';
import { CommandButton } from '../../components/CommandButton';
import { StatusBadge } from '../../components/StatusBadge';
import type { ProjectUnderstandingController } from './useProjectUnderstanding';

interface ProjectUnderstandingPanelProps {
  controller: ProjectUnderstandingController;
  onClose: () => void;
}

const factLabels: Record<string, string> = {
  ProductSummary: 'Product summary',
  PrimaryUsers: 'Primary users',
  Goals: 'Goals',
  Constraints: 'Constraints',
  ApplicationType: 'Application type',
  DesiredLanguage: 'Desired language',
  DesiredFramework: 'Desired framework',
  DesiredDatabase: 'Desired database',
  DesiredTestApproach: 'Desired test approach',
  TargetPlatform: 'Target platform',
  DeploymentIntent: 'Deployment intent'
};

const factKeys = Object.keys(factLabels);
const factGroupOrder = ['Confirmed', 'Inferred', 'Conflicted', 'Unknown'] as const;

export function ProjectUnderstandingPanel({ controller, onClose }: ProjectUnderstandingPanelProps) {
  const {
    model,
    loadState,
    loadError,
    mutationError,
    isMutating,
    hasUnresolvedMutation,
    retryLoad,
    editFact,
    confirmFact,
    setFactLock,
    resolveConflict,
    acceptRenameProposal,
    retryPendingMutation
  } = controller;

  return (
    <aside className="chat-context-panel project-understanding-panel" data-testid="chat.projectContext">
      <header className="chat-context-panel__header">
        <div className="section-heading">
          <p className="eyebrow">Project context</p>
          <h3>Understanding and status</h3>
        </div>
        <CommandButton type="button" variant="subtle" testId="chat.projectContext.close" onClick={onClose}>
          Hide
        </CommandButton>
      </header>

      {loadState === 'loading' && !model ? (
        <p className="project-understanding-panel__state" data-testid="chat.projectUnderstanding.loading">
          Loading project understanding...
        </p>
      ) : null}

      {loadState === 'error' ? (
        <section className="project-understanding-panel__state" data-testid="chat.projectUnderstanding.error" role="alert">
          <p>{loadError}</p>
          <CommandButton type="button" variant="secondary" testId="chat.projectUnderstanding.retryLoad" onClick={retryLoad}>
            Retry project context
          </CommandButton>
        </section>
      ) : null}

      {hasUnresolvedMutation ? (
        <section className="project-understanding-panel__delivery" role="alert" data-testid="chat.projectUnderstanding.deliveryUnresolved">
          <strong>Project-context delivery is unresolved.</strong>
          <p>Replay the exact change before editing another fact.</p>
          <CommandButton
            type="button"
            variant="secondary"
            testId="chat.projectUnderstanding.retryMutation"
            disabled={isMutating}
            onClick={() => void retryPendingMutation()}
          >
            {isMutating ? 'Retrying' : 'Retry exact change'}
          </CommandButton>
        </section>
      ) : null}

      {mutationError ? <p className="state-error" data-testid="chat.projectUnderstanding.mutationError">{mutationError}</p> : null}

      {model ? (
        <ProjectUnderstandingContents
          model={model}
          actionsDisabled={isMutating || hasUnresolvedMutation}
          onEditFact={editFact}
          onConfirmFact={confirmFact}
          onSetFactLock={setFactLock}
          onResolveConflict={resolveConflict}
          onAcceptRename={acceptRenameProposal}
        />
      ) : null}
    </aside>
  );
}

function ProjectUnderstandingContents({
  model,
  actionsDisabled,
  onEditFact,
  onConfirmFact,
  onSetFactLock,
  onResolveConflict,
  onAcceptRename
}: {
  model: ProjectUnderstandingReadModel;
  actionsDisabled: boolean;
  onEditFact: (factKey: string, value: string) => Promise<boolean>;
  onConfirmFact: (factKey: string) => Promise<boolean>;
  onSetFactLock: (factKey: string, userLocked: boolean) => Promise<boolean>;
  onResolveConflict: (factKey: string, conflictId: string, value: string) => Promise<boolean>;
  onAcceptRename: (proposalId: string) => Promise<boolean>;
}) {
  const pendingRename = model.pendingRenameProposal;
  const openConflicts = model.conflicts.filter((conflict) => conflict.status === 'Open');

  return (
    <>
      <section className="project-understanding-panel__section" data-testid="chat.projectUnderstanding.summary">
        <div className="project-understanding-panel__section-heading">
          <div>
            <p className="eyebrow">Project understanding</p>
            <h4>{model.projectName}</h4>
          </div>
          <StatusBadge status="info" data-testid="chat.projectUnderstanding.revision">Revision {model.revision}</StatusBadge>
        </div>
        {model.facts.length === 0 ? (
          <p className="project-understanding-panel__empty" data-testid="chat.projectUnderstanding.empty">
            No durable product facts have been captured yet. Add a confirmed value here or continue shaping the idea with the Business Analyst.
          </p>
        ) : null}
        {factGroupOrder.map((state) => {
          const facts = model.facts.filter((fact) => fact.state === state);
          const unknownKeys = state === 'Unknown'
            ? factKeys.filter((key) => !model.facts.some((fact) => fact.key === key))
            : [];
          return facts.length > 0 || unknownKeys.length > 0 ? (
              <section className="project-understanding-panel__fact-group" key={state} data-testid={`chat.projectUnderstanding.group.${state.toLowerCase()}`}>
                <h5>{state}</h5>
                {facts.map((fact) => (
                  <FactCard
                    key={`${fact.key}:${fact.revision}:${fact.value}:${fact.userLocked}`}
                    fact={fact}
                    disabled={actionsDisabled}
                    onEdit={onEditFact}
                    onConfirm={onConfirmFact}
                    onSetLock={onSetFactLock}
                  />
                ))}
                {unknownKeys.map((factKey) => (
                  <UnknownFactCard
                    key={factKey}
                    factKey={factKey}
                    disabled={actionsDisabled}
                    onEdit={onEditFact}
                  />
                ))}
              </section>
          ) : null;
        })}
      </section>

      {openConflicts.length > 0 ? (
        <section className="project-understanding-panel__section" data-testid="chat.projectUnderstanding.conflicts">
          <p className="eyebrow">Needs your decision</p>
          <h4>Conflicting intent</h4>
          {openConflicts.map((conflict) => (
            <ConflictCard
              key={conflict.conflictId}
              conflict={conflict}
              fact={model.facts.find((candidate) => candidate.key === conflict.factKey) ?? null}
              disabled={actionsDisabled}
              onResolve={onResolveConflict}
            />
          ))}
        </section>
      ) : null}

      {model.openQuestions.length > 0 ? (
        <section className="project-understanding-panel__section" data-testid="chat.projectUnderstanding.questions">
          <p className="eyebrow">Open questions</p>
          <ul>{model.openQuestions.map((question) => <li key={question}>{question}</li>)}</ul>
        </section>
      ) : null}

      {pendingRename ? (
        <section className="project-understanding-panel__section project-understanding-panel__rename" data-testid="chat.projectUnderstanding.renameProposal">
          <p className="eyebrow">Suggested project name</p>
          <h4>{pendingRename.proposedName}</h4>
          <p>{pendingRename.evidenceSummary}</p>
          <SourceMessageLinks sourceMessageIds={pendingRename.sourceMessageIds} />
          <p className="project-understanding-panel__boundary">
            The Business Analyst proposed this name. Your acceptance changes the canonical project name.
          </p>
          <CommandButton
            type="button"
            variant="primary"
            testId="chat.projectUnderstanding.acceptRename"
            disabled={actionsDisabled}
            onClick={() => void onAcceptRename(pendingRename.proposalId)}
          >
            Accept rename
          </CommandButton>
        </section>
      ) : null}

      <OperationalStatus model={model} />
    </>
  );
}

function FactCard({
  fact,
  disabled,
  onEdit,
  onConfirm,
  onSetLock
}: {
  fact: ProjectUnderstandingFact;
  disabled: boolean;
  onEdit: (factKey: string, value: string) => Promise<boolean>;
  onConfirm: (factKey: string) => Promise<boolean>;
  onSetLock: (factKey: string, userLocked: boolean) => Promise<boolean>;
}) {
  const [isEditing, setIsEditing] = useState(false);
  const [draft, setDraft] = useState(fact.value);
  const testKey = fact.key.toLowerCase();

  return (
    <article className="project-understanding-fact" data-testid={`chat.projectUnderstanding.fact.${testKey}`}>
      <header>
        <strong>{factLabels[fact.key] ?? splitPascalCase(fact.key)}</strong>
        <span className="project-understanding-fact__badges">
          <StatusBadge status={factStateTone(fact.state)}>{fact.state}</StatusBadge>
          {fact.userLocked ? <StatusBadge status="info">Locked by you</StatusBadge> : null}
        </span>
      </header>
      {isEditing ? (
        <label className="project-understanding-fact__editor">
          <span>Edit confirmed value</span>
          <textarea
            value={draft}
            maxLength={4_000}
            disabled={disabled}
            data-testid={`chat.projectUnderstanding.fact.${testKey}.input`}
            onChange={(event) => setDraft(event.target.value)}
          />
        </label>
      ) : (
        <p className="project-understanding-fact__value">{fact.value}</p>
      )}
      <p className="project-understanding-fact__evidence">{fact.evidenceSummary}</p>
      <p className="project-understanding-fact__author">
        {factAuthorLabel(fact)} · fact revision {fact.revision}
      </p>
      <SourceMessageLinks sourceMessageIds={fact.sourceMessageIds} />
      <div className="project-understanding-fact__actions">
        {isEditing ? (
          <>
            <CommandButton
              type="button"
              variant="primary"
              testId={`chat.projectUnderstanding.fact.${testKey}.save`}
              disabled={disabled || draft.trim().length === 0}
              onClick={() => void onEdit(fact.key, draft).then((saved) => {
                if (saved) setIsEditing(false);
              })}
            >
              Save confirmed value
            </CommandButton>
            <CommandButton type="button" variant="subtle" disabled={disabled} onClick={() => { setDraft(fact.value); setIsEditing(false); }}>
              Cancel
            </CommandButton>
          </>
        ) : (
          <>
            <CommandButton
              type="button"
              variant="subtle"
              testId={`chat.projectUnderstanding.fact.${testKey}.edit`}
              disabled={disabled}
              onClick={() => setIsEditing(true)}
            >
              Edit
            </CommandButton>
            {fact.state === 'Inferred' ? (
              <CommandButton
                type="button"
                variant="secondary"
                testId={`chat.projectUnderstanding.fact.${testKey}.confirm`}
                disabled={disabled}
                onClick={() => void onConfirm(fact.key)}
              >
                Confirm
              </CommandButton>
            ) : null}
            <CommandButton
              type="button"
              variant="subtle"
              testId={`chat.projectUnderstanding.fact.${testKey}.${fact.userLocked ? 'unlock' : 'lock'}`}
              disabled={disabled}
              onClick={() => void onSetLock(fact.key, !fact.userLocked)}
            >
              {fact.userLocked ? 'Unlock' : 'Lock'}
            </CommandButton>
          </>
        )}
      </div>
    </article>
  );
}

function UnknownFactCard({
  factKey,
  disabled,
  onEdit
}: {
  factKey: string;
  disabled: boolean;
  onEdit: (factKey: string, value: string) => Promise<boolean>;
}) {
  const [isEditing, setIsEditing] = useState(false);
  const [draft, setDraft] = useState('');
  const testKey = factKey.toLowerCase();

  return (
    <article className="project-understanding-fact project-understanding-fact--unknown" data-testid={`chat.projectUnderstanding.fact.${testKey}`}>
      <header>
        <strong>{factLabels[factKey] ?? splitPascalCase(factKey)}</strong>
        <span className="project-understanding-fact__badges">
          <StatusBadge status="neutral">Unknown</StatusBadge>
        </span>
      </header>
      {isEditing ? (
        <label className="project-understanding-fact__editor">
          <span>Add confirmed value</span>
          <textarea
            value={draft}
            maxLength={4_000}
            disabled={disabled}
            data-testid={`chat.projectUnderstanding.fact.${testKey}.input`}
            onChange={(event) => setDraft(event.target.value)}
          />
        </label>
      ) : (
        <p className="project-understanding-fact__value project-understanding-fact__unknown-value">
          No durable value captured.
        </p>
      )}
      <div className="project-understanding-fact__actions">
        {isEditing ? (
          <>
            <CommandButton
              type="button"
              variant="primary"
              testId={`chat.projectUnderstanding.fact.${testKey}.save`}
              disabled={disabled || draft.trim().length === 0}
              onClick={() => void onEdit(factKey, draft).then((saved) => {
                if (saved) setIsEditing(false);
              })}
            >
              Save confirmed value
            </CommandButton>
            <CommandButton type="button" variant="subtle" disabled={disabled} onClick={() => { setDraft(''); setIsEditing(false); }}>
              Cancel
            </CommandButton>
          </>
        ) : (
          <CommandButton
            type="button"
            variant="secondary"
            testId={`chat.projectUnderstanding.fact.${testKey}.add`}
            disabled={disabled}
            onClick={() => setIsEditing(true)}
          >
            Add confirmed value
          </CommandButton>
        )}
      </div>
    </article>
  );
}

function ConflictCard({
  conflict,
  fact,
  disabled,
  onResolve
}: {
  conflict: ProjectUnderstandingConflict;
  fact: ProjectUnderstandingFact | null;
  disabled: boolean;
  onResolve: (factKey: string, conflictId: string, value: string) => Promise<boolean>;
}) {
  const currentValue = fact?.value ?? conflict.currentValue;
  return (
    <article className="project-understanding-conflict" data-testid={`chat.projectUnderstanding.conflict.${conflict.conflictId}`}>
      <h5>{factLabels[conflict.factKey] ?? splitPascalCase(conflict.factKey)}</h5>
      <dl>
        <div><dt>Current value</dt><dd>{currentValue}</dd></div>
        <div><dt>New proposal</dt><dd>{conflict.proposedValue}</dd></div>
      </dl>
      <p>{conflict.evidenceSummary}</p>
      <SourceMessageLinks sourceMessageIds={conflict.sourceMessageIds} />
      <div className="project-understanding-fact__actions">
        <CommandButton
          type="button"
          variant="secondary"
          testId={`chat.projectUnderstanding.conflict.${conflict.conflictId}.keep`}
          disabled={disabled}
          onClick={() => void onResolve(conflict.factKey, conflict.conflictId, currentValue)}
        >
          Keep current value
        </CommandButton>
        <CommandButton
          type="button"
          variant="primary"
          testId={`chat.projectUnderstanding.conflict.${conflict.conflictId}.useProposed`}
          disabled={disabled}
          onClick={() => void onResolve(conflict.factKey, conflict.conflictId, conflict.proposedValue)}
        >
          Use proposed value
        </CommandButton>
      </div>
    </article>
  );
}

function OperationalStatus({ model }: { model: ProjectUnderstandingReadModel }) {
  const operational = model.operationalProjections;
  return (
    <section className="project-understanding-panel__section project-understanding-operational" data-testid="chat.projectUnderstanding.operational">
      <p className="eyebrow">Read-only operational status</p>
      <h4>Observed by owning services</h4>
      <p className="project-understanding-panel__boundary">These observations cannot be edited or confirmed from Workshop.</p>
      <OperationalRow
        label="Project phase"
        value={formatState(operational.projectLifecyclePhase)}
        authority={operational.projectLifecycleAuthority}
        testId="chat.projectUnderstanding.operational.lifecycle"
      />
      <OperationalRow
        label="Execution readiness"
        value={formatState(operational.executionReadiness)}
        authority={operational.executionReadinessAuthority}
        testId="chat.projectUnderstanding.operational.readiness"
      />
      <OperationalRow
        label="Repository"
        value={operational.repositoryBinding === null ? 'Not configured' : 'Configured'}
        authority="RepositoryBinding"
        testId="chat.projectUnderstanding.operational.repository"
      />
    </section>
  );
}

function OperationalRow({ label, value, authority, testId }: { label: string; value: string; authority: string; testId: string }) {
  return (
    <dl className="project-understanding-operational__row" data-testid={testId}>
      <dt>{label}</dt>
      <dd><strong>{value}</strong><small>{authority} · read only</small></dd>
    </dl>
  );
}

function SourceMessageLinks({ sourceMessageIds }: { sourceMessageIds: number[] }) {
  if (sourceMessageIds.length === 0) {
    return <p className="project-understanding-fact__sources">No conversation source recorded.</p>;
  }

  return (
    <p className="project-understanding-fact__sources">
      <span>Sources:</span>{' '}
      {sourceMessageIds.map((messageId, index) => (
        <span key={messageId}>
          {index > 0 ? ', ' : null}
          <button type="button" onClick={() => scrollToMessage(messageId)}>Message #{messageId}</button>
        </span>
      ))}
    </p>
  );
}

function scrollToMessage(messageId: number) {
  const element = document.querySelector<HTMLElement>(`[data-message-id$="-${messageId}"]`);
  element?.scrollIntoView({ behavior: 'smooth', block: 'center' });
  element?.focus({ preventScroll: true });
}

function factStateTone(state: string): 'neutral' | 'info' | 'ready' | 'warning' {
  if (state === 'Confirmed') return 'ready';
  if (state === 'Inferred') return 'info';
  if (state === 'Conflicted') return 'warning';
  return 'neutral';
}

function factAuthorLabel(fact: ProjectUnderstandingFact) {
  if (fact.authorKind === 'Actor') return 'Confirmed by a project member';
  if (fact.state === 'Inferred') return 'Inferred by the Business Analyst';
  if (fact.state === 'Confirmed') return 'Captured by the Business Analyst from explicit user evidence';
  return 'Captured by the Business Analyst';
}

function splitPascalCase(value: string) {
  return value.replace(/([a-z])([A-Z])/g, '$1 $2');
}

function formatState(value: string) {
  return splitPascalCase(value).replace(/\bnot configured\b/i, 'Not configured');
}
