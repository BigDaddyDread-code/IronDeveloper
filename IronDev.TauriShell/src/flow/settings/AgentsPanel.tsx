import { useCallback, useEffect, useState } from 'react';
import { IronDevApiError } from '../../api/ironDevApi';
import type { EffectiveSkeletonAgentProfile, SkeletonAgentProfile, SkeletonAgentProfileDraft, SkeletonAgentProfilePublishedVersion } from '../../api/types';
import { useSessionContext } from '../../state/useSessionContext';

// AG-5 — per-agent configuration: the model each agent runs on and its skill +
// personality. Editing here writes the same AgentProfiles/{role}/ files the loop
// reads. A profile configures voice and model, never authority — the backend
// refuses anything that looks like a secret, and the copy says so.

// User-selectable providers only — 'fake' is test/local, never offered here.
const PROVIDERS = ['openai', 'localopenai', 'ollama', 'custom'];

// AG-7 — the orchestrator runs no model: it deterministically composes the loop
// and enforces the gates. Showing it a provider/model/voice editor would be a
// lie, so its card says what it actually is and offers nothing to configure.
const DETERMINISTIC_ROLES = ['Orchestrator'];

export function AgentsPanel() {
  const session = useSessionContext();
  const [profiles, setProfiles] = useState<SkeletonAgentProfile[]>([]);
  const [effectiveProfiles, setEffectiveProfiles] = useState<Record<string, EffectiveSkeletonAgentProfile>>({});
  const [drafts, setDrafts] = useState<Record<string, SkeletonAgentProfile>>({});
  const [state, setState] = useState<'loading' | 'ready' | 'error'>('loading');
  const [error, setError] = useState<string | null>(null);
  const [savingRole, setSavingRole] = useState<string | null>(null);
  const [savedRole, setSavedRole] = useState<string | null>(null);
  const [draftState, setDraftState] = useState<Record<string, SkeletonAgentProfileDraft>>({});
  const [publishReasons, setPublishReasons] = useState<Record<string, string>>({});
  const [notice, setNotice] = useState<Record<string, string>>({});
  const [histories, setHistories] = useState<Record<string, SkeletonAgentProfilePublishedVersion[]>>({});
  const [resetFields, setResetFields] = useState<Record<string, string>>({});
  const [recoveryReasons, setRecoveryReasons] = useState<Record<string, string>>({});

  const load = useCallback(async () => {
    setState('loading');
    try {
      const [result, effective] = await Promise.all([
        session.client.listAgentProfiles(),
        session.client.listEffectiveAgentProfiles(session.config.selectedProjectId)
      ]);
      const editable = result.filter((profile) => !DETERMINISTIC_ROLES.includes(profile.role));
      const persistedDrafts = await Promise.all(editable.map(async (profile) => [profile.role, await session.client.getAgentProfileDraft(profile.role)] as const));
      const publishedHistory = await Promise.all(editable.map(async (profile) => [profile.role, await session.client.listAgentProfileHistory(profile.role)] as const));
      setProfiles(result);
      setEffectiveProfiles(Object.fromEntries(effective.map((profile) => [profile.role, profile])));
      setDraftState(Object.fromEntries(persistedDrafts));
      setHistories(Object.fromEntries(publishedHistory));
      setDrafts(Object.fromEntries(result.map((profile) => {
        const persisted = persistedDrafts.find(([role]) => role === profile.role)?.[1];
        return [profile.role, persisted ? { ...profile, ...persisted.values } : profile];
      })));
      setState('ready');
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Could not load agent profiles.');
      setState('error');
    }
  }, [session.client, session.config.selectedProjectId]);

  useEffect(() => {
    void load();
  }, [load]);

  const patch = (role: string, change: Partial<SkeletonAgentProfile>) =>
    setDrafts((prev) => ({ ...prev, [role]: { ...prev[role], ...change } }));

  const save = useCallback(
    async (role: string) => {
      const draft = drafts[role];
      if (!draft || savingRole) return;
      setSavingRole(role);
      setSavedRole(null);
      setError(null);
      try {
        const outcome = await session.client.saveAgentProfileDraft(role, {
          expectedRevision: draftState[role]?.revision ?? 0,
          provider: draft.provider,
          model: draft.model,
          timeoutSeconds: draft.timeoutSeconds,
          skill: draft.skill,
          personality: draft.personality
        });
        if (!outcome.succeeded) {
          setError(outcome.failureReason);
          return;
        }
        if (outcome.draft) {
          setDraftState((previous) => ({ ...previous, [role]: outcome.draft! }));
        }
        setSavedRole(role);
        setNotice((previous) => ({ ...previous, [role]: outcome.draft?.isValid ? 'Draft saved and valid.' : 'Draft saved with validation issues.' }));
      } catch (e) {
        // A refused update (e.g. secret detected) returns 400 with the outcome.
        const body = e instanceof IronDevApiError ? (e.body as { failureReason?: string } | undefined) : undefined;
        setError(body?.failureReason ?? (e instanceof Error ? e.message : 'Save failed.'));
      } finally {
        setSavingRole(null);
      }
    },
    [draftState, drafts, savingRole, session.client]
  );

  const testDraft = useCallback(async (role: string) => {
    if (savingRole) return;
    setSavingRole(role);
    setError(null);
    try {
      const outcome = await session.client.testAgentProfileDraft(role);
      setNotice((previous) => ({ ...previous, [role]: outcome.summary || outcome.status }));
    } catch (e) {
      const body = e instanceof IronDevApiError ? (e.body as { failureReason?: string } | undefined) : undefined;
      setError(body?.failureReason ?? (e instanceof Error ? e.message : 'Draft test failed.'));
    } finally {
      setSavingRole(null);
    }
  }, [savingRole, session.client]);

  const publishDraft = useCallback(async (role: string) => {
    if (savingRole) return;
    setSavingRole(role);
    setError(null);
    try {
      const outcome = await session.client.publishAgentProfileDraft(role, {
        expectedRevision: draftState[role]?.revision ?? 0,
        reason: publishReasons[role]?.trim() ?? ''
      });
      if (!outcome.succeeded) {
        setError(outcome.failureReason);
        return;
      }
      setNotice((previous) => ({ ...previous, [role]: `Published version ${outcome.publishedVersion?.version ?? ''}.` }));
      setPublishReasons((previous) => ({ ...previous, [role]: '' }));
      await load();
    } catch (e) {
      const body = e instanceof IronDevApiError ? (e.body as { failureReason?: string } | undefined) : undefined;
      setError(body?.failureReason ?? (e instanceof Error ? e.message : 'Publish failed.'));
    } finally {
      setSavingRole(null);
    }
  }, [draftState, load, publishReasons, savingRole, session.client]);

  const resetProfile = useCallback(async (role: string, scope: 'Field' | 'Agent') => {
    if (savingRole) return;
    setSavingRole(role);
    setError(null);
    try {
      const outcome = await session.client.resetAgentProfile(role, {
        expectedRevision: draftState[role]?.revision ?? 0,
        scope,
        field: scope === 'Field' ? (resetFields[role] || 'skill') : '',
        reason: recoveryReasons[role]?.trim() ?? ''
      });
      setNotice((previous) => ({ ...previous, [role]: `Reset published as version ${outcome.publishedVersion?.version ?? ''}.` }));
      setRecoveryReasons((previous) => ({ ...previous, [role]: '' }));
      await load();
    } catch (e) {
      const body = e instanceof IronDevApiError ? (e.body as { failureReason?: string } | undefined) : undefined;
      setError(body?.failureReason ?? (e instanceof Error ? e.message : 'Reset failed.'));
    } finally {
      setSavingRole(null);
    }
  }, [draftState, load, recoveryReasons, resetFields, savingRole, session.client]);

  const restoreProfile = useCallback(async (role: string, version: number) => {
    if (savingRole) return;
    setSavingRole(role);
    setError(null);
    try {
      const outcome = await session.client.restoreAgentProfile(role, version, {
        expectedRevision: draftState[role]?.revision ?? 0,
        reason: recoveryReasons[role]?.trim() ?? ''
      });
      setNotice((previous) => ({ ...previous, [role]: `Version ${version} restored as new version ${outcome.publishedVersion?.version ?? ''}.` }));
      setRecoveryReasons((previous) => ({ ...previous, [role]: '' }));
      await load();
    } catch (e) {
      const body = e instanceof IronDevApiError ? (e.body as { failureReason?: string } | undefined) : undefined;
      setError(body?.failureReason ?? (e instanceof Error ? e.message : 'Restore failed.'));
    } finally {
      setSavingRole(null);
    }
  }, [draftState, load, recoveryReasons, savingRole, session.client]);

  if (state === 'loading') {
    return <p className="fl-empty">Loading agents…</p>;
  }
  if (state === 'error') {
    return <div className="fl-error">Agents did not load: {error}</div>;
  }

  return (
    <div data-testid="flow.settings.agents">
      <p className="fl-plabel">Agents</p>
      <p className="fl-empty" style={{ marginTop: 0 }}>
        Each agent can run a different model and carry its own skill and personality. A profile configures voice and model,
        never authority — and never a secret (keep API keys in your environment). The critic stays blind by contract no
        matter what you write here.
      </p>

      {error ? <div className="fl-error" data-testid="flow.settings.agents.error">{error}</div> : null}

      {profiles.map((profile) => {
        const draft = drafts[profile.role] ?? profile;
        const isDeterministic = DETERMINISTIC_ROLES.includes(profile.role);
        const displayName = profile.displayName || displayAgentRole(profile.role);
        const effective = effectiveProfiles[profile.role];
        return (
          <div className="fl-panel-box" key={profile.role} style={{ marginTop: 10 }} data-testid={`flow.settings.agent.${profile.role.toLowerCase()}`}>
            <p className="fl-plabel" style={{ marginTop: 0 }}>
              {displayName}
            </p>
            {isDeterministic ? (
              <>
              <p className="fl-empty" style={{ marginTop: 0 }} data-testid={`flow.settings.agent.${profile.role.toLowerCase()}.deterministic`}>
                Deterministic — the orchestrator composes the loop and enforces the gates. It runs no model, so there is
                nothing to configure here. (It never judges whether the work is satisfactory; only the human gate approves.)
              </p>
              <EffectiveProfileSummary role={profile.role} effective={effective} />
              </>
            ) : (
            <>
            <div style={{ display: 'flex', gap: 8, flexWrap: 'wrap', alignItems: 'center' }}>
              <select
                className="fl-select"
                value={draft.provider}
                onChange={(e) => patch(profile.role, { provider: e.target.value })}
                data-testid={`flow.settings.agent.${profile.role.toLowerCase()}.provider`}
              >
                {PROVIDERS.map((p) => (
                  <option key={p} value={p}>
                    {p}
                  </option>
                ))}
              </select>
              <input
                placeholder="model"
                value={draft.model}
                onChange={(e) => patch(profile.role, { model: e.target.value })}
                data-testid={`flow.settings.agent.${profile.role.toLowerCase()}.model`}
              />
            </div>
            <p className="fl-plabel" style={{ marginTop: 10 }}>
              skill.md
            </p>
            {profile.builtInDefaultVersion ? (
              <p className="fl-empty" style={{ marginTop: 0 }} data-testid={`flow.settings.agent.${profile.role.toLowerCase()}.defaultVersion`}>
                {profile.builtInDefaultName || 'Built-in default'}: {profile.builtInDefaultVersion}
              </p>
            ) : null}
            <p className="fl-empty" style={{ marginTop: 0 }} data-testid={`flow.settings.agent.${profile.role.toLowerCase()}.boundary`}>
              {profile.boundary}
            </p>
            <EffectiveProfileSummary role={profile.role} effective={effective} />
            <textarea
              style={{ width: '100%', minHeight: 60, fontSize: 12.5 }}
              value={draft.skill}
              onChange={(e) => patch(profile.role, { skill: e.target.value })}
              placeholder="How this agent approaches its job (the structured task always overrides this)."
              data-testid={`flow.settings.agent.${profile.role.toLowerCase()}.skill`}
            />
            <p className="fl-plabel" style={{ marginTop: 10 }}>
              personality.md
            </p>
            <textarea
              style={{ width: '100%', minHeight: 40, fontSize: 12.5 }}
              value={draft.personality}
              onChange={(e) => patch(profile.role, { personality: e.target.value })}
              placeholder="This agent's voice."
              data-testid={`flow.settings.agent.${profile.role.toLowerCase()}.personality`}
            />
            <div style={{ display: 'flex', gap: 8, alignItems: 'center', marginTop: 8 }}>
              <button
                className="fl-btn fl-pri"
                disabled={savingRole !== null}
                onClick={() => void save(profile.role)}
                data-testid={`flow.settings.agent.${profile.role.toLowerCase()}.save`}
              >
                {savingRole === profile.role ? 'Working…' : 'Save draft'}
              </button>
              <button
                className="fl-btn"
                disabled={savingRole !== null || !draftState[profile.role]}
                onClick={() => void testDraft(profile.role)}
                data-testid={`flow.settings.agent.${profile.role.toLowerCase()}.test`}
              >
                Test draft
              </button>
              {savedRole === profile.role ? <span style={{ fontSize: 12.5, color: 'var(--fl-acc-ink)' }}>Draft saved.</span> : null}
            </div>
            <div style={{ display: 'flex', gap: 8, alignItems: 'center', marginTop: 8, flexWrap: 'wrap' }}>
              <input
                style={{ flex: '1 1 260px' }}
                value={publishReasons[profile.role] ?? ''}
                onChange={(event) => setPublishReasons((previous) => ({ ...previous, [profile.role]: event.target.value }))}
                placeholder="Reason for publishing this version"
                data-testid={`flow.settings.agent.${profile.role.toLowerCase()}.publishReason`}
              />
              <button
                className="fl-btn fl-pri"
                disabled={savingRole !== null || !draftState[profile.role]?.isValid || !(publishReasons[profile.role]?.trim())}
                onClick={() => void publishDraft(profile.role)}
                data-testid={`flow.settings.agent.${profile.role.toLowerCase()}.publish`}
              >
                Publish
              </button>
            </div>
            {draftState[profile.role]?.validationIssues.length ? (
              <ul className="fl-empty" data-testid={`flow.settings.agent.${profile.role.toLowerCase()}.validation`}>
                {draftState[profile.role].validationIssues.map((issue) => <li key={`${issue.field}-${issue.code}`}>{issue.field}: {issue.message}</li>)}
              </ul>
            ) : null}
            {notice[profile.role] ? <p className="fl-empty" data-testid={`flow.settings.agent.${profile.role.toLowerCase()}.notice`}>{notice[profile.role]}</p> : null}
            <div style={{ marginTop: 12, borderTop: '1px solid var(--fl-line)', paddingTop: 10 }} data-testid={`flow.settings.agent.${profile.role.toLowerCase()}.recovery`}>
              <p className="fl-plabel" style={{ marginTop: 0 }}>Reset and restore</p>
              <p className="fl-empty" style={{ marginTop: 0 }}>A reset or restore publishes a new version. Existing history is never rewritten.</p>
              <div style={{ display: 'flex', gap: 8, flexWrap: 'wrap', alignItems: 'center' }}>
                <select
                  className="fl-select"
                  value={resetFields[profile.role] || 'skill'}
                  onChange={(event) => setResetFields((previous) => ({ ...previous, [profile.role]: event.target.value }))}
                  data-testid={`flow.settings.agent.${profile.role.toLowerCase()}.resetField`}
                >
                  <option value="provider">Provider</option>
                  <option value="model">Model</option>
                  <option value="timeoutSeconds">Timeout</option>
                  <option value="skill">Skill</option>
                  <option value="personality">Personality</option>
                </select>
                <input
                  style={{ flex: '1 1 240px' }}
                  value={recoveryReasons[profile.role] ?? ''}
                  onChange={(event) => setRecoveryReasons((previous) => ({ ...previous, [profile.role]: event.target.value }))}
                  placeholder="Reason for reset or restore"
                  data-testid={`flow.settings.agent.${profile.role.toLowerCase()}.recoveryReason`}
                />
                <button
                  className="fl-btn"
                  disabled={savingRole !== null || !(recoveryReasons[profile.role]?.trim())}
                  onClick={() => void resetProfile(profile.role, 'Field')}
                  data-testid={`flow.settings.agent.${profile.role.toLowerCase()}.resetFieldAction`}
                >Reset field</button>
                <button
                  className="fl-btn"
                  disabled={savingRole !== null || !(recoveryReasons[profile.role]?.trim())}
                  onClick={() => void resetProfile(profile.role, 'Agent')}
                  data-testid={`flow.settings.agent.${profile.role.toLowerCase()}.resetAgent`}
                >Reset agent</button>
              </div>
              {histories[profile.role]?.length ? (
                <div style={{ marginTop: 10 }} data-testid={`flow.settings.agent.${profile.role.toLowerCase()}.history`}>
                  {histories[profile.role].map((version) => (
                    <div key={version.version} style={{ display: 'flex', gap: 8, alignItems: 'center', justifyContent: 'space-between', padding: '5px 0' }}>
                      <span className="fl-empty">v{version.version} · {version.reason} · user {version.actorUserId}</span>
                      <button
                        className="fl-btn"
                        disabled={savingRole !== null || !(recoveryReasons[profile.role]?.trim())}
                        onClick={() => void restoreProfile(profile.role, version.version)}
                        data-testid={`flow.settings.agent.${profile.role.toLowerCase()}.restore.${version.version}`}
                      >Restore</button>
                    </div>
                  ))}
                </div>
              ) : <p className="fl-empty">No published profile versions yet.</p>}
            </div>
            </>
            )}
          </div>
        );
      })}
    </div>
  );
}

