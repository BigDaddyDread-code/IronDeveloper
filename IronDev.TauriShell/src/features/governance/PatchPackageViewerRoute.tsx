import { useEffect, useMemo, useState } from 'react';
import type { WorkspaceRoute, WorkspaceRouteMeta } from '../../app/routes';
import { IronDevApiError } from '../../api/ironDevApi';
import { useSessionContext } from '../../state/useSessionContext';
import { PatchPackageViewer } from './PatchPackageViewer';
import type { PatchPackageViewerLoadStatus, PatchPackageViewerModel } from './PatchPackageViewerTypes';

interface PatchPackageViewerRouteProps {
  route: WorkspaceRoute;
  onRouteReady?: (state: WorkspaceRouteMeta) => void;
}

export function PatchPackageViewerRoute({ onRouteReady }: PatchPackageViewerRouteProps) {
  const session = useSessionContext();
  const packageId = packageIdFromPath(window.location.pathname);
  const compact = new URLSearchParams(window.location.search).get('compact') === 'true';
  const [status, setStatus] = useState<PatchPackageViewerLoadStatus>('idle');
  const [model, setModel] = useState<PatchPackageViewerModel | null>(null);
  const [message, setMessage] = useState('Patch package has not been loaded.');
  const canRead = session.apiStatus.status === 'connected' && session.tokenConfigured;

  const routeMeta: WorkspaceRouteMeta = useMemo(
    () => ({
      workspaceCommands: [],
      workspaceBlockReason: canRead ? null : 'Patch package viewer requires API connection and authentication.',
      workspaceSummaryChips: [
        { label: model?.metadata.packageId ? `Package ${model.metadata.packageId}` : 'Patch package', testId: 'patch-package.chip.package' },
        { label: 'Read-only', testId: 'patch-package.chip.readonly' }
      ]
    }),
    [canRead, model?.metadata.packageId]
  );

  useEffect(() => {
    onRouteReady?.(routeMeta);
  }, [onRouteReady, routeMeta]);

  useEffect(() => {
    if (!canRead) {
      setStatus('error');
      setMessage('Patch package viewer requires API connection and authentication.');
      setModel(null);
      return;
    }

    if (!packageId) {
      setStatus('missing');
      setMessage('Patch package ID is required in the route before artifacts can be displayed.');
      setModel(null);
      return;
    }

    const controller = new AbortController();
    setStatus('loading');
    setMessage('Reading patch package metadata and artifacts through the frontend readiness API.');

    Promise.all([
      session.client.getFrontendPatchPackageMetadata(packageId, compact, controller.signal),
      session.client.getFrontendPatchPackageArtifacts(packageId, compact, controller.signal)
    ])
      .then(([metadataResponse, artifactsResponse]) => {
        if (!metadataResponse.data || !artifactsResponse.data) {
          setStatus('missing');
          setModel(null);
          setMessage('Patch package metadata or artifacts were not found. Nothing was executed.');
          return;
        }

        setModel({
          metadata: metadataResponse.data,
          artifacts: artifactsResponse.data,
          envelopeBoundary: artifactsResponse.boundary ?? metadataResponse.boundary,
          envelopeWarnings: [...(metadataResponse.warnings ?? []), ...(artifactsResponse.warnings ?? [])]
        });
        setStatus('ready');
        setMessage('Patch package loaded for inspection only.');
      })
      .catch((error: unknown) => {
        if (controller.signal.aborted) {
          return;
        }

        setStatus(error instanceof IronDevApiError && error.status === 404 ? 'missing' : 'error');
        setModel(null);
        setMessage(error instanceof IronDevApiError ? error.message : 'Patch package read failed without mutation.');
      });

    return () => controller.abort();
  }, [canRead, compact, packageId, session.client]);

  return <PatchPackageViewer status={status} model={model} message={message} />;
}

function packageIdFromPath(pathname: string) {
  const match = pathname.match(/^\/(?:governance\/)?patch-packages\/([^/]+)\/?$/i);
  return match ? decodeURIComponent(match[1]) : '';
}
