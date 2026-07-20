import { createContext, useCallback, useContext, useEffect, useMemo, useRef, useState, type ReactNode } from 'react';
import { IronDevApiError } from '../api/ironDevApi';
import { useSessionContext } from './useSessionContext';
import type {
  ProductAccessStatus,
  ProjectSummary,
  StartProjectResponse,
  TenantSummary,
  UserProfile,
  WorkbenchProjectEntryContext
} from '../api/types';

interface ProjectContextState {
  userProfile: UserProfile | null;
  tenants: TenantSummary[];
  projects: ProjectSummary[];
  selectedTenantId: number | null;
  selectedProjectId: number | null;
  selectedProjectName: string | null;
  workbenchSession: ActiveWorkbenchSession | null;
  projectSelectionMode: 'api' | 'fallback-config' | 'missing';
  accessStatus: ProductAccessStatus;
  isRefreshing: boolean;
  refreshProjectContext: () => Promise<void>;
  refreshTicketsContext: () => void;
  selectTenantContext: (tenantId: number) => Promise<void>;
  selectProjectContext: (projectId: number, takeOver?: boolean) => Promise<void>;
  activateStartedProject: (started: StartProjectResponse) => void;
  applySelectedProjectName: (projectId: number, name: string) => void;
  setProjectAccessStatus: (status: ProductAccessStatus) => void;
}

export interface ActiveWorkbenchSession {
  projectId: number;
  workbenchSessionId: number;
  leaseEpoch: number;
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
  const [selectedProjectId, setSelectedProjectId] = useState<number | null>(null);
  const [selectedProjectName, setSelectedProjectName] = useState<string | null>(null);
  const [workbenchSession, setWorkbenchSession] = useState<ActiveWorkbenchSession | null>(null);
  const [projectSelectionMode, setProjectSelectionMode] = useState<'api' | 'fallback-config' | 'missing'>('missing');
  const [accessStatus, setAccessStatus] = useState<ProductAccessStatus>('loading');
  const [isRefreshing, setIsRefreshing] = useState(false);
  const refreshRequestEpoch = useRef(0);
  const pendingOpenOperation = useRef<{ payloadKey: string; operationId: string } | null>(null);

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
    setSelectedProjectId(null);
    setSelectedProjectName(null);
    setWorkbenchSession(null);
    pendingOpenOperation.current = null;
    setProjectSelectionMode('missing');
  }, [config.selectedTenantId]);

  const refreshProjectContext = useCallback(async () => {
    const requestEpoch = ++refreshRequestEpoch.current;
    const isCurrentRequest = () => requestEpoch === refreshRequestEpoch.current;
    setIsRefreshing(true);
    setAccessStatus('loading');

    try {
      const health = await checkApiConnection();
      if (!isCurrentRequest()) return;

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
      if (!isCurrentRequest()) return;
      setUserProfile(profile);

      const tenantList = await client.getTenants();
      if (!isCurrentRequest()) return;
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
        if (!isCurrentRequest()) return;
        window.sessionStorage.setItem('irondev.token', response.token);
        window.localStorage.removeItem('irondev.token');
        window.localStorage.setItem('irondev.tenantId', `${tenantId}`);
        window.localStorage.removeItem('irondev.selectedProjectId');
        refreshConfig();
        return;
      }

      const projectList = await client.getProjects();
      if (!isCurrentRequest()) return;
      setProjects(projectList);

      setSelectedProjectId(null);
      setSelectedProjectName(null);
      setWorkbenchSession(null);
      setProjectSelectionMode('missing');
      setAccessStatus('projectRequired');
    } catch (error) {
      if (!isCurrentRequest()) return;
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
      if (isCurrentRequest()) {
        setIsRefreshing(false);
        setTokenEditorOpen(false);
      }
    }
  }, [checkApiConnection, clearRejectedSession, clearWorkspace, client, config.selectedTenantId, refreshConfig, setTokenEditorOpen, tokenConfigured]);

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
        setWorkbenchSession(null);
        pendingOpenOperation.current = null;
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
    async (projectId: number, takeOver = false) => {
      if (!Number.isFinite(projectId)) {
        return;
      }

      const payloadKey = `${projectId}:${takeOver ? 'takeover' : 'resume'}`;
      const pending = pendingOpenOperation.current?.payloadKey === payloadKey
        ? pendingOpenOperation.current
        : { payloadKey, operationId: crypto.randomUUID() };
      pendingOpenOperation.current = pending;

      let opened: WorkbenchProjectEntryContext;
      try {
        opened = await client.openWorkbenchProject(projectId, pending.operationId, takeOver);
      } catch (error) {
        if (error instanceof IronDevApiError && pendingOpenOperation.current?.operationId === pending.operationId) {
          pendingOpenOperation.current = null;
        }
        throw error;
      }

      if (pendingOpenOperation.current?.operationId === pending.operationId) {
        pendingOpenOperation.current = null;
      }
      setSelectedProjectId(projectId);
      setProjectSelectionMode('api');
      setSelectedProjectName(opened.name);
      setWorkbenchSession({
        projectId: opened.projectId,
        workbenchSessionId: opened.workbenchSessionId,
        leaseEpoch: opened.leaseEpoch
      });
      setAccessStatus('loadingTickets');
    },
    [client]
  );

  const activateStartedProject = useCallback((started: StartProjectResponse) => {
    pendingOpenOperation.current = null;
    setSelectedProjectId(started.projectId);
    setSelectedProjectName(started.name);
    setProjectSelectionMode('api');
    setWorkbenchSession({
      projectId: started.projectId,
      workbenchSessionId: started.workbenchSessionId,
      leaseEpoch: started.leaseEpoch
    });
    setAccessStatus('loadingTickets');
    setProjects((current) => current.some((candidate) => candidate.id === started.projectId)
      ? current
      : [{
          id: started.projectId,
          tenantId: started.tenantId,
          name: started.name,
          localPath: null,
          lifecyclePhase: started.projectLifecyclePhase,
          executionReadiness: started.executionReadiness
        }, ...current]);
  }, []);

  const applySelectedProjectName = useCallback((projectId: number, name: string) => {
    const normalizedName = name.trim();
    if (!Number.isSafeInteger(projectId) || projectId <= 0 || normalizedName.length === 0) {
      return;
    }

    setProjects((current) => current.map((candidate) =>
      candidate.id === projectId ? { ...candidate, name: normalizedName } : candidate
    ));
    setSelectedProjectName((current) => selectedProjectId === projectId ? normalizedName : current);
  }, [selectedProjectId]);

  useEffect(() => {
    setSelectedTenantId(config.selectedTenantId ?? null);
    setSelectedProjectId(null);
    setWorkbenchSession(null);
    void refreshProjectContext();
  }, [config.selectedTenantId, refreshProjectContext, tokenConfigured]);

  const value: ProjectContextState = useMemo(
    () => ({
      userProfile,
      tenants,
      projects,
      selectedTenantId,
      selectedProjectId,
      selectedProjectName,
      workbenchSession,
      projectSelectionMode,
      accessStatus,
      isRefreshing,
      refreshProjectContext,
      refreshTicketsContext,
      selectTenantContext,
      selectProjectContext,
      activateStartedProject,
      applySelectedProjectName,
      setProjectAccessStatus: setAccessStatus
    }),
    [
      accessStatus,
      applySelectedProjectName,
      activateStartedProject,
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
      userProfile,
      workbenchSession
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
