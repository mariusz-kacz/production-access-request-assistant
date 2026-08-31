using GovernedAccess.Core.Preparations.Authority;
using System.Collections.Generic;
using GovernedAccess.Core.Preparations.Contracts;
using System;
using System.Text.Json.Serialization;
using GovernedAccess.Web.Ai;
using GovernedAccess.Core.Domain.Preparations;

namespace GovernedAccess.Web.Evaluation;

internal enum EvaluationScenarioStatus
{
	Passed,
	Failed,
	Cancelled,
	NotRun
}

internal enum EvaluationRunStatus
{
	Passed,
	Failed,
	Cancelled,
	PrerequisiteFailed
}

internal sealed record WorkflowSideEffectCounts(int Requests, int ApprovalDecisions, int ProvisioningOperations, int AccessGrants)
{
	internal static WorkflowSideEffectCounts None { get; } = new WorkflowSideEffectCounts(0, 0, 0, 0);

	internal bool HasAny => Requests != 0 || ApprovalDecisions != 0 || ProvisioningOperations != 0 || AccessGrants != 0;
}

internal sealed record EvaluationArtifactPaths(string JsonPath, string MarkdownPath);

internal enum EvaluationOutcome
{
	Ready,
	DraftUpdated,
	DraftUnchanged,
	Clarification,
	Discussion,
	SubmissionGuidance,
	UnrelatedGuidance,
	UnclearGuidance,
	Failed
}

internal enum EvaluationEnvironmentReferenceKind
{
	ExactEnvironmentId,
	SearchQuery
}

internal enum EvaluationOperationKind
{
	Set,
	Clear
}

internal enum EvaluationFailureMode
{
	None,
	ProviderUnavailable,
	McpUnavailable
}

internal sealed record EvaluationCandidate(string? ClientId, string? EnvironmentId, string? RoleId, string? Justification, string? IncidentId);

internal sealed record EvaluationChoice(string CanonicalId, string DisplayName, string? ClientId, string? ClientDisplayName, string? Region, EnvironmentClassification? Classification);

internal sealed record EvaluationClarification(ClarificationTarget Target, IReadOnlyList<EvaluationChoice> Choices);

internal sealed record EvaluationStartingState(EvaluationCandidate Candidate, EvaluationClarification? Clarification);

internal sealed record EvaluationOperationExpectation(EvaluationOperationKind Operation, EvaluationEnvironmentReferenceKind? EnvironmentReferenceKind, string? Value);

internal sealed record EvaluationProposalExpectation(EvaluationOperationExpectation? Environment, EvaluationOperationExpectation? Role, EvaluationOperationExpectation? Justification, EvaluationOperationExpectation? Incident);

internal sealed record EvaluationInterpretationExpectation(DialogueAct? DialogueAct, DiscussionTopic? DiscussionTopic, AgentInterpretationFailure? Failure, EvaluationProposalExpectation? Proposal, IReadOnlyList<string> AllowedTools, IReadOnlyList<string> RequiredTools, int MaximumToolCalls, IReadOnlyList<EvaluationInterpretationSnapshot>? AcceptableInterpretations = null)
{
	internal static EvaluationInterpretationExpectation Unclear()
	{
		return new EvaluationInterpretationExpectation(GovernedAccess.Core.Preparations.Contracts.DialogueAct.Unclear, null, null, null, Array.Empty<string>(), Array.Empty<string>(), 0);
	}
}

internal sealed record EvaluationTurn(string Id, string RequesterMessage, EvaluationInterpretationExpectation Expected, EvaluationFailureMode FailureMode = EvaluationFailureMode.None);

internal sealed record EvaluationApplicationGroupExpectation(ApplicationGroupResultKind Kind, ApplicationGroupRejectionReason? RejectionReason);

internal sealed record EvaluationCanonicalExpectation(EvaluationOutcome Outcome, PreparationLifecycle? Lifecycle, EvaluationCandidate? Candidate, ClarificationTarget? ClarificationTarget, IReadOnlyList<string> ClarificationChoiceIds, EvaluationApplicationGroupExpectation? ScopeResult, EvaluationApplicationGroupExpectation? JustificationResult, IReadOnlyList<EvaluationOutcome>? AcceptableOutcomes = null)
{
	internal static EvaluationCanonicalExpectation EmptyCollecting()
	{
		return new EvaluationCanonicalExpectation(EvaluationOutcome.UnclearGuidance, null, null, null, Array.Empty<string>(), null, null);
	}
}

internal sealed record EvaluationVariation(string Id, EvaluationStartingState? StartingState, IReadOnlyList<EvaluationTurn> Turns, EvaluationCanonicalExpectation ExpectedFinal);

internal sealed record EvaluationGroup(string Id, bool Promoted, bool AbsoluteOutcomeGate, IReadOnlyList<EvaluationVariation> Variations);

internal sealed record EvaluationDataset(int SchemaVersion, string DatasetVersion, string Environment, IReadOnlyList<EvaluationGroup> Groups)
{
	[JsonIgnore]
	internal string Sha256 { get; init; } = string.Empty;
}

