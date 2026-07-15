const runAgentRoleNames = new Map<number, string>([
  [0, 'Orchestrator'],
  [1, 'Builder'],
  [2, 'Tester'],
  [3, 'Critic'],
  [4, 'Analyst']
]);

export function runAgentRoleLabel(role: number | string): string {
  if (typeof role === 'number') return runAgentRoleNames.get(role) ?? `Role ${role}`;

  const trimmed = role.trim();
  const numeric = Number.parseInt(trimmed, 10);
  return /^\d+$/.test(trimmed) && Number.isFinite(numeric)
    ? runAgentRoleNames.get(numeric) ?? `Role ${trimmed}`
    : trimmed;
}
