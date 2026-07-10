import { FormEvent, useCallback, useEffect, useMemo, useState } from 'react';
import { IronDevApiError } from '../../api/ironDevApi';
import type {
  ProjectChannelDirectoryEntry,
  ProjectChannelMembershipEntry,
  ProjectMemberDirectoryEntry,
  ProjectMemberDirectoryResponse
} from '../../api/types';
import { StatusBadge } from '../../components/StatusBadge';
import { useSessionContext } from '../../state/useSessionContext';
import { RouteOutcomeScreen } from '../components/RouteOutcomeScreen';
import { libraryPath, navigateProductPath } from '../navigation/productRoutes';

interface MembersScreenProps {
  projectId: number;
}

type MemberLoadState = 'loading' | 'ready' | 'empty' | 'notFound' | 'unavailable';
type ChannelMembershipDraft = { channelRole: string; notificationLevel: string };
type ChannelAddDraft = ChannelMembershipDraft & { userId: string };

export function MembersScreen({ projectId }: MembersScreenProps) {
  const session = useSessionContext();
  const [directory, setDirectory] = useState<ProjectMemberDirectoryResponse | null>(null);
  const [loadState, setLoadState] = useState<MemberLoadState>('loading');
  const [loadError, setLoadError] = useState('');
  const [reloadKey, setReloadKey] = useState(0);
  const [roleDrafts, setRoleDrafts] = useState<Record<number, string>>({});
  const [channelDrafts, setChannelDrafts] = useState<Record<string, ChannelMembershipDraft>>({});
  const [channelAddDrafts, setChannelAddDrafts] = useState<Record<number, ChannelAddDraft>>({});
  const [mutationError, setMutationError] = useState('');
  const [notice, setNotice] = useState('');
  const [busyAction, setBusyAction] = useState<string | null>(null);
  const [addOpen, setAddOpen] = useState(false);
  const [newEmail, setNewEmail] = useState('');
  const [newName, setNewName] = useState('');
  const [newPassword, setNewPassword] = useState('');
  const [newRole, setNewRole] = useState('Viewer');
  const [removeCandidateId, setRemoveCandidateId] = useState<number | null>(null);
  const [channelRemoveCandidate, setChannelRemoveCandidate] = useState<{ channelId: number; userId: number } | null>(null);

  const applyDirectory = useCallback((loaded: ProjectMemberDirectoryResponse) => {
    setDirectory(loaded);
    setRoleDrafts(Object.fromEntries(loaded.members.map((member) => [member.userId, member.tenantRole])));
    setChannelDrafts(Object.fromEntries(loaded.channels.flatMap((channel) =>
      channel.members.map((member) => [channelDraftKey(channel.channelId, member.userId), {
        channelRole: member.channelRole,
        notificationLevel: member.notificationLevel
      }])
    )));
    setChannelAddDrafts(Object.fromEntries(loaded.channels.map((channel) => [channel.channelId, {
      userId: '',
      channelRole: 'Member',
      notificationLevel: 'Mentions'
    }])));
    setLoadState(loaded.members.length === 0 ? 'empty' : 'ready');
  }, []);

  useEffect(() => {
    const controller = new AbortController();
    const load = async () => {
      setLoadState('loading');
      setLoadError('');
      try {
        applyDirectory(await session.client.getProjectMembers(projectId, controller.signal));
      } catch (error) {
        if (controller.signal.aborted) return;
        setLoadState(error instanceof IronDevApiError && error.status === 404 ? 'notFound' : 'unavailable');
        setLoadError(describeError(error, 'The member directory could not be loaded.'));
      }
    };
    void load();
    return () => controller.abort();
  }, [applyDirectory, projectId, reloadKey, session.client]);

  const refreshAfterMutation = useCallback(async () => {
    applyDirectory(await session.client.getProjectMembers(projectId));
  }, [applyDirectory, projectId, session.client]);

  const runMutation = useCallback(async (
    action: string,
    mutation: () => Promise<void>,
    success: string
  ) => {
    let mutationAccepted = false;
    setBusyAction(action);
    setMutationError('');
    setNotice('');
    try {
      await mutation();
      mutationAccepted = true;
      await refreshAfterMutation();
      setNotice(success);
      return true;
    } catch (error) {
      setMutationError(
        mutationAccepted
          ? 'The backend accepted the change, but the member directory could not be reloaded. Reload Members before making another change.'
          : describeError(error, 'The membership change was not applied.')
      );
      if (directory) {
        setRoleDrafts(Object.fromEntries(directory.members.map((member) => [member.userId, member.tenantRole])));
        setChannelDrafts(Object.fromEntries(directory.channels.flatMap((channel) =>
          channel.members.map((member) => [channelDraftKey(channel.channelId, member.userId), {
            channelRole: member.channelRole,
            notificationLevel: member.notificationLevel
          }])
        )));
      }
      return false;
    } finally {
      setBusyAction(null);
    }
  }, [directory, refreshAfterMutation]);

  const removeCandidate = useMemo(
    () => directory?.members.find((member) => member.userId === removeCandidateId) ?? null,
    [directory?.members, removeCandidateId]
  );
  const channelRemoval = useMemo(() => {
    if (!channelRemoveCandidate || !directory) return null;
    const channel = directory.channels.find((candidate) => candidate.channelId === channelRemoveCandidate.channelId);
    const member = directory.members.find((candidate) => candidate.userId === channelRemoveCandidate.userId);
    return channel && member ? { channel, member } : null;
  }, [channelRemoveCandidate, directory]);

  if (loadState === 'loading') {
    return <p className="fl-empty" data-testid="flow.members.loading">Loading members...</p>;
  }

  if (loadState === 'notFound') {
    return (
      <RouteOutcomeScreen
        kind="notFound"
        title="Project member directory not found"
        message="The backend did not return this project in the current tenant."
        nextSafeAction="Return to Library and choose a project returned by the backend."
        actionLabel="Back to Library"
        onAction={() => navigateProductPath(libraryPath(projectId, 'explorer'))}
      />
    );
  }

  if (loadState === 'unavailable' || !directory) {
    return (
      <RouteOutcomeScreen
        kind="unavailable"
        title="Members are unavailable"
        message={loadError || 'The backend did not return a member directory.'}
        nextSafeAction="Retry the backend-owned directory. No membership state has been changed."
        actionLabel="Retry"
        onAction={() => setReloadKey((current) => current + 1)}
      />
    );
  }

  const addMember = async (event: FormEvent) => {
    event.preventDefault();
    const email = newEmail.trim();
    const displayName = newName.trim();
    if (!email || !displayName) return;

    const created = await runMutation(
      'add',
      async () => {
        await session.client.createTenantUser(directory.tenantId, {
          email,
          displayName,
          password: newPassword || null,
          role: newRole
        });
      },
      `${displayName} was added to the tenant.`
    );
    if (created) {
      setNewEmail('');
      setNewName('');
      setNewPassword('');
      setNewRole('Viewer');
      setAddOpen(false);
    }
  };

  const saveRole = (member: ProjectMemberDirectoryEntry) => {
    const role = roleDrafts[member.userId] ?? member.tenantRole;
    void runMutation(
      `role-${member.userId}`,
      () => session.client.setTenantUserRole(directory.tenantId, member.userId, role),
      `${member.displayName}'s tenant role is now ${formatRole(role)}.`
    );
  };

  const removeMember = async () => {
    if (!removeCandidate) return;
    const removed = await runMutation(
      `remove-${removeCandidate.userId}`,
      () => session.client.removeTenantUser(directory.tenantId, removeCandidate.userId),
      `${removeCandidate.displayName} was removed from the tenant.`
    );
    if (removed) setRemoveCandidateId(null);
  };

  const saveChannelMembership = (channel: ProjectChannelDirectoryEntry, membership: ProjectChannelMembershipEntry) => {
    const draft = channelDrafts[channelDraftKey(channel.channelId, membership.userId)] ?? membership;
    void runMutation(
      `channel-${channel.channelId}-${membership.userId}`,
      () => session.client.setProjectChannelMembership(projectId, channel.channelId, membership.userId, draft),
      `${memberName(directory, membership.userId)}'s ${channel.name} membership was saved.`
    );
  };

  const addChannelMembership = (channel: ProjectChannelDirectoryEntry) => {
    const draft = channelAddDrafts[channel.channelId];
    const userId = Number(draft?.userId);
    if (!draft || !Number.isInteger(userId) || userId <= 0) return;
    void runMutation(
      `channel-add-${channel.channelId}`,
      () => session.client.setProjectChannelMembership(projectId, channel.channelId, userId, {
        channelRole: draft.channelRole,
        notificationLevel: draft.notificationLevel
      }),
      `${memberName(directory, userId)} was added to ${channel.name}.`
    );
  };

  const removeChannelMembership = async () => {
    if (!channelRemoval) return;
    const removed = await runMutation(
      `channel-remove-${channelRemoval.channel.channelId}-${channelRemoval.member.userId}`,
      () => session.client.removeProjectChannelMembership(projectId, channelRemoval.channel.channelId, channelRemoval.member.userId),
      `${channelRemoval.member.displayName} was removed from ${channelRemoval.channel.name}.`
    );
    if (removed) setChannelRemoveCandidate(null);
  };

  return (
    <section className="fl-members" data-testid="flow.members.directory" aria-labelledby="members-heading">
      <header className="fl-members__heading">
        <div>
          <p className="fl-plabel">Project administration</p>
          <h2 id="members-heading">Members</h2>
          <p>Tenant identities visible while working in {directory.projectName}.</p>
        </div>
        {directory.canAdministerTenantMembership ? (
          <button
            className="fl-btn fl-pri"
            type="button"
            onClick={() => setAddOpen((current) => !current)}
            data-testid="flow.members.add.toggle"
          >
            Add tenant member
          </button>
        ) : null}
      </header>

      <div className="fl-member-scope" aria-label="Membership model status">
        <MemberScope label="Your tenant role" value={formatRole(directory.currentUserTenantRole)} />
        <MemberScope label="Project membership" value={directory.projectMembershipStatus} />
        <MemberScope label="Channel membership" value={directory.channelMembershipStatus} />
      </div>

      {!directory.canAdministerTenantMembership ? (
        <p className="fl-member-permission" data-testid="flow.members.readOnly">
          Tenant administration requires Owner or Tenant admin. This directory remains readable.
        </p>
      ) : null}

      {addOpen ? (
        <form className="fl-member-add" onSubmit={addMember} data-testid="flow.members.add.form">
          <div className="fl-member-add__heading">
            <div>
              <h3>Add tenant member</h3>
              <p>This creates or reuses a tenant account. It does not create project or channel membership.</p>
            </div>
            <button className="fl-btn" type="button" onClick={() => setAddOpen(false)}>Cancel</button>
          </div>
          <div className="fl-member-add__fields">
            <label>Email<input value={newEmail} onChange={(event) => setNewEmail(event.target.value)} type="email" required data-testid="flow.members.add.email" /></label>
            <label>Display name<input value={newName} onChange={(event) => setNewName(event.target.value)} required data-testid="flow.members.add.name" /></label>
            <label>Password<input value={newPassword} onChange={(event) => setNewPassword(event.target.value)} type="password" placeholder="Required for new accounts" data-testid="flow.members.add.password" /></label>
            <label>
              Tenant role
              <select value={newRole} onChange={(event) => setNewRole(event.target.value)} data-testid="flow.members.add.role">{directory.availableTenantRoles.map(roleOption)}</select>
              <small className="fl-member-role-note">{roleDescription(newRole)}</small>
            </label>
          </div>
          <div className="fl-member-add__actions">
            <p>An existing account is reused by email without changing its password.</p>
            <button className="fl-btn fl-pri" type="submit" disabled={busyAction === 'add'} data-testid="flow.members.add.submit">
              {busyAction === 'add' ? 'Adding...' : 'Add member'}
            </button>
          </div>
        </form>
      ) : null}

      {mutationError ? <div className="fl-error" role="alert" data-testid="flow.members.error">{mutationError}</div> : null}
      {notice ? <div className="fl-member-notice" role="status" data-testid="flow.members.notice">{notice}</div> : null}

      {loadState === 'empty' ? (
        <div className="fl-members__empty" data-testid="flow.members.empty">
          <h3>No tenant members were returned</h3>
          <p>The backend returned an empty directory. No project or channel membership has been inferred.</p>
        </div>
      ) : (
        <div className="fl-member-list" aria-label="Tenant member directory">
          {directory.members.map((member) => (
            <MemberRow
              key={member.userId}
              member={member}
              canAdminister={directory.canAdministerTenantMembership}
              roles={directory.availableTenantRoles}
              roleDraft={roleDrafts[member.userId] ?? member.tenantRole}
              busyAction={busyAction}
              onRoleChange={(role) => setRoleDrafts((current) => ({ ...current, [member.userId]: role }))}
              onSaveRole={() => saveRole(member)}
              onRemove={() => setRemoveCandidateId(member.userId)}
            />
          ))}
        </div>
      )}

      {directory.members.length === 1 && directory.members[0]?.isCurrentUser ? (
        <p className="fl-member-permission">No additional tenant members are present.</p>
      ) : null}

      <section className="fl-channel-admin" aria-labelledby="channel-membership-heading" data-testid="flow.members.channels">
        <header className="fl-channel-admin__heading">
          <div>
            <p className="fl-plabel">Channel access</p>
            <h3 id="channel-membership-heading">Channel memberships</h3>
            <p>Explicit visibility, moderation role, and notification preference from the backend.</p>
          </div>
          <StatusBadge status={directory.channels.length > 0 ? 'ready' : 'neutral'}>{directory.channelMembershipStatus}</StatusBadge>
        </header>

        {directory.channels.length === 0 ? (
          <div className="fl-members__empty" data-testid="flow.members.channels.empty">
            <h4>No active channels</h4>
            <p>No channel membership has been inferred. Channel creation belongs to a later Chat slice.</p>
          </div>
        ) : (
          <div className="fl-channel-list">
            {directory.channels.map((channel) => (
              <ChannelMembershipPanel
                key={channel.channelId}
                channel={channel}
                members={directory.members}
                canAdminister={directory.canAdministerChannelMembership}
                roles={directory.availableChannelRoles}
                notificationLevels={directory.availableNotificationLevels}
                drafts={channelDrafts}
                addDraft={channelAddDrafts[channel.channelId] ?? { userId: '', channelRole: 'Member', notificationLevel: 'Mentions' }}
                busyAction={busyAction}
                onDraftChange={(userId, draft) => setChannelDrafts((current) => ({
                  ...current,
                  [channelDraftKey(channel.channelId, userId)]: draft
                }))}
                onAddDraftChange={(draft) => setChannelAddDrafts((current) => ({ ...current, [channel.channelId]: draft }))}
                onSave={(membership) => saveChannelMembership(channel, membership)}
                onAdd={() => addChannelMembership(channel)}
                onRemove={(userId) => setChannelRemoveCandidate({ channelId: channel.channelId, userId })}
              />
            ))}
          </div>
        )}
      </section>

      {removeCandidate ? (
        <div className="fl-member-remove" role="alertdialog" aria-labelledby="remove-member-heading" data-testid="flow.members.remove.confirm">
          <div>
            <h3 id="remove-member-heading">Remove {removeCandidate.displayName}?</h3>
            <p>Tenant access is removed. Authored messages, versions, decisions, and receipts retain their attribution.</p>
          </div>
          <div>
            <button className="fl-btn" type="button" onClick={() => setRemoveCandidateId(null)}>Cancel</button>
            <button className="fl-btn fl-danger" type="button" onClick={() => void removeMember()} disabled={busyAction === `remove-${removeCandidate.userId}`} data-testid="flow.members.remove.submit">
              {busyAction === `remove-${removeCandidate.userId}` ? 'Removing...' : 'Remove tenant membership'}
            </button>
          </div>
        </div>
      ) : null}

      {channelRemoval ? (
        <div className="fl-member-remove" role="alertdialog" aria-labelledby="remove-channel-member-heading" data-testid="flow.members.channel.remove.confirm">
          <div>
            <h3 id="remove-channel-member-heading">Remove {channelRemoval.member.displayName} from {channelRemoval.channel.name}?</h3>
            <p>This removes channel visibility or moderation membership only. It does not alter tenant access or authored history.</p>
          </div>
          <div>
            <button className="fl-btn" type="button" onClick={() => setChannelRemoveCandidate(null)}>Cancel</button>
            <button className="fl-btn fl-danger" type="button" onClick={() => void removeChannelMembership()} disabled={busyAction !== null} data-testid="flow.members.channel.remove.submit">
              {busyAction?.startsWith('channel-remove-') ? 'Removing...' : 'Remove channel membership'}
            </button>
          </div>
        </div>
      ) : null}

      <p className="fl-member-boundary">{directory.boundary}</p>
    </section>
  );
}

