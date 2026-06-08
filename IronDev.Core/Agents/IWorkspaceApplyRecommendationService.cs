namespace IronDev.Core.Agents;

public interface IWorkspaceApplyRecommendationService
{
    WorkspaceApplyRecommendation Recommend(WorkspaceApplyRecommendationRequest request);
}
