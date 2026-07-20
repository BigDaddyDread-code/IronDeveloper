import { useState } from 'react';
import type { ChatDocumentSource } from '../../api/types';
import { CommandButton } from '../../components/CommandButton';
import type { ChatAgentRunState } from './chatTypes';
import {
  shouldShowWorkbenchCommandMenu,
  workbenchCommands,
  type WorkbenchCommandNotice,
  type WorkbenchCommandToken
} from './workbenchCommands';

interface ChatComposerProps {
  value: string;
  isSending: boolean;
  isCancellingAgentRun: boolean;
  agentRun: ChatAgentRunState | null;
  conversationAuthorityEnabled: boolean;
  agentCancellationDeliveryUnresolved: boolean;
  disabledReason: string | null;
  sendDisabledReason: string | null;
  documentSources: ChatDocumentSource[];
  documentSourceLoadState: 'idle' | 'loading' | 'ready' | 'error';
  documentSourceError: string | null;
  selectedDocumentSource: ChatDocumentSource | null;
  commandNotice: WorkbenchCommandNotice | null;
  onChange: (value: string) => void;
  onSelectCommand: (token: WorkbenchCommandToken) => void;
  onSend: () => void;
  onCancelAgentRun: () => void;
  onLoadDocumentSources: () => void;
  onSelectDocumentSource: (source: ChatDocumentSource | null) => void;
}

export function ChatComposer({
  value,
  isSending,
  isCancellingAgentRun,
  agentRun,
  conversationAuthorityEnabled,
  agentCancellationDeliveryUnresolved,
  disabledReason,
  sendDisabledReason,
  documentSources,
  documentSourceLoadState,
  documentSourceError,
  selectedDocumentSource,
  commandNotice,
  onChange,
  onSelectCommand,
  onSend,
  onCancelAgentRun,
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
      {conversationAuthorityEnabled && agentRun ? (
        <div className="chat-composer__agent-run" role="status" data-testid="chat.agentRun.status">
          <strong>{agentRunStatusLabel(agentRun)}</strong>
          <small>Run {agentRun.agentRunId}</small>
        </div>
      ) : null}
      {conversationAuthorityEnabled ? (
        <p className="chat-composer__authority-note" data-testid="chat.agentRun.boundary">
          Messages and replies are saved by the governed Business Analyst run. Document attachments are not available in this slice.
        </p>
      ) : null}
      {conversationAuthorityEnabled && commandNotice ? (
        <section
          className={`chat-command-notice chat-command-notice--${commandNotice.kind}`}
          aria-live="polite"
          data-testid={`chat.command.${commandNotice.kind}.result`}
        >
          <strong>{commandNotice.title}</strong>
          <p>{commandNotice.message}</p>
        </section>
      ) : null}
      {!conversationAuthorityEnabled && selectedDocumentSource ? (
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
      {!conversationAuthorityEnabled && isSourcePickerOpen ? (
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
          maxLength={conversationAuthorityEnabled ? 20_000 : undefined}
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
      {conversationAuthorityEnabled && shouldShowWorkbenchCommandMenu(value) ? (
        <section className="chat-command-menu" aria-label="Workbench commands" data-testid="chat.command.menu">
          <div className="chat-command-menu__heading">
            <strong>Commands</strong>
            <small>Exact commands only</small>
          </div>
          <div className="chat-command-menu__items">
            {workbenchCommands.map((command) => (
              <button
                type="button"
                key={command.token}
                data-testid={`chat.command.option.${command.token.slice(1)}`}
                onClick={() => onSelectCommand(command.token)}
              >
                <code>{command.token}</code>
                <span><strong>{command.label}</strong><small>{command.description}</small></span>
              </button>
            ))}
          </div>
        </section>
      ) : null}
      <div className="chat-composer__actions">
        <div>
          {!conversationAuthorityEnabled ? (
            <CommandButton
              type="button"
              variant="subtle"
              testId="chat.documentSource.open"
              disabled={Boolean(disabledReason)}
              onClick={toggleSourcePicker}
            >
              Attach document
            </CommandButton>
          ) : null}
          {conversationAuthorityEnabled &&
          (agentCancellationDeliveryUnresolved || agentRun?.status === 'Pending' || agentRun?.status === 'Running') ? (
            <CommandButton
              type="button"
              variant="subtle"
              testId="chat.agentRun.cancel"
              disabled={isCancellingAgentRun || Boolean(agentRun?.cancellationRequested && !agentCancellationDeliveryUnresolved)}
              onClick={onCancelAgentRun}
            >
              {isCancellingAgentRun
                ? 'Cancelling'
                : agentCancellationDeliveryUnresolved
                  ? 'Retry cancellation'
                  : agentRun?.cancellationRequested
                    ? 'Cancellation requested'
                    : 'Cancel run'}
            </CommandButton>
          ) : null}
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
          {conversationAuthorityEnabled && isSending
            ? agentRun?.status === 'Pending' ? 'Queued' : 'Working'
            : isSending ? 'Sending' : 'Send'}
        </CommandButton>
      </div>
    </section>
  );
}

function agentRunStatusLabel(run: ChatAgentRunState) {
  if (run.cancellationRequested && (run.status === 'Pending' || run.status === 'Running')) {
    return 'Business Analyst cancellation requested';
  }

  switch (run.status) {
    case 'Pending':
      return 'Business Analyst queued';
    case 'Running':
      return 'Business Analyst working';
    case 'NeedsInput':
      return 'Business Analyst needs input';
    case 'Completed':
      return 'Business Analyst response saved';
    case 'Cancelled':
      return 'Business Analyst run cancelled';
    case 'Failed':
      return 'Business Analyst run failed safely';
    case 'Superseded':
      return 'Business Analyst run superseded';
    case 'Stale':
      return 'Business Analyst run is stale';
  }
}

function formatDocumentType(value: string) {
  return value.replace(/([a-z])([A-Z])/g, '$1 $2');
}
