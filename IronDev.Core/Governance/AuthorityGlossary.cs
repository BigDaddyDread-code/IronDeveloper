namespace IronDev.Core.Governance;

public static class AuthorityGlossary
{
    public const string EvidenceIsNotApproval =
        "evidence is not approval";

    public const string ValidationPassedIsNotApproval =
        "validation passed is not approval";

    public const string ReceiptRefsAreNotAuthority =
        "receipt refs are not authority";

    public const string EvidenceRefsAreNotAuthority =
        "evidence refs are not authority";

    public const string StatusIsNotAuthority =
        "status is not authority";

    public const string ProfileKindIsNotAuthority =
        "profile kind is not authority";

    public const string ProfileAllowanceNecessaryNotSufficient =
        "profile allowance is necessary but not sufficient";

    public const string DoNotTreatProfileAllowanceAsApproval =
        "do not treat profile allowance as approval";

    public const string DoNotTreatProfileAllowanceAsPolicySatisfaction =
        "do not treat profile allowance as policy satisfaction";

    public const string DoNotTreatProfileAllowanceAsExecutionAuthority =
        "do not treat profile allowance as execution authority";

    public const string DoNotMutateDurableSourceFromProfileAllowance =
        "do not mutate durable source from profile allowance";

    public const string ProfileAndGrantEligibilityNecessaryNotSufficient =
        "profile and grant eligibility is necessary but not sufficient";

    public const string OperationSpecificGovernanceStillRequired =
        "operation-specific governance still required";

    public const string DoNotTreatEligibilityAsApproval =
        "do not treat eligibility as approval";

    public const string DoNotTreatEligibilityAsPolicySatisfaction =
        "do not treat eligibility as policy satisfaction";

    public const string DoNotTreatEligibilityAsExecutionAuthority =
        "do not treat eligibility as execution authority";

    public const string DoNotTreatEligibilityAsSourceApplyAuthority =
        "do not treat eligibility as source apply authority";

    public const string DoNotMutateDurableSourceFromEligibility =
        "do not mutate durable source from eligibility";

    public const string DoNotExecuteFromStatusAlone =
        "do not execute from status alone";

    public const string DoNotTreatEligibleStatusAsApproval =
        "do not treat Eligible status as approval";

    public const string DoNotTreatEligibleStatusAsPolicySatisfaction =
        "do not treat Eligible status as policy satisfaction";

    public const string DoNotApplySourceFromStatusAlone =
        "do not apply source from status alone";

    public const string ExecutorMustRecheckProfileGrantScopePatchHashValidationMutationBudgetWorktree =
        "executor must independently re-check profile/grant/scope/patch hash/validation/mutation budget/worktree state";

    public const string StatusMayExplainGateMustNotBecomeGate =
        "Status may explain the gate. It must not become the gate.";

    public const string DoNotTreatAcceptedApplyApprovalAsLaterLaneAuthority =
        "do not treat accepted apply approval as authority for later mutation lanes";

    public const string SourceApplyReceiptIsNotAcceptedApplyApproval =
        "source apply receipt is not accepted apply approval";

    public const string AskBeforeMutationOneGuardedDoorNotHallway =
        "AskBeforeMutation asks for one guarded door. It does not open the hallway.";

    public const string BoundedProfileAllowanceIsNotLaterStageAuthority =
        "do not treat bounded profile allowance as later-stage authority";

    public const string SourceApplyDecisionIsNotCommitAuthority =
        "A source apply decision is not commit authority.";

    public const string CommitDecisionIsNotPushAuthority =
        "A commit decision is not push authority.";

    public const string PushDecisionIsNotDraftPrAuthority =
        "A push decision is not draft PR authority.";

    public const string DraftPrDecisionIsNotReadyForReviewAuthority =
        "A draft PR decision is not ready-for-review authority.";

    public const string BoundedLaneEndsWhereNextBoundaryBegins =
        "A bounded lane ends where the next authority boundary begins.";

    public const string ProposalOnlyMeansProposalOnly =
        "ProposalOnly means proposal only, even when every receipt begs otherwise.";
}
