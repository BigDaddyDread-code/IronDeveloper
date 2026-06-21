namespace IronDev.Core.Governance;

public enum AuthorityProfileStatusReason
{
    AuthorityProfileKnownRequired = 1,
    BoundedRunGrantExpired = 2,
    ProposalOnlyDoesNotAllowDurableMutation = 3,
    MutationRequiresExplicitHumanApproval = 4,
    OperationEligibilityDecisionRequired = 5,
    OperationEligibilityDecisionOperationMismatch = 6,
    OperationEligibilityDecisionBlocked = 7,
    OperationEligibilityEvidenceMissing = 8,
    OperationEligibilityDecisionNotEligible = 9
}
