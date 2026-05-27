type FlowStageStatus = 'waiting' | 'ready' | 'complete' | 'active' | 'error';

export interface FlowStageItem {
  id: string;
  label: string;
  status: FlowStageStatus;
}

export function FlowStageRail({ stages }: { stages: FlowStageItem[] }) {
  return (
    <ol className="chat-build-stage-rail" data-testid="chat-build.stageRail">
      {stages.map((stage) => (
        <li key={stage.id} className={`chat-build-stage chat-build-stage--${stage.status}`}>
          <span className="chat-build-stage__marker" aria-hidden="true" />
          <span>{stage.label}</span>
        </li>
      ))}
    </ol>
  );
}
