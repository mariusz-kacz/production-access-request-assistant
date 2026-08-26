using GovernedAccess.Core.Application;
using GovernedAccess.Core.Domain.Preparations;
using GovernedAccess.Core.Preparations;
using GovernedAccess.Core.Preparations.Contracts;

namespace GovernedAccess.Web.Ai;

internal sealed class TargetRequestPreparationOrchestrator
{
    private readonly ITurnProposalInterpreter interpreter;
    private readonly PreparationTurnService turnService;

    internal TargetRequestPreparationOrchestrator(
        PreparationTurnService turnService,
        ITurnProposalInterpreter interpreter)
    {
        ArgumentNullException.ThrowIfNull(turnService);
        ArgumentNullException.ThrowIfNull(interpreter);
        this.turnService = turnService;
        this.interpreter = interpreter;
    }

    internal async Task<PreparationTurnResult> ProcessTurnAsync(
        PreparationBinding binding,
        string latestRequesterText,
        string correlationId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(binding);
        var started = await turnService.BeginAsync(
            binding,
            correlationId,
            cancellationToken);
        if (started.IsFailure)
        {
            return Failed(started.Failure!);
        }

        var turn = started.Value;
        if (!turn.RequiresInterpretation)
        {
            return new PreparationTurnResult(
                turn.Preparation,
                turn.ImmediateResponse!);
        }

        var interpretation = await interpreter.InterpretAsync(
            CreateAgentInput(turn, latestRequesterText, correlationId),
            cancellationToken);
        return interpretation switch
        {
            AgentInterpretationSucceeded succeeded =>
                await turnService.ApplyAsync(
                    turn,
                    succeeded.Proposal,
                    ToAttribution(succeeded.ExecutionMetadata),
                    cancellationToken),
            AgentInterpretationFailed
            {
                Failure: AgentInterpretationFailure.BudgetExhausted,
            } => PreparationTurnService.Exhausted(turn),
            AgentInterpretationFailed failed => PreparationTurnService.Reject(
                turn,
                ToApplicationFailure(failed.Failure)),
            _ => throw new InvalidOperationException(
                "The target interpretation result is unsupported."),
        };
    }

    internal Task<PreparationTurnResult> ResetAsync(
        PreparationBinding binding,
        string correlationId,
        CancellationToken cancellationToken) =>
        turnService.ResetAsync(
            new ResetPreparationCommand(binding, correlationId),
            cancellationToken);

    private static AgentTurnInput CreateAgentInput(
        PreparationTurnContext turn,
        string latestRequesterText,
        string correlationId)
    {
        var preparation = turn.Preparation;
        return new AgentTurnInput(
            latestRequesterText,
            preparation?.Candidate ?? PreparationCandidate.Empty,
            preparation?.Lifecycle ?? PreparationLifecycle.Collecting,
            preparation?.InterpretedTurnCount ?? 0,
            CreateAgentClarification(preparation?.Clarification),
            correlationId);
    }

    private static AgentClarificationContext? CreateAgentClarification(
        PreparationClarificationContext? clarification) =>
        clarification is null
            ? null
            : new AgentClarificationContext(
                clarification.Target,
                clarification.OrderedCanonicalIds.Select(
                    static identifier => new AgentClarificationChoice(
                        identifier,
                        identifier)));

    private static PreparationTurnAttribution ToAttribution(
        AgentExecutionMetadata metadata) =>
        new(
            metadata.ModelDeployment,
            metadata.ProviderModelVersion,
            metadata.PromptContractVersion,
            metadata.StructuredOutputSchemaVersion);

    private static ApplicationFailure ToApplicationFailure(
        AgentInterpretationFailure failure) =>
        failure switch
        {
            AgentInterpretationFailure.InvalidInput => new ApplicationFailure(
                ApplicationFailureKind.InvalidInput,
                "request-preparation-agent-input-invalid",
                "The request-preparation turn input is invalid."),
            AgentInterpretationFailure.MalformedModelOutput => new ApplicationFailure(
                ApplicationFailureKind.DependencyFailure,
                "request-preparation-agent-output-invalid",
                "The request-preparation agent returned an invalid result."),
            AgentInterpretationFailure.ExecutionBudgetExceeded => new ApplicationFailure(
                ApplicationFailureKind.DependencyFailure,
                "request-preparation-agent-execution-limit",
                "The request-preparation agent exceeded its execution limit."),
            AgentInterpretationFailure.Timeout => new ApplicationFailure(
                ApplicationFailureKind.Timeout,
                "request-preparation-agent-timeout",
                "The request-preparation agent timed out."),
            AgentInterpretationFailure.Unavailable => new ApplicationFailure(
                ApplicationFailureKind.DependencyUnavailable,
                "request-preparation-agent-unavailable",
                "The request-preparation agent is unavailable."),
            AgentInterpretationFailure.BudgetExhausted => throw new InvalidOperationException(
                "Budget exhaustion requires its dedicated outcome."),
            _ => throw new InvalidOperationException(
                "The target interpretation failure is unsupported."),
        };

    private static PreparationTurnResult Failed(ApplicationFailure failure) =>
        new(
            preparation: null,
            new PreparationResponse(new Failed(failure)));
}
