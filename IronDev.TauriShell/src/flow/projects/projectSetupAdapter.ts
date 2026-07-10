import type { ProjectProvisioningReadinessUi, ProvisioningCheckUi } from '../../api/types';
import {
  setupCheckCodes,
  type ProjectSetupCheckModel,
  type ProjectSetupModel,
  type SetupRowStatus
} from './projectSetupModel';

const frontStageCodes = new Set<string>([
  setupCheckCodes.repositoryAccess,
  setupCheckCodes.rootSafety,
  setupCheckCodes.buildCommand,
  setupCheckCodes.testCommand,
  setupCheckCodes.projectProfile
]);

function statusFor(check: ProvisioningCheckUi): { status: SetupRowStatus; label: string } {
  if (check.state === 'Confirmed') {
    return { status: 'complete', label: 'Confirmed' };
  }
  if (check.state === 'NotEvaluated') {
    return { status: 'unavailable', label: 'Unavailable' };
  }
  if (check.state === 'Detected') {
    return { status: 'checking', label: 'Checking' };
  }
  return { status: 'attention', label: 'Needs attention' };
}

function conciseLabel(check: ProvisioningCheckUi): string {
  switch (check.code) {
    case setupCheckCodes.repositoryAccess:
    case setupCheckCodes.rootSafety:
      return 'Repository';
    case setupCheckCodes.buildCommand:
      return 'Build command';
    case setupCheckCodes.testCommand:
      return 'Test command';
    case setupCheckCodes.projectProfile:
      return 'Project structure';
    default:
      return check.label || check.name || 'Additional setup';
  }
}

function adaptCheck(check: ProvisioningCheckUi): ProjectSetupCheckModel {
  const status = statusFor(check);
  return {
    code: check.code,
    label: conciseLabel(check),
    state: check.state,
    status: status.status,
    statusLabel: status.label,
    summary: check.summary || check.evidence,
    evidence: check.evidence,
    remedy: check.remedy,
    blocking: check.blocking,
    detectedValue: check.detectedValue || '',
    raw: check
  };
}

export function adaptProjectSetup(readiness: ProjectProvisioningReadinessUi): ProjectSetupModel {
  const checks = readiness.checks.map(adaptCheck);
  const currentCheck = readiness.nextAction.checkCode
    ? checks.find((check) => check.code === readiness.nextAction.checkCode) ?? null
    : null;
  const checklist = checks.filter((check) => frontStageCodes.has(check.code) || check.blocking);

  return {
    source: readiness,
    isReady: readiness.isReady,
    blockedCount: readiness.blockedCount,
    checks,
    checklist,
    currentCheck,
    nextAction: readiness.nextAction
  };
}
