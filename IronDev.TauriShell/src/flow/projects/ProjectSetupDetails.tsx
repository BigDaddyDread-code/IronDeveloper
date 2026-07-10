import type { ProjectSetupCheckModel } from './projectSetupModel';

export function ProjectSetupDetails({ checks }: { checks: ProjectSetupCheckModel[] }) {
  return (
    <details className="fl-project-setup__details" data-testid="flow.projectSetup.details">
      <summary>Setup details</summary>
      <div className="fl-project-setup__detail-list">
        {checks.map((check, index) => (
          <article key={`${check.code}-${index}`}>
            <div className="fl-project-setup__detail-heading">
              <strong>{check.raw.label || check.raw.name}</strong>
              <code>{check.code}</code>
            </div>
            <dl>
              <div>
                <dt>Backend state</dt>
                <dd>{check.state}</dd>
              </div>
              {check.detectedValue ? (
                <div>
                  <dt>Detected value</dt>
                  <dd>{check.detectedValue}</dd>
                </div>
              ) : null}
              <div>
                <dt>Evidence</dt>
                <dd>{check.evidence}</dd>
              </div>
              {check.remedy ? (
                <div>
                  <dt>Remedy</dt>
                  <dd>{check.remedy}</dd>
                </div>
              ) : null}
            </dl>
          </article>
        ))}
      </div>
    </details>
  );
}
