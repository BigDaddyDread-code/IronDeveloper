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
      <aside className="app-shell__navigation">
        <div className="app-shell__brand">IronDev</div>
        {navigation}
      </aside>
      <div className="app-shell__workspace">
        {header}
        <div className="app-shell__content" data-testid="app.routeOutlet">
          {children}
        </div>
        {footer}
      </div>
    </div>
  );
}
