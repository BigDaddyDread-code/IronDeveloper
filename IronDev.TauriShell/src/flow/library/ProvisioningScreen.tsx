import { useCallback, useEffect, useState } from 'react';
import type { ProjectProvisioningReadinessUi, ProvisioningCheckUi } from '../../api/types';
import { useProjectContext } from '../../state/useProjectContext';
import { useSessionContext } from '../../state/useSessionContext';

// PROJECT-1..3: the provisioning screen, real. Readiness is computed server-side
// from stored truth plus scan evidence; this screen renders it and asks only the
// pointed questions detection cannot prove — confirm the build command, confirm
// the test command, confirm the proposed architecture profile. Every confirmation
// is a POST to an existing governed endpoint followed by a re-evaluation; the UI
// never marks anything ready itself.
//
// Vocabulary rules (future-ux-product-spec §9.4): profile proposed / confirmed,
// readiness satisfied, unknowns remain. Never "architecture understood".

type LoadState = 'loading' | 'ready' | 'error';

function stateTone(state: string): string {
  switch (state) {
    case 'Confirmed':
      return 'var(--fl-acc-ink, #0a4a3b)';
    case 'Unsafe':
    case 'Missing':
      return 'var(--fl-red, #a63232)';
    case 'NeedsConfirmation':
      return 'var(--fl-gate-ink, #7a4c0a)';
    default:
      return 'var(--fl-muted, #7a8087)';
  }
}

