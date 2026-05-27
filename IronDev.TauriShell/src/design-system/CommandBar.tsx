import type { WorkspaceCommand } from '../app/routes';
import { CommandButton } from '../components/CommandButton';

interface CommandBarProps {
  commands: WorkspaceCommand[];
}

const intentMap: Record<WorkspaceCommand['intent'], 'primary' | 'secondary' | 'subtle' | 'danger'> = {
  primary: 'primary',
  secondary: 'secondary',
  danger: 'danger',
  ghost: 'subtle'
};

export function CommandBar({ commands }: CommandBarProps) {
  if (commands.length === 0) {
    return null;
  }

  return (
    <div className="command-bar" data-testid="workspace.commands">
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
  );
}