function EffectiveProfileSummary({ role, effective }: { role: string; effective?: EffectiveSkeletonAgentProfile }) {
  if (!effective) {
    return null;
  }

  const roleKey = role.toLowerCase();
  const providerSource = fieldSource(effective, 'provider');
  const modelSource = fieldSource(effective, 'model');
  const skillSource = fieldSource(effective, 'effectiveSkill');
  const personalitySource = fieldSource(effective, 'effectivePersonality');
  const providerLabel = effective.provider || 'Deterministic';
  const modelLabel = effective.model || 'No model';

  return (
    <div style={{ marginTop: 10, borderTop: '1px solid var(--fl-line)', paddingTop: 10 }} data-testid={`flow.settings.agent.${roleKey}.effective`}>
      <p className="fl-plabel" style={{ marginTop: 0 }}>
        Effective profile
      </p>
      <p className="fl-empty" style={{ marginTop: 0 }} data-testid={`flow.settings.agent.${roleKey}.effective.summary`}>
        {providerLabel} / {modelLabel} / {effective.timeoutSeconds}s
      </p>
      <dl className="fl-kv" style={{ marginTop: 8 }}>
        <dt>Provider source</dt>
        <dd style={{ margin: 0 }} data-testid={`flow.settings.agent.${roleKey}.effective.providerSource`}>
          {formatSource(providerSource)}
        </dd>
        <dt>Model source</dt>
        <dd style={{ margin: 0 }} data-testid={`flow.settings.agent.${roleKey}.effective.modelSource`}>
          {formatSource(modelSource)}
        </dd>
        <dt>Skill source</dt>
        <dd style={{ margin: 0 }} data-testid={`flow.settings.agent.${roleKey}.effective.skillSource`}>
          {formatSource(skillSource)}
        </dd>
        <dt>Personality source</dt>
        <dd style={{ margin: 0 }} data-testid={`flow.settings.agent.${roleKey}.effective.personalitySource`}>
          {formatSource(personalitySource)}
        </dd>
        <dt>Effective hash</dt>
        <dd style={{ margin: 0 }} data-testid={`flow.settings.agent.${roleKey}.effective.hash`}>
          {effective.effectiveHash}
        </dd>
      </dl>
    </div>
  );
}

function fieldSource(effective: EffectiveSkeletonAgentProfile, field: string) {
  return effective.fieldSources.find((source) => source.field === field);
}

function formatSource(source: EffectiveSkeletonAgentProfile['fieldSources'][number] | undefined) {
  if (!source) {
    return 'Unknown';
  }

  const inherited = source.inherited ? 'inherited' : 'set here';
  return `${source.sourceLayer} - ${source.sourceLabel} (${inherited})`;
}

function displayAgentRole(role: string) {
  return role === 'Analyst' ? 'Workshop guide' : role;
}
