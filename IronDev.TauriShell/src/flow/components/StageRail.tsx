import { Fragment } from 'react';
import { GateInfo, WorkItemStage, stageLabels, stageOrder } from '../flowTypes';

interface StageRailProps {
  activeStage: WorkItemStage;
  gates: GateInfo[];
}

export function StageRail({ activeStage, gates }: StageRailProps) {
  const activeIndex = stageOrder.indexOf(activeStage);

  return (
    <div className="fl-rail" data-testid="flow.stagerail">
      {stageOrder.map((stage, index) => {
        const gate = gates.find((g) => g.afterStage === stage);
        const stageClass =
          stage === activeStage ? 'fl-stage fl-active' : index < activeIndex ? 'fl-stage fl-done' : 'fl-stage';

        return (
          <Fragment key={stage}>
            <span className={stageClass}>{stageLabels[stage]}</span>
            {gate ? (
              <span
                className={gate.state === 'locked' ? 'fl-gatechip fl-locked' : 'fl-gatechip fl-open'}
                title={gate.state === 'locked' ? `Gate locked: ${gate.label}` : `Gate satisfied: ${gate.label}`}
              >
                <span className="fl-gate-glyph" aria-hidden="true" />
                {gate.label}
              </span>
            ) : null}
          </Fragment>
        );
      })}
    </div>
  );
}
