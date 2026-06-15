namespace IronDev.Core.Governance;

public interface IGovernanceDataRetentionRuleService
{
    GovernanceDataRetentionRuleResult Evaluate(GovernanceDataRetentionRuleRequest request);
}
