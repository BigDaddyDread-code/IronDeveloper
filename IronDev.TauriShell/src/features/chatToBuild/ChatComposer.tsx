import { CommandButton } from '../../components/CommandButton';

interface ChatComposerProps {
  value: string;
  isSending: boolean;
  disabledReason: string | null;
  sendDisabledReason: string | null;
  onChange: (value: string) => void;
  onSend: () => void;
}

export function ChatComposer({
  value,
  isSending,
  disabledReason,
  sendDisabledReason,
  onChange,
  onSend
}: ChatComposerProps) {
  const sendBlockedReason = sendDisabledReason ?? disabledReason;

  return (
    <section className="chat-composer" data-testid="chat.composer">
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
        {disabledReason ? (
          <p id="chat-composer-blocked" data-testid="chat.composer.disabledReason">{disabledReason}</p>
        ) : <span />}
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
