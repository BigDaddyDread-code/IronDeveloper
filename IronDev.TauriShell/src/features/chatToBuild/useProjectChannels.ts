import { useCallback, useEffect, useState } from 'react';
import { IronDevApiError } from '../../api/ironDevApi';
import type { CreateProjectChannelRequest, ProjectChannelChatSummary } from '../../api/types';
import { useProjectContext } from '../../state/useProjectContext';
import { useSessionContext } from '../../state/useSessionContext';

export function useProjectChannels() {
  const session = useSessionContext();
  const project = useProjectContext();
  const [channels, setChannels] = useState<ProjectChannelChatSummary[]>([]);
  const [canCreateChannels, setCanCreateChannels] = useState(false);
  const [loadState, setLoadState] = useState<'loading' | 'ready' | 'error'>('loading');
  const [error, setError] = useState<string | null>(null);
  const [reloadKey, setReloadKey] = useState(0);

  const projectId = project.selectedProjectId;

  useEffect(() => {
    const controller = new AbortController();

    async function load() {
      if (!projectId || !session.tokenConfigured || session.apiStatus.status !== 'connected') {
        setChannels([]);
        setCanCreateChannels(false);
        setLoadState('ready');
        return;
      }

      setLoadState('loading');
      setError(null);
      try {
        const result = await session.client.getProjectChannels(projectId, controller.signal);
        setChannels(result.channels);
        setCanCreateChannels(result.canCreateChannels);
        setLoadState('ready');
      } catch (loadError) {
        if (controller.signal.aborted) return;
        setChannels([]);
        setCanCreateChannels(false);
        setError(errorMessage(loadError, 'Project channels could not be loaded.'));
        setLoadState('error');
      }
    }

    void load();
    return () => controller.abort();
  }, [projectId, reloadKey, session.apiStatus.status, session.client, session.tokenConfigured]);

  const createChannel = useCallback(async (request: CreateProjectChannelRequest) => {
    if (!projectId) throw new Error('Select a project before creating a channel.');
    const created = await session.client.createProjectChannel(projectId, request);
    setChannels((current) => [...current, created]);
    return created;
  }, [projectId, session.client]);

  return {
    channels,
    canCreateChannels,
    loadState,
    error,
    retry: () => setReloadKey((value) => value + 1),
    createChannel
  };
}

export function errorMessage(error: unknown, fallback: string) {
  if (error instanceof IronDevApiError && error.body && typeof error.body === 'object' && 'error' in error.body) {
    const message = (error.body as { error?: unknown }).error;
    if (typeof message === 'string' && message.trim()) return message;
  }
  return error instanceof Error && error.message ? error.message : fallback;
}