function ChannelMembershipPanel({
  channel,
  members,
  canAdminister,
  roles,
  notificationLevels,
  drafts,
  addDraft,
  busyAction,
  onDraftChange,
  onAddDraftChange,
  onSave,
  onAdd,
  onRemove
}: {
  channel: ProjectChannelDirectoryEntry;
  members: ProjectMemberDirectoryEntry[];
  canAdminister: boolean;
  roles: string[];
  notificationLevels: string[];
  drafts: Record<string, ChannelMembershipDraft>;
  addDraft: ChannelAddDraft;
  busyAction: string | null;
  onDraftChange: (userId: number, draft: ChannelMembershipDraft) => void;
  onAddDraftChange: (draft: ChannelAddDraft) => void;
  onSave: (membership: ProjectChannelMembershipEntry) => void;
  onAdd: () => void;
  onRemove: (userId: number) => void;
}) {
  const availableMembers = members.filter((member) => member.isActive && !channel.members.some((membership) => membership.userId === member.userId));
  return (
    <article className="fl-channel-row" data-testid={`flow.members.channel.${channel.channelId}`}>
      <header>
        <div>
          <h4>{channel.name}</h4>
          <p>{channel.description || `${channel.channelKind} project conversation.`}</p>
        </div>
        <StatusBadge status={channel.visibility === 'MembersOnly' ? 'warning' : 'neutral'}>
          {channel.visibility === 'MembersOnly' ? 'Members only' : 'Project visible'}
        </StatusBadge>
      </header>
      <p className="fl-channel-scope">
        {channel.visibility === 'MembersOnly'
          ? 'Only listed members can discover and open this channel.'
          : 'Tenant project users can discover this channel; explicit membership sets moderation and notifications.'}
      </p>

      <div className="fl-channel-members">
        {channel.members.length === 0 ? <p className="fl-member-permission">No explicit channel members.</p> : null}
        {channel.members.map((membership) => {
          const member = members.find((candidate) => candidate.userId === membership.userId);
          const draft = drafts[channelDraftKey(channel.channelId, membership.userId)] ?? membership;
          const changed = draft.channelRole !== membership.channelRole || draft.notificationLevel !== membership.notificationLevel;
          return (
            <div className="fl-channel-member" key={membership.userId} data-testid={`flow.members.channel.${channel.channelId}.member.${membership.userId}`}>
              <strong>{member?.displayName ?? `User ${membership.userId}`}</strong>
              {canAdminister ? (
                <>
                  <label>Role<select value={draft.channelRole} disabled={busyAction !== null} onChange={(event) => onDraftChange(membership.userId, { ...draft, channelRole: event.target.value })} data-testid={`flow.members.channel.${channel.channelId}.role.${membership.userId}`}>{roles.map(channelOption)}</select></label>
                  <label>Notifications<select value={draft.notificationLevel} disabled={busyAction !== null} onChange={(event) => onDraftChange(membership.userId, { ...draft, notificationLevel: event.target.value })} data-testid={`flow.members.channel.${channel.channelId}.notifications.${membership.userId}`}>{notificationLevels.map(channelOption)}</select></label>
                  <button className="fl-btn" type="button" disabled={!changed || busyAction !== null} onClick={() => onSave(membership)} data-testid={`flow.members.channel.${channel.channelId}.save.${membership.userId}`}>Save</button>
                  <button className="fl-btn" type="button" disabled={busyAction !== null} onClick={() => onRemove(membership.userId)} data-testid={`flow.members.channel.${channel.channelId}.remove.${membership.userId}`}>Remove</button>
                </>
              ) : (
                <span>{formatChannelRole(membership.channelRole)} / {formatNotificationLevel(membership.notificationLevel)}</span>
              )}
            </div>
          );
        })}
      </div>

      {canAdminister && availableMembers.length > 0 ? (
        <div className="fl-channel-add" data-testid={`flow.members.channel.${channel.channelId}.add`}>
          <label>Member<select value={addDraft.userId} onChange={(event) => onAddDraftChange({ ...addDraft, userId: event.target.value })} data-testid={`flow.members.channel.${channel.channelId}.add.member`}><option value="">Select tenant member</option>{availableMembers.map((member) => <option key={member.userId} value={member.userId}>{member.displayName}</option>)}</select></label>
          <label>Role<select value={addDraft.channelRole} onChange={(event) => onAddDraftChange({ ...addDraft, channelRole: event.target.value })} data-testid={`flow.members.channel.${channel.channelId}.add.role`}>{roles.map(channelOption)}</select></label>
          <label>Notifications<select value={addDraft.notificationLevel} onChange={(event) => onAddDraftChange({ ...addDraft, notificationLevel: event.target.value })} data-testid={`flow.members.channel.${channel.channelId}.add.notifications`}>{notificationLevels.map(channelOption)}</select></label>
          <button className="fl-btn fl-pri" type="button" disabled={!addDraft.userId || busyAction !== null} onClick={onAdd} data-testid={`flow.members.channel.${channel.channelId}.add.submit`}>Add to channel</button>
        </div>
      ) : null}
      <p className="fl-member-boundary">{channel.boundary}</p>
    </article>
  );
}

