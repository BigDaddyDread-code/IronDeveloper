import type {
  ControlledActionRequestCreateResponse,
  ControlledActionRequestKind,
  FrontendActionRequestBoundary
} from '../../api/types';

export type ActionRequestUiLoadStatus = 'idle' | 'submitting' | 'submitted' | 'rejected' | 'error';

export interface ActionRequestUiModel {
  response: ControlledActionRequestCreateResponse | null;
  message: string;
}

export interface ActionRequestDraft {
  requestKind: ControlledActionRequestKind;
  requestId: string;
  operationId: string;
  repository: string;
  branch: string;
  runId: string;
  humanIntent: string;
  evidenceRefsText: string;
  receiptRefsText: string;
  patchPackageId: string;
  patchHash: string;
  proposedFilePathsText: string;
  sourceApplyReceiptRef: string;
  commitPackageId: string;
  commitMessageEvidenceRef: string;
  commitSha: string;
  remoteTarget: string;
  pushIntent: string;
  headBranch: string;
  baseBranch: string;
  pushedCommitSha: string;
  draftPullRequestPackageId: string;
  pullRequestTextPackageRef: string;
  rollbackTargetReceiptRef: string;
  rollbackScopePathsText: string;
}

export const controlledActionRequestKinds = [
  'SourceApply',
  'Commit',
  'Push',
  'DraftPullRequest',
  'Rollback'
] as const satisfies readonly ControlledActionRequestKind[];

export const controlledActionRequestLabels: Record<ControlledActionRequestKind, string> = {
  SourceApply: 'Request source apply',
  Commit: 'Request commit',
  Push: 'Request push',
  DraftPullRequest: 'Request draft PR',
  Rollback: 'Request rollback'
};

export const controlledActionRequestWarnings: Record<ControlledActionRequestKind, string> = {
  SourceApply: 'Requesting source apply is not source apply.',
  Commit: 'Apply authority is not commit authority.',
  Push: 'Commit authority is not push authority.',
  DraftPullRequest: 'Push authority is not PR authority. Draft PR is not ready-for-review authority.',
  Rollback: 'Rollback is mutation. It does not get a free pass.'
};

export const actionRequestCommonWarning =
  'This creates a request only. Backend eligibility still decides. No action is executed from the UI.';

export const actionRequestBoundaryWarnings = [
  'UI may request authority. It cannot be authority.',
  'A request button asks for a key. It is not the key.',
  'A request is not approval.',
  'A request is not policy satisfaction.',
  'A request is not execution.',
  'Backend eligibility decides.',
  'Evidence refs are not approval.',
  'Receipt refs are not authority.',
  'Memory is not authority.',
  'UI text is not authority.',
  'Workflow continuation is forbidden in this PR.'
] as const;

export const actionRequestBoundaryFields = [
  ['CanCreateRequest', 'canCreateRequest', true],
  ['CanApprove', 'canApprove', false],
  ['CanAcceptApproval', 'canAcceptApproval', false],
  ['CanSatisfyPolicy', 'canSatisfyPolicy', false],
  ['CanExecute', 'canExecute', false],
  ['CanMutateSource', 'canMutateSource', false],
  ['CanRollback', 'canRollback', false],
  ['CanCommit', 'canCommit', false],
  ['CanPush', 'canPush', false],
  ['CanCreatePullRequest', 'canCreatePullRequest', false],
  ['CanMarkReadyForReview', 'canMarkReadyForReview', false],
  ['CanMerge', 'canMerge', false],
  ['CanRelease', 'canRelease', false],
  ['CanDeploy', 'canDeploy', false],
  ['CanPromoteMemory', 'canPromoteMemory', false],
  ['CanContinueWorkflow', 'canContinueWorkflow', false]
] as const satisfies ReadonlyArray<readonly [string, keyof FrontendActionRequestBoundary, boolean]>;
