import { createContext, useCallback, useContext, useEffect, useMemo, useState, type ReactNode } from 'react';
import { routeForId, routeForPath, type WorkspaceRoute } from '../app/routes';

export interface BuildDiscussionDraft {
  title: string;
  content: string;
  source: 'chat';
  createdUtc: string;
}

interface WorkspaceNavigationContextValue {
  activeRouteId: WorkspaceRoute['id'];
  navigateToWorkspace: (routeId: WorkspaceRoute['id']) => void;
  selectedRunId: string | null;
  setSelectedRunId: (runId: string | null) => void;
  buildDiscussionDraft: BuildDiscussionDraft | null;
  setBuildDiscussionDraft: (draft: BuildDiscussionDraft | null) => void;
  consumeBuildDiscussionDraft: () => void;
}

const WorkspaceNavigationContext = createContext<WorkspaceNavigationContextValue | null>(null);

export function WorkspaceNavigationProvider({ children }: { children: ReactNode }) {
  const [activeRouteId, setActiveRouteId] = useState<WorkspaceRoute['id']>(() => routeForPath(window.location.pathname).id);
  const [selectedRunId, setSelectedRunId] = useState<string | null>(null);
  const [buildDiscussionDraft, setBuildDiscussionDraft] = useState<BuildDiscussionDraft | null>(null);
  const navigateToWorkspace = useCallback((routeId: WorkspaceRoute['id']) => {
    const route = routeForId(routeId);
    if (window.location.pathname !== route.route) {
      window.history.pushState({ workspaceRouteId: routeId }, '', route.route);
    }

    setActiveRouteId(routeId);
  }, []);
  const consumeBuildDiscussionDraft = useCallback(() => {
    setBuildDiscussionDraft(null);
  }, []);

  useEffect(() => {
    const onPopState = () => {
      setActiveRouteId(routeForPath(window.location.pathname).id);
    };

    window.addEventListener('popstate', onPopState);
    return () => window.removeEventListener('popstate', onPopState);
  }, []);

  const contextValue = useMemo(
    () => ({
      activeRouteId,
      navigateToWorkspace,
      selectedRunId,
      setSelectedRunId,
      buildDiscussionDraft,
      setBuildDiscussionDraft,
      consumeBuildDiscussionDraft
    }),
    [activeRouteId, buildDiscussionDraft, consumeBuildDiscussionDraft, navigateToWorkspace, selectedRunId]
  );

  return <WorkspaceNavigationContext.Provider value={contextValue}>{children}</WorkspaceNavigationContext.Provider>;
}

export function useWorkspaceNavigation() {
  const context = useContext(WorkspaceNavigationContext);

  if (!context) {
    throw new Error('useWorkspaceNavigation must be used within a WorkspaceNavigationProvider');
  }

  return context;
}
