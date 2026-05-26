import { SessionProvider } from '../state/useSessionContext';
import { ProjectProvider } from '../state/useProjectContext';
import type { ReactNode } from 'react';

interface AppProvidersProps {
  children: ReactNode;
}

export function AppProviders({ children }: AppProvidersProps) {
  return (
    <SessionProvider>
      <ProjectProvider>{children}</ProjectProvider>
    </SessionProvider>
  );
}
