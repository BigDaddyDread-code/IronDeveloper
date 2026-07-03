// Approval policy DRAFT store.
//
// Boundary: these settings request policy; they do not grant it. The backend's
// authority gates remain the only authority. Until the Block F policy endpoints
// exist, the draft persists locally and is clearly labelled as such in the UI.
// Vocabulary matches IronDev.Core AuthorityProfileKind.

export type AutonomyProfileKind = 'ProposalOnly' | 'AskBeforeMutation' | 'BoundedRunAuthority';

export interface AutonomyProfileOption {
  kind: AutonomyProfileKind;
  title: string;
  description: string;
}

export const autonomyProfiles: AutonomyProfileOption[] = [
  {
    kind: 'ProposalOnly',
    title: 'Proposal only',
    description: 'Every action produces review material. A human approves each step before anything mutates.'
  },
  {
    kind: 'AskBeforeMutation',
    title: 'Ask before mutation',
    description: 'Reads and sandbox runs proceed; every mutating step halts for human approval.'
  },
  {
    kind: 'BoundedRunAuthority',
    title: 'Bounded run authority',
    description: 'Low-risk steps proceed within declared bounds; humans gate batch boundaries and high-risk actions.'
  }
];

export interface ActionClassPolicy {
  id: string;
  label: string;
  requiresHuman: boolean;
  locked: boolean;
  lockedReason?: string;
}

export interface ApprovalPolicyDraft {
  autonomyProfile: AutonomyProfileKind;
  actionClasses: ActionClassPolicy[];
  updatedAtUtc: string;
}

export function defaultApprovalPolicy(): ApprovalPolicyDraft {
  return {
    autonomyProfile: 'ProposalOnly',
    actionClasses: [
      { id: 'ticket-promotion', label: 'Promote draft to ticket', requiresHuman: true, locked: false },
      { id: 'sandbox-build-run', label: 'Sandbox build and test run', requiresHuman: false, locked: false },
      { id: 'source-apply', label: 'Apply changes to source', requiresHuman: true, locked: true, lockedReason: 'Source apply always requires explicit approval. Backend invariant.' },
      { id: 'commit', label: 'Commit to branch', requiresHuman: true, locked: false },
      { id: 'push', label: 'Push to remote', requiresHuman: true, locked: false },
      { id: 'draft-pr', label: 'Open draft pull request', requiresHuman: true, locked: false },
      { id: 'memory-promotion', label: 'Promote memory', requiresHuman: true, locked: true, lockedReason: 'Memory cannot promote itself. Backend invariant.' },
      { id: 'release', label: 'Release or deploy', requiresHuman: true, locked: true, lockedReason: 'Release execution is parked. Backend invariant.' }
    ],
    updatedAtUtc: new Date().toISOString()
  };
}

const storageKey = 'irondev.flow.approvalPolicyDraft.v1';

export function loadApprovalPolicy(): ApprovalPolicyDraft {
  try {
    const raw = window.localStorage.getItem(storageKey);
    if (!raw) {
      return defaultApprovalPolicy();
    }
    const parsed = JSON.parse(raw) as ApprovalPolicyDraft;
    const defaults = defaultApprovalPolicy();
    const merged = defaults.actionClasses.map((def) => {
      const saved = parsed.actionClasses?.find((a) => a.id === def.id);
      if (!saved || def.locked) {
        return def;
      }
      return { ...def, requiresHuman: saved.requiresHuman };
    });
    const profile = autonomyProfiles.some((p) => p.kind === parsed.autonomyProfile)
      ? parsed.autonomyProfile
      : defaults.autonomyProfile;
    return { autonomyProfile: profile, actionClasses: merged, updatedAtUtc: parsed.updatedAtUtc ?? defaults.updatedAtUtc };
  } catch {
    return defaultApprovalPolicy();
  }
}

export function saveApprovalPolicy(draft: ApprovalPolicyDraft): void {
  window.localStorage.setItem(storageKey, JSON.stringify({ ...draft, updatedAtUtc: new Date().toISOString() }));
}
