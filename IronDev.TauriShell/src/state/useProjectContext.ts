import { createContext, useCallback, useContext, useEffect, useMemo, useState, type ReactNode } from 'react';
import { IronDevApiError } from '../api/ironDevApi';
import { useSessionContext } from './useSessionContext';
import type { ProductAccessStatus, ProjectSummary, TenantSummary, UserProfile } from '../api/types';

interface ProjectContextState {
  userProfile: UserProfile | null;
  tenants: TenantSummary[];
  projects: ProjectSummary[];
  selectedTenantId: number | null;
  selectedProjectId: number | null;
  selectedProjectName: string | null;
  projectSelectionMode: 'api' | 'fallback-config' | 'missing';
  accessStatus: ProductAccessStatus;
  isRefreshing: boolean;
  refreshProjectContext: () => Promise<void>;
  refreshTicketsContext: () => void;
  selectTenantContext: (tenantId: number) => Promise<void>;
  selectProjectContext: (projectId: number) => void;
  setProjectAccessStatus: (status: ProductAccessStatus) => void;
}

const ProjectContext = createContext<ProjectContextState | null>(null);

export function ProjectProvider({ children }: { children: ReactNode }) {
  const session = useSessionContext();
  const [userProfile, setUserProfile] = useState<UserProfile | null>(null);
  const [tenants, setTenants] = useState<TenantSummary[]>([]);
  const [projects, setProjects] = useState<ProjectSummary[]>([]);
  const [selectedTenantId, setSelectedTenantId] = useState<number | null>(session.config.selectedTenantId ?? null);
  const [selectedProjectId, setSelectedProjectId] = useState<number | null>(session.config.selectedProjectId ?? null);
  const [selectedProjectName, setSelectedProjectName] = useState<string | null>(null);
  const [projectSelectionMode, setProjectSelectionMode] = useState<'api' | 'fallback-config' | 'missing'>('missing');
  const [accessStatus, setAccessStatus] = useState<ProductAccessStatus>('loading');
  const [isRefreshing, setIsRefreshing] = useState(false);

  const selectedProject = useMemo(
    () => projects.find((project) => project.id === selectedProjectId),
    [projects, selectedProjectId]
  );

  useEffect(() => {
    setSelectedProjectName(selectedProject?.name ?? null);
  }, [selectedProject]);

  const clearWorkspace = useCallback(() => {
    setUserProfile(null);
    setTenants([]);
    setProjects([]);
    setSelectedTenantId(session.config.selectedTenantId ?? null);
    setSelectedProjectId(session.config.selectedProjectId ?? null);
    setSelectedProjectName(null);
    setProjectSelectionMode('missing');
  }, [session.config.selectedProjectId, session.config.selectedTenantId]);

  const refreshProjectContext = useCallback(async () => {
    setIsRefreshing(true);
    setAccessStatus('loading');

    try {
      const health = await session.checkApiConnection();

      if (health.status === 'disconnected' || health.status === 'error') {
        clearWorkspace();
        setAccessStatus(health.status === 'disconnected' ? 'apiOffline' : 'apiError');
        return;
      }

      if (!session.tokenConfigured) {
        clearWorkspace();
        setAccessStatus('authRequired');
        return;
      }

      const profile = await session.client.getCurrentUser();
      setUserProfile(profile);

      const tenantList = await session.client.getTenants();
      setTenants(tenantList);

      const tenantId = profile.selectedTenantId ?? session.config.selectedTenantId ?? null;
      setSelectedTenantId(tenantId);

      if (!tenantId) {
        setProjects([]);
        setSelectedProjectId(null);
        setSelectedProjectName(null);
        setProjectSelectionMode('missing');
        setAccessStatus('tenantRequired');
        return;
      }

      const projectList = await session.client.getProjects();
      setProjects(projectList);

      const configuredProject = getSelectedProject(projectList, session.config.selectedProjectId, session.config.fallbackProjectId);
      const resolvedProjectId = configuredProject?.id ?? null;
      setSelectedProjectId(resolvedProjectId);
      setProjectSelectionMode(configuredProject?.mode ?? 'missing');
      setSelectedProjectName(configuredProject?.name ?? null);

      if (!resolvedProjectId) {
        setAccessStatus('projectRequired');
        return;
      }

      if (configuredProject?.mode === 'api') {
        await session.client.selectProject(resolvedProjectId);
      }

      setAccessStatus('loadingTickets');
    } catch (error) {
      clearWorkspace();
      if (error instanceof IronDevApiError) {
        if (error.isAuthFailure) {
          setAccessStatus('authInvalid');
        } else {
          setAccessStatus('apiError');
        }
      } else {
        setAccessStatus('apiOffline');
      }
    } finally {
      setIsRefreshing(false);
      session.setTokenEditorOpen(false);
    }
  }, [clearWorkspace, session]);

  const refreshTicketsContext = useCallback(() => {
    if (accessStatus === 'ready' || accessStatus === 'emptyTickets') {
      setAccessStatus('loadingTickets');
      return;
    }

    if (accessStatus === 'loading') {
      setAccessStatus('loading');
    }
  }, [accessStatus]);

  const selectTenantContext = useCallback(
    async (tenantId: number) => {
      if (!Number.isFinite(tenantId)) {
        return;
      }

      setIsRefreshing(true);

      try {
        const response = await session.client.selectTenant(tenantId);
        window.localStorage.setItem('irondev.token', response.token);
        window.localStorage.setItem('irondev.tenantId', `${tenantId}`);
        window.localStorage.removeItem('irondev.selectedProjectId');
        setSelectedTenantId(tenantId);
        setSelectedProjectId(null);
        setSelectedProjectName(null);
        setProjectSelectionMode('missing');
        setAccessStatus('loading');
        session.refreshConfig();
      } catch (error) {
        if (error instanceof IronDevApiError) {
          setAccessStatus('apiError');
        } else {
          setAccessStatus('apiOffline');
        }
      } finally {
        setIsRefreshing(false);
      }
    },
    [session]
  );

  const selectProjectContext = useCallback(
    (projectId: number) => {
      if (!Number.isFinite(projectId)) {
        return;
      }

      window.localStorage.setItem('irondev.selectedProjectId', `${projectId}`);
      setSelectedProjectId(projectId);
      setProjectSelectionMode('api');
      setSelectedProjectName(projects.find((project) => project.id === projectId)?.name ?? `Project ${projectId}`);
      setAccessStatus('loadingTickets');
      session.refreshConfig();
    },
    [projects, session]
  );

  useEffect(() => {
    setSelectedTenantId(session.config.selectedTenantId ?? null);
    setSelectedProjectId(session.config.selectedProjectId ?? null);
    void refreshProjectContext();
  }, [session.config.selectedTenantId, session.config.selectedProjectId, refreshProjectContext, session.config.token]);

  const value: ProjectContextState = {
    userProfile,
    tenants,
    projects,
    selectedTenantId,
    selectedProjectId,
    selectedProjectName,
    projectSelectionMode,
    accessStatus,
    isRefreshing,
    refreshProjectContext,
    refreshTicketsContext,
    selectTenantContext,
    selectProjectContext,
    setProjectAccessStatus: setAccessStatus
  };

  return <ProjectContext.Provider value={value}>{children}</ProjectContext.Provider>;
}

export function useProjectContext() {
  const context = useContext(ProjectContext);

  if (!context) {
    throw new Error('useProjectContext must be used within a ProjectProvider');
  }

  return context;
}

function getSelectedProject(
  projects: ProjectSummary[],
  selectedProjectId?: number,
  fallbackProjectId?: number
): (ProjectSummary & { mode: 'api' | 'fallback-config' }) | null {
  const configuredProject = projects.find((project) => project.id === selectedProjectId);

  if (configuredProject) {
    return { ...configuredProject, mode: 'api' };
  }

  if (!selectedProjectId) {
    const fallbackProject = projects.find((project) => project.id === fallbackProjectId);
    return fallbackProject ? { ...fallbackProject, mode: 'fallback-config' } : null;
  }

  return null;
}
