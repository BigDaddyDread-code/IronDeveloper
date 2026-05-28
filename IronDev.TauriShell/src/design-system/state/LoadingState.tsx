interface LoadingStateProps {
  title: string;
  body: string;
}

export function LoadingState({ title, body }: LoadingStateProps) {
  return (
    <div className="state-panel state-panel--loading">
      <p className="eyebrow">Loading</p>
      <h3>{title}</h3>
      <p>{body}</p>
    </div>
  );
}
