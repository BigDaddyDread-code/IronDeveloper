import { CommandButton } from '../../components/CommandButton';
import { Surface } from '../../design-system/Surface';

interface DiscussionComposerProps {
  title: string;
  content: string;
  isBusy: boolean;
  disabledReason: string | null;
  onTitleChange: (value: string) => void;
  onContentChange: (value: string) => void;
  onSave: () => void;
}

export function DiscussionComposer({
  title,
  content,
  isBusy,
  disabledReason,
  onTitleChange,
  onContentChange,
  onSave
}: DiscussionComposerProps) {
  return (
    <Surface className="chat-build-panel" testId="chat-build.discussionComposer">
      <div className="section-heading">
        <p className="eyebrow">Discussion</p>
        <h2>Chat input</h2>
      </div>
      <label className="field-stack">
        <span>Title</span>
        <input
          value={title}
          onChange={(event) => onTitleChange(event.target.value)}
          placeholder="Short discussion title"
          data-testid="chat-build.discussion.title"
        />
      </label>
      <label className="field-stack">
        <span>Discussion text</span>
        <textarea
          value={content}
          onChange={(event) => onContentChange(event.target.value)}
          placeholder="Describe the small build request to send through the reusable spine."
          rows={8}
          data-testid="chat-build.discussion.content"
        />
      </label>
      <div className="chat-build-actions">
        <CommandButton
          type="button"
          variant="primary"
          onClick={onSave}
          disabled={isBusy || Boolean(disabledReason)}
          testId="chat-build.command.saveDiscussion"
        >
          {isBusy ? 'Saving...' : 'Save Discussion Document'}
        </CommandButton>
        {disabledReason ? <p className="state-muted">{disabledReason}</p> : null}
      </div>
    </Surface>
  );
}
