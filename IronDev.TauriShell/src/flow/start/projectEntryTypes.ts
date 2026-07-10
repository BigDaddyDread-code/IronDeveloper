import type { ProjectProvisioningReadinessUi } from '../../api/types';

export type ProjectDestination = 'board' | 'provisioning';

export type ProjectTileReadiness =
  | { kind: 'loading' }
  | { kind: 'loaded'; readiness: ProjectProvisioningReadinessUi }
  | { kind: 'error'; message: string };

export function isReadyReadiness(readiness: ProjectTileReadiness): boolean {
  return readiness.kind === 'loaded' && readiness.readiness.isReady;
}

export function readinessLabel(readiness: ProjectTileReadiness | undefined): string {
  if (readiness === undefined || readiness.kind === 'loading') {
    return 'Checking setup';
  }

  if (readiness.kind === 'error') {
    return 'Status unavailable';
  }

  if (readiness.readiness.isReady) {
    return 'Ready';
  }

  const blockedCount = readiness.readiness.blockedCount;
  return `Setup required${blockedCount > 0 ? `, ${blockedCount} item${blockedCount === 1 ? '' : 's'}` : ''}`;
}
