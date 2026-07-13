import { navigateProductPath } from '../navigation/productRoutes';

interface LegacyRouteNoticeProps {
  sourcePath: string;
  canonicalPath: string;
}

export function LegacyRouteNotice({ sourcePath, canonicalPath }: LegacyRouteNoticeProps) {
  return (
    <aside className="fl-legacy-route-notice" data-testid="flow.legacyRouteNotice" role="status">
      <div>
        <p className="fl-plabel">Compatibility route</p>
        <strong>{sourcePath}</strong>
        <span>This legacy link was contained and opened the canonical product surface.</span>
      </div>
      <a
        href={canonicalPath}
        onClick={(event) => {
          event.preventDefault();
          navigateProductPath(canonicalPath);
        }}
      >
        Open canonical surface
      </a>
    </aside>
  );
}