internal sealed record EvaluationSafetyResult(bool ZeroConsequentialSideEffects, bool NoUnknownOrMutatingToolCalls, bool NoModelProse, bool AuthoritativeIdentifiers, bool Restraint, bool ClarificationResolution, bool JustificationFidelity)
{
	internal static EvaluationSafetyResult Passed { get; } = new EvaluationSafetyResult(ZeroConsequentialSideEffects: true, NoUnknownOrMutatingToolCalls: true, NoModelProse: true, AuthoritativeIdentifiers: true, Restraint: true, ClarificationResolution: true, JustificationFidelity: true);

	internal bool IsPassed => ZeroConsequentialSideEffects && NoUnknownOrMutatingToolCalls && NoModelProse && AuthoritativeIdentifiers && Restraint && ClarificationResolution && JustificationFidelity;
}

internal sealed record EvaluationInterpretationSnapshot(DialogueAct? DialogueAct, DiscussionTopic? DiscussionTopic, AgentInterpretationFailure? Failure);

internal sealed record EvaluationInterpretationComparison(EvaluationInterpretationSnapshot Expected, EvaluationInterpretationSnapshot Observed, IReadOnlyList<EvaluationInterpretationSnapshot>? Acceptable = null, bool Matches = true);

internal sealed record EvaluationOperationSnapshot(EvaluationOperationKind Operation, EvaluationEnvironmentReferenceKind? EnvironmentReferenceKind, string? Value);

internal sealed record EvaluationProposalFieldComparison(bool Matches, EvaluationOperationSnapshot? Expected, EvaluationOperationSnapshot? Observed);

internal sealed record EvaluationProposalComparison(bool ExpectedPresent, bool ObservedPresent, EvaluationProposalFieldComparison Environment, EvaluationProposalFieldComparison Role, EvaluationProposalFieldComparison Justification, EvaluationProposalFieldComparison Incident);

internal sealed record EvaluationToolUseExpectation(IReadOnlyList<string> AllowedNames, IReadOnlyList<string> RequiredNames, int MaximumCalls);

internal sealed record EvaluationToolUseObservation(IReadOnlyList<string> Names, int CallCount);

internal sealed record EvaluationToolUseComparison(EvaluationToolUseExpectation Expected, EvaluationToolUseObservation Observed);

internal sealed record EvaluationTurnComparison(EvaluationInterpretationComparison Interpretation, EvaluationProposalComparison Proposal, EvaluationToolUseComparison Tools);

internal sealed record EvaluationCandidateSnapshot(string? ClientId, string? EnvironmentId, string? RoleId, string? Justification, string? IncidentId);

internal sealed record EvaluationCanonicalSnapshot(EvaluationOutcome? Outcome, PreparationLifecycle? Lifecycle, EvaluationCandidateSnapshot? Candidate, ClarificationTarget? ClarificationTarget, IReadOnlyList<string> ClarificationChoiceIds, EvaluationApplicationGroupExpectation? ScopeResult, EvaluationApplicationGroupExpectation? JustificationResult);

internal sealed record EvaluationCanonicalComparison(EvaluationCanonicalSnapshot Expected, EvaluationCanonicalSnapshot? Observed, IReadOnlyList<string> CandidateMismatchFields, IReadOnlyList<EvaluationOutcome>? AcceptableOutcomes = null);

internal sealed record EvaluationTurnResult(string Id, string RequesterMessage, EvaluationScenarioStatus Status, DialogueAct? DialogueAct, AgentInterpretationFailure? Failure, string? ProviderModelVersion, int ProviderIterationCount, IReadOnlyList<string> ToolNames, IReadOnlyList<string> FailureCodes, EvaluationTurnComparison? Comparison = null);

internal sealed record EvaluationVariationResult(string Id, EvaluationScenarioStatus Status, bool CanonicalOutcomeMatched, EvaluationSafetyResult Safety, long? ElapsedMilliseconds, WorkflowSideEffectCounts SideEffects, IReadOnlyList<string> FailureCodes, IReadOnlyList<EvaluationTurnResult> Turns, EvaluationOutcome? Outcome = null, EvaluationCanonicalComparison? CanonicalComparison = null);

internal sealed record EvaluationGroupResult(string Id, bool Promoted, bool AbsoluteOutcomeGate, EvaluationScenarioStatus Status, IReadOnlyList<EvaluationVariationResult> Variations);

internal sealed record EvaluationVersionMetadata(string ProviderId, string ModelDeployment, string? ProviderModelVersion, string PromptContractVersion, string ProposalSchemaVersion, string McpContractVersion, string EnvironmentSearchPolicyVersion)
{
	internal static EvaluationVersionMetadata TestDefault { get; } = new EvaluationVersionMetadata("test-provider", "test-deployment", "test-model", "test-prompt", "test-schema", "test-mcp", "test-search");
}

internal sealed record EvaluationSummary(int PromotedTotal, int PromotedPassed, int RequiredPasses, int AdvisoryTotal, int AdvisoryPassed, bool AbsoluteSafetyPassed);

internal sealed record EvaluationRunResult(Guid RunId, string SourceCommit, string DatasetVersion, string DatasetSha256, string Environment, DateTimeOffset StartedAt, DateTimeOffset CompletedAt, EvaluationRunStatus Status, EvaluationVersionMetadata Versions, EvaluationSummary Summary, WorkflowSideEffectCounts SideEffects, IReadOnlyList<EvaluationGroupResult> Groups)
{
	internal string? DiagnosticVariationId { get; init; }
}

internal sealed record EvaluationSourceMetadata(string Commit);
