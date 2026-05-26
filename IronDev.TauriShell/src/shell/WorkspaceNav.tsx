import type { WorkspaceRoute } from '../app/routes';
import { workspaceRoutes } from '../app/routes';

interface WorkspaceNavProps {
  activeRouteId: WorkspaceRoute['id'];
  onSelect: (routeId: WorkspaceRoute['id']) => void;
}

export function WorkspaceNav({ activeRouteId, onSelect }: WorkspaceNavProps) {
  return (
    <nav className="shell-nav" aria-label="Workspace navigation">
      {workspaceRoutes.map((route) => (
        <button
          key={route.id}
          className={`shell-nav__item ${route.id === activeRouteId ? 'shell-nav__item--active' : ''}`}
          data-testid={`shell.nav.${route.id}`}
          title={route.parityNotes.join(' / ')}
          onClick={() => onSelect(route.id)}
        >
          <span>{route.label}</span>
          <span className={`status-badge status-badge--${route.id === 'tickets' ? 'neutral' : 'info'}`}>{route.parityStatus}</span>
        </button>
      ))}
    </nav>
  );
}
