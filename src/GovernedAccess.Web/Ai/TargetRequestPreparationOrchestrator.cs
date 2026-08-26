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
            CreateAgentClarification(preparation?.Clarification),
            correlationId);
    }

    private static AgentClarificationContext? CreateAgentClarification(
        PreparationClarificationContext? clarification) =>
        clarification is null
            ? null
            : new AgentClarificationContext(
                clarification.Target,
                clarification.CreatedAt,
                clarification.Choices.Select(
                    static (choice, index) => ToAgentChoice(choice, index + 1)));

    private static AgentClarificationChoice ToAgentChoice(
        ClarificationChoice choice,
        int position) =>
        choice switch
        {
            EnvironmentClarificationChoice environment => new(
                position,
                environment.CanonicalId,
                environment.DisplayName,
                environment.ClientId,
                environment.ClientDisplayName,
                environment.Region,
                environment.Classification),
            RoleClarificationChoice role => new(
                position,
                role.CanonicalId,
                role.DisplayName,
                clientId: null,
                clientDisplayName: null,
                region: null,
                environmentClassification: null),
            _ => throw new InvalidOperationException(
                "The clarification choice type is unsupported."),
        };

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
            _ => throw new InvalidOperationException(
                "The target interpretation failure is unsupported."),
        };

    private static PreparationTurnResult Failed(ApplicationFailure failure) =>
        new(
            preparation: null,
            new PreparationResponse(new Failed(failure)));
}
