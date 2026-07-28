using GovernedAccess.Core.Domain;
using GovernedAccess.Core.Ports;

namespace GovernedAccess.Core.Application;

/// <summary>
/// Coordinates one channel-neutral request-preparation turn. Interpreter proposals
/// remain untrusted until the application validator accepts and canonicalizes them.
/// </summary>
public sealed class RequestPreparationService
{
    public const string ConversationNotCollectingCode =
        "request_preparation_not_collecting";

    public const string MalformedModelOutputCode =
        "request_preparation_model_output_malformed";

    public const string ModelTimeoutCode =
        "request_preparation_model_timeout";

    public const string ModelCancelledCode =
        "request_preparation_model_cancelled";

    public const string ModelUnavailableCode =
        "request_preparation_model_unavailable";

    private readonly IRequestPreparationInterpreter interpreter;
    private readonly RequestValidator requestValidator;
    private readonly IRequestContextReader requestContext;
    private readonly IRequestIntakeStore intakeStore;
    private readonly IClock clock;

    public RequestPreparationService(
        IRequestPreparationInterpreter interpreter,
        RequestValidator requestValidator,
        IRequestContextReader requestContext,
        IRequestIntakeStore intakeStore,
        IClock clock)
    {
        ArgumentNullException.ThrowIfNull(interpreter);
        ArgumentNullException.ThrowIfNull(requestValidator);
        ArgumentNullException.ThrowIfNull(requestContext);
        ArgumentNullException.ThrowIfNull(intakeStore);
        ArgumentNullException.ThrowIfNull(clock);

        this.interpreter = interpreter;
        this.requestValidator = requestValidator;
        this.requestContext = requestContext;
        this.intakeStore = intakeStore;
        this.clock = clock;
    }

    public async Task<RequestPreparationOutcome> PrepareAsync(
        PrepareAccessRequestCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var conversationResult = await intakeStore.GetActiveConversationAsync(
            command.Actor,
            cancellationToken);
        if (conversationResult.IsFailure
            && conversationResult.Failure!.Kind != ApplicationFailureKind.NotFound)
        {
            return new RequestPreparationFailed(conversationResult.Failure);
        }

        var conversation = conversationResult.IsSuccess
            ? conversationResult.Value
            : CreateConversation(command);

        if (conversation.Status != RequestPreparationConversationStatus.Collecting)
        {
            return Failed(
                ApplicationFailureKind.InvalidTransition,
                ConversationNotCollectingCode,
                "The active preparation is not collecting request details.");
        }

        var interpretation = await interpreter.InterpretAsync(
            new RequestPreparationTurn(
                command.LatestMessage,
                ToCandidate(conversation),
                conversation.PendingClarification,
                command.CorrelationId),
            cancellationToken);

        if (interpretation.Kind != RequestPreparationInterpretationOutcomeKind.Proposal)
        {
            return MapInterpretationFailure(interpretation.Kind);
        }

        var proposal = interpretation.Proposal
            ?? throw new InvalidOperationException(
                "A successful preparation interpretation must contain a proposal.");

        if (proposal.Kind == RequestPreparationProposalKind.Clarification)
        {
            var clarificationResult = await CanonicalizeClarificationAsync(
                proposal.Clarification!,
                proposal.Candidate,
                cancellationToken);
            if (clarificationResult.IsFailure)
            {
                return new RequestPreparationFailed(clarificationResult.Failure!);
            }

            return await PersistClarificationAsync(
                conversation,
                proposal.Candidate,
                clarificationResult.Value,
                command.CorrelationId,
                cancellationToken);
        }

        var candidate = proposal.Candidate;
        var validation = await requestValidator.ValidateAsync(
            new RequestValidationInput(
                candidate.ClientId,
                candidate.EnvironmentId,
                candidate.RequestedRoleId,
                candidate.Justification,
                candidate.IncidentId),
            cancellationToken);

        if (validation is RequestValidationFailed validationFailed)
        {
            return new RequestPreparationFailed(validationFailed.Failure);
        }

        if (validation is RequestValidationRejected validationRejected)
        {
            return new RequestCandidateRejected(validationRejected.Errors);
        }

        if (validation is not RequestValidationSucceeded validationSucceeded)
        {
            throw new InvalidOperationException(
                "The request validation outcome is unsupported.");
        }

        cancellationToken.ThrowIfCancellationRequested();

        var occurredAt = clock.UtcNow.ToUniversalTime();
        var fields = validationSucceeded.Fields;
        var preparationId = Guid.NewGuid();
        var preparedRequest = new PreparedAccessRequest(
            preparationId,
            conversation.Id,
            Guid.NewGuid(),
            command.Actor.Channel,
            command.Actor.TenantId,
            command.Actor.ChannelActorId,
            command.Actor.ConversationId,
            command.Actor.RequesterId,
            fields.ClientId,
            fields.EnvironmentId,
            fields.RequestedRoleId,
            fields.Justification,
            fields.IncidentId,
            occurredAt,
            command.CorrelationId);

        conversation.UpdateCandidate(
            fields.ClientId,
            fields.EnvironmentId,
            fields.RequestedRoleId,
            fields.Justification,
            fields.IncidentId,
            pendingClarification: null,
            occurredAt,
            command.CorrelationId);
        conversation.MarkReady(
            preparationId,
            occurredAt,
            command.CorrelationId);

        intakeStore.AddPreparedRequest(preparedRequest);
        var saveResult = await intakeStore.SaveChangesAsync(cancellationToken);
        return saveResult.IsFailure
            ? new RequestPreparationFailed(saveResult.Failure!)
            : new RequestReadyForConfirmation(preparedRequest);
    }

