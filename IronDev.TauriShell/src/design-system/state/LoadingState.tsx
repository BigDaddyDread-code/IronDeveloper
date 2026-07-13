interface LoadingStateProps {
  title: string;
  body: string;
}

export function LoadingState({ title, body }: LoadingStateProps) {
  return <TruthStateRenderer kind="loading" title={title} body={body} className="state-panel state-panel--loading" />;
}
import { TruthStateRenderer } from './TruthStateRenderer';
