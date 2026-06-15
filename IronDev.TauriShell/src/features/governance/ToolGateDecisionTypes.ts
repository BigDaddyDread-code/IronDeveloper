import type { ToolGateDecisionListItem, ToolGateFilter, ToolGateIssue, ToolRequestListItem } from '../../api/types';

export interface ToolGateViewModel {
  isReadOnly: true;
  mutationOccurred: false;
  canApprove: false;
  canReject: false;
  canOverrideGate: false;
  canReopenGate: false;
  canSatisfyPolicy: false;
  canExecuteTool: false;
  canInvokeTool: false;
  canDispatchAgent: false;
  canTransitionWorkflow: false;
  canApplySource: false;
  canApplyPatch: false;
  requests: ToolRequestListItem[];
  decisions: ToolGateDecisionListItem[];
  warnings: string[];
  errors: ToolGateIssue[];
}

export type ToolGateLoadStatus = 'idle' | 'loading' | 'loaded' | 'empty' | 'validation' | 'error';

export interface ToolGateDecisionRouteState {
  filters: Required<Omit<ToolGateFilter, 'take'>> & { take: string };
  viewModel: ToolGateViewModel;
  status: ToolGateLoadStatus;
  message: string;
}

export const readOnlyToolGateViewModel: ToolGateViewModel = {
  isReadOnly: true,
  mutationOccurred: false,
  canApprove: false,
  canReject: false,
  canOverrideGate: false,
  canReopenGate: false,
  canSatisfyPolicy: false,
  canExecuteTool: false,
  canInvokeTool: false,
  canDispatchAgent: false,
  canTransitionWorkflow: false,
  canApplySource: false,
  canApplyPatch: false,
  requests: [],
  decisions: [],
  warnings: [
    'Tool request visibility is not tool execution.',
    'Gate decision visibility is not gate authority.',
    'Approval requirement is not approval.',
    'Policy evidence is not policy satisfaction.'
  ],
  errors: []
};