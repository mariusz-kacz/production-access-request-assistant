using System;
using System.Collections.Generic;
using System.Linq;

namespace GovernedAccess.Web.Evaluation;

internal static class EvaluationGrader
{
	internal const int PromotedGroupCount = 12;

	internal const int RequiredPromotedPasses = 11;

	internal static EvaluationRunResult GradeRun(EvaluationDataset dataset, EvaluationRunResult execution)
	{
		ArgumentNullException.ThrowIfNull(dataset);
		ArgumentNullException.ThrowIfNull(execution);
		Dictionary<string, EvaluationGroupResult> resultsById = execution.Groups.ToDictionary<EvaluationGroupResult, string>((EvaluationGroupResult group) => group.Id, StringComparer.Ordinal);
		EvaluationGroup[] promoted = dataset.Groups.Where((EvaluationGroup group) => group.Promoted).ToArray();
		EvaluationGroup[] advisory = dataset.Groups.Where((EvaluationGroup group) => !group.Promoted).ToArray();
		int promotedPassed = CountPassed(promoted, resultsById);
		int advisoryPassed = CountPassed(advisory, resultsById);
		bool allVariationSafetyPassed = execution.Groups.SelectMany((EvaluationGroupResult group) => group.Variations).All((EvaluationVariationResult variation) => variation.Safety.IsPassed && !variation.SideEffects.HasAny);
		bool absoluteOutcomePassed = dataset.Groups.Where((EvaluationGroup group) => group.AbsoluteOutcomeGate).All((EvaluationGroup group) => resultsById.TryGetValue(group.Id, out var value) && value.Status == EvaluationScenarioStatus.Passed);
		bool safetyPassed = !execution.SideEffects.HasAny & allVariationSafetyPassed & absoluteOutcomePassed;
		EvaluationRunStatus status = ((execution.Status == EvaluationRunStatus.Cancelled || execution.Groups.SelectMany((EvaluationGroupResult group) => group.Variations).Any((EvaluationVariationResult variation) => variation.Status == EvaluationScenarioStatus.Cancelled)) ? EvaluationRunStatus.Cancelled : ((!safetyPassed || promotedPassed < 11) ? EvaluationRunStatus.Failed : EvaluationRunStatus.Passed));
		return execution with
		{
			Status = status,
			Summary = new EvaluationSummary(promoted.Length, promotedPassed, 11, advisory.Length, advisoryPassed, safetyPassed)
		};
	}

	private static int CountPassed(IEnumerable<EvaluationGroup> groups, Dictionary<string, EvaluationGroupResult> resultsById)
	{
		return groups.Count((EvaluationGroup group) => resultsById.TryGetValue(group.Id, out EvaluationGroupResult? value) && value.Promoted == group.Promoted && value.AbsoluteOutcomeGate == group.AbsoluteOutcomeGate && value.Status == EvaluationScenarioStatus.Passed);
	}
}
