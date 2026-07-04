export type FlowSurface = 'board' | 'workitem' | 'batch' | 'library' | 'settings';

export type WorkItemStage = 'shape' | 'ticket' | 'build' | 'review' | 'done';

export const stageLabels: Record<WorkItemStage, string> = {
  shape: 'Shape',
  ticket: 'Ticket',
  build: 'Build',
  review: 'Review',
  done: 'Done'
};

export const stageOrder: WorkItemStage[] = ['shape', 'ticket', 'build', 'review', 'done'];

export type GateState = 'locked' | 'open';

export interface GateInfo {
  afterStage: WorkItemStage;
  label: string;
  state: GateState;
}

export interface DraftCriterion {
  id: string;
  text: string;
  confirmed: boolean;
}

export interface DraftOpenQuestion {
  id: string;
  text: string;
  resolved: boolean;
}

export interface ShapeDraft {
  title: string;
  summary: string;
  criteria: DraftCriterion[];
  openQuestions: DraftOpenQuestion[];
  architectureRefs: string[];
}

export function emptyShapeDraft(): ShapeDraft {
  return {
    title: '',
    summary: '',
    criteria: [],
    openQuestions: [],
    architectureRefs: []
  };
}
