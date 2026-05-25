import type { ReactNode } from 'react';

interface AppShellProps {
  header: ReactNode;
  navigation: ReactNode;
  children: ReactNode;
  footer: ReactNode;
}

export function AppShell({ header, navigation, children, footer }: AppShellProps) {
  return (
    <div className="app-shell" data-testid="app.shell">
      {header}
      {navigation}
      {children}
      {footer}
    </div>
  );
}
