export const forbiddenDependencyMarkers = [
  'ReleaseReadinessGateEvaluator',
  'ReleaseReadinessDecisionRecordStore',
  'ReleaseReadinessDecisionWriter',
  'GovernedReleaseGateService',
  'ReleaseApproval',
  'DeploymentApproval',
  'MergeApproval',
  'ReleaseExecutor',
  'GovernedWorkflowContinuationService',
  'WorkflowContinuationRunner',
  'WorkflowContinuationExecutor',
  'WorkflowTransitionRecordStore',
  'WorkflowTransitionStore',
  'CreateWorkflowTransitionRecord',
  'ControlledRollbackExecutor',
  'RollbackExecutor',
  'RollbackRunner',
  'RollbackRecoveryRunner',
  'RollbackAuditExecutor',
  'ControlledSourceApplyExecutor',
  'SourceApplyExecutor',
  'SourceApplyRunner',
  'SourceApplyDryRunExecutor',
  'PatchArtifactCreator',
  'PatchArtifactWriter',
  'ApprovalWriter',
  'AcceptedApprovalWriter',
  'PolicySatisfactionWriter',
  'IHostedService',
  'BackgroundService',
  'Scheduler',
  'AgentDispatch',
  'ModelProvider',
  'ToolInvoker',
  'PromoteMemory',
  'ActivateRetrieval',
  'SqlConnection',
  'IDbConnection',
  'Dapper',
  'HttpClient',
  'fetch(',
  'axios',
  'post(',
  'CLI mutation',
  'git commit',
  'git push',
  'gh pr'
] as const;

export const forbiddenActionLabels = [
  'Approve Release',
  'Approve Deployment',
  'Approve Merge',
  'Execute Release',
  'Mark Release Ready',
  'Create Release Decision',
  'Run Release Gate',
  'Approve Source Apply',
  'Run Dry-run',
  'Apply Source',
  'Apply Patch',
  'Approve Rollback',
  'Execute Rollback',
  'Retry Rollback',
  'Start Recovery',
  'Approve Continuation',
  'Continue Workflow',
  'Create Transition Record',
  'Retry Continuation',
  'Refresh Authority',
  'Reissue Evidence',
  'Run Git',
  'Create Pull Request',
  'Run Agent',
  'Call Model',
  'Run Tool'
] as const;

export const allowedCopyInspectionLabels = [
  'Copy Evidence References',
  'Copy Release Readiness Evidence ID',
  'Copy Release Readiness Evidence Hash',
  'Copy Release Readiness Report Hash',
  'Copy Workflow Continuation Evidence ID',
  'Copy Workflow Continuation Evidence Hash',
  'Copy Continuation Gate Hash',
  'Copy Rollback Evidence ID',
  'Copy Rollback Evidence Hash',
  'Copy Rollback Plan Hash',
  'Copy Review ID',
  'Copy Review Hash',
  'Copy Source Apply Request Hash',
  'Copy Patch Artifact ID',
  'Copy Patch Artifact Hash',
  'Copy Source Hash',
  'Copy Dry-run Receipt ID',
  'Copy Dry-run Receipt Hash',
  'Copy Policy Satisfaction ID',
  'Copy Accepted Approval ID'
] as const;

export const unsafePrivateRawMarkers = [
  'raw prompt',
  'raw completion',
  'raw tool output',
  'chain of thought',
  'private reasoning',
  'scratchpad',
  'password',
  'secret',
  'api key',
  'private key',
  'bearer',
  'entire patch',
  'patch payload',
  'raw patch',
  'raw diff',
  'full diff'
] as const;