function MemberRow({
  member,
  canAdminister,
  roles,
  roleDraft,
  busyAction,
  onRoleChange,
  onSaveRole,
  onRemove
}: {
  member: ProjectMemberDirectoryEntry;
  canAdminister: boolean;
  roles: string[];
  roleDraft: string;
  busyAction: string | null;
  onRoleChange: (role: string) => void;
  onSaveRole: () => void;
  onRemove: () => void;
}) {
  const roleChanged = roleDraft !== member.tenantRole;
  return (
    <article className="fl-member-row" data-testid={`flow.members.row.${member.userId}`}>
      <div className="fl-member-row__identity">
        <span className="fl-member-avatar" aria-hidden="true">{initials(member.displayName)}</span>
        <span>
          <strong>{member.displayName}{member.isCurrentUser ? ' (you)' : ''}</strong>
          <small>{member.email}</small>
        </span>
        <StatusBadge status={member.isActive ? 'ready' : 'neutral'}>{member.isActive ? 'Active' : 'Inactive'}</StatusBadge>
      </div>
      <div className="fl-member-row__facts">
        <span><small>Tenant role</small><strong>{formatRole(member.tenantRole)}</strong></span>
        <span><small>Project access</small><strong>{member.projectAccessStatus}</strong></span>
        <span><small>Channels</small><strong>{member.channelMembershipSummary}</strong></span>
      </div>
      {canAdminister ? (
        <div className="fl-member-row__admin">
          <label>
            Tenant role
            <select value={roleDraft} onChange={(event) => onRoleChange(event.target.value)} disabled={busyAction !== null} data-testid={`flow.members.role.${member.userId}`}>
              {roles.map(roleOption)}
            </select>
            <small className="fl-member-role-note">{roleDescription(roleDraft)}</small>
          </label>
          <button className="fl-btn" type="button" onClick={onSaveRole} disabled={!roleChanged || busyAction !== null} data-testid={`flow.members.role.save.${member.userId}`}>Save role</button>
          <button className="fl-btn" type="button" onClick={onRemove} disabled={member.isCurrentUser || busyAction !== null} title={member.isCurrentUser ? 'You cannot remove your current tenant session here.' : 'Remove tenant membership'} data-testid={`flow.members.remove.${member.userId}`}>Remove</button>
        </div>
      ) : null}
    </article>
  );
}

