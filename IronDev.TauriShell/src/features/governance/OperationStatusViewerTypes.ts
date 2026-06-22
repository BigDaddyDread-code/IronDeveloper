import type { FrontendOperationStatusReadModel, FrontendReadBoundary } from '../../api/types';

export type OperationStatusViewerLoadStatus = 'idle' | 'loading' | 'ready' | 'missing' | 'error';

export interface OperationStatusViewerModel extends FrontendOperationStatusReadModel {
  envelopeBoundary?: FrontendReadBoundary | null;
  envelopeWarnings?: string[];
}

export interface OperationStatusViewerSection {
  id: string;
  title: string;
  emptyText: string;
  items: string[];
}

export const operationStatusViewerBoundaryWarnings = [
  'The first frontend is a window, not a cockpit.',
  'UI is not authority.',
  'Display is not execution.',
  'Next safe action is guidance, not a button.',
  'Evidence refs are not approval.',
  'Receipt refs are not authority.'
] as const;

export const operationStatusViewerForbiddenControlLabels = [
  'Apply',
  'Approve',
  'Accept approval',
  'Satisfy policy',
  'Run',
  'Execute',
  'Retry',
  'Resume',
  'Rollback',
  'Commit',
  'Push',
  'Create PR',
  'Update PR',
  'Ready for review',
  'Merge',
  'Release',
  'Deploy',
  'Promote memory',
  'Continue workflow'
] as const;

export const operationStatusViewerBoundaryFields = [
  ['ReadOnly', 'readOnly', true],
  ['StatusOnly', 'statusOnly', true],
  ['CanCreateApproval', 'canCreateApproval', false],
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
] as const satisfies ReadonlyArray<readonly [string, keyof FrontendReadBoundary, boolean]>;
