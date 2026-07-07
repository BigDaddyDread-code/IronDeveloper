import { useProjectContext } from '../../state/useProjectContext';
import { NotImplementedPanel } from '../components/NotImplementedPanel';
import { ProvisioningScreen } from './ProvisioningScreen';

// NAV-1: the full information architecture is visible, and every unbuilt surface is an
// honest 501 route — a real controller refusing with the owning roadmap slice named.
// These sections graduate into real screens when their slices land; until then, clicking
// through the whole product tells the truth about where the product ends.
// PROJECT-0..3 graduated: ProvisioningSection is a real screen now.

export function ProvisioningSection() {
  return <ProvisioningScreen />;
}

export function AuditSection() {
  return (
    <div style={{ display: 'grid', gap: 12 }}>
      <p className="fl-sub" style={{ margin: 0 }}>
        Who did what, when, under what authority, with what evidence — one ledger. The truth exists today across
        governance traces, agent-run audit, and workflow records; the unified view is not built.
      </p>
      <NotImplementedPanel title="Audit ledger" path="/api/v1/audit/ledger" testId="flow.library.audit" />
    </div>
  );
}

export function AdminInviteSection() {
  const project = useProjectContext();
  const tenantId = project.selectedTenantId;

  return (
    <div style={{ display: 'grid', gap: 12 }}>
      <p className="fl-sub" style={{ margin: 0 }}>
        The team&apos;s front door. Direct user creation is live in Settings today; the invite/pending/accept flow is
        gated on TEAM-0 (tenant-scope proof + role/visibility matrix) before any second human joins a tenant.
      </p>
      <NotImplementedPanel
        title="Invite a user"
        method="POST"
        path={tenantId === null ? null : `/api/tenants/${tenantId}/users/invite`}
        missingPrerequisite="Select a tenant to probe the invite flow."
        testId="flow.library.adminInvite"
      />
    </div>
  );
}
