import type { ComponentType } from 'react';
import type { WorkspaceRoute, WorkspaceRouteMeta } from '../../app/routes';
import { AcceptedApprovalPanelRoute } from '../../features/governance/AcceptedApprovalPanelRoute';
import { ApprovalPackageReviewRoute } from '../../features/governance/ApprovalPackageReviewRoute';
import { ControlledActionRequestRoute } from '../../features/governance/ControlledActionRequestRoute';
import { DogfoodReceiptViewerRoute } from '../../features/governance/DogfoodReceiptViewerRoute';
import { GovernanceTimelineRoute } from '../../features/governance/GovernanceTimelineRoute';
import { MemoryProposalReviewRoute } from '../../features/governance/MemoryProposalReviewRoute';
import { OperationStatusViewerRoute } from '../../features/governance/OperationStatusViewerRoute';
import { PatchArtifactPanelRoute } from '../../features/governance/PatchArtifactPanelRoute';
import { PatchPackageViewerRoute } from '../../features/governance/PatchPackageViewerRoute';
import { PolicySatisfactionPanelRoute } from '../../features/governance/PolicySatisfactionPanelRoute';
import { ReleaseReadinessEvidencePanelRoute } from '../../features/governance/ReleaseReadinessEvidencePanelRoute';
import { RollbackEvidencePanelRoute } from '../../features/governance/RollbackEvidencePanelRoute';
import { SourceApplyDryRunReceiptPanelRoute } from '../../features/governance/SourceApplyDryRunReceiptPanelRoute';
import { SourceApplyReviewPanelRoute } from '../../features/governance/SourceApplyReviewPanelRoute';
import { ToolGateDecisionRoute } from '../../features/governance/ToolGateDecisionRoute';
import { WorkflowContinuationEvidencePanelRoute } from '../../features/governance/WorkflowContinuationEvidencePanelRoute';
import { WorkflowRunStepViewerRoute } from '../../features/governance/WorkflowRunStepViewerRoute';

export interface GovernanceViewerProps {
  route: WorkspaceRoute;
  onRouteReady?: (state: WorkspaceRouteMeta) => void;
}

export interface GovernanceViewerEntry {
  id: string;
  label: string;
  prefixes: string[];
  entryPath: string;
  component: ComponentType<GovernanceViewerProps>;
}

// Declarative replacement for the pathname ternary chain the old shell used.
// First matching prefix wins; the timeline is the fallback viewer.
export const governanceViewers: GovernanceViewerEntry[] = [
  {
    id: 'operations',
    label: 'Operation status',
    prefixes: ['/operations/'],
    entryPath: '/operations/',
    component: OperationStatusViewerRoute
  },
  {
    id: 'action-requests',
    label: 'Action requests',
    prefixes: ['/governance/action-requests', '/action-requests'],
    entryPath: '/action-requests',
    component: ControlledActionRequestRoute
  },
  {
    id: 'workflow-runs',
    label: 'Workflow runs and steps',
    prefixes: ['/workflows/runs'],
    entryPath: '/workflows/runs',
    component: WorkflowRunStepViewerRoute
  },
  {
    id: 'patch-packages',
    label: 'Patch packages',
    prefixes: ['/governance/patch-packages', '/patch-packages'],
    entryPath: '/patch-packages',
    component: PatchPackageViewerRoute
  },
  {
    id: 'memory-proposals',
    label: 'Memory proposals',
    prefixes: ['/governance/memory-proposals'],
    entryPath: '/governance/memory-proposals',
    component: MemoryProposalReviewRoute
  },
  {
    id: 'patch-artifacts',
    label: 'Patch artifacts',
    prefixes: ['/governance/patch-artifacts'],
    entryPath: '/governance/patch-artifacts',
    component: PatchArtifactPanelRoute
  },
  {
    id: 'dogfood-receipts',
    label: 'Dogfood receipts',
    prefixes: ['/governance/dogfood-receipts'],
    entryPath: '/governance/dogfood-receipts',
    component: DogfoodReceiptViewerRoute
  },
  {
    id: 'approval-packages',
    label: 'Approval packages',
    prefixes: ['/governance/approval-packages'],
    entryPath: '/governance/approval-packages',
    component: ApprovalPackageReviewRoute
  },
  {
    id: 'accepted-approvals',
    label: 'Accepted approvals',
    prefixes: ['/governance/accepted-approvals'],
    entryPath: '/governance/accepted-approvals',
    component: AcceptedApprovalPanelRoute
  },
  {
    id: 'policy-satisfaction',
    label: 'Policy satisfaction',
    prefixes: ['/governance/policy-satisfaction'],
    entryPath: '/governance/policy-satisfaction',
    component: PolicySatisfactionPanelRoute
  },
  {
    id: 'release-readiness-evidence',
    label: 'Release readiness evidence',
    prefixes: ['/governance/release-readiness-evidence'],
    entryPath: '/governance/release-readiness-evidence',
    component: ReleaseReadinessEvidencePanelRoute
  },
  {
    id: 'workflow-continuation-evidence',
    label: 'Workflow continuation evidence',
    prefixes: ['/governance/workflow-continuation-evidence'],
    entryPath: '/governance/workflow-continuation-evidence',
    component: WorkflowContinuationEvidencePanelRoute
  },
  {
    id: 'rollback-evidence',
    label: 'Rollback evidence',
    prefixes: ['/governance/rollback-evidence'],
    entryPath: '/governance/rollback-evidence',
    component: RollbackEvidencePanelRoute
  },
  {
    id: 'source-apply-reviews',
    label: 'Source apply reviews',
    prefixes: ['/governance/source-apply-reviews'],
    entryPath: '/governance/source-apply-reviews',
    component: SourceApplyReviewPanelRoute
  },
  {
    id: 'source-apply-dry-run-receipts',
    label: 'Source apply dry-run receipts',
    prefixes: ['/governance/source-apply-dry-run-receipts'],
    entryPath: '/governance/source-apply-dry-run-receipts',
    component: SourceApplyDryRunReceiptPanelRoute
  },
  {
    id: 'tool-gates',
    label: 'Tool gate decisions',
    prefixes: ['/governance/tool-gates'],
    entryPath: '/governance/tool-gates',
    component: ToolGateDecisionRoute
  },
  {
    id: 'timeline',
    label: 'Governance timeline',
    prefixes: ['/governance'],
    entryPath: '/governance/timeline',
    component: GovernanceTimelineRoute
  }
];

export function viewerForPath(pathname: string): GovernanceViewerEntry {
  const fallback = governanceViewers[governanceViewers.length - 1];
  return (
    governanceViewers.find((entry) => entry.prefixes.some((prefix) => pathname.startsWith(prefix))) ?? fallback
  );
}

export function isGovernancePath(pathname: string): boolean {
  return governanceViewers.some((entry) => entry.prefixes.some((prefix) => pathname.startsWith(prefix)));
}
