import { SessionProvider } from '../state/useSessionContext';
import { ProjectProvider } from '../state/useProjectContext';
import { WorkspaceNavigationProvider } from '../state/useWorkspaceNavigation';
import type { ReactNode } from 'react';

interface AppProvidersProps {
  children: ReactNode;
}

export function AppProviders({ children }: AppProvidersProps) {
  return (
    <SessionProvider>
      <ProjectProvider>
        <WorkspaceNavigationProvider>{children}</WorkspaceNavigationProvider>
      </ProjectProvider>
    </SessionProvider>
  );
}
