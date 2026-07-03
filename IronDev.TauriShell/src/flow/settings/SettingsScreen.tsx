import { FormEvent, useState } from 'react';
import { useProjectContext } from '../../state/useProjectContext';
import {
  ApprovalPolicyDraft,
  autonomyProfiles,
  loadApprovalPolicy,
  saveApprovalPolicy
} from './approvalPolicy';
import { DraftUser, FlowRole, flowRoles, loadDraftUsers, newDraftUser, saveDraftUsers } from './usersDraft';

export function SettingsScreen() {
  const project = useProjectContext();

  const [users, setUsers] = useState<DraftUser[]>(() => loadDraftUsers());
  const [policy, setPolicy] = useState<ApprovalPolicyDraft>(() => loadApprovalPolicy());
  const [policySaved, setPolicySaved] = useState(false);

  const [newEmail, setNewEmail] = useState('');
  const [newName, setNewName] = useState('');
  const [newRole, setNewRole] = useState<FlowRole>('viewer');

  const updateUsers = (next: DraftUser[]) => {
    setUsers(next);
    saveDraftUsers(next);
  };

  const addUser = (event: FormEvent) => {
    event.preventDefault();
    const email = newEmail.trim();
    const name = newName.trim();
    if (email.length === 0 || name.length === 0) {
      return;
    }
    updateUsers([...users, newDraftUser(email, name, newRole)]);
    setNewEmail('');
    setNewName('');
    setNewRole('viewer');
  };

  const setUserRole = (id: string, role: FlowRole) => {
    updateUsers(users.map((user) => (user.id === id ? { ...user, role } : user)));
  };

  const removeUser = (id: string) => {
    updateUsers(users.filter((user) => user.id !== id));
  };

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
      <p className="fl-sub">Users, roles, and how much of the pipeline waits for a human.</p>

      <div className="fl-banner" data-testid="flow.settings.banner">
        Settings request policy — they do not grant it. Role assignment decides visibility, never mutation authority.
        User and approval records below are a local draft until the backend role and policy endpoints land (Block F);
        the backend's authority gates remain the only authority either way.
      </div>

      <div className="fl-settings-grid">
        <div className="fl-panel-box">
          <p className="fl-plabel">Users and roles</p>

          <table className="fl-table">
            <thead>
              <tr>
                <th>User</th>
                <th>Role</th>
                <th aria-label="Actions" />
              </tr>
            </thead>
            <tbody>
              <tr>
                <td>
                  <strong>{project.userProfile?.displayName ?? 'Current user'}</strong>
                  <div style={{ fontSize: 12, color: 'var(--fl-muted)' }}>
                    {project.userProfile?.email ?? '—'} · {currentTenant?.name ?? 'no tenant'} · signed in
                  </div>
                </td>
                <td>
                  <span className="fl-chip fl-ok">tenant admin</span>
                </td>
                <td />
              </tr>
              {users.map((user) => (
                <tr key={user.id}>
                  <td>
                    <strong>{user.displayName}</strong>
                    <div style={{ fontSize: 12, color: 'var(--fl-muted)' }}>{user.email} · draft</div>
                  </td>
                  <td>
                    <select
                      className="fl-select"
                      value={user.role}
                      onChange={(event) => setUserRole(user.id, event.target.value as FlowRole)}
                    >
                      {flowRoles.map((option) => (
                        <option key={option.role} value={option.role}>
                          {option.label}
                        </option>
                      ))}
                    </select>
                  </td>
                  <td style={{ textAlign: 'right' }}>
                    <button className="fl-btn fl-mini" onClick={() => removeUser(user.id)}>
                      Remove
                    </button>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>

          <form onSubmit={addUser} style={{ display: 'grid', gap: 8, marginTop: 14 }} data-testid="flow.settings.addUser">
            <p className="fl-plabel" style={{ margin: 0 }}>
              Add a user (draft)
            </p>
            <div style={{ display: 'flex', gap: 8, flexWrap: 'wrap' }}>
              <input
                className="fl-select"
                style={{ flex: 2, minWidth: 160 }}
                placeholder="name@company.com"
                value={newEmail}
                onChange={(event) => setNewEmail(event.target.value)}
              />
              <input
                className="fl-select"
                style={{ flex: 2, minWidth: 140 }}
                placeholder="Display name"
                value={newName}
                onChange={(event) => setNewName(event.target.value)}
              />
              <select className="fl-select" value={newRole} onChange={(event) => setNewRole(event.target.value as FlowRole)}>
                {flowRoles.map((option) => (
                  <option key={option.role} value={option.role}>
                    {option.label}
                  </option>
                ))}
              </select>
              <button className="fl-btn" type="submit">
                Add user
              </button>
            </div>
          </form>

          <p className="fl-plabel" style={{ marginTop: 18 }}>
            What each role sees
          </p>
          {flowRoles.map((option) => (
            <div key={option.role} style={{ fontSize: 12.5, color: 'var(--fl-ink2)', padding: '3px 0' }}>
              <strong style={{ color: 'var(--fl-ink)' }}>{option.label}</strong> — {option.description}
            </div>
          ))}
        </div>

        <div className="fl-panel-box">
          <p className="fl-plabel">Approval policy — how much waits for a human</p>

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
                    actionClasses: policy.actionClasses.map((a) =>
                      a.id === action.id ? { ...a, requiresHuman: event.target.checked } : a
                    )
                  })
                }
              />
            </div>
          ))}

          <div className="fl-foot" style={{ marginTop: 12 }}>
            <span className="fl-gatemsg fl-okmsg" style={{ fontSize: 12.5 }}>
              {policySaved ? 'Draft saved locally.' : 'Locked rows are backend invariants — no setting can unlock them.'}
            </span>
            <button className="fl-btn fl-pri" onClick={savePolicy} data-testid="flow.settings.savePolicy">
              Save policy draft
            </button>
          </div>
        </div>
      </div>
    </div>
  );
}
