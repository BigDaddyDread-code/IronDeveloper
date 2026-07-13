import type { ReactNode } from 'react';

export type TruthStateKind =
  | 'authRequired'
  | 'apiUnreachable'
  | 'tenantRequired'
  | 'projectRequired'
  | 'readiness'
  | 'governedRefusal'
  | 'notImplemented'
  | 'loading'
  | 'empty'
  | 'error'
  | 'staleData'
  | 'partialData';

export interface TruthStateDescriptor {
  eyebrow: string;
  tone: 'neutral' | 'loading' | 'warning' | 'error';
  live: 'polite' | 'assertive';
}

export const truthStateDescriptors: Record<TruthStateKind, TruthStateDescriptor> = {
  authRequired: { eyebrow: 'Authentication required', tone: 'warning', live: 'polite' },
  apiUnreachable: { eyebrow: 'API unreachable', tone: 'error', live: 'assertive' },
  tenantRequired: { eyebrow: 'Tenant required', tone: 'warning', live: 'polite' },
  projectRequired: { eyebrow: 'Project required', tone: 'warning', live: 'polite' },
  readiness: { eyebrow: 'Readiness', tone: 'neutral', live: 'polite' },
  governedRefusal: { eyebrow: 'Governed refusal', tone: 'warning', live: 'polite' },
  notImplemented: { eyebrow: 'Not implemented', tone: 'neutral', live: 'polite' },
  loading: { eyebrow: 'Loading', tone: 'loading', live: 'polite' },
  empty: { eyebrow: 'Empty', tone: 'neutral', live: 'polite' },
  error: { eyebrow: 'Needs attention', tone: 'error', live: 'assertive' },
  staleData: { eyebrow: 'Stale data', tone: 'warning', live: 'polite' },
  partialData: { eyebrow: 'Partial data', tone: 'warning', live: 'polite' }
};

interface TruthStateRendererProps {
  kind: TruthStateKind;
  title: string;
  body: string;
  action?: ReactNode;
  className?: string;
  headingLevel?: 2 | 3;
  testId?: string;
}

/** Renders supplied product truth. It never derives access or enables actions. */
export function TruthStateRenderer({
  kind,
  title,
  body,
  action,
  className,
  headingLevel = 3,
  testId = `truth-state.${kind}`
}: TruthStateRendererProps) {
  const descriptor = truthStateDescriptors[kind];
  const Heading = headingLevel === 2 ? 'h2' : 'h3';
  const classes = ['truth-state', `truth-state--${descriptor.tone}`, className].filter(Boolean).join(' ');

  return (
    <section
      className={classes}
      data-state-kind={kind}
      data-testid={testId}
      role={descriptor.live === 'assertive' ? 'alert' : 'status'}
      aria-live={descriptor.live}
      aria-busy={kind === 'loading' ? true : undefined}
    >
      <p className="eyebrow">{descriptor.eyebrow}</p>
      <Heading>{title}</Heading>
      <p>{body}</p>
      {action ? <div className="truth-state__action">{action}</div> : null}
    </section>
  );
}
