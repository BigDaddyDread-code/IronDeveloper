import { useEffect, useState } from 'react';
import { useProjectContext } from '../../state/useProjectContext';
import { useSessionContext } from '../../state/useSessionContext';
import { NotImplementedPanel } from '../components/NotImplementedPanel';
import { navigateProductPath, projectPath, type SettingsSection } from '../navigation/productRoutes';
import { AboutPanel } from './AboutPanel';
import { AgentsPanel } from './AgentsPanel';
import { AiConnectionsPanel } from './AiConnectionsPanel';
import {
  ApprovalPolicyDraft,
  autonomyProfiles,
  loadApprovalPolicy,
  saveApprovalPolicy
} from './approvalPolicy';

const sections: Array<{ id: SettingsSection; label: string }> = [
  { id: 'project', label: 'Project' },
  { id: 'aiConnections', label: 'AI Connections' },
  { id: 'agents', label: 'Agents' },
  { id: 'safety', label: 'Safety' },
  { id: 'runtime', label: 'Runtime' },
  { id: 'advanced', label: 'Advanced' }
];

export function SettingsScreen({ initialSection = 'project' }: { initialSection?: SettingsSection }) {
  const project = useProjectContext();
  const session = useSessionContext();
  const [section, setSection] = useState<SettingsSection>(initialSection);
  const [policy, setPolicy] = useState<ApprovalPolicyDraft>(() => loadApprovalPolicy());
  const [policySaved, setPolicySaved] = useState(false);
  const currentTenant = project.tenants.find((tenant) => tenant.id === project.selectedTenantId);

  useEffect(() => setSection(initialSection), [initialSection]);

  const updatePolicy = (next: ApprovalPolicyDraft) => {
    setPolicy(next);
    setPolicySaved(false);
  };

  const savePolicy = () => {
    saveApprovalPolicy(policy);
    setPolicySaved(true);
  };

  return (
    <div data-testid="flow.settings.hub">
      <h1 className="fl-h1">Settings</h1>
      <p className="fl-sub">Configuration and runtime truth for {project.selectedProjectName ?? 'the selected project'}.</p>

      <div className="fl-settings-hub" role="tablist" aria-label="Settings sections">
        {sections.map((item) => (
          <button
            key={item.id}
            type="button"
            role="tab"
            aria-selected={section === item.id}
            className={section === item.id ? 'fl-on' : ''}
            onClick={() => setSection(item.id)}
            data-testid={`flow.settings.section.${item.id}`}
          >
            {item.label}
          </button>
        ))}
      </div>

      <section className="fl-settings-section" role="tabpanel" data-testid={`flow.settings.panel.${section}`}>
        {section === 'project' ? (
          <div className="fl-panel-box">
            <p className="fl-plabel">Project</p>
            <dl className="fl-kv">
              <dt>Name</dt><dd>{project.selectedProjectName ?? 'No project selected'}</dd>
              <dt>Project ID</dt><dd>{project.selectedProjectId ?? 'Not selected'}</dd>
              <dt>Tenant</dt><dd>{currentTenant?.name ?? 'Not selected'}</dd>
              <dt>Access state</dt><dd>{project.accessStatus}</dd>
            </dl>
            <button
              className="fl-btn"
              type="button"
              disabled={project.selectedProjectId === null}
              onClick={() => project.selectedProjectId !== null && navigateProductPath(projectPath(project.selectedProjectId, 'setup'))}
              data-testid="flow.settings.project.setup"
            >
              Open project setup
            </button>
          </div>
        ) : null}

        {section === 'aiConnections' ? <AiConnectionsPanel /> : null}
        {section === 'agents' ? <AgentsPanel /> : null}

        {section === 'safety' ? (
          <>
            <div className="fl-banner" data-testid="flow.settings.banner">
              Tenant membership and roles live under Library &gt; Members. Settings change preference, never mutation authority. The approval policy below remains a local draft until backend policy endpoints land.
            </div>
            <div className="fl-settings-grid">
              <div className="fl-panel-box">
                <p className="fl-plabel">Approval policy - how much waits for a human</p>
                {autonomyProfiles.map((option) => (
                  <label key={option.kind} className={policy.autonomyProfile === option.kind ? 'fl-radio-card fl-sel' : 'fl-radio-card'}>
                    <input
                      type="radio"
                      name="autonomy"
                      checked={policy.autonomyProfile === option.kind}
                      onChange={() => updatePolicy({ ...policy, autonomyProfile: option.kind })}
                    />
                    <span>
                      <p className="fl-radio-title">{option.title}</p>
                      <p className="fl-radio-desc">{option.description}</p>
                    </span>
                  </label>
                ))}
                <p className="fl-plabel" style={{ marginTop: 16 }}>Human approval per action class</p>
                {policy.actionClasses.map((action) => (
                  <div className="fl-toggle-row" key={action.id}>
                    <span>{action.label}{action.locked ? <div className="fl-lockednote">{action.lockedReason}</div> : null}</span>
                    <input
                      type="checkbox"
                      className="fl-switch"
                      checked={action.requiresHuman}
                      disabled={action.locked}
                      aria-label={`Require human approval for: ${action.label}`}
                      onChange={(event) => updatePolicy({
                        ...policy,
                        actionClasses: policy.actionClasses.map((candidate) => candidate.id === action.id
                          ? { ...candidate, requiresHuman: event.target.checked }
                          : candidate)
                      })}
                    />
                  </div>
                ))}
                <div className="fl-foot" style={{ marginTop: 12 }}>
                  <span className="fl-gatemsg fl-okmsg" style={{ fontSize: 12.5 }}>
                    {policySaved ? 'Draft saved locally.' : 'Locked rows are backend invariants - no setting can unlock them.'}
                  </span>
                  <button className="fl-btn fl-pri" onClick={savePolicy} data-testid="flow.settings.savePolicy">Save policy draft</button>
                </div>
              </div>
            </div>
            <div style={{ marginTop: 16 }}>
              <NotImplementedPanel
                title="Human-intervention dial - backend contract"
                path={project.selectedProjectId === null ? null : `/api/projects/${project.selectedProjectId}/authority/intervention-dial`}
                missingPrerequisite="Select a project to probe the dial's backend contract."
                testId="flow.settings.interventionDial"
              />
            </div>
          </>
        ) : null}

        {section === 'runtime' ? (
          <div className="fl-panel-box">
            <p className="fl-plabel">Runtime</p>
            <dl className="fl-kv" data-testid="flow.settings.runtime">
              <dt>API</dt><dd>{session.apiStatus.status}</dd>
              <dt>API URL</dt><dd>{session.config.apiBaseUrl}</dd>
              <dt>Environment</dt><dd>{session.environmentInfo?.environment ?? 'Not reported'}</dd>
              <dt>Database</dt><dd>{session.environmentInfo?.database ?? 'Not reported'}</dd>
              <dt>Workspace root</dt><dd>{session.environmentInfo?.workspaceRoot ?? 'Not reported'}</dd>
              <dt>Real repo writes</dt><dd>{session.environmentInfo?.dangerRealRepoWritesEnabled ? 'Enabled' : 'Disabled'}</dd>
            </dl>
          </div>
        ) : null}

        {section === 'advanced' ? <AboutPanel /> : null}
      </section>
    </div>
  );
}
