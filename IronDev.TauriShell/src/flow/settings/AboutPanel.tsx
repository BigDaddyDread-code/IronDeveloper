import { useState } from 'react';
import { IronDevBrand } from '../../components/IronDevBrand';
import { IRONDEV_BUILD, IRONDEV_VERSION, ironDevVersionText } from '../../productIdentity';
import { useSessionContext } from '../../state/useSessionContext';

export function AboutPanel() {
  const session = useSessionContext();
  const [copied, setCopied] = useState(false);
  const [showDiagnostics, setShowDiagnostics] = useState(false);
  const [showLicences, setShowLicences] = useState(false);
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

  return (
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
  );
}
