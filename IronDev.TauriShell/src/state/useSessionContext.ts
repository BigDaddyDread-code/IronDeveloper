import { createContext, useCallback, useContext, useEffect, useMemo, useState, type ReactNode } from 'react';
import { IronDevApiError, createIronDevApiClient, getIronDevApiConfig, type IronDevApiConfig } from '../api/ironDevApi';
import type { ApiConnectionStatus, ApiStatus, LoginRequest } from '../api/types';

interface SessionContextState {
  config: IronDevApiConfig;
  client: ReturnType<typeof createIronDevApiClient>;
  tokenConfigured: boolean;
  isConnectionBusy: boolean;
  isAuthBusy: boolean;
  isConnectionReady: boolean;
  isTokenEditorOpen: boolean;
  apiStatus: ApiStatus;
  tokenDraft: string;
  email: string;
  password: string;
  errorMessage: string | null;
  refreshConfig: () => void;
  clearError: () => void;
  checkApiConnection: () => Promise<ApiStatus>;
  saveToken: () => void;
  signIn: (request: LoginRequest) => Promise<void>;
  setTokenDraft: (value: string) => void;
  setEmail: (value: string) => void;
  setPassword: (value: string) => void;
  setTokenEditorOpen: (value: boolean) => void;
}

const SessionContext = createContext<SessionContextState | null>(null);
const initialApiStatusStatus: ApiConnectionStatus = 'loading';

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
  const [isConnectionBusy, setIsConnectionBusy] = useState(false);
  const [isAuthBusy, setIsAuthBusy] = useState(false);
  const [isTokenEditorOpen, setTokenEditorOpen] = useState(false);
  const [errorMessage, setErrorMessage] = useState<string | null>(null);
  const [tokenDraft, setTokenDraft] = useState('');
  const [email, setEmail] = useState('');
  const [password, setPassword] = useState('');

  useEffect(() => {
    setApiStatus(createInitialStatus(config));
  }, [config.apiBaseUrl, config.fallbackProjectId, config.selectedProjectId, config.selectedTenantId, config.token]);

  const refreshConfig = useCallback(() => {
    setConfigVersion((value) => value + 1);
  }, []);

  const saveToken = useCallback(() => {
    const trimmed = tokenDraft.trim();

    if (!trimmed) {
      return;
    }

    window.localStorage.setItem('irondev.token', trimmed);
    setTokenDraft('');
    setTokenEditorOpen(false);
    refreshConfig();
  }, [refreshConfig, tokenDraft]);

  const clearError = useCallback(() => setErrorMessage(null), []);

  const checkApiConnection = useCallback(async () => {
    setIsConnectionBusy(true);

    try {
      const status = await client.checkHealth();
      setApiStatus(status);

      return status;
    } finally {
      setIsConnectionBusy(false);
    }
  }, [client]);

  const signIn = useCallback(
    async (request: LoginRequest) => {
      setIsAuthBusy(true);
      setErrorMessage(null);

      try {
        const response = await client.login(request);
        window.localStorage.setItem('irondev.token', response.token);
        window.localStorage.removeItem('irondev.tenantId');
        window.localStorage.removeItem('irondev.selectedProjectId');
        refreshConfig();
      } catch (error) {
        if (error instanceof IronDevApiError) {
          setErrorMessage('Sign in failed. Check credentials and retry.');
        } else {
          setErrorMessage('Sign in failed.');
        }
      } finally {
        setIsAuthBusy(false);
      }
    },
    [client, refreshConfig]
  );

  const tokenConfigured = Boolean(config.token);

  const value: SessionContextState = {
    config,
    client,
    tokenConfigured,
    isConnectionBusy,
    isAuthBusy,
    isConnectionReady: !isConnectionBusy && !isAuthBusy && apiStatus.status === 'connected',
    isTokenEditorOpen,
    apiStatus,
    tokenDraft,
    email,
    password,
    errorMessage,
    refreshConfig,
    clearError,
    checkApiConnection,
    saveToken,
    signIn,
    setTokenDraft,
    setEmail,
    setPassword,
    setTokenEditorOpen
  };

  return <SessionContext.Provider value={value}>{children}</SessionContext.Provider>;
}

export function useSessionContext() {
  const context = useContext(SessionContext);

  if (!context) {
    throw new Error('useSessionContext must be used within a SessionProvider');
  }

  return context;
}
