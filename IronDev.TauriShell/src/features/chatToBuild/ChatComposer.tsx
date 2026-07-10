import { useState } from 'react';
import type { ChatDocumentSource } from '../../api/types';
import { CommandButton } from '../../components/CommandButton';

interface ChatComposerProps {
  value: string;
  isSending: boolean;
  disabledReason: string | null;
  sendDisabledReason: string | null;
  documentSources: ChatDocumentSource[];
  documentSourceLoadState: 'idle' | 'loading' | 'ready' | 'error';
  documentSourceError: string | null;
  selectedDocumentSource: ChatDocumentSource | null;
  onChange: (value: string) => void;
  onSend: () => void;
  onLoadDocumentSources: () => void;
  onSelectDocumentSource: (source: ChatDocumentSource | null) => void;
}

export function ChatComposer({
  value,
  isSending,
  disabledReason,
  sendDisabledReason,
  documentSources,
  documentSourceLoadState,
  documentSourceError,
  selectedDocumentSource,
  onChange,
  onSend,
  onLoadDocumentSources,
  onSelectDocumentSource
}: ChatComposerProps) {
  const [isSourcePickerOpen, setSourcePickerOpen] = useState(false);
  const sendBlockedReason = sendDisabledReason ?? disabledReason;

  const toggleSourcePicker = () => {
    const opening = !isSourcePickerOpen;
    setSourcePickerOpen(opening);
    if (opening && (documentSourceLoadState === 'idle' || documentSourceLoadState === 'error')) {
      onLoadDocumentSources();
    }
  };

  return (
    <section className="chat-composer" data-testid="chat.composer">
      {selectedDocumentSource ? (
        <div className="chat-composer__selected-source" data-testid="chat.documentSource.selected">
          <span>
            <strong>{selectedDocumentSource.title}</strong>
            <small>{selectedDocumentSource.versionLabel} / {formatDocumentType(selectedDocumentSource.documentType)}</small>
          </span>
          <button
            type="button"
            aria-label={`Remove ${selectedDocumentSource.title}`}
            onClick={() => onSelectDocumentSource(null)}
          >
            Remove
          </button>
        </div>
      ) : null}
      {isSourcePickerOpen ? (
        <section className="chat-document-picker" data-testid="chat.documentSource.picker" aria-label="Project document context">
          <header>
            <div>
              <strong>Link existing document</strong>
              <p>Only backend-ready exact versions are available.</p>
            </div>
            <button type="button" onClick={() => setSourcePickerOpen(false)}>Close</button>
          </header>
          {documentSourceLoadState === 'loading' ? (
            <p>Loading Ready documents...</p>
          ) : documentSourceLoadState === 'error' ? (
            <div className="chat-document-picker__state" role="alert">
              <p>{documentSourceError ?? 'Project document sources are unavailable.'}</p>
              <button type="button" onClick={onLoadDocumentSources}>Retry</button>
            </div>
          ) : documentSources.length === 0 ? (
            <p className="chat-document-picker__state">No Ready project documents are available.</p>
          ) : (
            <ul>
              {documentSources.map((source) => (
                <li key={source.documentVersionId}>
                  <button
                    type="button"
                    aria-pressed={selectedDocumentSource?.documentVersionId === source.documentVersionId}
                    onClick={() => {
                      onSelectDocumentSource(source);
                      setSourcePickerOpen(false);
                    }}
                    data-testid={`chat.documentSource.select.${source.documentVersionId}`}
                  >
                    <span><strong>{source.title}</strong><small>{formatDocumentType(source.documentType)}</small></span>
                    <span>{source.versionLabel}<small>{source.status}</small></span>
                  </button>
                </li>
              ))}
            </ul>
          )}
        </section>
      ) : null}
      <label className="chat-composer__field" htmlFor="chat-composer-input">
        <span className="fl-visually-hidden">Message IronDev</span>
        <textarea
          id="chat-composer-input"
          value={value}
          placeholder="Ask about this project or describe work..."
          disabled={Boolean(disabledReason)}
          aria-describedby={disabledReason ? 'chat-composer-blocked' : undefined}
          data-testid="chat.composer.input"
          onChange={(event) => onChange(event.target.value)}
          onKeyDown={(event) => {
            if (event.key !== 'Enter' || event.shiftKey) {
              return;
            }

            event.preventDefault();
            onSend();
          }}
        />
      </label>
      <div className="chat-composer__actions">
        <div>
          <CommandButton
            type="button"
            variant="subtle"
            testId="chat.documentSource.open"
            disabled={Boolean(disabledReason)}
            onClick={toggleSourcePicker}
          >
            Attach document
          </CommandButton>
          {disabledReason ? (
            <p id="chat-composer-blocked" data-testid="chat.composer.disabledReason">{disabledReason}</p>
          ) : null}
        </div>
        <CommandButton
          type="button"
          variant="primary"
          onClick={onSend}
          disabled={Boolean(sendBlockedReason)}
          title={sendBlockedReason ?? undefined}
          testId="chat.command.send"
        >
          {isSending ? 'Sending' : 'Send'}
        </CommandButton>
      </div>
    </section>
  );
}

function formatDocumentType(value: string) {
  return value.replace(/([a-z])([A-Z])/g, '$1 $2');
}
