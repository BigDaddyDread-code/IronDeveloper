import { libraryPath, navigateProductPath, type LibrarySection } from '../navigation/productRoutes';
import { GovernanceHost } from './GovernanceHost';
import { AuditSection, ProvisioningSection } from './PlannedSections';
import { SolutionExplorer } from './SolutionExplorer';
import { SettingsScreen } from '../settings/SettingsScreen';
import { DocumentsScreen } from './DocumentsScreen';
import { ToolsScreen } from './ToolsScreen';
import { MembersScreen } from './MembersScreen';

interface LibraryScreenProps {
  projectId: number;
  section: LibrarySection;
  documentId?: number | null;
  documentVersionId?: number | null;
  documentAction?: 'upload' | null;
  toolId?: string | null;
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

export function LibraryScreen({
  projectId,
  section,
  documentId = null,
  documentVersionId = null,
  documentAction = null,
  toolId = null,
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
      {section === 'tools' ? <ToolsScreen projectId={projectId} toolId={toolId} /> : null}
      {section === 'members' ? <MembersScreen projectId={projectId} /> : null}
      {section === 'governance' ? <GovernanceHost /> : null}
      {section === 'provisioning' ? (
        <ProvisioningSection onBackToProjects={onBackToProjects} onOpenBoard={onOpenBoard} />
      ) : null}
      {section === 'audit' ? <AuditSection /> : null}
      {section === 'settings' ? <SettingsScreen /> : null}
      {preserveGovernancePath ? (
        <span className="fl-visually-hidden" data-testid="flow.library.compatibilityPath">
          Legacy governance deep link preserved
        </span>
      ) : null}
    </div>
  );
}
