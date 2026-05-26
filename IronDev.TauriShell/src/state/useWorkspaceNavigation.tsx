import { createContext, useCallback, useContext, useMemo, useState, type ReactNode } from 'react';
import type { WorkspaceRoute } from '../app/routes';

interface WorkspaceNavigationContextValue {
  activeRouteId: WorkspaceRoute['id'];
  navigateToWorkspace: (routeId: WorkspaceRoute['id']) => void;
  selectedRunId: string | null;
  setSelectedRunId: (runId: string | null) => void;
}

const WorkspaceNavigationContext = createContext<WorkspaceNavigationContextValue | null>(null);

export function WorkspaceNavigationProvider({ children }: { children: ReactNode }) {
  const [activeRouteId, setActiveRouteId] = useState<WorkspaceRoute['id']>('tickets');
  const [selectedRunId, setSelectedRunId] = useState<string | null>(null);
  const navigateToWorkspace = useCallback((routeId: WorkspaceRoute['id']) => {
    setActiveRouteId(routeId);
  }, []);

  const contextValue = useMemo(
    () => ({
      activeRouteId,
      navigateToWorkspace,
      selectedRunId,
      setSelectedRunId
    }),
    [activeRouteId, navigateToWorkspace, selectedRunId]
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

