import { ChangeEvent, useState } from 'react';
import { IronDevBrand } from '../../components/IronDevBrand';
import { IRONDEV_BUILD, IRONDEV_VERSION, ironDevVersionText } from '../../productIdentity';
import type { AgentConfigurationPack, AgentConfigurationPackImportOutcome, AgentConfigurationPackPreview } from '../../api/types';
import { useProjectContext } from '../../state/useProjectContext';
import { useSessionContext } from '../../state/useSessionContext';

export function AboutPanel() {
  const session = useSessionContext();
  const project = useProjectContext();
  const [copied, setCopied] = useState(false);
  const [showDiagnostics, setShowDiagnostics] = useState(false);
  const [showLicences, setShowLicences] = useState(false);
  const [packScope, setPackScope] = useState<'project' | 'tenant'>('project');
  const [pack, setPack] = useState<AgentConfigurationPack | null>(null);
  const [packPreview, setPackPreview] = useState<AgentConfigurationPackPreview | null>(null);
  const [packOutcome, setPackOutcome] = useState<AgentConfigurationPackImportOutcome | null>(null);
  const [packBusy, setPackBusy] = useState(false);
  const [packError, setPackError] = useState<string | null>(null);
  const apiConnected = session.apiStatus.status === 'connected';
  const database = session.environmentInfo?.database?.trim();

  const versionInfo = [
    ironDevVersionText(),
    `API: ${apiConnected ? 'Connected' : session.apiStatus.status}`,
    `Environment: ${session.environmentInfo?.environment ?? 'Not reported'}`,
    `Database: ${database || 'Not reported'}`,
    'Worker: Not reported by the environment contract'
  ].join('\n');

  const copyVersion = async () => {
    await navigator.clipboard.writeText(versionInfo);
    setCopied(true);
    window.setTimeout(() => setCopied(false), 1800);
  };

  const projectId = packScope === 'project' ? project.selectedProjectId : null;

  const exportPack = async () => {
    setPackBusy(true);
    setPackError(null);
    try {
      const exported = await session.client.exportAgentConfigurationPack(projectId, packScope);
      const blob = new Blob([JSON.stringify(exported, null, 2)], { type: 'application/json' });
      const url = URL.createObjectURL(blob);
      const link = document.createElement('a');
      link.href = url;
      link.download = `irondev-${packScope}-agent-config-${exported.packId}.json`;
      link.click();
      URL.revokeObjectURL(url);
    } catch (error) {
      setPackError(readError(error, 'Configuration export failed.'));
    } finally {
      setPackBusy(false);
    }
  };

  const selectPack = async (event: ChangeEvent<HTMLInputElement>) => {
    const file = event.target.files?.[0];
    event.target.value = '';
    if (!file) return;
    setPackBusy(true);
    setPackError(null);
    setPackPreview(null);
    setPackOutcome(null);
    try {
      const parsed = JSON.parse(await file.text()) as AgentConfigurationPack;
      const preview = await session.client.previewAgentConfigurationPack(parsed, projectId, packScope);
      setPack(parsed);
      setPackPreview(preview);
    } catch (error) {
      setPack(null);
      setPackError(readError(error, 'The selected file is not a valid configuration pack.'));
    } finally {
      setPackBusy(false);
    }
  };

  const createDrafts = async () => {
    if (!pack || !packPreview) return;
    setPackBusy(true);
    setPackError(null);
    try {
      const outcome = await session.client.importAgentConfigurationPack(
        pack,
        packPreview.expectedRevisions,
        projectId,
        packScope
      );
      setPackOutcome(outcome);
      setPackPreview(outcome.preview);
    } catch (error) {
      setPackError(readError(error, 'Configuration drafts were not created.'));
    } finally {
      setPackBusy(false);
    }
  };

  return (
    <div style={{ display: 'grid', gap: 16 }}>
    <section className="fl-panel-box" data-testid="flow.settings.about">
      <IronDevBrand descriptor />
      <p className="fl-empty">Governed AI-assisted software engineering</p>
      <dl className="fl-kv">
        <dt>Version</dt><dd>{IRONDEV_VERSION}</dd>
        <dt>Build</dt><dd>{IRONDEV_BUILD}</dd>
        <dt>API</dt><dd>{apiConnected ? 'Connected' : session.apiStatus.status}</dd>
        <dt>Database</dt><dd>{database ? `Reported: ${database}` : 'Not reported'}</dd>
        <dt>Worker</dt><dd>Not reported</dd>
      </dl>
      <div style={{ display: 'flex', gap: 8, flexWrap: 'wrap', marginTop: 12 }}>
        <button className="fl-btn" type="button" onClick={() => void copyVersion()} data-testid="flow.settings.about.copy">
          {copied ? 'Copied' : 'Copy version information'}
        </button>
        <button className="fl-btn" type="button" onClick={() => setShowDiagnostics((value) => !value)} data-testid="flow.settings.about.diagnostics">
          {showDiagnostics ? 'Close diagnostics' : 'Open diagnostics'}
        </button>
        <button className="fl-btn" type="button" onClick={() => setShowLicences((value) => !value)} data-testid="flow.settings.about.licences">
          View licences
        </button>
      </div>
      {showDiagnostics ? <pre className="fl-about-diagnostics">{versionInfo}</pre> : null}
      {showLicences ? (
        <p className="fl-empty" data-testid="flow.settings.about.licenceText">
          IronDev includes open-source dependencies. Package-level licence metadata is retained in the npm and Cargo lockfiles; distribution notices will be generated with the installer.
        </p>
      ) : null}
    </section>
    <section className="fl-panel-box" data-testid="flow.settings.configurationPack">
      <p className="fl-plabel">Configuration pack</p>
      <p className="fl-empty">Move non-secret agent profile proposals between tenant or project scopes. Imported configuration remains a draft until separately published.</p>
      <div className="fl-settings-hub" role="tablist" aria-label="Configuration pack scope" style={{ marginTop: 12 }}>
        <button type="button" role="tab" aria-selected={packScope === 'project'} className={packScope === 'project' ? 'fl-on' : ''} onClick={() => { setPackScope('project'); setPackPreview(null); setPackOutcome(null); }} data-testid="flow.settings.configurationPack.scope.project">Project</button>
        <button type="button" role="tab" aria-selected={packScope === 'tenant'} className={packScope === 'tenant' ? 'fl-on' : ''} onClick={() => { setPackScope('tenant'); setPackPreview(null); setPackOutcome(null); }} data-testid="flow.settings.configurationPack.scope.tenant">Tenant</button>
      </div>
      <div style={{ display: 'flex', gap: 8, flexWrap: 'wrap', marginTop: 12 }}>
        <button className="fl-btn" type="button" disabled={packBusy || (packScope === 'project' && project.selectedProjectId === null)} onClick={() => void exportPack()} data-testid="flow.settings.configurationPack.export">
          {packBusy ? 'Working...' : 'Export non-secret configuration'}
        </button>
        <label className="fl-btn" style={{ cursor: packBusy ? 'default' : 'pointer' }}>
          Preview import
          <input type="file" accept="application/json,.json" disabled={packBusy || (packScope === 'project' && project.selectedProjectId === null)} onChange={(event) => void selectPack(event)} data-testid="flow.settings.configurationPack.file" style={{ display: 'none' }} />
        </label>
      </div>
      {packError ? <p className="fl-error" data-testid="flow.settings.configurationPack.error">{packError}</p> : null}
      {packPreview ? (
        <div style={{ marginTop: 14 }} data-testid="flow.settings.configurationPack.preview">
          <p className="fl-empty">{packPreview.sourceProvenance}</p>
          <p className="fl-empty">{packPreview.differences.filter((item) => item.changed).length} field changes across {Object.keys(packPreview.expectedRevisions).length} profile drafts.</p>
          <div style={{ overflowX: 'auto' }}>
            <table className="fl-table">
              <thead><tr><th>Agent</th><th>Field</th><th>Current</th><th>Imported</th></tr></thead>
              <tbody>
                {packPreview.differences.filter((item) => item.changed).map((item, index) => (
                  <tr key={`${item.role}-${item.field}-${index}`}><td>{roleName(item.role)}</td><td>{item.field}</td><td>{summarize(item.currentValue)}</td><td>{summarize(item.importedValue)}</td></tr>
                ))}
              </tbody>
            </table>
          </div>
          <button className="fl-btn fl-pri" type="button" disabled={packBusy || packOutcome?.succeeded === true} onClick={() => void createDrafts()} data-testid="flow.settings.configurationPack.createDrafts" style={{ marginTop: 12 }}>
            {packBusy ? 'Creating drafts...' : 'Create drafts'}
          </button>
        </div>
      ) : null}
      {packOutcome?.succeeded ? <p className="fl-okmsg" data-testid="flow.settings.configurationPack.success">Created {packOutcome.createdDrafts.length} draft profiles. Nothing was published.</p> : null}
    </section>
    </div>
  );
}

function readError(error: unknown, fallback: string): string {
  if (error instanceof Error && error.message.trim()) return error.message;
  return fallback;
}

function summarize(value: string): string {
  const normalized = value.replace(/\s+/g, ' ').trim();
  return normalized.length > 80 ? `${normalized.slice(0, 77)}...` : normalized || 'Empty';
}

function roleName(role: string | number): string {
  if (typeof role === 'string') return role;
  return ({ 1: 'Builder', 2: 'Tester', 3: 'Critic', 4: 'Analyst' } as Record<number, string>)[role] ?? String(role);
}
