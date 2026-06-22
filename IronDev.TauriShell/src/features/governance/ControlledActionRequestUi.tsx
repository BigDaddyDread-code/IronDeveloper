import type { FormEvent } from 'react';
import { StatusBadge } from '../../design-system/StatusBadge';
import { Surface } from '../../design-system/Surface';
import {
  actionRequestBoundaryFields,
  actionRequestBoundaryWarnings,
  actionRequestCommonWarning,
  controlledActionRequestKinds,
  controlledActionRequestLabels,
  controlledActionRequestWarnings,
  type ActionRequestDraft,
  type ActionRequestUiLoadStatus,
  type ActionRequestUiModel
} from './ControlledActionRequestTypes';

interface ControlledActionRequestUiProps {
  draft: ActionRequestDraft;
  status: ActionRequestUiLoadStatus;
  model: ActionRequestUiModel;
  onDraftChange: (draft: ActionRequestDraft) => void;
  onSubmit: () => void;
}

export function ControlledActionRequestUi({
  draft,
  status,
  model,
  onDraftChange,
  onSubmit
}: ControlledActionRequestUiProps) {
  const selectedWarning = controlledActionRequestWarnings[draft.requestKind];
  const submitLabel = controlledActionRequestLabels[draft.requestKind];

  function update<K extends keyof ActionRequestDraft>(key: K, value: ActionRequestDraft[K]) {
    onDraftChange({ ...draft, [key]: value });
  }

  function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    onSubmit();
  }

  return (
    <main className="action-request-ui-workspace" data-testid="action-request.workspace">
      <section className="workspace-frame">
        <div className="workspace-frame__header">
          <div>
            <p className="eyebrow">Controlled action request</p>
            <h1>Controlled Action Request</h1>
            <p className="lede">UI may request authority. It cannot be authority.</p>
          </div>
          <StatusBadge status={status === 'submitted' ? 'ready' : status === 'error' || status === 'rejected' ? 'warning' : 'neutral'}>
            {model.response?.state ?? 'Request only'}
          </StatusBadge>
        </div>

        <div className="action-request-ui-banner" data-testid="action-request.warningBanner">
          {actionRequestBoundaryWarnings.map((warning) => (
            <span key={warning}>{warning}</span>
          ))}
        </div>

        <Surface className="action-request-ui-kind-panel" testId="action-request.supportedKinds">
          <div className="action-request-ui-panel-header">
            <h2>Request kind</h2>
            <span>{draft.requestKind}</span>
          </div>
          <div className="action-request-ui-kind-grid">
            {controlledActionRequestKinds.map((kind) => (
              <div key={kind} className="action-request-ui-kind-control">
                <button
                  type="button"
                  className={kind === draft.requestKind ? 'is-selected' : ''}
                  aria-pressed={kind === draft.requestKind}
                  onClick={() => update('requestKind', kind)}
                >
                  {controlledActionRequestLabels[kind]}
                </button>
                <p>{actionRequestCommonWarning}</p>
              </div>
            ))}
          </div>
        </Surface>

        <form className="action-request-ui-form" data-testid="action-request.form" onSubmit={submit}>
          <Surface className="action-request-ui-panel" testId="action-request.commonFields">
            <div className="action-request-ui-panel-header">
              <h2>Request scope</h2>
              <span>{selectedWarning}</span>
            </div>
            <div className="action-request-ui-field-grid">
              <Field label="Request ID" value={draft.requestId} onChange={(value) => update('requestId', value)} testId="action-request.field.requestId" />
              <Field label="Operation ID" value={draft.operationId} onChange={(value) => update('operationId', value)} testId="action-request.field.operationId" />
              <Field label="Repository" value={draft.repository} onChange={(value) => update('repository', value)} testId="action-request.field.repository" />
              <Field label="Branch" value={draft.branch} onChange={(value) => update('branch', value)} testId="action-request.field.branch" />
              <Field label="Run ID" value={draft.runId} onChange={(value) => update('runId', value)} testId="action-request.field.runId" />
              <Field label="Human intent" value={draft.humanIntent} onChange={(value) => update('humanIntent', value)} testId="action-request.field.humanIntent" />
            </div>
            <div className="action-request-ui-field-grid">
              <TextArea label="Evidence refs" value={draft.evidenceRefsText} onChange={(value) => update('evidenceRefsText', value)} testId="action-request.field.evidenceRefs" />
              <TextArea label="Receipt refs" value={draft.receiptRefsText} onChange={(value) => update('receiptRefsText', value)} testId="action-request.field.receiptRefs" />
            </div>
          </Surface>

          {draft.requestKind === 'SourceApply' ? (
            <Surface className="action-request-ui-panel" testId="action-request.sourceApplyForm">
              <KindHeader warning={controlledActionRequestWarnings.SourceApply} />
              <div className="action-request-ui-field-grid">
                <Field label="Patch package ID" value={draft.patchPackageId} onChange={(value) => update('patchPackageId', value)} testId="action-request.field.patchPackageId" />
                <Field label="Patch hash" value={draft.patchHash} onChange={(value) => update('patchHash', value)} testId="action-request.field.patchHash" />
                <TextArea label="Proposed file paths" value={draft.proposedFilePathsText} onChange={(value) => update('proposedFilePathsText', value)} testId="action-request.field.proposedFilePaths" />
              </div>
            </Surface>
          ) : null}

          {draft.requestKind === 'Commit' ? (
            <Surface className="action-request-ui-panel" testId="action-request.commitForm">
              <KindHeader warning={controlledActionRequestWarnings.Commit} />
              <div className="action-request-ui-field-grid">
                <Field label="Source apply receipt ref" value={draft.sourceApplyReceiptRef} onChange={(value) => update('sourceApplyReceiptRef', value)} testId="action-request.field.sourceApplyReceiptRef" />
                <Field label="Commit package ID" value={draft.commitPackageId} onChange={(value) => update('commitPackageId', value)} testId="action-request.field.commitPackageId" />
                <Field label="Commit message evidence ref" value={draft.commitMessageEvidenceRef} onChange={(value) => update('commitMessageEvidenceRef', value)} testId="action-request.field.commitMessageEvidenceRef" />
                <TextArea label="Expected changed files" value={draft.proposedFilePathsText} onChange={(value) => update('proposedFilePathsText', value)} testId="action-request.field.commitFiles" />
              </div>
            </Surface>
          ) : null}

          {draft.requestKind === 'Push' ? (
            <Surface className="action-request-ui-panel" testId="action-request.pushForm">
              <KindHeader warning={controlledActionRequestWarnings.Push} />
              <div className="action-request-ui-field-grid">
                <Field label="Commit SHA" value={draft.commitSha} onChange={(value) => update('commitSha', value)} testId="action-request.field.commitSha" />
                <Field label="Remote target" value={draft.remoteTarget} onChange={(value) => update('remoteTarget', value)} testId="action-request.field.remoteTarget" />
                <Field label="Push intent" value={draft.pushIntent} onChange={(value) => update('pushIntent', value)} testId="action-request.field.pushIntent" />
              </div>
            </Surface>
          ) : null}

          {draft.requestKind === 'DraftPullRequest' ? (
            <Surface className="action-request-ui-panel" testId="action-request.draftPrForm">
              <KindHeader warning={controlledActionRequestWarnings.DraftPullRequest} />
              <div className="action-request-ui-field-grid">
                <Field label="Head branch" value={draft.headBranch} onChange={(value) => update('headBranch', value)} testId="action-request.field.headBranch" />
                <Field label="Base branch" value={draft.baseBranch} onChange={(value) => update('baseBranch', value)} testId="action-request.field.baseBranch" />
                <Field label="Pushed commit SHA" value={draft.pushedCommitSha} onChange={(value) => update('pushedCommitSha', value)} testId="action-request.field.pushedCommitSha" />
                <Field label="PR title/body package ref" value={draft.pullRequestTextPackageRef} onChange={(value) => update('pullRequestTextPackageRef', value)} testId="action-request.field.pullRequestTextPackageRef" />
              </div>
            </Surface>
          ) : null}

          {draft.requestKind === 'Rollback' ? (
            <Surface className="action-request-ui-panel" testId="action-request.rollbackForm">
              <KindHeader warning={controlledActionRequestWarnings.Rollback} />
              <div className="action-request-ui-field-grid">
                <Field label="Rollback target receipt ref" value={draft.rollbackTargetReceiptRef} onChange={(value) => update('rollbackTargetReceiptRef', value)} testId="action-request.field.rollbackTargetReceiptRef" />
                <Field label="Source apply receipt ref" value={draft.sourceApplyReceiptRef} onChange={(value) => update('sourceApplyReceiptRef', value)} testId="action-request.field.rollbackSourceApplyReceiptRef" />
                <TextArea label="Rollback scope paths" value={draft.rollbackScopePathsText} onChange={(value) => update('rollbackScopePathsText', value)} testId="action-request.field.rollbackScopePaths" />
              </div>
            </Surface>
          ) : null}

          <Surface className="action-request-ui-submit-panel" testId="action-request.submitPanel">
            <p>{actionRequestCommonWarning}</p>
            <button type="submit" disabled={status === 'submitting'} data-testid="action-request.submit">
              {status === 'submitting' ? `${submitLabel}...` : submitLabel}
            </button>
          </Surface>
        </form>

        <ResponsePanel response={model.response} message={model.message} />
      </section>
    </main>
  );
}

