import { useEffect, useMemo } from 'react';
import type { WorkspaceRoute, WorkspaceRouteMeta } from '../../app/routes';
import { PolicySatisfactionPanel } from './PolicySatisfactionPanel';
import type { PolicySatisfactionEvidence } from './PolicySatisfactionTypes';
import { policySatisfactionDefaultDisplayState } from './PolicySatisfactionTypes';

interface PolicySatisfactionPanelRouteProps {
  route: WorkspaceRoute;
  onRouteReady?: (state: WorkspaceRouteMeta) => void;
}

export function PolicySatisfactionPanelRoute({ onRouteReady }: PolicySatisfactionPanelRouteProps) {
  const routeMeta = useMemo<WorkspaceRouteMeta>(() => ({
    workspaceCommands: [],
    workspaceBlockReason: null,
    workspaceSummaryChips: [
      { label: 'Policy satisfaction evidence', testId: 'policy-satisfaction.chip.evidence' },
      { label: 'Read-only', testId: 'policy-satisfaction.chip.readonly' }
    ]
  }), []);

  useEffect(() => {
    onRouteReady?.(routeMeta);
  }, [onRouteReady, routeMeta]);

  const fixture = new URLSearchParams(window.location.search).get('fixture') ?? 'missing';

  if (fixture === 'loading') {
    return <PolicySatisfactionPanel isLoading />;
  }

  if (fixture === 'error') {
    return <PolicySatisfactionPanel errorMessage="Unable to load policy satisfaction evidence." />;
  }

  if (fixture === 'missing') {
    return <PolicySatisfactionPanel evidence={null} />;
  }

  return <PolicySatisfactionPanel evidence={fixtureEvidence(fixture)} />;
}

function fixtureEvidence(fixture: string): PolicySatisfactionEvidence {
  const current: PolicySatisfactionEvidence = {
    policyId: 'policy-230',
    policyName: 'Controlled source apply policy',
    policyVersion: 'v1',
    subjectId: 'source-apply-request-230',
    subjectHash: 'sha256:subject-hash-230',
    workflowId: 'workflow-run-230',
    approvalId: 'accepted-approval-230',
    approvalHash: 'sha256:approval-hash-230',
    evidenceRefs: ['policy-decision-230', 'accepted-approval-230', 'source-apply-gate-230'],
    evaluatedAtUtc: '2026-06-18T00:00:00Z',
    expiresAtUtc: '2026-06-19T00:00:00Z',
    warnings: ['Policy evidence display does not satisfy policy.'],
    displayState: {
      ...policySatisfactionDefaultDisplayState,
      evidencePresent: true,
      evidenceSatisfied: true,
      recordStored: true,
      humanReviewRequired: true
    }
  };

  if (fixture === 'missing-policy') {
    return { ...current, policyId: '', incomplete: true };
  }

  if (fixture === 'missing-subject') {
    return { ...current, subjectId: '', subjectHash: '', incomplete: true };
  }

  if (fixture === 'missing-workflow') {
    return { ...current, workflowId: '', incomplete: true };
  }

  if (fixture === 'invalid-timestamp') {
    return { ...current, evaluatedAtUtc: 'not-a-date', incomplete: true };
  }

  if (fixture === 'empty-refs') {
    return { ...current, evidenceRefs: [], displayState: { ...current.displayState, evidencePresent: false, evidenceSatisfied: false } };
  }

  if (fixture === 'stale') {
    return { ...current, stale: true, warnings: ['stale policy evidence'] };
  }

  if (fixture === 'expired') {
    return { ...current, expired: true, warnings: ['expired policy evidence'] };
  }

  if (fixture === 'unsafe') {
    return {
      ...current,
      evidenceRefs: ['raw prompt private reasoning should redact', 'policy-decision-230'],
      warnings: ['secret bearer token should redact'],
      unsafeMaterialDetected: true
    };
  }

  if (fixture === 'authority-claim') {
    return {
      ...current,
      warnings: ['release approved by fixture data'],
      authorityClaimsDetected: true
    };
  }

  if (fixture === 'contradictory') {
    return {
      ...current,
      displayState: {
        ...current.displayState,
        releaseApproved: true,
        sourceApplyExecuted: true,
        mutationPerformed: true
      }
    } as unknown as PolicySatisfactionEvidence;
  }

  return current;
}
