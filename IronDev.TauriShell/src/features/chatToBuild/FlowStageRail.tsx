export type FlowStageStatus = 'not-started' | 'ready' | 'running' | 'done' | 'blocked' | 'failed';

export interface FlowStageItem {
  id: string;
  label: string;
  status: FlowStageStatus;
  detail: string;
}

export function FlowStageRail({ stages }: { stages: FlowStageItem[] }) {
  return (
    <ol className="chat-build-stage-rail" data-testid="chat-build.stageRail">
      {stages.map((stage, index) => (
        <li key={stage.id} className={`chat-build-stage chat-build-stage--${stage.status}`}>
          <span className="chat-build-stage__marker" aria-hidden="true">{index + 1}</span>
          <span className="chat-build-stage__body">
            <span className="chat-build-stage__label">{stage.label}</span>
            <span className="chat-build-stage__detail">{stage.detail}</span>
          </span>
        </li>
      ))}
    </ol>
  );
}
