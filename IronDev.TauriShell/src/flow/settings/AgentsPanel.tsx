import { useCallback, useEffect, useState } from 'react';
import { IronDevApiError } from '../../api/ironDevApi';
import type { SkeletonAgentProfile } from '../../api/types';
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
  const [drafts, setDrafts] = useState<Record<string, SkeletonAgentProfile>>({});
  const [state, setState] = useState<'loading' | 'ready' | 'error'>('loading');
  const [error, setError] = useState<string | null>(null);
  const [savingRole, setSavingRole] = useState<string | null>(null);
  const [savedRole, setSavedRole] = useState<string | null>(null);

  const load = useCallback(async () => {
    setState('loading');
    try {
      const result = await session.client.listAgentProfiles();
      setProfiles(result);
      setDrafts(Object.fromEntries(result.map((p) => [p.role, p])));
      setState('ready');
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Could not load agent profiles.');
      setState('error');
    }
  }, [session.client]);

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
        const outcome = await session.client.updateAgentProfile(role, {
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
        setSavedRole(role);
      } catch (e) {
        // A refused update (e.g. secret detected) returns 400 with the outcome.
        const body = e instanceof IronDevApiError ? (e.body as { failureReason?: string } | undefined) : undefined;
        setError(body?.failureReason ?? (e instanceof Error ? e.message : 'Save failed.'));
      } finally {
        setSavingRole(null);
      }
    },
    [drafts, savingRole, session.client]
  );

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
        return (
          <div className="fl-panel-box" key={profile.role} style={{ marginTop: 10 }} data-testid={`flow.settings.agent.${profile.role.toLowerCase()}`}>
            <p className="fl-plabel" style={{ marginTop: 0 }}>
              {displayName}
            </p>
            {isDeterministic ? (
              <p className="fl-empty" style={{ marginTop: 0 }} data-testid={`flow.settings.agent.${profile.role.toLowerCase()}.deterministic`}>
                Deterministic — the orchestrator composes the loop and enforces the gates. It runs no model, so there is
                nothing to configure here. (It never judges whether the work is satisfactory; only the human gate approves.)
              </p>
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
                {savingRole === profile.role ? 'Saving…' : 'Save'}
              </button>
              {savedRole === profile.role ? <span style={{ fontSize: 12.5, color: 'var(--fl-acc-ink)' }}>Saved.</span> : null}
            </div>
            </>
            )}
          </div>
        );
      })}
    </div>
  );
}

function displayAgentRole(role: string) {
  return role === 'Analyst' ? 'Workshop guide' : role;
}
