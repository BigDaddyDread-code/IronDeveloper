import { createContext, useCallback, useContext, useEffect, useMemo, useState, type ReactNode } from 'react';
import { IronDevApiError, createIronDevApiClient, getIronDevApiConfig, type IronDevApiConfig } from '../api/ironDevApi';
import type { ApiConnectionStatus, ApiStatus, EnvironmentInfo, LoginRequest } from '../api/types';

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
  signIn: (request: LoginRequest) => Promise<void>;
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

function createInitialStatus(config: IronDevApiConfig): ApiStatus {
  return {
    status: initialApiStatusStatus,
    baseUrl: config.apiBaseUrl,
    message: 'Checking IronDev.Api...'
  };
}

export function SessionProvider({ children }: { children: ReactNode }) {
  const [configVersion, setConfigVersion] = useState(0);
  const config = useMemo(() => getIronDevApiConfig(), [configVersion]);
  const client = useMemo(() => createIronDevApiClient(config), [config]);
  const [apiStatus, setApiStatus] = useState<ApiStatus>(() => createInitialStatus(config));
  const [environmentInfo, setEnvironmentInfo] = useState<EnvironmentInfo | null>(null);
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
  }, [config.apiBaseUrl, config.token]);

  useEffect(() => {
    if (environmentInfo?.isTestEnvironment) {
      setEmail((value) => value || localTestEmail);
      setPassword((value) => value || localTestPassword);
      return;
    }

    setEmail((value) => (value === localTestEmail ? '' : value));
    setPassword((value) => (value === localTestPassword ? '' : value));
  }, [environmentInfo?.isTestEnvironment]);

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
        try {
          setEnvironmentInfo(await client.getEnvironment());
        } catch {
          setEnvironmentInfo(null);
        }
      } else {
        setEnvironmentInfo(null);
      }

      return status;
    } finally {
      setIsConnectionBusy(false);
    }
  }, [client]);

  useEffect(() => {
    let active = true;
    if (!config.token) return () => { active = false; };

    void client.getEnvironment()
      .then((info) => { if (active) setEnvironmentInfo(info); })
      .catch(() => { if (active) setEnvironmentInfo(null); });

    return () => { active = false; };
  }, [client, config.token]);

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
      } catch (error) {
        if (error instanceof IronDevApiError) {
          if (environmentInfo?.isTestEnvironment) {
            setErrorMessage('LocalTest sign in failed. Reset the LocalTest data and retry.');
          } else {
            setErrorMessage('Sign in failed. Check the email and password and try again.');
          }
        } else {
          setErrorMessage(
            environmentInfo?.isTestEnvironment
              ? 'LocalTest sign in failed. Reset the LocalTest data and retry.'
              : 'Sign in failed. Check the email and password and try again.'
          );
        }
      } finally {
        setIsAuthBusy(false);
      }
    },
    [client, environmentInfo?.isTestEnvironment, refreshConfig]
  );

  const signOut = useCallback(async () => {
    try {
      if (config.token) {
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
  }, [client, config.token, refreshConfig]);

  const tokenConfigured = Boolean(config.token);

  const value: SessionContextState = useMemo(
    () => ({
      config,
      client,
      tokenConfigured,
      isConnectionBusy,
      isAuthBusy,
      isConnectionReady: !isConnectionBusy && !isAuthBusy && apiStatus.status === 'connected',
      isTokenEditorOpen,
      apiStatus,
      environmentInfo,
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
