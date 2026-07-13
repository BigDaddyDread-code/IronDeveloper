import { useEffect, useRef } from 'react';

export type RouteOutcomeKind = 'permission' | 'notFound' | 'notImplemented' | 'unavailable' | 'conflict' | 'blocked';

interface RouteOutcomeScreenProps {
  kind: RouteOutcomeKind;
  title: string;
  message: string;
  nextSafeAction: string;
  actionLabel?: string;
  onAction?: () => void;
}

const statusByKind: Record<RouteOutcomeKind, number> = {
  permission: 403,
  notFound: 404,
  notImplemented: 501,
  unavailable: 503,
  conflict: 409,
  blocked: 422
};

export function RouteOutcomeScreen({
  kind,
  title,
  message,
  nextSafeAction,
  actionLabel,
  onAction
}: RouteOutcomeScreenProps) {
  const outcomeRef = useRef<HTMLElement | null>(null);

  useEffect(() => {
    outcomeRef.current?.focus();
  }, [kind, title]);

  return (
    <section
      ref={outcomeRef}
      className="fl-route-outcome"
      data-testid="flow.routeOutcome"
      aria-labelledby="route-outcome-title"
      role="alert"
      tabIndex={-1}
    >
      <p className="fl-plabel" data-testid="flow.routeOutcome.kind">
        {statusByKind[kind]} {kind.replace(/[A-Z]/g, (letter) => ` ${letter.toLowerCase()}`)}
      </p>
      <h1 id="route-outcome-title" className="fl-h1">
        {title}
      </h1>
      <p className="fl-sub">{message}</p>
      <div className="fl-panel-box">
        <strong>Next safe action</strong>
        <p>{nextSafeAction}</p>
        {actionLabel && onAction ? (
          <button className="fl-btn fl-pri" type="button" data-testid="flow.routeOutcome.primary" onClick={onAction}>
            {actionLabel}
          </button>
        ) : null}
      </div>
    </section>
  );
}