function MemberScope({ label, value }: { label: string; value: string }) {
  return <span><small>{label}</small><strong>{value}</strong></span>;
}

function roleOption(role: string) {
  return <option key={role} value={role}>{formatRole(role)}</option>;
}

function formatRole(role: string) {
  return role === 'TenantAdmin' ? 'Tenant admin' : role;
}

function channelOption(value: string) {
  return <option key={value} value={value}>{value === 'ReadOnly' ? 'Read only' : value}</option>;
}

function formatChannelRole(role: string) {
  return role === 'ReadOnly' ? 'Read only' : role;
}

function formatNotificationLevel(level: string) {
  return level === 'None' ? 'Notifications off' : `${level} notifications`;
}

function channelDraftKey(channelId: number, userId: number) {
  return `${channelId}-${userId}`;
}

function memberName(directory: ProjectMemberDirectoryResponse, userId: number) {
  return directory.members.find((member) => member.userId === userId)?.displayName ?? `User ${userId}`;
}

function roleDescription(role: string) {
  const descriptions: Record<string, string> = {
    Owner: 'Full tenant administration. Consequential actions still require their own backend authority.',
    TenantAdmin: 'Manages tenant membership and settings without inheriting workflow authority.',
    Approver: 'Eligible for approval consideration when the backend policy and object state also allow it.',
    Reviewer: 'Coordinates review work without gaining approval or source-apply authority.',
    Operator: 'Coordinates operational work; run and continuation eligibility remain backend decisions.',
    Viewer: 'Read-oriented tenant access with no tenant administration.',
    Member: 'Standard collaboration visibility with no implied consequential authority.'
  };
  return descriptions[role] ?? 'The backend evaluates every consequential action separately.';
}

function initials(name: string) {
  return name.split(/\s+/).filter(Boolean).slice(0, 2).map((part) => part[0]?.toUpperCase()).join('') || '?';
}

function describeError(error: unknown, fallback: string) {
  if (error instanceof IronDevApiError) {
    const body = error.body as { error?: string; message?: string } | null | undefined;
    return body?.error ?? body?.message ?? `${fallback} HTTP ${error.status}.`;
  }
  return error instanceof Error ? error.message : fallback;
}
