import type { ProjectProvisioningReadinessUi, ProvisioningCheckUi } from '../../api/types';

export const setupCheckCodes = {
  repositoryAccess: 'RepositoryAccess',
  rootSafety: 'RootSafety',
  buildCommand: 'BuildCommand',
  testCommand: 'TestCommand',
  projectProfile: 'ProjectProfile',
  codeIndex: 'CodeIndex',
  builderApplyPermission: 'BuilderApplyPermission'
} as const;

export const setupActionKinds = {
  changeRepository: 'ChangeRepository',
  confirmBuildCommand: 'ConfirmBuildCommand',
  confirmTestCommand: 'ConfirmTestCommand',
  confirmProjectProfile: 'ConfirmProjectProfile',
  recheckSetup: 'RecheckSetup',
  resolveAdditionalSetup: 'ResolveAdditionalSetup',
  indexProject: 'IndexProject',
  enableBuilderApply: 'EnableBuilderApply',
  disableBuilderApply: 'DisableBuilderApply',
  openBoard: 'OpenBoard'
} as const;

export type SetupRowStatus = 'complete' | 'attention' | 'checking' | 'unavailable';

export interface ProjectSetupCheckModel {
  code: string;
  label: string;
  state: string;
  status: SetupRowStatus;
  statusLabel: string;
  summary: string;
  evidence: string;
  remedy: string;
  blocking: boolean;
  detectedValue: string;
  raw: ProvisioningCheckUi;
}

export interface ProjectSetupModel {
  source: ProjectProvisioningReadinessUi;
  isReady: boolean;
  blockedCount: number;
  checks: ProjectSetupCheckModel[];
  checklist: ProjectSetupCheckModel[];
  currentCheck: ProjectSetupCheckModel | null;
  nextAction: ProjectProvisioningReadinessUi['nextAction'];
}
