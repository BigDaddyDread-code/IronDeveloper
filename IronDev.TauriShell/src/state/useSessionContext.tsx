import { createContext, useCallback, useContext, useEffect, useMemo, useState, type ReactNode } from 'react';
import { IronDevApiError, createIronDevApiClient, getIronDevApiConfig, type IronDevApiConfig } from '../api/ironDevApi';
import type {
  ApiConnectionStatus,
  ApiStatus,
  EnvironmentInfo,
  LocalTestPreflightInfo,
  LocalTestPreflightState,
  LoginRequest
} from '../api/types';

interface SessionContextState {
  config: IronDevApiConfig;
  client: ReturnType<typeof createIronDevApiClient>;
  tokenConfigured: boolean;
  isConnectionBusy: boolean;
  isAuthBusy: boolean;
  isConnectionReady: boolean;
  isTokenEditorOpen: boolean;
  apiStatus: ApiStatus;
  environmentInfo: EnvironmentInfo | null;
  localTestPreflight: LocalTestPreflightInfo | null;
  tokenDraft: string;
  email: string;
  password: string;
  errorMessage: string | null;
  sessionMessage: string | null;
  refreshConfig: () => void;
  clearError: () => void;
  clearRejectedSession: () => void;
  checkApiConnection: () => Promise<ApiStatus>;
  saveToken: () => void;
  signIn: (request: LoginRequest) => Promise<boolean>;
  signOut: () => Promise<void>;
  setTokenDraft: (value: string) => void;
  setEmail: (value: string) => void;
  setPassword: (value: string) => void;
  setTokenEditorOpen: (value: boolean) => void;
}

const SessionContext = createContext<SessionContextState | null>(null);
const initialApiStatusStatus: ApiConnectionStatus = 'loading';
const localTestEmail = 'bob@irondev.local';
const localTestPassword = 'change-me-local-only';
const localTestResetCommand = '.\\tools\\localtest\\start-pr-manual-test.ps1 -FreshSession -BrowserOnly -Reset';
const sandboxApplyRestartCommand = `${localTestResetCommand} -EnableSandboxApply`;

function createInitialStatus(config: IronDevApiConfig): ApiStatus {
  return {
    status: initialApiStatusStatus,
    baseUrl: config.apiBaseUrl,
    message: 'Checking IronDev.Api...'
  };
}

function clientPreflight(
  state: LocalTestPreflightState,
  config: IronDevApiConfig,
  detail: string,
  resetCommand: string | null = null
): LocalTestPreflightInfo {
  return {
    state,
    environment: import.meta.env.VITE_IRONDEV_LOCALTEST_SESSION_ID ? 'LocalTest' : 'Unknown',
    database: null,
    apiBuildIdentity: 'Not reported',
    apiBuildCommit: 'Not reported',
    launcherRepositoryCommit: import.meta.env.VITE_IRONDEV_LOCALTEST_REPOSITORY_COMMIT ?? null,
    sessionId: import.meta.env.VITE_IRONDEV_LOCALTEST_SESSION_ID ?? null,
    apiBaseUrl: config.apiBaseUrl,
    apiPid: 0,
    seedContractVersion: null,
    seededLoginCheckResult: 'NotChecked',
    nextSafeAction: state === 'ApiOffline'
      ? 'Start the supported LocalTest launcher and retry the connection.'
      : 'Wait for the LocalTest identity and seed checks to complete.',
    resetCommand,
    detail,
    workbenchVersion: import.meta.env.VITE_IRONDEV_WORKBENCH_VERSION ?? '0.1.0-preview.3',
    workbenchMode: import.meta.env.VITE_IRONDEV_WORKBENCH_MODE === 'V2' ? 'V2' : 'V1',
    previewId: import.meta.env.VITE_IRONDEV_PREVIEW_ID ?? 'default',
    sessionMode: import.meta.env.VITE_IRONDEV_LOCALTEST_SESSION_MODE ?? '',
    sandboxApplyRequested: import.meta.env.VITE_IRONDEV_LOCALTEST_SANDBOX_APPLY_REQUESTED === 'true',
    sandboxApplyEnabled: import.meta.env.VITE_IRONDEV_LOCALTEST_SANDBOX_APPLY_ENABLED === 'true',
    sandboxApplyRoot: import.meta.env.VITE_IRONDEV_LOCALTEST_SANDBOX_APPLY_ROOT ?? null,
    capabilities: (import.meta.env.VITE_IRONDEV_LOCALTEST_CAPABILITIES ?? '').split(';').filter(Boolean),
    sandboxApplyRestartCommand
  };
}