function KindHeader({ warning }: { warning: string }) {
  return (
    <div className="action-request-ui-panel-header">
      <h2>Required request evidence</h2>
      <span>{warning}</span>
    </div>
  );
}

function Field({ label, value, onChange, testId }: { label: string; value: string; onChange: (value: string) => void; testId: string }) {
  return (
    <label data-testid={testId}>
      <span>{label}</span>
      <input value={value} onChange={(event) => onChange(event.target.value)} />
    </label>
  );
}

function TextArea({ label, value, onChange, testId }: { label: string; value: string; onChange: (value: string) => void; testId: string }) {
  return (
    <label data-testid={testId}>
      <span>{label}</span>
      <textarea value={value} onChange={(event) => onChange(event.target.value)} rows={4} />
    </label>
  );
}

function ResponsePanel({ response, message }: { response: ActionRequestUiModel['response']; message: string }) {
  if (!response) {
    return (
      <Surface className="action-request-ui-response" testId="action-request.response.empty">
        <p>{message}</p>
      </Surface>
    );
  }

  return (
    <Surface className="action-request-ui-response" testId="action-request.response">
      <div className="action-request-ui-panel-header">
        <div>
          <p className="eyebrow">Backend response</p>
          <h2>{response.state}</h2>
        </div>
        <StatusBadge status={response.requestCreated ? 'ready' : 'warning'}>
          RequestCreated = {String(response.requestCreated)}
        </StatusBadge>
      </div>
      <div className="action-request-ui-response-flags">
        <Metadata label="Request ID" value={response.requestId} testId="action-request.response.requestId" />
        <Metadata label="Request kind" value={response.requestKind} testId="action-request.response.requestKind" />
        <Metadata label="ExecutionStarted" value={String(response.executionStarted)} testId="action-request.response.executionStarted" />
        <Metadata label="SourceMutated" value={String(response.sourceMutated)} testId="action-request.response.sourceMutated" />
        <Metadata label="WorkflowContinued" value={String(response.workflowContinued)} testId="action-request.response.workflowContinued" />
      </div>
      <div className="action-request-ui-grid">
        <ListPanel title="Blocked reasons" items={response.blockedReasons} testId="action-request.blockedReasons" />
        <ListPanel title="Missing evidence" items={response.missingEvidence} testId="action-request.missingEvidence" />
        <ListPanel title="Next safe actions" items={response.nextSafeActions} testId="action-request.nextSafeActions" />
        <ListPanel title="Forbidden actions" items={response.forbiddenActions} testId="action-request.forbiddenActions" />
        <ListPanel title="Evidence refs" items={response.evidenceRefs} testId="action-request.evidenceRefs" />
        <ListPanel title="Receipt refs" items={response.receiptRefs} testId="action-request.receiptRefs" />
        <ListPanel title="Authority warnings" items={response.authorityWarnings} testId="action-request.authorityWarnings" />
      </div>
      <Surface className="action-request-ui-boundary" testId="action-request.boundary">
        <div className="action-request-ui-boundary-grid">
          {actionRequestBoundaryFields.map(([label, key, expected]) => {
            const value = response.boundary?.[key] === true;
            return (
              <div key={label} data-testid={`action-request.boundary.${label}`}>
                <dt>{label}</dt>
                <dd className={value === expected ? 'is-expected' : 'is-violation'}>{String(value)}</dd>
              </div>
            );
          })}
        </div>
      </Surface>
    </Surface>
  );
}

function Metadata({ label, value, testId }: { label: string; value: string; testId: string }) {
  return (
    <div data-testid={testId}>
      <dt>{label}</dt>
      <dd>{value}</dd>
    </div>
  );
}

function ListPanel({ title, items, testId }: { title: string; items: readonly string[]; testId: string }) {
  return (
    <div className="action-request-ui-list-panel" data-testid={testId}>
      <h3>{title}</h3>
      {items.length === 0 ? (
        <p className="state-muted">No {title.toLowerCase()} returned.</p>
      ) : (
        <ul>
          {items.map((item, index) => (
            <li key={`${item}-${index}`}>{item}</li>
          ))}
        </ul>
      )}
    </div>
  );
}
