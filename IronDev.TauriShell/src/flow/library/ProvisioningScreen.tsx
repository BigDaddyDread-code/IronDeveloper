import { useProjectContext } from '../../state/useProjectContext';
import { ProjectSetupScreen } from '../projects/ProjectSetupScreen';

interface ProvisioningScreenProps {
  onBackToProjects: () => void;
  onOpenBoard: () => void;
}

export function ProvisioningScreen({ onBackToProjects, onOpenBoard }: ProvisioningScreenProps) {
  const project = useProjectContext();

  if (project.selectedProjectId === null) {
    return <p className="fl-empty">Select a project to check its setup.</p>;
  }

  return (
    <ProjectSetupScreen
      projectId={project.selectedProjectId}
      onBackToProjects={onBackToProjects}
      onOpenBoard={onOpenBoard}
    />
  );
}
