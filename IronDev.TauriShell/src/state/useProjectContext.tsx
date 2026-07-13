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
  selectProjectContext: (projectId: number) => Promise<void>;
  setProjectAccessStatus: (status: ProductAccessStatus) => void;
}

const ProjectContext = createContext<ProjectContextState | null>(null);

export function ProjectProvider({ children }: { children: ReactNode }) {
  const session = useSessionContext();
  const {
    checkApiConnection,
    client,
    clearRejectedSession,
    config,
    refreshConfig,
    setTokenEditorOpen,
    tokenConfigured
  } = session;
  const [userProfile, setUserProfile] = useState<UserProfile | null>(null);
  const [tenants, setTenants] = useState<TenantSummary[]>([]);
  const [projects, setProjects] = useState<ProjectSummary[]>([]);
  const [selectedTenantId, setSelectedTenantId] = useState<number | null>(config.selectedTenantId ?? null);
  const [selectedProjectId, setSelectedProjectId] = useState<number | null>(config.selectedProjectId ?? null);
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
    setSelectedTenantId(config.selectedTenantId ?? null);
    setSelectedProjectId(config.selectedProjectId ?? null);
    setSelectedProjectName(null);
    setProjectSelectionMode('missing');
  }, [config.selectedProjectId, config.selectedTenantId]);

  const refreshProjectContext = useCallback(async () => {
    setIsRefreshing(true);
    setAccessStatus('loading');

    try {
      const health = await checkApiConnection();

      if (health.status === 'disconnected' || health.status === 'error') {
        clearWorkspace();
        setAccessStatus(health.status === 'disconnected' ? 'apiOffline' : 'apiError');
        return;
      }

      if (!tokenConfigured) {
        clearWorkspace();
        setAccessStatus('authRequired');
        return;
      }

      const profile = await client.getCurrentUser();
      setUserProfile(profile);

      const tenantList = await client.getTenants();
      setTenants(tenantList);

      const tenantId = profile.selectedTenantId ?? config.selectedTenantId ?? (tenantList.length === 1 ? tenantList[0].id ?? null : null);
      setSelectedTenantId(tenantId);

      if (!tenantId) {
        setProjects([]);
        setSelectedProjectId(null);
        setSelectedProjectName(null);
        setProjectSelectionMode('missing');
        setAccessStatus('tenantRequired');
        return;
      }

      if (profile.selectedTenantId !== tenantId) {
        const response = await client.selectTenant(tenantId);
        window.sessionStorage.setItem('irondev.token', response.token);
        window.localStorage.removeItem('irondev.token');
        window.localStorage.setItem('irondev.tenantId', `${tenantId}`);
        window.localStorage.removeItem('irondev.selectedProjectId');
        refreshConfig();
        return;
      }

      const projectList = await client.getProjects();
      setProjects(projectList);

      const configuredProject = getSelectedProject(projectList, config.selectedProjectId);
      const resolvedProjectId = configuredProject?.id ?? null;
      setSelectedProjectId(resolvedProjectId);
      setProjectSelectionMode(configuredProject?.mode ?? 'missing');
      setSelectedProjectName(configuredProject?.name ?? null);

      if (!resolvedProjectId) {
        setAccessStatus('projectRequired');
        return;
      }

      if (configuredProject?.mode === 'api') {
        await client.selectProject(resolvedProjectId);
      }

      setAccessStatus('loadingTickets');
    } catch (error) {
      clearWorkspace();
      if (error instanceof IronDevApiError) {
        if (error.isAuthFailure) {
          clearRejectedSession();
          setAccessStatus('authInvalid');
        } else {
          setAccessStatus('apiError');
        }
      } else {
        setAccessStatus('apiOffline');
      }
    } finally {
      setIsRefreshing(false);
      setTokenEditorOpen(false);
    }
  }, [checkApiConnection, clearRejectedSession, clearWorkspace, client, config.selectedProjectId, config.selectedTenantId, refreshConfig, setTokenEditorOpen, tokenConfigured]);

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
        const response = await client.selectTenant(tenantId);
        window.sessionStorage.setItem('irondev.token', response.token);
        window.localStorage.removeItem('irondev.token');
        window.localStorage.setItem('irondev.tenantId', `${tenantId}`);
        window.localStorage.removeItem('irondev.selectedProjectId');
        setSelectedTenantId(tenantId);
        setSelectedProjectId(null);
        setSelectedProjectName(null);
        setProjectSelectionMode('missing');
        setAccessStatus('loading');
        refreshConfig();
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
    [client, refreshConfig]
  );

  const selectProjectContext = useCallback(
    async (projectId: number) => {
      if (!Number.isFinite(projectId)) {
        return;
      }

      await client.selectProject(projectId);
      window.localStorage.setItem('irondev.selectedProjectId', `${projectId}`);
      setSelectedProjectId(projectId);
      setProjectSelectionMode('api');
      setSelectedProjectName(projects.find((project) => project.id === projectId)?.name ?? `Project ${projectId}`);
      setAccessStatus('loadingTickets');
      refreshConfig();
    },
    [client, projects, refreshConfig]
  );

  useEffect(() => {
    setSelectedTenantId(config.selectedTenantId ?? null);
    setSelectedProjectId(config.selectedProjectId ?? null);
    void refreshProjectContext();
  }, [config.selectedTenantId, config.selectedProjectId, refreshProjectContext, tokenConfigured]);

  const value: ProjectContextState = useMemo(
    () => ({
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
    }),
    [
      accessStatus,
      isRefreshing,
      projectSelectionMode,
      projects,
      refreshProjectContext,
      refreshTicketsContext,
      selectProjectContext,
      selectTenantContext,
      selectedProjectId,
      selectedProjectName,
      selectedTenantId,
      tenants,
      userProfile
    ]
  );

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
  selectedProjectId?: number
): (ProjectSummary & { mode: 'api' }) | null {
  const configuredProject = projects.find((project) => project.id === selectedProjectId);

  if (configuredProject) {
    return { ...configuredProject, mode: 'api' };
  }

  return null;
}
