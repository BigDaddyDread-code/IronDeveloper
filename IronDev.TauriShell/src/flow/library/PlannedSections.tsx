import { useProjectContext } from '../../state/useProjectContext';
import { NotImplementedPanel } from '../components/NotImplementedPanel';

// NAV-1: the full information architecture is visible, and every unbuilt surface is an
// honest 501 route — a real controller refusing with the owning roadmap slice named.
// These sections graduate into real screens when their slices land; until then, clicking
// through the whole product tells the truth about where the product ends.

export function ProvisioningSection() {
  const project = useProjectContext();
  const projectId = project.selectedProjectId;

  return (
    <div style={{ display: 'grid', gap: 12 }}>
      <p className="fl-sub" style={{ margin: 0 }}>
        The bridge from demo to real use: repo scan, architecture wizard, readiness result. Profile and command
        configuration exist today; the wizard and readiness contract arrive with PROJECT-0..3.
      </p>
      <NotImplementedPanel
        title="Provisioning readiness"
        path={projectId === null ? null : `/api/projects/${projectId}/provisioning/readiness`}
        missingPrerequisite="Select a project to probe its provisioning readiness."
        testId="flow.library.provisioning"
      />
    </div>
  );
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
