import { useEffect, useMemo } from 'react';
import type { WorkspaceRoute, WorkspaceRouteMeta } from '../../app/routes';
import { patchArtifactBoundaryRules } from './PatchArtifactBoundary';
import { PatchArtifactPanel } from './PatchArtifactPanel';
import type { PatchArtifactEvidence } from './PatchArtifactTypes';
import { patchArtifactDefaultDisplayState } from './PatchArtifactTypes';

interface PatchArtifactPanelRouteProps {
  route: WorkspaceRoute;
  onRouteReady?: (state: WorkspaceRouteMeta) => void;
}

export function PatchArtifactPanelRoute({ onRouteReady }: PatchArtifactPanelRouteProps) {
  const routeMeta = useMemo<WorkspaceRouteMeta>(() => ({
    workspaceCommands: [],
    workspaceBlockReason: null,
    workspaceSummaryChips: [
      { label: 'Patch artifact evidence', testId: 'patch-artifact.chip.evidence' },
      { label: 'Read-only', testId: 'patch-artifact.chip.readonly' }
    ]
  }), []);

  useEffect(() => {
    onRouteReady?.(routeMeta);
  }, [onRouteReady, routeMeta]);

  const fixture = new URLSearchParams(window.location.search).get('fixture') ?? 'missing';

  if (fixture === 'loading') {
    return <PatchArtifactPanel isLoading />;
  }

  if (fixture === 'error') {
    return <PatchArtifactPanel errorMessage="Unable to load patch artifact evidence." />;
  }

  if (fixture === 'missing') {
    return <PatchArtifactPanel evidence={null} />;
  }

  return <PatchArtifactPanel evidence={fixtureEvidence(fixture)} />;
}

function fixtureEvidence(fixture: string): PatchArtifactEvidence {
  const current: PatchArtifactEvidence = {
    patchArtifactId: 'patch-artifact-232',
    patchArtifactHash: 'sha256:patch-artifact-hash-232',
    patchArtifactStatus: 'Stored',
    projectId: 'project-7',
    subjectKind: 'SourceApplyRequest',
    subjectId: 'source-apply-request-232',
    subjectHash: 'sha256:source-apply-request-hash-232',
    workflowRunId: 'workflow-run-232',
    workflowStepId: 'workflow-step-232',
    createdBy: 'human-reviewer-232',
    createdAtUtc: '2026-06-18T00:00:00Z',
    storedAtUtc: '2026-06-18T00:01:00Z',
    expiresAtUtc: '2026-06-19T00:00:00Z',
    sourceKind: 'ImplementationProposal',
    sourceId: 'implementation-proposal-232',
    sourceHash: 'sha256:implementation-proposal-hash-232',
    fileCount: 2,
    files: [
      {
        path: 'src/apply/Widget.cs',
        action: 'Modify',
        fileHashBefore: 'sha256:before-widget-232',
        fileHashAfter: 'sha256:after-widget-232',
        safeSummary: 'Patch artifact summary says Widget.cs would change after review.'
      },
      {
        path: 'tests/apply/WidgetTests.cs',
        action: 'Create',
        fileHashAfter: 'sha256:after-widget-tests-232',
        safeSummary: 'Patch artifact summary says a test file would be added.'
      }
    ],
    warnings: ['Patch artifact display does not apply the patch.'],
    evidenceRefs: ['implementation-proposal-232', 'source-apply-request-232', 'patch-artifact-store-232'],
    boundaryMaxims: patchArtifactBoundaryRules,
    rawPatchBodyPresent: false,
    rawPatchBodyRendered: false,
    displayState: {
      ...patchArtifactDefaultDisplayState,
      evidencePresent: true,
      evidenceSatisfied: true,
      recordStored: true,
      humanReviewRequired: true
    }
  };

  if (fixture === 'missing-artifact-id') {
    return { ...current, patchArtifactId: '', incomplete: true };
  }

  if (fixture === 'missing-artifact-hash') {
    return { ...current, patchArtifactHash: '', incomplete: true };
  }

  if (fixture === 'missing-source') {
    return { ...current, sourceKind: '', sourceId: '', sourceHash: '', incomplete: true };
  }

  if (fixture === 'missing-subject') {
    return { ...current, subjectKind: '', subjectId: '', subjectHash: '', incomplete: true };
  }

  if (fixture === 'missing-workflow') {
    return { ...current, workflowRunId: '', workflowStepId: '', incomplete: true };
  }

  if (fixture === 'invalid-timestamp') {
    return { ...current, createdAtUtc: 'not-a-date', incomplete: true };
  }

  if (fixture === 'empty-refs') {
    return { ...current, evidenceRefs: [], displayState: { ...current.displayState, evidencePresent: false, evidenceSatisfied: false } };
  }

  if (fixture === 'missing-boundary') {
    return { ...current, boundaryMaxims: [], incomplete: true };
  }

  if (fixture === 'stale') {
    return { ...current, stale: true, warnings: ['stale patch artifact evidence'] };
  }

  if (fixture === 'expired') {
    return { ...current, expired: true, warnings: ['expired patch artifact evidence'] };
  }

  if (fixture === 'raw-patch') {
    return {
      ...current,
      rawPatchBodyPresent: true,
      warnings: ['raw patch payload withheld by this viewer']
    };
  }

  if (fixture === 'unsafe') {
    return {
      ...current,
      files: [{ ...current.files[0], safeSummary: 'raw patch private reasoning should redact' }],
      evidenceRefs: ['raw diff should redact', 'patch-artifact-store-232'],
      warnings: ['secret bearer token should redact'],
      unsafeMaterialDetected: true
    };
  }

  if (fixture === 'authority-claim') {
    return {
      ...current,
      warnings: ['patch approved and safe to merge by fixture data'],
      authorityClaimsDetected: true
    };
  }

  if (fixture === 'contradictory') {
    return {
      ...current,
      displayState: {
        ...current.displayState,
        patchArtifactCreated: true,
        sourceApplyApproved: true,
        sourceApplyExecuted: true,
        mutationPerformed: true
      }
    } as unknown as PatchArtifactEvidence;
  }

  return current;
}