    private RequestPreparationConversation CreateConversation(
        PrepareAccessRequestCommand command)
    {
        var actor = command.Actor;
        var conversation = new RequestPreparationConversation(
            Guid.NewGuid(),
            actor.Channel,
            actor.TenantId,
            actor.ChannelActorId,
            actor.ConversationId,
            actor.RequesterId,
            clock.UtcNow,
            command.CorrelationId);
        intakeStore.AddConversation(conversation);
        return conversation;
    }

    private async Task<RequestPreparationOutcome> PersistClarificationAsync(
        RequestPreparationConversation conversation,
        RequestCandidate candidate,
        RequestClarificationContext clarification,
        string correlationId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        conversation.UpdateCandidate(
            candidate.ClientId,
            candidate.EnvironmentId,
            candidate.RequestedRoleId,
            candidate.Justification,
            candidate.IncidentId,
            clarification,
            clock.UtcNow,
            correlationId);

        var saveResult = await intakeStore.SaveChangesAsync(cancellationToken);
        return saveResult.IsFailure
            ? new RequestPreparationFailed(saveResult.Failure!)
            : new RequestClarificationRequired(clarification);
    }

    private async Task<ApplicationResult<RequestClarificationContext>>
        CanonicalizeClarificationAsync(
            RequestClarificationContext clarification,
            RequestCandidate candidate,
            CancellationToken cancellationToken)
    {
        var canonicalOptions = new List<RequestClarificationOption>(
            clarification.Options.Count);
        foreach (var option in clarification.Options)
        {
            var optionResult = await CanonicalizeOptionAsync(
                clarification.Target,
                option.Value,
                candidate,
                cancellationToken);
            if (optionResult.IsFailure)
            {
                return ApplicationResult.Failed<RequestClarificationContext>(
                    optionResult.Failure!);
            }

            canonicalOptions.Add(optionResult.Value);
        }

        return ApplicationResult.Succeeded(
            new RequestClarificationContext(
                clarification.Target,
                clarification.Prompt,
                canonicalOptions));
    }

