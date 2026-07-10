import { RouteOutcomeScreen } from '../components/RouteOutcomeScreen';
import { libraryPath, navigateProductPath, projectPath, type LibrarySection } from '../navigation/productRoutes';
import { GovernanceHost } from './GovernanceHost';
import { AuditSection, ProvisioningSection } from './PlannedSections';
import { SolutionExplorer } from './SolutionExplorer';
import { SettingsScreen } from '../settings/SettingsScreen';
import { DocumentsScreen } from './DocumentsScreen';

interface LibraryScreenProps {
  projectId: number;
  section: LibrarySection;
  documentId?: number | null;
  documentVersionId?: number | null;
  documentAction?: 'upload' | null;
  preserveGovernancePath?: boolean;
  onBackToProjects: () => void;
  onOpenBoard: () => void;
}

const sections: Array<{ id: LibrarySection; label: string }> = [
  { id: 'explorer', label: 'Explorer' },
  { id: 'documents', label: 'Documents' },
  { id: 'tools', label: 'Tools' },
  { id: 'members', label: 'Members' },
  { id: 'governance', label: 'Governance' },
  { id: 'provisioning', label: 'Project setup' },
  { id: 'audit', label: 'Audit' },
  { id: 'settings', label: 'Settings' }
];

const plannedCopy: Record<'tools' | 'members', { title: string; message: string; next: string }> = {
  tools: {
    title: 'Project tools are not implemented',
    message: 'Tenant connections and project enablement need their governed backend contract before this catalogue can operate.',
    next: 'Return to Library. No tool connection or permission has been created.'
  },
  members: {
    title: 'Project members are not implemented',
    message: 'Tenant users exist, but project visibility and collaboration membership are a separate contract.',
    next: 'Open Settings for tenant users, or return to Library.'
  }
};

export function LibraryScreen({
  projectId,
  section,
  documentId = null,
  documentVersionId = null,
  documentAction = null,
  preserveGovernancePath = false,
  onBackToProjects,
  onOpenBoard
}: LibraryScreenProps) {
  const openSection = (next: LibrarySection) => navigateProductPath(libraryPath(projectId, next));

  return (
    <div data-testid="flow.library">
      <div className="fl-section-heading">
        <div>
          <h1 className="fl-h1">Library</h1>
          <p className="fl-sub">Project reference and evidence. Nothing here grants authority.</p>
        </div>
        <nav className="fl-nav fl-library-nav" aria-label="Library sections">
          {sections.map((candidate) => (
            <button
              key={candidate.id}
              className={section === candidate.id ? 'fl-on' : ''}
              type="button"
              onClick={() => openSection(candidate.id)}
              data-testid={
                candidate.id === 'explorer'
                  ? 'flow.library.explorer'
                  : candidate.id === 'governance'
                    ? 'flow.library.governance'
                    : `flow.library.nav.${candidate.id}`
              }
            >
              {candidate.label}
            </button>
          ))}
        </nav>
      </div>

      {section === 'explorer' ? <SolutionExplorer /> : null}
      {section === 'documents' ? (
        <DocumentsScreen
          projectId={projectId}
          documentId={documentId}
          versionId={documentVersionId}
          action={documentAction}
        />
      ) : null}
      {section === 'governance' ? <GovernanceHost /> : null}
      {section === 'provisioning' ? (
        <ProvisioningSection onBackToProjects={onBackToProjects} onOpenBoard={onOpenBoard} />
      ) : null}
      {section === 'audit' ? <AuditSection /> : null}
      {section === 'settings' ? <SettingsScreen /> : null}
      {section === 'tools' || section === 'members' ? (
        <RouteOutcomeScreen
          kind="notImplemented"
          title={plannedCopy[section].title}
          message={plannedCopy[section].message}
          nextSafeAction={plannedCopy[section].next}
          actionLabel="Back to Library"
          onAction={() => navigateProductPath(projectPath(projectId, 'library'))}
        />
      ) : null}

      {preserveGovernancePath ? (
        <span className="fl-visually-hidden" data-testid="flow.library.compatibilityPath">
          Legacy governance deep link preserved
        </span>
      ) : null}
    </div>
  );
}