export const forbiddenAuthorityMarkers = [
  'approved',
  'approval created',
  'release approved',
  'release authorized',
  'release executed',
  'release complete',
  'deployment approved',
  'deployment authorized',
  'merge approved',
  'ready to deploy',
  'safe to deploy',
  'safe to merge',
  'safe to release',
  'ready to release',
  'green to ship',
  'can execute',
  'can deploy',
  'can merge',
  'release readiness decided',
  'release gate passed',
  'release gate approved',
  'decision record created',
  'source apply approved',
  'source approved',
  'source applied',
  'source apply executed',
  'dry-run approved',
  'dry-run executed',
  'patch approved',
  'patch created by ui',
  'patch edited by ui',
  'rollback approved',
  'rollback authorized',
  'rollback executed',
  'rollback retried',
  'rollback recovered',
  'recovery complete',
  'workflow continuation approved',
  'continuation approved',
  'workflow continued',
  'workflow transition created',
  'workflow transition record created',
  'workflow mutated',
  'continuation complete',
  'authority refreshed',
  'evidence reissued',
  'git committed',
  'git pushed',
  'tag created',
  'pull request created'
] as const;

export const allowedNegativeBoundaryPhrases = [
  'not authority',
  'not backend truth',
  'not release approval',
  'not deployment approval',
  'not merge approval',
  'not release execution',
  'not release readiness decision',
  'not source apply',
  'not source apply approval',
  'not dry-run',
  'not rollback approval',
  'not rollback execution',
  'not workflow continuation',
  'not continuation approval',
  'not workflow mutation',
  'does not approve release',
  'does not approve deployment',
  'does not approve merge',
  'does not approve release deployment merge or execution',
  'does not approve source apply',
  'does not approve or apply source',
  'does not approve source apply execute dry-run apply source',
  'does not approve rollback execute rollback or continue workflow',
  'does not approve continuation continue workflow or create transition records',
  'does not approve continuation continue workflow create transition records',
  'does not decide readiness approve release or execute release',
  'does not decide readiness approve release approve deployment approve merge or execute release',
  'do not approve release',
  'cannot approve release',
  'does not execute release',
  'does not create patch artifacts run dry-run or apply source',
  'does not create or edit patch artifacts execute dry-run approve source apply apply source',
  'does not create rollback plans approve rollback execute rollback retry rollback',
  'does not apply source',
  'does not execute source apply',
  'does not execute dry-run',
  'does not execute dry-run or apply source',
  'does not execute dry-run approve source apply apply source',
  'does not execute rollback',
  'does not retry rollback',
  'does not recover automatically',
  'does not permit source apply',
  'does not permit dry-run or source apply',
  'does not permit rollback execution',
  'does not permit rollback execution retry recovery or workflow continuation',
  'does not permit workflow continuation',
  'does not continue workflow',
  'does not create transition record',
  'does not mutate workflow state',
  'does not refresh authority',
  'does not reissue evidence',
  'will not refresh authority',
  'will not retry rollback or declare recovery complete',
  'will not retry continuation or declare continuation complete',
  'will not start recovery source apply rollback or workflow continuation',
  'will not retry recover approve deploy merge execute or continue workflow',
  'cannot permit workflow continuation',
  'display only',
  'inspection only',
  'human review required'
] as const;

export const governanceEvidenceUiFileAllowList = [
  'src/features/governance/AcceptedApprovalPanel.tsx',
  'src/features/governance/AcceptedApprovalPanelRoute.tsx',
  'src/features/governance/PolicySatisfactionPanel.tsx',
  'src/features/governance/PolicySatisfactionPanelRoute.tsx',
  'src/features/governance/SourceApplyDryRunReceiptPanel.tsx',
  'src/features/governance/SourceApplyDryRunReceiptPanelRoute.tsx',
  'src/features/governance/PatchArtifactPanel.tsx',
  'src/features/governance/PatchArtifactPanelRoute.tsx',
  'src/features/governance/SourceApplyReviewPanel.tsx',
  'src/features/governance/SourceApplyReviewPanelRoute.tsx',
  'src/features/governance/RollbackEvidencePanel.tsx',
  'src/features/governance/RollbackEvidencePanelRoute.tsx',
  'src/features/governance/WorkflowContinuationEvidencePanel.tsx',
  'src/features/governance/WorkflowContinuationEvidencePanelRoute.tsx',
  'src/features/governance/ReleaseReadinessEvidencePanel.tsx',
  'src/features/governance/ReleaseReadinessEvidencePanelRoute.tsx',
  'src/flow/library/GovernanceHost.tsx',
  'src/flow/library/GovernanceOverview.tsx'
] as const;