    private async Task<ApplicationResult<RequestClarificationOption>>
        CanonicalizeOptionAsync(
            RequestClarificationTarget target,
            string proposedValue,
            RequestCandidate candidate,
            CancellationToken cancellationToken)
    {
        switch (target)
        {
            case RequestClarificationTarget.ClientId:
                {
                    var result = await requestContext.GetClientAsync(
                        proposedValue,
                        cancellationToken);
                    return result.IsFailure
                        ? FailedOption(result.Failure!)
                        : CanonicalOption(result.Value.Id, result.Value.DisplayName);
                }

            case RequestClarificationTarget.EnvironmentId:
                {
                    var result = await requestContext.GetProductionEnvironmentAsync(
                        proposedValue,
                        cancellationToken);
                    if (result.IsFailure)
                    {
                        return FailedOption(result.Failure!);
                    }

                    var environment = result.Value;
                    if (candidate.ClientId is not null
                        && !string.Equals(
                            environment.ClientId,
                            candidate.ClientId,
                            StringComparison.Ordinal))
                    {
                        return InvalidOption();
                    }

                    return CanonicalOption(environment.Id, environment.DisplayName);
                }

            case RequestClarificationTarget.RequestedRoleId:
                {
                    if (candidate.EnvironmentId is null)
                    {
                        return InvalidOption();
                    }

                    var result = await requestContext.GetEnvironmentRoleAsync(
                        candidate.EnvironmentId,
                        proposedValue,
                        cancellationToken);
                    return result.IsFailure
                        ? FailedOption(result.Failure!)
                        : CanonicalOption(
                            result.Value.RoleId,
                            GetRoleDisplayName(result.Value.RoleId));
                }

            case RequestClarificationTarget.IncidentId:
                {
                    var result = await requestContext.GetIncidentAsync(
                        proposedValue,
                        cancellationToken);
                    if (result.IsFailure)
                    {
                        return FailedOption(result.Failure!);
                    }

                    var incident = result.Value;
                    var matchesCandidate = incident.Status == IncidentStatus.Active
                        && (candidate.ClientId is null
                            || string.Equals(
                                incident.ClientId,
                                candidate.ClientId,
                                StringComparison.Ordinal))
                        && (candidate.EnvironmentId is null
                            || incident.EnvironmentId is null
                            || string.Equals(
                                incident.EnvironmentId,
                                candidate.EnvironmentId,
                                StringComparison.Ordinal));
                    return matchesCandidate
                        ? CanonicalOption(incident.Id, incident.Title)
                        : InvalidOption();
                }

            case RequestClarificationTarget.Justification:
                return InvalidOption();

            default:
                throw new InvalidOperationException(
                    "The clarification target is unsupported.");
        }
    }

    private static RequestCandidate ToCandidate(
        RequestPreparationConversation conversation) =>
        new(
            conversation.ClientId,
            conversation.EnvironmentId,
            conversation.RequestedRoleId,
            conversation.Justification,
            conversation.IncidentId);

    private static RequestPreparationFailed MapInterpretationFailure(
        RequestPreparationInterpretationOutcomeKind kind) =>
        kind switch
        {
            RequestPreparationInterpretationOutcomeKind.MalformedModelOutput => Failed(
                ApplicationFailureKind.DependencyFailure,
                MalformedModelOutputCode,
                "The request assistant returned an invalid response."),
            RequestPreparationInterpretationOutcomeKind.Timeout => Failed(
                ApplicationFailureKind.Timeout,
                ModelTimeoutCode,
                "Request preparation timed out."),
            RequestPreparationInterpretationOutcomeKind.Cancelled => Failed(
                ApplicationFailureKind.Cancelled,
                ModelCancelledCode,
                "Request preparation was cancelled."),
            RequestPreparationInterpretationOutcomeKind.Unavailable => Failed(
                ApplicationFailureKind.DependencyUnavailable,
                ModelUnavailableCode,
                "The request assistant is unavailable."),
            RequestPreparationInterpretationOutcomeKind.Proposal =>
                throw new InvalidOperationException(
                    "A proposal is not an interpretation failure."),
            _ => throw new InvalidOperationException(
                "The preparation interpretation outcome is unsupported."),
        };

    private static ApplicationResult<RequestClarificationOption> CanonicalOption(
        string value,
        string label) =>
        ApplicationResult.Succeeded(new RequestClarificationOption(value, label));

    private static ApplicationResult<RequestClarificationOption> FailedOption(
        ApplicationFailure failure) =>
        failure.Kind == ApplicationFailureKind.NotFound
            ? InvalidOption()
            : ApplicationResult.Failed<RequestClarificationOption>(failure);

    private static ApplicationResult<RequestClarificationOption> InvalidOption() =>
        ApplicationResult.Failed<RequestClarificationOption>(
            new ApplicationFailure(
                ApplicationFailureKind.DependencyFailure,
                MalformedModelOutputCode,
                "The request assistant proposed an invalid clarification option."));

    private static string GetRoleDisplayName(string roleId) =>
        roleId switch
        {
            ProductionRoleIds.ReadOnly => "Production read-only",
            ProductionRoleIds.Support => "Production support",
            _ => throw new InvalidOperationException(
                "The authoritative role identifier is unsupported."),
        };

    private static RequestPreparationFailed Failed(
        ApplicationFailureKind kind,
        string code,
        string message) =>
        new(new ApplicationFailure(kind, code, message));
}
