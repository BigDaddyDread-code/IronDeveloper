import { FormEvent, useCallback, useEffect, useState } from 'react';
import type { TenantUser } from '../../api/types';
import { useProjectContext } from '../../state/useProjectContext';
import { useSessionContext } from '../../state/useSessionContext';
import {
  ApprovalPolicyDraft,
  autonomyProfiles,
  loadApprovalPolicy,
  saveApprovalPolicy
} from './approvalPolicy';
import { FlowRole, flowRoles } from './usersDraft';
import { AgentsPanel } from './AgentsPanel';

const apiRoles = ['Owner', 'TenantAdmin', 'Approver', 'Reviewer', 'Operator', 'Viewer', 'Member'];

export function SettingsScreen() {
  const session = useSessionContext();
  const project = useProjectContext();

  const [users, setUsers] = useState<TenantUser[]>([]);
  const [usersState, setUsersState] = useState<'loading' | 'ready' | 'error'>('loading');
  const [usersError, setUsersError] = useState<string | null>(null);
  const [isMutating, setIsMutating] = useState(false);

  const [policy, setPolicy] = useState<ApprovalPolicyDraft>(() => loadApprovalPolicy());
  const [policySaved, setPolicySaved] = useState(false);

  const [newEmail, setNewEmail] = useState('');
  const [newName, setNewName] = useState('');
  const [newPassword, setNewPassword] = useState('');
  const [newRole, setNewRole] = useState('Viewer');

  const tenantId = project.selectedTenantId;

  const refreshUsers = useCallback(async () => {
    if (tenantId === null) {
      setUsers([]);
      setUsersState('ready');
      return;
    }
    setUsersState('loading');
    setUsersError(null);
    try {
      setUsers(await session.client.getTenantUsers(tenantId));
      setUsersState('ready');
    } catch (error: unknown) {
      setUsersError(error instanceof Error ? error.message : 'Could not load tenant users.');
      setUsersState('error');
    }
  }, [session.client, tenantId]);

  useEffect(() => {
    void refreshUsers();
  }, [refreshUsers]);

  const runMutation = useCallback(
    async (mutation: () => Promise<void>) => {
      setIsMutating(true);
      setUsersError(null);
      try {
        await mutation();
        await refreshUsers();
      } catch (error: unknown) {
        setUsersError(error instanceof Error ? error.message : 'The change was not applied.');
      } finally {
        setIsMutating(false);
      }
    },
    [refreshUsers]
  );

  const addUser = (event: FormEvent) => {
    event.preventDefault();
    if (tenantId === null) {
      return;
    }
    const email = newEmail.trim();
    const displayName = newName.trim();
    if (email.length === 0 || displayName.length === 0) {
      return;
    }
    void runMutation(async () => {
      await session.client.createTenantUser(tenantId, {
        email,
        displayName,
        password: newPassword.length > 0 ? newPassword : null,
        role: newRole
      });
      setNewEmail('');
      setNewName('');
      setNewPassword('');
      setNewRole('Viewer');
    });
  };

  const setUserRole = (userId: number, role: string) => {
    if (tenantId === null) {
      return;
    }
    void runMutation(() => session.client.setTenantUserRole(tenantId, userId, role));
  };

  const removeUser = (userId: number) => {
    if (tenantId === null) {
      return;
    }
    void runMutation(() => session.client.removeTenantUser(tenantId, userId));
  };

  const updatePolicy = (next: ApprovalPolicyDraft) => {
    setPolicy(next);
    setPolicySaved(false);
  };

  const savePolicy = () => {
    saveApprovalPolicy(policy);
    setPolicySaved(true);
  };

  const currentTenant = project.tenants.find((tenant) => tenant.id === tenantId);
  const currentUserId = project.userProfile?.userId;

  return (
    <div>
      <h1 className="fl-h1">Settings</h1>
      <p className="fl-sub">
        Users, roles, and how much of the pipeline waits for a human
        {currentTenant ? ` — ${currentTenant.name}` : ''}.
      </p>

      <div className="fl-banner" data-testid="flow.settings.banner">
        Role assignment decides visibility, never mutation authority — the backend's authority gates remain the only
        authority. User management is live against the tenant user API and security-audited. The approval policy below
        is still a local draft until the backend policy endpoints land.
      </div>

      <div className="fl-settings-grid">
        <div className="fl-panel-box">
          <p className="fl-plabel">Users and roles</p>

          {tenantId === null ? (
            <p className="fl-empty">Select a tenant to manage its users.</p>
          ) : usersState === 'loading' ? (
            <p className="fl-empty">Loading users…</p>
          ) : (
            <table className="fl-table">
              <thead>
                <tr>
                  <th>User</th>
                  <th>Role</th>
                  <th aria-label="Actions" />
                </tr>
              </thead>
              <tbody>
                {users.map((user) => (
                  <tr key={user.id}>
                    <td>
                      <strong>{user.displayName}</strong>
                      <div style={{ fontSize: 12, color: 'var(--fl-muted)' }}>
                        {user.email}
                        {user.id === currentUserId ? ' · you' : ''}
                        {user.isActive ? '' : ' · inactive'}
                      </div>
                    </td>
                    <td>
                      <select
                        className="fl-select"
                        value={user.role}
                        disabled={isMutating}
                        onChange={(event) => setUserRole(user.id, event.target.value)}
                      >
                        {apiRoles.map((role) => (
                          <option key={role} value={role}>
                            {role}
                          </option>
                        ))}
                      </select>
                    </td>
                    <td style={{ textAlign: 'right' }}>
                      <button
                        className="fl-btn fl-mini"
                        disabled={isMutating || user.id === currentUserId}
                        title={user.id === currentUserId ? 'You cannot remove yourself.' : 'Remove tenant membership'}
                        onClick={() => removeUser(user.id)}
                      >
                        Remove
                      </button>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          )}

          {usersError ? <div className="fl-error">{usersError}</div> : null}

          <form onSubmit={addUser} style={{ display: 'grid', gap: 8, marginTop: 14 }} data-testid="flow.settings.addUser">
            <p className="fl-plabel" style={{ margin: 0 }}>
              Add a user
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
              <input
                className="fl-select"
                type="password"
                style={{ flex: 2, minWidth: 140 }}
                placeholder="Password (new accounts)"
                value={newPassword}
                onChange={(event) => setNewPassword(event.target.value)}
              />
              <select className="fl-select" value={newRole} onChange={(event) => setNewRole(event.target.value)}>
                {apiRoles.map((role) => (
                  <option key={role} value={role}>
                    {role}
                  </option>
                ))}
              </select>
              <button className="fl-btn" type="submit" disabled={isMutating || tenantId === null}>
                Add user
              </button>
            </div>
            <p style={{ fontSize: 12, color: 'var(--fl-muted)', margin: 0 }}>
              An existing account is added by email without touching its password. The last owner cannot be demoted or
              removed.
            </p>
          </form>

          <p className="fl-plabel" style={{ marginTop: 18 }}>
            What each role sees
          </p>
          {flowRoles.map((option: { role: FlowRole; label: string; description: string }) => (
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

      <div style={{ marginTop: 16 }}>
        <AgentsPanel />
      </div>
    </div>
  );
}
