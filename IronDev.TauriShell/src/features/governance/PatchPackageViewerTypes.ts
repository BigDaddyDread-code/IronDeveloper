import type {
  FrontendPatchPackageArtifactsReadModel,
  FrontendPatchPackageMetadataReadModel,
  FrontendReadBoundary
} from '../../api/types';

export type PatchPackageViewerLoadStatus = 'idle' | 'loading' | 'ready' | 'missing' | 'error';

export interface PatchPackageViewerModel {
  metadata: FrontendPatchPackageMetadataReadModel;
  artifacts: FrontendPatchPackageArtifactsReadModel;
  envelopeBoundary?: FrontendReadBoundary | null;
  envelopeWarnings?: string[];
}

export const patchPackageViewerBoundaryWarnings = [
  'Reviewable work must be easy to inspect before it is easy to mutate.',
  'Reading the patch is not permission to apply it.',
  'Patch package evidence is not source apply authority.',
  'Validation evidence is not approval.',
  'Receipt refs are not workflow continuation.',
  'UI text cannot approve, execute, or continue workflow.'
] as const;

export const patchPackageViewerForbiddenControlLabels = [
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

export const patchPackageViewerBoundaryFields = [
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