function normalizeUrl(value: string | null | undefined) {
  return value?.trim().replace(/\/+$/, '').replace('://localhost', '://127.0.0.1') ?? '';
}

function verifyBrowserIdentity(info: LocalTestPreflightInfo, config: IronDevApiConfig): LocalTestPreflightInfo {
  const expectedSessionId = import.meta.env.VITE_IRONDEV_LOCALTEST_SESSION_ID;
  const expectedCommit = import.meta.env.VITE_IRONDEV_LOCALTEST_REPOSITORY_COMMIT;
  const expectedApiBaseUrl = import.meta.env.VITE_IRONDEV_LOCALTEST_API_BASE_URL;
  const expectedPreviewId = import.meta.env.VITE_IRONDEV_PREVIEW_ID;
  const expectedWorkbenchVersion = import.meta.env.VITE_IRONDEV_WORKBENCH_VERSION;
  const expectedWorkbenchMode = import.meta.env.VITE_IRONDEV_WORKBENCH_MODE;
  const expectedSessionMode = import.meta.env.VITE_IRONDEV_LOCALTEST_SESSION_MODE;
  const expectedApplyRequested = import.meta.env.VITE_IRONDEV_LOCALTEST_SANDBOX_APPLY_REQUESTED;
  const expectedApplyEnabled = import.meta.env.VITE_IRONDEV_LOCALTEST_SANDBOX_APPLY_ENABLED;
  const expectedSandboxRoot = import.meta.env.VITE_IRONDEV_LOCALTEST_SANDBOX_APPLY_ROOT;
  const expectedCapabilities = (import.meta.env.VITE_IRONDEV_LOCALTEST_CAPABILITIES ?? '').split(';').filter(Boolean).sort();
  const reportedApiMismatch =
    info.environment === 'LocalTest' &&
    Boolean(info.apiBaseUrl) &&
    normalizeUrl(info.apiBaseUrl) !== normalizeUrl(config.apiBaseUrl);
  const reportedCapabilities = info.capabilities ?? [];
  const reportedCapabilityConsistent = info.sandboxApplyRequested
    ? info.sandboxApplyEnabled &&
      info.sessionMode === 'ProjectFeatureWork' &&
      Boolean(info.sandboxApplyRoot) &&
      reportedCapabilities.includes('ProjectFeatureWork') &&
      reportedCapabilities.includes('ControlledSandboxApply')
    : !info.sandboxApplyEnabled &&
      !info.sandboxApplyRoot &&
      info.sessionMode === 'SmokeSimulation' &&
      !reportedCapabilities.includes('ControlledSandboxApply');
  if (!expectedSessionId && !expectedCommit && !expectedApiBaseUrl && !reportedApiMismatch && reportedCapabilityConsistent) {
    return info;
  }

  const identityMatches =
    (!expectedSessionId || info.sessionId === expectedSessionId) &&
    (!expectedCommit || (info.launcherRepositoryCommit === expectedCommit && info.apiBuildCommit === expectedCommit)) &&
    (!expectedPreviewId || info.previewId === expectedPreviewId) &&
    (!expectedWorkbenchVersion || info.workbenchVersion === expectedWorkbenchVersion) &&
    (!expectedWorkbenchMode || info.workbenchMode === expectedWorkbenchMode) &&
    normalizeUrl(info.apiBaseUrl) === normalizeUrl(expectedApiBaseUrl || config.apiBaseUrl) &&
    (!expectedApiBaseUrl || normalizeUrl(config.apiBaseUrl) === normalizeUrl(expectedApiBaseUrl));

  if (identityMatches) {
    const capabilityMatches =
      reportedCapabilityConsistent &&
      (!expectedSessionMode || info.sessionMode === expectedSessionMode) &&
      (expectedApplyRequested === undefined || info.sandboxApplyRequested === (expectedApplyRequested === 'true')) &&
      (expectedApplyEnabled === undefined || info.sandboxApplyEnabled === (expectedApplyEnabled === 'true')) &&
      (expectedSandboxRoot === undefined || (info.sandboxApplyRoot ?? '').toLowerCase() === expectedSandboxRoot.toLowerCase()) &&
      (expectedCapabilities.length === 0 || JSON.stringify([...(info.capabilities ?? [])].sort()) === JSON.stringify(expectedCapabilities));

    if (capabilityMatches) return info;

    return {
      ...info,
      state: 'SessionCapabilityMismatch',
      seededLoginCheckResult: 'NotChecked',
      nextSafeAction: 'Session capability mismatch. Restart through the supported project-work launcher.',
      resetCommand: sandboxApplyRestartCommand,
      sandboxApplyRestartCommand,
      detail: 'The browser and API disagree about the project-work or controlled sandbox-apply capability.'
    };
  }

  return {
    ...info,
    state: 'ApiIdentityMismatch',
    seededLoginCheckResult: 'NotChecked',
    nextSafeAction: 'Stop. Restart LocalTest through the supported launcher so the browser and API share one session identity.',
    resetCommand: localTestResetCommand,
    detail: 'The browser launcher identity does not match the connected API identity.'
  };
}

