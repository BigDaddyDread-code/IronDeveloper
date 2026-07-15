import { expect, test } from '@playwright/test';
import { getChatModeGate } from '../src/features/chatToBuild/chatGovernanceGate';
import { statusTone } from '../src/flow/workitem/BuildStage';
import { parseProductRoute } from '../src/flow/navigation/productRoutes';

test('navigation seam preserves project identity and compatibility classification', () => {
  const board = parseProductRoute('/projects/42/board');
  const workItem = parseProductRoute('/projects/42/work-items/9');
  const legacyTickets = parseProductRoute('/tickets');

  expect(board).toMatchObject({ kind: 'board', projectId: 42, compatibility: false });
  expect(workItem).toMatchObject({ kind: 'workItem', projectId: 42, workItemId: 9, compatibility: false });
  expect(legacyTickets).toMatchObject({ kind: 'board', projectId: null, compatibility: true });
});

test('chat gate seam never manufactures permissions from mode or action labels', () => {
  const withoutBackendGate = getChatModeGate({
    mode: 'Formalization',
    governanceActions: ['CreateTicket']
  } as never);

  expect(withoutBackendGate).toMatchObject({
    mode: 'Formalization',
    canSaveDiscussion: false,
    canCreateTicket: false,
    canViewSources: false,
    canCopyMarkdown: false,
    showGovernanceActions: false
  });
  expect(withoutBackendGate.governanceActions).toEqual([]);

  const withExplicitBackendGate = getChatModeGate({
    gate: {
      mode: 'Formalization',
      canSaveDiscussion: true,
      canCreateTicket: false,
      canViewSources: true,
      canCopyMarkdown: false,
      governanceActions: ['SaveDiscussion', 'ViewSources']
    }
  } as never);
  expect(withExplicitBackendGate).toMatchObject({
    mode: 'Formalization',
    canSaveDiscussion: true,
    canCreateTicket: false,
    canViewSources: true,
    canCopyMarkdown: false,
    showGovernanceActions: true,
    governanceActions: ['SaveDiscussion', 'ViewSources']
  });
});

test('build-stage tone mapping remains a presentation-only projection', () => {
  expect(statusTone('PausedForApproval')).toBe('var(--fl-acc-ink)');
  expect(statusTone('Completed')).toBe('var(--fl-acc-ink)');
  expect(statusTone('Applied')).toBe('var(--fl-acc-ink)');
  expect(statusTone('Failed')).toBe('var(--fl-gate-ink)');
  expect(statusTone('Cancelled')).toBe('var(--fl-gate-ink)');
  expect(statusTone('UnexpectedBackendValue')).toBe('var(--fl-ink2)');
});
