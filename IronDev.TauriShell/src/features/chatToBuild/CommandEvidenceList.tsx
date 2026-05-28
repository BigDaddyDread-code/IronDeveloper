import { MetadataRow } from '../../components/MetadataRow';
import type { CommandEvidence } from '../../api/types';

export function CommandEvidenceList({ commands }: { commands: CommandEvidence[] }) {
  return (
    <section className="chat-build-command-evidence" data-testid="chat-build.commandEvidence">
      <div className="section-heading">
        <p className="eyebrow">Command evidence</p>
        <h2>Build and run commands</h2>
      </div>
      {commands.length > 0 ? (
        <div className="chat-build-list">
          {commands.map((command, index) => (
            <article key={`${command.command}-${index}`} className="chat-build-evidence-card">
              <h3>{command.command}</h3>
              <MetadataRow label="Exit code" value={command.exitCode ?? 'unavailable'} />
              <MetadataRow label="Duration" value={command.durationMs ? `${command.durationMs}ms` : 'unavailable'} />
              <MetadataRow label="Stdout" value={command.stdoutPath ?? 'unavailable'} />
              <MetadataRow label="Stderr" value={command.stderrPath ?? 'unavailable'} />
            </article>
          ))}
        </div>
      ) : (
        <p className="state-muted">No command evidence has been loaded.</p>
      )}
    </section>
  );
}
