import { useState } from 'react';
import { useProjectContext } from '../../state/useProjectContext';
import { NotImplementedPanel } from '../components/NotImplementedPanel';
import { AgentsPanel } from './AgentsPanel';
import { AiConnectionsPanel } from './AiConnectionsPanel';
import { AboutPanel } from './AboutPanel';
import {
  ApprovalPolicyDraft,
  autonomyProfiles,
  loadApprovalPolicy,
  saveApprovalPolicy
} from './approvalPolicy';

export function SettingsScreen() {
  const project = useProjectContext();
  const [policy, setPolicy] = useState<ApprovalPolicyDraft>(() => loadApprovalPolicy());
  const [policySaved, setPolicySaved] = useState(false);

  const updatePolicy = (next: ApprovalPolicyDraft) => {
    setPolicy(next);
    setPolicySaved(false);
  };

  const savePolicy = () => {
    saveApprovalPolicy(policy);
    setPolicySaved(true);
  };

  const currentTenant = project.tenants.find((tenant) => tenant.id === project.selectedTenantId);

  return (
    <div>
      <h1 className="fl-h1">Settings</h1>
      <p className="fl-sub">
        How much of the pipeline waits for a human
        {currentTenant ? ` - ${currentTenant.name}` : ''}.
      </p>

      <div className="fl-banner" data-testid="flow.settings.banner">
        Tenant membership and roles now live under Library &gt; Members. The approval policy below is still a local
        draft until the backend policy endpoints land.
      </div>

      <div className="fl-settings-grid">
        <div className="fl-panel-box">
          <p className="fl-plabel">Approval policy - how much waits for a human</p>

          {autonomyProfiles.map((option) => (
            <label
              key={option.kind}
              className={policy.autonomyProfile === option.kind ? 'fl-radio-card fl-sel' : 'fl-radio-card'}
            >
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

          <p className="fl-plabel" style={{ marginTop: 16 }}>
            Human approval per action class
          </p>
          {policy.actionClasses.map((action) => (
            <div className="fl-toggle-row" key={action.id}>
              <span>
                {action.label}
                {action.locked ? <div className="fl-lockednote">{action.lockedReason}</div> : null}
              </span>
              <input
                type="checkbox"
                className="fl-switch"
                checked={action.requiresHuman}
                disabled={action.locked}
                aria-label={`Require human approval for: ${action.label}`}
                onChange={(event) =>
                  updatePolicy({
                    ...policy,
                    actionClasses: policy.actionClasses.map((candidate) =>
                      candidate.id === action.id
                        ? { ...candidate, requiresHuman: event.target.checked }
                        : candidate
                    )
                  })
                }
              />
            </div>
          ))}

          <div className="fl-foot" style={{ marginTop: 12 }}>
            <span className="fl-gatemsg fl-okmsg" style={{ fontSize: 12.5 }}>
              {policySaved ? 'Draft saved locally.' : 'Locked rows are backend invariants - no setting can unlock them.'}
            </span>
            <button className="fl-btn fl-pri" onClick={savePolicy} data-testid="flow.settings.savePolicy">
              Save policy draft
            </button>
          </div>
        </div>
      </div>

      <div style={{ marginTop: 16 }}>
        <NotImplementedPanel
          title="Human-intervention dial - backend contract"
          path={
            project.selectedProjectId === null
              ? null
              : `/api/projects/${project.selectedProjectId}/authority/intervention-dial`
          }
          missingPrerequisite="Select a project to probe the dial's backend contract."
          testId="flow.settings.interventionDial"
        />
      </div>

      <div style={{ marginTop: 16 }}>
        <AiConnectionsPanel />
      </div>

      <div style={{ marginTop: 16 }}>
        <AgentsPanel />
      </div>

      <div style={{ marginTop: 16 }}>
        <p className="fl-plabel">Advanced</p>
        <AboutPanel />
      </div>
    </div>
  );
}
