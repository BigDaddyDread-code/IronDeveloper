import type { ChatCompletionResponse } from '../../api/types';

interface ChatSourceListProps {
  response: ChatCompletionResponse | null;
}

export function ChatSourceList({ response }: ChatSourceListProps) {
  const files = splitSourceList(response?.linkedFilePaths);
  const symbols = splitSourceList(response?.linkedSymbols);

  return (
    <section className="chat-source-list" data-testid="chat.sources">
      <div className="workflow-section__header">
        <h4>Sources</h4>
        <span>{files.length + symbols.length} linked</span>
      </div>
      {files.length === 0 && symbols.length === 0 ? (
        <p className="state-muted">No sources were returned for the latest response.</p>
      ) : (
        <>
          {files.length > 0 ? <SourceGroup title="Files" items={files} /> : null}
          {symbols.length > 0 ? <SourceGroup title="Symbols" items={symbols} /> : null}
        </>
      )}
    </section>
  );
}

function SourceGroup({ title, items }: { title: string; items: string[] }) {
  return (
    <div className="chat-source-list__group">
      <strong>{title}</strong>
      <ul>
        {items.map((item) => (
          <li key={item}>
            <code>{item}</code>
          </li>
        ))}
      </ul>
    </div>
  );
}

function splitSourceList(value?: string | null) {
  if (!value) {
    return [];
  }

  return value
    .split(/\r?\n|;|\|/)
    .map((item) => item.replace(/^[-*]\s*/, '').trim())
    .filter(Boolean);
}