export function SessionProvider({ children }: { children: ReactNode }) {
  const [configVersion, setConfigVersion] = useState(0);
  const config = useMemo(() => getIronDevApiConfig(), [configVersion]);
  const client = useMemo(() => createIronDevApiClient(config), [config]);
  const tokenConfigured = Boolean(config.token);
  const [apiStatus, setApiStatus] = useState<ApiStatus>(() => createInitialStatus(config));
  const [environmentInfo, setEnvironmentInfo] = useState<EnvironmentInfo | null>(null);
  const [localTestPreflight, setLocalTestPreflight] = useState<LocalTestPreflightInfo | null>(null);
  const [isConnectionBusy, setIsConnectionBusy] = useState(false);
  const [isAuthBusy, setIsAuthBusy] = useState(false);
  const [isTokenEditorOpen, setTokenEditorOpen] = useState(false);
  const [errorMessage, setErrorMessage] = useState<string | null>(null);
  const [tokenDraft, setTokenDraft] = useState('');
  const [email, setEmail] = useState('');
  const [password, setPassword] = useState('');
  const [sessionMessage, setSessionMessage] = useState<string | null>(null);

  useEffect(() => {
    setApiStatus(createInitialStatus(config));
    setEnvironmentInfo(null);
    setLocalTestPreflight(null);
  }, [config.apiBaseUrl, tokenConfigured]);

  useEffect(() => {
    const isLocalTest = environmentInfo?.isTestEnvironment || localTestPreflight?.environment === 'LocalTest';
    if (isLocalTest) {
      setEmail((value) => value || localTestEmail);
      setPassword((value) => value || localTestPassword);
      return;
    }

    setEmail((value) => (value === localTestEmail ? '' : value));
    setPassword((value) => (value === localTestPassword ? '' : value));
  }, [environmentInfo?.isTestEnvironment, localTestPreflight?.environment]);

  const refreshConfig = useCallback(() => {
    setConfigVersion((value) => value + 1);
  }, []);

  const saveToken = useCallback(() => {
    const trimmed = tokenDraft.trim();

    if (!trimmed) {
      return;
    }

    window.sessionStorage.setItem('irondev.token', trimmed);
    window.localStorage.removeItem('irondev.token');
    setTokenDraft('');
    setTokenEditorOpen(false);
    refreshConfig();
  }, [refreshConfig, tokenDraft]);

  const clearError = useCallback(() => {
    setErrorMessage(null);
    setSessionMessage(null);
  }, []);

  const clearRejectedSession = useCallback(() => {
    window.sessionStorage.removeItem('irondev.token');
    window.localStorage.removeItem('irondev.token');
    window.localStorage.removeItem('irondev.tenantId');
    window.localStorage.removeItem('irondev.selectedProjectId');
    setTokenDraft('');
    setTokenEditorOpen(false);
    setSessionMessage('Your session has expired. Sign in again.');
    refreshConfig();
  }, [refreshConfig]);

  const checkApiConnection = useCallback(async () => {
    setIsConnectionBusy(true);

    try {
      const status = await client.checkHealth();
      setApiStatus(status);
      if (status.status === 'connected') {
        setLocalTestPreflight(clientPreflight('ApiConnected', config, 'The API is reachable; LocalTest identity and seed checks are in progress.'));
        try {
          setLocalTestPreflight(verifyBrowserIdentity(await client.getLocalTestPreflight(), config));
        } catch {
          setLocalTestPreflight(clientPreflight('ApiConnected', config, 'The API is reachable but did not report a LocalTest preflight identity.'));
        }
        try {
          setEnvironmentInfo(await client.getEnvironment());
        } catch {
          setEnvironmentInfo(null);
        }
      } else {
        setEnvironmentInfo(null);
        setLocalTestPreflight(clientPreflight('ApiOffline', config, status.message));
      }

      return status;
    } finally {
      setIsConnectionBusy(false);
    }
  }, [client, config]);

  useEffect(() => {
    let active = true;
    if (!tokenConfigured) return () => { active = false; };

    void client.getEnvironment()
      .then((info) => { if (active) setEnvironmentInfo(info); })
      .catch(() => { if (active) setEnvironmentInfo(null); });

    return () => { active = false; };
  }, [client, tokenConfigured]);

  const signIn = useCallback(
    async (request: LoginRequest) => {
      setIsAuthBusy(true);
      setErrorMessage(null);
      setSessionMessage(null);

      try {
        const response = await client.login(request);
        window.sessionStorage.setItem('irondev.token', response.token);
        window.localStorage.removeItem('irondev.token');
        window.localStorage.removeItem('irondev.tenantId');
        window.localStorage.removeItem('irondev.selectedProjectId');
        setTokenDraft('');
        setTokenEditorOpen(false);
        setPassword('');
        refreshConfig();
        return true;
      } catch (error) {
        const isLocalTest = environmentInfo?.isTestEnvironment || localTestPreflight?.environment === 'LocalTest';
        if (isLocalTest) {
          const state: LocalTestPreflightState = error instanceof IronDevApiError && error.status === 401
            ? 'SeedCredentialInvalid'
            : 'ApiConnected';
          setLocalTestPreflight((current) => ({
            ...(current ?? clientPreflight(state, config, 'LocalTest sign in failed.', localTestResetCommand)),
            state,
            seededLoginCheckResult: 'Failed',
            nextSafeAction: 'Run the explicit LocalTest reset command, then start a fresh supported session.',
            resetCommand: localTestResetCommand,
            detail: error instanceof Error ? error.message : 'The seeded LocalTest login was rejected.'
          }));
          setErrorMessage('LocalTest sign in failed. Use the exact safe reset command shown below.');
        } else {
          setErrorMessage('Sign in failed. Check the email and password and try again.');
        }
        return false;
      } finally {
        setIsAuthBusy(false);
      }
    },
    [client, config, environmentInfo?.isTestEnvironment, localTestPreflight?.environment, refreshConfig]
  );

  const signOut = useCallback(async () => {
    try {
      if (tokenConfigured) {
        await client.logout();
      }
    } catch {
      // Logout is stateless. Local session removal remains the safe outcome
      // when the API is unavailable.
    } finally {
      window.sessionStorage.removeItem('irondev.token');
      window.localStorage.removeItem('irondev.token');
      window.localStorage.removeItem('irondev.tenantId');
      window.localStorage.removeItem('irondev.selectedProjectId');
      setTokenDraft('');
      setPassword('');
      setTokenEditorOpen(false);
      refreshConfig();
    }
  }, [client, refreshConfig, tokenConfigured]);

  const value: SessionContextState = useMemo(
    () => ({
      config,
      client,
      tokenConfigured,
      isConnectionBusy,
      isAuthBusy,
      isConnectionReady:
        !isConnectionBusy &&
        !isAuthBusy &&
        apiStatus.status === 'connected' &&
        (localTestPreflight?.state === 'LocalTestReady' ||
          (localTestPreflight?.environment !== 'LocalTest' && localTestPreflight?.state !== 'ApiIdentityMismatch')),
      isTokenEditorOpen,
      apiStatus,
      environmentInfo,
      localTestPreflight,
      tokenDraft,
      email,
      password,
      errorMessage,
      sessionMessage,
      refreshConfig,
      clearError,
      clearRejectedSession,
      checkApiConnection,
      saveToken,
      signIn,
      signOut,
      setTokenDraft,
      setEmail,
      setPassword,
      setTokenEditorOpen
    }),
    [
      apiStatus,
      checkApiConnection,
      clearError,
      clearRejectedSession,
      client,
      config,
      email,
      environmentInfo,
      localTestPreflight,
      errorMessage,
      isAuthBusy,
      isConnectionBusy,
      isTokenEditorOpen,
      password,
      refreshConfig,
      saveToken,
      signIn,
      signOut,
      sessionMessage,
      tokenConfigured,
      tokenDraft
    ]
  );

  return <SessionContext.Provider value={value}>{children}</SessionContext.Provider>;
}

export function useSessionContext() {
  const context = useContext(SessionContext);

  if (!context) {
    throw new Error('useSessionContext must be used within a SessionProvider');
  }

  return context;
}
