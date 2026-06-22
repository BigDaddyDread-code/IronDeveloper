import { useEffect, useMemo, useState } from 'react';
import type { WorkspaceRoute, WorkspaceRouteMeta } from '../../app/routes';
import { IronDevApiError } from '../../api/ironDevApi';
import { useSessionContext } from '../../state/useSessionContext';
import { OperationStatusViewer } from './OperationStatusViewer';
import type { OperationStatusViewerLoadStatus, OperationStatusViewerModel } from './OperationStatusViewerTypes';

interface OperationStatusViewerRouteProps {
  route: WorkspaceRoute;
  onRouteReady?: (state: WorkspaceRouteMeta) => void;
}

export function OperationStatusViewerRoute({ onRouteReady }: OperationStatusViewerRouteProps) {
  const session = useSessionContext();
  const operationId = operationIdFromPath(window.location.pathname);
  const compact = new URLSearchParams(window.location.search).get('compact') === 'true';
  const [status, setStatus] = useState<OperationStatusViewerLoadStatus>('idle');
  const [model, setModel] = useState<OperationStatusViewerModel | null>(null);
  const [message, setMessage] = useState('Operation status has not been loaded.');
  const canRead = session.apiStatus.status === 'connected' && session.tokenConfigured;

  const routeMeta: WorkspaceRouteMeta = useMemo(
    () => ({
      workspaceCommands: [],
      workspaceBlockReason: canRead ? null : 'Operation status viewer requires API connection and authentication.',
      workspaceSummaryChips: [
        { label: model?.state ? `State ${model.state}` : 'Operation status', testId: 'operation-status.chip.state' },
        { label: 'Read-only', testId: 'operation-status.chip.readonly' }
      ]
    }),
    [canRead, model?.state]
  );

  useEffect(() => {
    onRouteReady?.(routeMeta);
  }, [onRouteReady, routeMeta]);

  useEffect(() => {
    if (!canRead) {
      setStatus('error');
      setMessage('Operation status viewer requires API connection and authentication.');
      setModel(null);
      return;
    }

    if (!operationId) {
      setStatus('missing');
      setMessage('Operation ID is required in the route before status can be displayed.');
      setModel(null);
      return;
    }

    const controller = new AbortController();
    setStatus('loading');
    setMessage('Reading governed operation status through the frontend readiness API.');

    session.client
      .getFrontendOperationStatus(operationId, compact, controller.signal)
      .then((response) => {
        if (!response.data) {
          setStatus('missing');
          setModel(null);
          setMessage('Operation status was not found. Nothing was executed.');
          return;
        }

        setModel({
          ...response.data,
          envelopeBoundary: response.boundary,
          envelopeWarnings: response.warnings ?? []
        });
        setStatus('ready');
        setMessage('Operation status loaded for inspection only.');
      })
      .catch((error: unknown) => {
        if (controller.signal.aborted) {
          return;
        }

        setStatus(error instanceof IronDevApiError && error.status === 404 ? 'missing' : 'error');
        setModel(null);
        setMessage(error instanceof IronDevApiError ? error.message : 'Operation status read failed without mutation.');
      });

    return () => controller.abort();
  }, [canRead, compact, operationId, session.client]);

  return <OperationStatusViewer status={status} model={model} message={message} />;
}

function operationIdFromPath(pathname: string) {
  const match = pathname.match(/^\/operations\/([^/]+)\/status\/?$/i);
  return match ? decodeURIComponent(match[1]) : '';
}
