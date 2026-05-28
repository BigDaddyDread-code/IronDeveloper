import { CommandButton } from '../../components/CommandButton';

interface ChatComposerProps {
  value: string;
  isSending: boolean;
  disabledReason: string | null;
  sendDisabledReason: string | null;
  onChange: (value: string) => void;
  onSend: () => void;
  onReviewProjectState: () => void;
}

export function ChatComposer({
  value,
  isSending,
  disabledReason,
  sendDisabledReason,
  onChange,
  onSend,
  onReviewProjectState
}: ChatComposerProps) {
  const sendBlockedReason = sendDisabledReason ?? disabledReason;

  return (
    <section className="chat-composer" data-testid="chat.composer">
      <div className="chat-composer__header">
        <div>
          <p className="eyebrow">Project chat</p>
          <h3>Ask IronDev</h3>
        </div>
        <CommandButton
          type="button"
          variant="secondary"
          onClick={onReviewProjectState}
          disabled={Boolean(disabledReason || isSending)}
          title={disabledReason ?? undefined}
          testId="chat.command.reviewProjectState"
        >
          Review Project State
        </CommandButton>
      </div>
      <label className="chat-composer__field">
        <span>Message</span>
        <textarea
          value={value}
          placeholder="Ask about current project state, tickets, runs, risks, or next actions."
          disabled={Boolean(disabledReason)}
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
        <p data-testid="chat.composer.disabledReason">{sendBlockedReason ?? 'Ready to send. Shift+Enter inserts a new line.'}</p>
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
