import type { WorkspaceCommand } from '../../app/routes';
import { CommandButton } from '../../components/CommandButton';

interface WorkspaceCommandBarProps {
  commands: WorkspaceCommand[];
}

const intentMap: Record<WorkspaceCommand['intent'], 'primary' | 'secondary' | 'subtle' | 'danger'> = {
  primary: 'primary',
  secondary: 'secondary',
  danger: 'danger',
  ghost: 'subtle'
};

export function WorkspaceCommandBar({ commands }: WorkspaceCommandBarProps) {
  if (commands.length === 0) {
    return null;
  }

  const blockedReasons = commands.filter((command) => command.disabled && command.disabledReason);

  return (
    <div className="workspace-command-bar" data-testid="workspace.commands">
      <div className="workspace-command-bar__actions">
        {commands.map((command) => (
          <CommandButton
            key={command.id}
            variant={intentMap[command.intent]}
            testId={command.testId}
            onClick={command.onExecute}
            disabled={Boolean(command.disabled || command.busy)}
            title={command.disabledReason ?? command.shortcut}
          >
            {command.icon ? <span className="command-icon">{command.icon}</span> : null}
            <span>{command.label}</span>
          </CommandButton>
        ))}
      </div>
      {blockedReasons.length > 0 ? (
        <div className="workspace-command-bar__reasons">
          {blockedReasons.slice(0, 2).map((command) => (
            <p key={command.id}>
              <strong>{command.label}</strong>: {command.disabledReason}
            </p>
          ))}
        </div>
      ) : null}
    </div>
  );
}
