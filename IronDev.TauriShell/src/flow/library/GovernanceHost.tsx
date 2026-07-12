import { useCallback, useEffect, useState } from 'react';
import { routeForId } from '../../app/routes';
import { governanceViewers, viewerForPath } from './governanceRoutes';
import { GovernanceOverview } from './GovernanceOverview';
import type { GovernanceSection } from '../navigation/productRoutes';

// Hosts the 17 read-only governance viewers inside the Library surface at their
// original URLs, so deep links and existing tests keep working. Pathname is real
// React state (synced with history/popstate) — the old shell read window.location
// during render, which React could not track.
interface GovernanceHostProps {
  projectId: number;
  section?: GovernanceSection;
  preserveCompatibilityPath?: boolean;
}

export function GovernanceHost({ projectId, section = 'overview', preserveCompatibilityPath = false }: GovernanceHostProps) {
  const [pathname, setPathname] = useState(() => window.location.pathname);

  useEffect(() => {
    const onPopState = () => setPathname(window.location.pathname);
    window.addEventListener('popstate', onPopState);
    return () => window.removeEventListener('popstate', onPopState);
  }, []);

  const navigate = useCallback((path: string) => {
    window.history.pushState(null, '', path);
    setPathname(path);
  }, []);

  const active = viewerForPath(pathname);
  const ActiveViewer = active.component;
  const governanceRoute = routeForId('governance');

  if (!preserveCompatibilityPath) {
    return <GovernanceOverview projectId={projectId} section={section} />;
  }

  return (
    <div data-testid="flow.governanceHost">
      {/* Viewer switchers are links, not buttons: they navigate to read-only URLs and
          must never read as action controls on evidence surfaces. */}
      <div className="fl-chips" style={{ marginBottom: 14 }}>
        {governanceViewers.map((entry) => (
          <a
            key={entry.id}
            className={entry.id === active.id ? 'fl-chip fl-ok' : 'fl-chip'}
            style={{ cursor: 'pointer', textDecoration: 'none', background: entry.id === active.id ? undefined : 'transparent' }}
            href={entry.entryPath}
            onClick={(event) => {
              event.preventDefault();
              navigate(entry.entryPath);
            }}
          >
            {entry.label}
          </a>
        ))}
      </div>
      <ActiveViewer key={active.id} route={governanceRoute} onRouteReady={() => undefined} />
    </div>
  );
}
