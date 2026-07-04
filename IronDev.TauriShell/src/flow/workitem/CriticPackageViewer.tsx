import { useState } from 'react';
import type { SkeletonCriticPackage } from '../../api/types';

// The proposed change at full fidelity: per-file diffs and build/test results.
// Read-only review material — rendering a diff grants nothing.

const MaxDiffLines = 400;

function clampDiff(diff: string): { text: string; truncated: boolean } {
  const lines = diff.split('\n');
  if (lines.length <= MaxDiffLines) {
    return { text: diff, truncated: false };
  }
  return { text: lines.slice(0, MaxDiffLines).join('\n'), truncated: true };
}

interface CriticPackageViewerProps {
  criticPackage: SkeletonCriticPackage;
}

export function CriticPackageViewer({ criticPackage }: CriticPackageViewerProps) {
  const [openDiff, setOpenDiff] = useState<string | null>(null);

  return (
    <>
      <p className="fl-plabel">Proposed change — full fidelity</p>
      <p style={{ fontSize: 13.5, marginTop: 0 }}>{criticPackage.proposalSummary || 'No summary.'}</p>
      <p style={{ fontSize: 12.5, color: 'var(--fl-ink2)' }}>
        Workspace build/test: {criticPackage.workspaceRunSucceeded ? 'succeeded' : 'FAILED'} ·{' '}
        {criticPackage.commandResults.map((command) => `${command.displayName} (exit ${command.exitCode})`).join(' · ') ||
          'no commands recorded'}
      </p>

      <div data-testid="flow.review.changes">
        {criticPackage.changes.map((change) => {
          const clamped = openDiff === change.filePath ? clampDiff(change.diff || change.fullContentAfter || '(no diff recorded)') : null;
          return (
            <div className="fl-qbox" key={change.filePath}>
              <span style={{ width: '100%' }}>
                <strong style={{ fontSize: 12.5 }}>
                  {change.filePath}
                  {change.isNewFile ? ' · new' : change.isDeletion ? ' · deletion' : ''}
                </strong>
                {change.description ? (
                  <span style={{ display: 'block', fontSize: 12.5, color: 'var(--fl-ink2)' }}>{change.description}</span>
                ) : null}
                <button
                  className="fl-btn"
                  style={{ marginTop: 6 }}
                  onClick={() => setOpenDiff((prev) => (prev === change.filePath ? null : change.filePath))}
                >
                  {openDiff === change.filePath ? 'Hide diff' : 'Show diff'}
                </button>
                {clamped ? (
                  <pre
                    style={{
                      fontSize: 11.5,
                      overflowX: 'auto',
                      background: 'var(--fl-bg2, rgba(0,0,0,0.04))',
                      padding: 8,
                      borderRadius: 6
                    }}
                  >
                    {clamped.text}
                    {clamped.truncated
                      ? `\n… diff truncated at ${MaxDiffLines} lines for display — the full diff is in the package evidence.`
                      : ''}
                  </pre>
                ) : null}
              </span>
            </div>
          );
        })}
      </div>
    </>
  );
}
