import { useEffect, useState } from 'react';
import { routeForId } from '../../app/routes';
import { viewerForPath, type GovernanceViewerEntry } from './governanceRoutes';
import { GovernanceOverview } from './GovernanceOverview';
import { governancePath, libraryPath, navigateProductPath, projectPath, type GovernanceSection } from '../navigation/productRoutes';

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

  const active = viewerForPath(pathname);
  const ActiveViewer = active.component;
  const governanceRoute = routeForId('governance');

  if (!preserveCompatibilityPath) {
    return <GovernanceOverview projectId={projectId} section={section} />;
  }

  return (
    <div data-testid="flow.governanceHost">
      <aside className="fl-governance-compatibility" data-testid="flow.governance.compatibilityNotice">
        <div>
          <p className="fl-plabel">Legacy evidence view</p>
          <strong>{active.label}</strong>
          <span>This direct link remains available for read-only evidence inspection.</span>
        </div>
        <nav aria-label="Evidence viewer destinations">
          <a
            href={governancePath(projectId)}
            onClick={(event) => {
              event.preventDefault();
              navigateProductPath(governancePath(projectId));
            }}
          >
            Back to Governance
          </a>
          <a
            href={canonicalDestination(active, projectId)}
            onClick={(event) => {
              event.preventDefault();
              navigateProductPath(canonicalDestination(active, projectId));
            }}
          >
            Open canonical surface
          </a>
        </nav>
      </aside>
      <ActiveViewer key={active.id} route={governanceRoute} onRouteReady={() => undefined} />
    </div>
  );
}

function canonicalDestination(viewer: GovernanceViewerEntry, projectId: number) {
  if (viewer.canonicalOwner === 'board') return projectPath(projectId, 'board');
  if (viewer.canonicalOwner === 'audit') return libraryPath(projectId, 'audit');
  if (viewer.canonicalOwner === 'governance') {
    return governancePath(projectId, viewer.id === 'accepted-approvals' ? 'decisions' : 'controls');
  }
  if (viewer.canonicalOwner === 'library' || viewer.canonicalOwner === 'developerEvidence' || viewer.canonicalOwner === 'release') {
    return governancePath(projectId, 'technical');
  }
  return governancePath(projectId);
}