export function ProvisioningScreen() {
  const session = useSessionContext();
  const project = useProjectContext();
  const projectId = project.selectedProjectId;

  const [readiness, setReadiness] = useState<ProjectProvisioningReadinessUi | null>(null);
  const [loadState, setLoadState] = useState<LoadState>('loading');
  const [errorMessage, setErrorMessage] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const [commandDrafts, setCommandDrafts] = useState<Record<string, string>>({});

  const evaluate = useCallback(
    async (signal?: AbortSignal) => {
      if (projectId === null) {
        setReadiness(null);
        setLoadState('ready');
        return;
      }
      setLoadState('loading');
      setErrorMessage(null);
      try {
        const result = await session.client.getProvisioningReadiness(projectId, signal);
        setReadiness(result);
        setLoadState('ready');
      } catch (error: unknown) {
        if (signal?.aborted) {
          return;
        }
        setErrorMessage(error instanceof Error ? error.message : 'Readiness could not be evaluated.');
        setLoadState('error');
      }
    },
    [projectId, session.client]
  );

  useEffect(() => {
    const controller = new AbortController();
    void evaluate(controller.signal);
    return () => controller.abort();
  }, [evaluate]);

  const confirmCommand = async (check: ProvisioningCheckUi) => {
    if (projectId === null || busy) {
      return;
    }
    const commandType = check.name === 'Build command' ? 'Build' : 'Test';
    const text = (commandDrafts[check.name] ?? check.detectedValue).trim();
    if (text.length === 0) {
      return;
    }
    setBusy(true);
    setErrorMessage(null);
    try {
      await session.client.saveProjectCommand(projectId, commandType, text);
      await evaluate();
    } catch (error: unknown) {
      setErrorMessage(error instanceof Error ? error.message : 'The command was not saved.');
    } finally {
      setBusy(false);
    }
  };

  const confirmProfile = async () => {
    if (projectId === null || busy || !readiness?.proposedProfile) {
      return;
    }
    setBusy(true);
    setErrorMessage(null);
    try {
      await session.client.saveProjectProfile(projectId, readiness.proposedProfile);
      await evaluate();
    } catch (error: unknown) {
      setErrorMessage(error instanceof Error ? error.message : 'The profile was not saved.');
    } finally {
      setBusy(false);
    }
  };

  if (projectId === null) {
    return <p className="fl-empty">Select a project to evaluate its provisioning readiness.</p>;
  }

  return (
    <div style={{ display: 'grid', gap: 12 }} data-testid="flow.provisioning">
      <p className="fl-sub" style={{ margin: 0 }}>
        A folder path is not a project. Readiness is computed by the backend from stored truth and scan evidence;
        detection proposes, only your confirmation makes it stored truth.
      </p>

      {errorMessage ? <div className="fl-error">{errorMessage}</div> : null}

      {loadState === 'loading' ? (
        <p className="fl-empty">Evaluating readiness…</p>
      ) : loadState === 'error' ? (
        <p className="fl-empty" data-testid="flow.provisioning.unavailable">
          Backend truth unavailable — nothing is shown rather than inventing state.
        </p>
      ) : readiness === null ? null : (
        <>
          <div
            className={readiness.isReady ? 'fl-banner' : 'fl-qbox'}
            data-testid="flow.provisioning.verdict"
          >
            {readiness.isReady ? (
              <span>
                <strong>Readiness satisfied.</strong> The governed loop may be attempted — readiness approves nothing.
              </span>
            ) : (
              <span>
                <strong>Blocked:</strong> {readiness.blockedStates.join(' · ')}. Unknowns remain; each check below names
                its remedy.
              </span>
            )}
          </div>

          <div data-testid="flow.provisioning.checks">
            {readiness.checks.map((check) => {
              const isCommandQuestion =
                (check.name === 'Build command' || check.name === 'Test command') &&
                (check.state === 'NeedsConfirmation' || check.state === 'Missing');
              return (
                <div className="fl-qbox" key={`${check.name}-${check.evidence}`}>
                  <span style={{ width: '100%' }}>
                    <strong style={{ fontSize: 12.5 }}>
                      {check.name} · <span style={{ color: stateTone(check.state) }}>{check.state}</span>
                      {check.blocking ? ' · blocking' : ''}
                    </strong>
                    <span style={{ display: 'block', fontSize: 12.5, color: 'var(--fl-ink2)' }}>{check.evidence}</span>
                    {check.remedy ? (
                      <span style={{ display: 'block', fontSize: 12, color: 'var(--fl-ink2)' }}>
                        Next safe action: {check.remedy}
                      </span>
                    ) : null}
                    {isCommandQuestion ? (
                      <span style={{ display: 'flex', gap: 6, marginTop: 6, flexWrap: 'wrap' }}>
                        <input
                          className="fl-select"
                          style={{ flex: 1, minWidth: 220, fontFamily: 'var(--fl-mono, monospace)' }}
                          placeholder={`${check.name} to run`}
                          value={commandDrafts[check.name] ?? check.detectedValue}
                          onChange={(event) =>
                            setCommandDrafts((prev) => ({ ...prev, [check.name]: event.target.value }))
                          }
                          data-testid={`flow.provisioning.input.${check.name === 'Build command' ? 'build' : 'test'}`}
                        />
                        <button
                          className="fl-btn"
                          disabled={busy || (commandDrafts[check.name] ?? check.detectedValue).trim().length === 0}
                          onClick={() => void confirmCommand(check)}
                          data-testid={`flow.provisioning.confirm.${check.name === 'Build command' ? 'build' : 'test'}`}
                        >
                          Confirm {check.name.toLowerCase()}
                        </button>
                      </span>
                    ) : null}
                  </span>
                </div>
              );
            })}
          </div>

          {readiness.proposedProfile ? (
            <div style={{ display: 'flex', gap: 8, alignItems: 'center', flexWrap: 'wrap' }}>
              <button
                className="fl-btn"
                disabled={busy}
                onClick={() => void confirmProfile()}
                data-testid="flow.provisioning.confirmProfile"
              >
                Confirm proposed profile
              </button>
              <span style={{ fontSize: 12, color: 'var(--fl-ink2)' }}>
                Profile proposed — confirming records it as stored truth. Edit later via project settings if it is wrong.
              </span>
            </div>
          ) : null}

          <div style={{ display: 'flex', gap: 8, alignItems: 'center' }}>
            <button className="fl-btn" disabled={busy} onClick={() => void evaluate()} data-testid="flow.provisioning.reevaluate">
              Re-evaluate readiness
            </button>
            <span style={{ fontSize: 11.5, color: 'var(--fl-muted)' }}>{readiness.boundary}</span>
          </div>
        </>
      )}
    </div>
  );
}
