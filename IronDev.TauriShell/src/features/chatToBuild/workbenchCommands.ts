export const workbenchCommands = [
  {
    token: '/help',
    label: 'Show Workbench commands',
    description: 'Open the deterministic command guide without calling the Business Analyst.'
  },
  {
    token: '/ticket',
    label: 'Prepare ticket proposals',
    description: 'Use the current project discussion to begin the governed ticket-proposal workflow.'
  }
] as const;

export type WorkbenchCommandToken = (typeof workbenchCommands)[number]['token'];

export type WorkbenchComposerClassification =
  | { kind: 'conversation' }
  | { kind: 'known'; token: WorkbenchCommandToken; instruction: string }
  | { kind: 'unknown'; rawToken: string };

export interface WorkbenchCommandNotice {
  kind: 'help' | 'ticket';
  title: string;
  message: string;
  clientOperationId: string;
}

export function classifyWorkbenchComposer(value: string): WorkbenchComposerClassification {
  const candidate = value.trimStart();
  if (!candidate.startsWith('/')) {
    return { kind: 'conversation' };
  }

  const delimiterIndex = candidate.search(/\s/);
  const rawToken = delimiterIndex < 0 ? candidate : candidate.slice(0, delimiterIndex);
  const instruction = delimiterIndex < 0 ? '' : candidate.slice(delimiterIndex).trim();
  const normalizedToken = rawToken.toLocaleLowerCase('en-US');

  if (normalizedToken === '/help' || normalizedToken === '/ticket') {
    return { kind: 'known', token: normalizedToken, instruction };
  }

  return { kind: 'unknown', rawToken };
}

export function shouldShowWorkbenchCommandMenu(value: string) {
  const candidate = value.trimStart();
  if (candidate === '') {
    return true;
  }
  if (!candidate.startsWith('/')) {
    return false;
  }
  return !/\s/.test(candidate);
}
