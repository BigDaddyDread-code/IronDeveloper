import type { TenantSummary } from '../../api/types';
import { IronDevBrand } from '../../components/IronDevBrand';
import { useProjectContext } from '../../state/useProjectContext';

interface TenantChooserProps {
  onOpenSettings: () => void;
}

export function TenantChooser({ onOpenSettings }: TenantChooserProps) {
  const project = useProjectContext();
  const displayName = project.userProfile?.displayName ?? 'there';

  return (
    <main className="fl-root fl-auth-root" data-testid="flow.tenantChooser" aria-label="Choose tenant">
      <header className="fl-auth-header">
        <IronDevBrand />
      </header>

      <section className="fl-auth-frame" aria-labelledby="tenant-title">
        <div className="fl-auth-intro">
          <p className="fl-plabel">Tenant</p>
          <h1 id="tenant-title" className="fl-h1">
            Welcome, {displayName}
          </h1>
          <p className="fl-sub">Choose a tenant.</p>
        </div>

        <div className="fl-tenant-list" data-testid="flow.tenantChooser.list">
          {project.tenants.length === 0 ? (
            <p className="fl-empty" data-testid="flow.tenantChooser.empty">
              No tenants are available for this account.
            </p>
          ) : (
            project.tenants.map((tenant: TenantSummary) => {
              const tenantId = tenant.id ?? -1;
              return (
                <button
                  key={tenantId}
                  className="fl-tenant-option"
                  data-testid={`flow.tenantChooser.tenant.${tenantId}`}
                  disabled={project.isRefreshing}
                  onClick={() => void project.selectTenantContext(tenantId)}
                >
                  <span className="fl-card-title">{tenant.name ?? `Tenant ${tenantId}`}</span>
                  {tenant.slug ? <span className="fl-tenant-slug">{tenant.slug}</span> : null}
                </button>
              );
            })
          )}
          <button className="fl-btn fl-mini" data-testid="flow.tenantChooser.settings" type="button" onClick={onOpenSettings}>
            Connection settings
          </button>
        </div>
      </section>
    </main>
  );
}
