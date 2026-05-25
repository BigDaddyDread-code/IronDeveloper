import type { ButtonHTMLAttributes, ReactNode } from 'react';

type CommandButtonVariant = 'primary' | 'secondary' | 'subtle';

interface CommandButtonProps extends ButtonHTMLAttributes<HTMLButtonElement> {
  children: ReactNode;
  variant?: CommandButtonVariant;
  testId?: string;
}

export function CommandButton({ children, variant = 'secondary', testId, className, ...props }: CommandButtonProps) {
  const classes = ['command-button', variant !== 'secondary' ? `command-button--${variant}` : '', className]
    .filter(Boolean)
    .join(' ');

  return (
    <button className={classes} data-testid={testId} {...props}>
      {children}
    </button>
  );
}
