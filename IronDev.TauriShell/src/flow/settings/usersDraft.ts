// User and role setup DRAFT store.
//
// Boundary: role assignment decides visibility, never mutation authority.
// The backend has no user administration or role endpoints yet (Block F).
// Until they exist, this screen manages a local draft of intended users and
// role assignments, labelled as pending backend contract in the UI.

export type FlowRole = 'viewer' | 'reviewer' | 'approver' | 'operator' | 'tenantAdmin';

export interface FlowRoleOption {
  role: FlowRole;
  label: string;
  description: string;
}

export const flowRoles: FlowRoleOption[] = [
  { role: 'viewer', label: 'Viewer', description: 'Read-only across the board and work items.' },
  { role: 'reviewer', label: 'Reviewer', description: 'Sees evidence and critic findings; cannot decide the gate.' },
  { role: 'approver', label: 'Approver', description: 'Rules the human gate: approve, reject, accept with known risk.' },
  { role: 'operator', label: 'Operator', description: 'Runs builds, watches runs, reads diagnostics.' },
  { role: 'tenantAdmin', label: 'Tenant admin', description: 'Manages users, roles, and tenant settings.' }
];

export interface DraftUser {
  id: string;
  email: string;
  displayName: string;
  role: FlowRole;
  isActive: boolean;
}

const storageKey = 'irondev.flow.usersDraft.v1';

export function loadDraftUsers(): DraftUser[] {
  try {
    const raw = window.localStorage.getItem(storageKey);
    if (!raw) {
      return [];
    }
    const parsed = JSON.parse(raw) as DraftUser[];
    return Array.isArray(parsed) ? parsed.filter((u) => typeof u.email === 'string') : [];
  } catch {
    return [];
  }
}

export function saveDraftUsers(users: DraftUser[]): void {
  window.localStorage.setItem(storageKey, JSON.stringify(users));
}

export function newDraftUser(email: string, displayName: string, role: FlowRole): DraftUser {
  return {
    id: `u-${Date.now().toString(36)}-${Math.random().toString(36).slice(2, 7)}`,
    email,
    displayName,
    role,
    isActive: true
  };
}