export function normalizeAuthorityFirewallText(text: string | null | undefined): string {
  return (text ?? '')
    .normalize('NFKC')
    .replace(/[“”]/g, '"')
    .replace(/[‘’]/g, "'")
    .replace(/[–—]/g, '-')
    .replace(/[^a-zA-Z0-9]+/g, ' ')
    .replace(/\s+/g, ' ')
    .trim()
    .toLowerCase();
}

export function isAllowedNegativeBoundaryText(text: string | null | undefined): boolean {
  return containsAnyNormalizedMarker(text, allowedNegativeBoundaryPhrases);
}

export function containsUnsafePrivateRawMarker(text: string | null | undefined): boolean {
  return findUnsafePrivateRawMarkers(text).length > 0;
}

export function containsAuthorityClaim(text: string | null | undefined): boolean {
  return findAuthorityClaims(text).length > 0;
}

export function containsForbiddenActionLabel(text: string | null | undefined): boolean {
  return findForbiddenActionLabels(text).length > 0;
}

export function containsForbiddenDependencyMarker(text: string | null | undefined): boolean {
  return findForbiddenDependencyMarkers(text).length > 0;
}

export function findUnsafePrivateRawMarkers(text: string | null | undefined): string[] {
  return findMarkers(text, unsafePrivateRawMarkers);
}

export function findAuthorityClaims(text: string | null | undefined): string[] {
  return findMarkers(stripAllowedNegativeBoundaryText(text), forbiddenAuthorityMarkers);
}

export function findForbiddenActionLabels(text: string | null | undefined): string[] {
  return findMarkers(stripAllowedNegativeBoundaryText(text), forbiddenActionLabels);
}

export function findForbiddenDependencyMarkers(text: string | null | undefined): string[] {
  const rawText = (text ?? '').toLowerCase();
  const normalized = padNormalizedText(normalizeAuthorityFirewallText(text));

  return forbiddenDependencyMarkers.filter((marker) => {
    if (marker.includes('(')) {
      return rawText.includes(marker.toLowerCase());
    }

    return normalized.includes(padNormalizedText(normalizeAuthorityFirewallText(marker)));
  });
}

function containsAnyNormalizedMarker(text: string | null | undefined, markers: readonly string[]): boolean {
  return findMarkers(text, markers).length > 0;
}

function findMarkers(text: string | null | undefined, markers: readonly string[]): string[] {
  const normalized = padNormalizedText(normalizeAuthorityFirewallText(text));
  if (normalized.trim().length === 0) {
    return [];
  }

  return markers.filter((marker) => normalized.includes(padNormalizedText(normalizeAuthorityFirewallText(marker))));
}

function stripAllowedNegativeBoundaryText(text: string | null | undefined): string {
  let normalized = padNormalizedText(normalizeAuthorityFirewallText(text));
  const normalizedPhrases = [...allowedNegativeBoundaryPhrases]
    .map((phrase) => padNormalizedText(normalizeAuthorityFirewallText(phrase)))
    .sort((left, right) => right.length - left.length);

  for (const normalizedPhrase of normalizedPhrases) {
    normalized = normalized.split(normalizedPhrase).join(' ');
  }

  return normalized;
}

function padNormalizedText(text: string): string {
  return ` ${text} `;
}

export const UiAuthorityFirewall = {
  forbiddenDependencyMarkers,
  forbiddenActionLabels,
  forbiddenAuthorityMarkers,
  unsafePrivateRawMarkers,
  allowedNegativeBoundaryPhrases,
  allowedCopyInspectionLabels,
  governanceEvidenceUiFileAllowList,
  isAllowedNegativeBoundaryText,
  containsUnsafePrivateRawMarker,
  containsAuthorityClaim,
  containsForbiddenActionLabel,
  containsForbiddenDependencyMarker,
  findUnsafePrivateRawMarkers,
  findAuthorityClaims,
  findForbiddenActionLabels,
  findForbiddenDependencyMarkers,
  normalizeAuthorityFirewallText
} as const;
