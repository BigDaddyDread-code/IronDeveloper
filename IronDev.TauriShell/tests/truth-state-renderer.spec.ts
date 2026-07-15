import { expect, test } from '@playwright/test';
import { truthStateAccessibility, truthStateDescriptors } from '../src/design-system/state/TruthStateRenderer';

test('shared renderer covers every cleanup truth state exactly once', () => {
  expect(Object.keys(truthStateDescriptors)).toEqual([
    'authRequired',
    'apiUnreachable',
    'tenantRequired',
    'projectRequired',
    'readiness',
    'governedRefusal',
    'notImplemented',
    'loading',
    'empty',
    'error',
    'staleData',
    'partialData'
  ]);
});

test('only failures interrupt assistive technology and loading stays explicitly busy', () => {
  expect(truthStateDescriptors.apiUnreachable.live).toBe('assertive');
  expect(truthStateDescriptors.error.live).toBe('assertive');
  expect(Object.entries(truthStateDescriptors).filter(([, value]) => value.live === 'assertive').map(([key]) => key)).toEqual([
    'apiUnreachable',
    'error'
  ]);

  expect(truthStateAccessibility('loading')).toEqual({ role: 'status', live: 'polite', busy: true });
  expect(truthStateAccessibility('apiUnreachable')).toEqual({ role: 'alert', live: 'assertive', busy: undefined });
  expect(truthStateAccessibility('error')).toEqual({ role: 'alert', live: 'assertive', busy: undefined });
  expect(truthStateAccessibility('empty')).toEqual({ role: 'status', live: 'polite', busy: undefined });
});
