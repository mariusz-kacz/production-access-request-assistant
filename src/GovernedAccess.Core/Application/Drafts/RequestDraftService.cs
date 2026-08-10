using GovernedAccess.Core.Domain.AccessRequests;
using GovernedAccess.Core.Domain.Drafts;
using GovernedAccess.Core.Ports;

namespace GovernedAccess.Core.Application.Drafts;

/// <summary>
/// Coordinates channel-neutral request preparation and draft lifecycle operations.
/// Interpreter proposals remain untrusted until the application validator accepts
/// and canonicalizes them.
/// </summary>
public sealed class RequestDraftService
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
    private readonly RequestDraftValidator requestValidator;
    private readonly IRequestContextReader requestContext;
    private readonly IRequestIntakeStore intakeStore;
    private readonly IClock clock;

    public RequestDraftService(
        IRequestPreparationInterpreter interpreter,
        RequestDraftValidator requestValidator,
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

    public async Task<RequestPreparationResult> PrepareAsync(
        PrepareAccessRequestCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var sessionResult = await intakeStore.GetActiveAsync(
            command.Actor,
            cancellationToken);
        if (sessionResult.IsFailure
            && sessionResult.Failure!.Kind != ApplicationFailureKind.NotFound)
        {
            return RequestPreparationResult.Failed(sessionResult.Failure);
        }

        var session = sessionResult.IsSuccess
            ? sessionResult.Value
            : CreateSession(command);
        RequestCandidate? readyCandidate = null;

        if (session.Status == RequestIntakeStatus.Ready)
        {
            var occurredAt = clock.UtcNow.ToUniversalTime();
            if (session.IsExpired(occurredAt))
            {
                session.MarkExpired(occurredAt, command.CorrelationId);

                var lifecycleSave = await intakeStore.SaveChangesAsync(
                    cancellationToken);
                if (lifecycleSave.IsFailure)
                {
                    return RequestPreparationResult.Failed(
                        lifecycleSave.Failure!);
                }

                session = CreateSession(command);
            }
            else
            {
                readyCandidate = ToCandidate(session);
            }
        }
        else if (session.Status != RequestIntakeStatus.Collecting)
        {
            return Failed(
                ApplicationFailureKind.InvalidTransition,
                ConversationNotCollectingCode,
                "The active preparation is not collecting request details.");
        }

        var interpretation = await interpreter.InterpretAsync(
            new RequestPreparationTurn(
                session.Id,
                command.LatestMessage,
                ToCandidate(session),
                command.CorrelationId),
            cancellationToken);

        if (interpretation is RequestPreparationInterpretationFailed failed)
        {
            return MapInterpretationFailure(failed.Failure);
        }

        if (interpretation is not RequestPreparationInterpretationSucceeded succeeded)
        {
            throw new InvalidOperationException(
                "The request preparation interpretation result is unsupported.");
        }

        var proposal = succeeded.Proposal;
        var assessmentResult =
            await requestValidator.AssessCandidateAsync(
                proposal.Candidate,
                cancellationToken);
        if (assessmentResult.IsFailure)
        {
            return RequestPreparationResult.Failed(
                assessmentResult.Failure!);
        }

        if (readyCandidate is not null)
        {
            var assessedCandidate = ToCandidate(assessmentResult.Value);
            if (assessmentResult.Value is RequestCandidateAssessmentReady
                && MatchesCandidate(readyCandidate, assessedCandidate))
            {
                return proposal.Kind == RequestPreparationProposalKind.Clarification
                    ? await CreateDraftDiscussionAsync(
                        session,
                        proposal.Clarification!,
                        cancellationToken)
                    : RequestPreparationResult.DraftDiscussion(
                        "The current draft remains ready for confirmation.",
                        session);
            }

            session.MarkSuperseded(
                clock.UtcNow.ToUniversalTime(),
                command.CorrelationId);
            var supersessionSave = await intakeStore.SaveChangesAsync(
                cancellationToken);
            if (supersessionSave.IsFailure)
            {
                return RequestPreparationResult.Failed(
                    supersessionSave.Failure!);
            }

            session = CreateSession(command);
        }

        if (assessmentResult.Value
            is RequestCandidateAssessmentRejected rejected)
        {
            return await PersistRejectedCandidateAsync(
                session,
                rejected.Candidate,
                rejected.Errors,
                command.CorrelationId,
                cancellationToken);
        }

        if (assessmentResult.Value
            is RequestCandidateAssessmentReady ready)
        {
            return await PersistReadyAsync(
                session,
                ready.Details,
                command.CorrelationId,
                cancellationToken);
        }

        if (assessmentResult.Value
                is RequestCandidateAssessmentIncomplete incomplete
            && proposal.Kind == RequestPreparationProposalKind.Clarification
            && IsClarificationUnresolved(
                incomplete.Candidate,
                proposal.Clarification!.Target))
        {
            return await PersistClarificationAsync(
                session,
                incomplete.Candidate,
                proposal.Clarification,
                command.CorrelationId,
                cancellationToken);
        }

        if (assessmentResult.Value
            is RequestCandidateAssessmentIncomplete rejectedIncomplete)
        {
            return await PersistRejectedCandidateAsync(
                session,
                rejectedIncomplete.Candidate,
                rejectedIncomplete.Errors,
                command.CorrelationId,
                cancellationToken);
        }

        throw new InvalidOperationException(
            "The request candidate assessment is unsupported.");
    }

    private async Task<RequestPreparationResult> PersistReadyAsync(
        RequestIntakeSession session,
        ValidatedRequestDetails details,
        string correlationId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var occurredAt = clock.UtcNow.ToUniversalTime();
        session.MarkReady(
            details,
            Guid.NewGuid(),
            occurredAt,
            correlationId);

        var saveResult = await intakeStore.SaveChangesAsync(cancellationToken);
        return saveResult.IsFailure
            ? RequestPreparationResult.Failed(saveResult.Failure!)
            : RequestPreparationResult.ReadyForConfirmation(session);
    }

    public async Task<RequestIntakeResetResult> ResetAsync(
        ResetRequestIntakeCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var sessionResult = await intakeStore.GetActiveAsync(
            command.Actor,
            cancellationToken);
        if (sessionResult.IsFailure)
        {
            return sessionResult.Failure!.Kind == ApplicationFailureKind.NotFound
                ? RequestIntakeResetResult.AlreadyClear()
                : RequestIntakeResetResult.Failed(sessionResult.Failure);
        }

        var session = sessionResult.Value;
        var occurredAt = clock.UtcNow.ToUniversalTime();
        if (session.IsExpired(occurredAt))
        {
            session.MarkExpired(occurredAt, command.CorrelationId);
        }
        else
        {
            session.MarkSuperseded(occurredAt, command.CorrelationId);
        }

        var saveResult = await intakeStore.SaveChangesAsync(cancellationToken);
        return saveResult.IsFailure
            ? RequestIntakeResetResult.Failed(
                saveResult.Failure!,
                session.Id)
            : RequestIntakeResetResult.Reset(session.Id);
    }

    private RequestIntakeSession CreateSession(
        PrepareAccessRequestCommand command)
    {
        var actor = command.Actor;
        var session = new RequestIntakeSession(
            Guid.NewGuid(),
            actor.Channel,
            actor.TenantId,
            actor.ChannelActorId,
            actor.ConversationId,
            actor.RequesterId,
            clock.UtcNow,
            command.CorrelationId);
        intakeStore.Add(session);
        return session;
    }

    private async Task<RequestPreparationResult> PersistClarificationAsync(
        RequestIntakeSession session,
        RequestCandidate candidate,
        RequestClarificationProposal clarification,
        string correlationId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var choicesResult = await ResolveEnvironmentChoicesAsync(
            clarification,
            cancellationToken);
        if (choicesResult.IsFailure)
        {
            return choicesResult.Failure!.Kind == ApplicationFailureKind.InvalidInput
                ? await PersistRejectedCandidateAsync(
                    session,
                    candidate,
                    [InvalidEnvironmentOptionError()],
                    correlationId,
                    cancellationToken)
                : RequestPreparationResult.Failed(choicesResult.Failure);
        }

        session.UpdateCandidate(
            candidate.ClientId,
            candidate.EnvironmentId,
            candidate.RequestedRoleId,
            candidate.Justification,
            candidate.IncidentId,
            clock.UtcNow,
            correlationId);

        var saveResult = await intakeStore.SaveChangesAsync(cancellationToken);
        return saveResult.IsFailure
            ? RequestPreparationResult.Failed(saveResult.Failure!)
            : RequestPreparationResult.ClarificationRequired(
                clarification,
                choicesResult.Value);
    }

    private async Task<RequestPreparationResult> CreateDraftDiscussionAsync(
        RequestIntakeSession session,
        RequestClarificationProposal discussion,
        CancellationToken cancellationToken)
    {
        var choicesResult = await ResolveEnvironmentChoicesAsync(
            discussion,
            cancellationToken);
        if (choicesResult.IsFailure)
        {
            return choicesResult.Failure!.Kind == ApplicationFailureKind.InvalidInput
                ? RequestPreparationResult.DraftDiscussion(
                    "The suggested alternatives could not be validated.",
                    session)
                : RequestPreparationResult.Failed(choicesResult.Failure);
        }

        return RequestPreparationResult.DraftDiscussion(
            discussion.Message,
            session,
            choicesResult.Value);
    }

    private async Task<ApplicationResult<IReadOnlyList<RequestEnvironmentChoice>>>
        ResolveEnvironmentChoicesAsync(
            RequestClarificationProposal clarification,
            CancellationToken cancellationToken)
    {
        var environmentChoices = new List<RequestEnvironmentChoice>(
            clarification.EnvironmentOptionIds.Count);
        foreach (var environmentOptionId in clarification.EnvironmentOptionIds)
        {
            var contextResult =
                await requestContext.GetProductionEnvironmentContextAsync(
                    environmentOptionId,
                    cancellationToken);
            if (contextResult.IsFailure)
            {
                if (contextResult.Failure!.Kind
                    != ApplicationFailureKind.NotFound)
                {
                    return ApplicationResult.Failed<
                        IReadOnlyList<RequestEnvironmentChoice>>(
                        contextResult.Failure);
                }

                return InvalidEnvironmentChoiceResolution();
            }

            var context = contextResult.Value;
            if (!string.Equals(
                    context.Environment.Id,
                    environmentOptionId,
                    StringComparison.Ordinal))
            {
                return InvalidEnvironmentChoiceResolution();
            }

            environmentChoices.Add(
                new RequestEnvironmentChoice(
                    context.Environment.Id,
                    context.Environment.DisplayName,
                    context.Client.Id,
                    context.Client.DisplayName));
        }

        environmentChoices.Sort(
            static (left, right) => StringComparer.Ordinal.Compare(
                left.EnvironmentId,
                right.EnvironmentId));

        return ApplicationResult.Succeeded<IReadOnlyList<RequestEnvironmentChoice>>(
            environmentChoices.AsReadOnly());
    }

    private static ApplicationResult<IReadOnlyList<RequestEnvironmentChoice>>
        InvalidEnvironmentChoiceResolution() =>
        ApplicationResult.Failed<IReadOnlyList<RequestEnvironmentChoice>>(
            new ApplicationFailure(
                ApplicationFailureKind.InvalidInput,
                "environment_option_not_found",
                "A proposed production environment option does not exist."));

    private static FieldValidationError InvalidEnvironmentOptionError() =>
        new(
            "environmentOptionIds",
            "environment_option_not_found",
            "A proposed production environment option does not exist.");

    private async Task<RequestPreparationResult> PersistRejectedCandidateAsync(
        RequestIntakeSession session,
        RequestCandidate candidate,
        IReadOnlyList<FieldValidationError> validationErrors,
        string correlationId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        session.UpdateCandidate(
            candidate.ClientId,
            candidate.EnvironmentId,
            candidate.RequestedRoleId,
            candidate.Justification,
            candidate.IncidentId,
            clock.UtcNow,
            correlationId);

        var saveResult = await intakeStore.SaveChangesAsync(cancellationToken);
        return saveResult.IsFailure
            ? RequestPreparationResult.Failed(saveResult.Failure!)
            : RequestPreparationResult.CandidateRejected(validationErrors);
    }

    private static RequestCandidate ToCandidate(
        RequestIntakeSession session) =>
        new(
            session.ClientId,
            session.EnvironmentId,
            session.RequestedRoleId,
            session.Justification,
            session.IncidentId);

    private static RequestCandidate ToCandidate(
        RequestCandidateAssessment assessment) =>
        assessment switch
        {
            RequestCandidateAssessmentRejected rejected => rejected.Candidate,
            RequestCandidateAssessmentIncomplete incomplete => incomplete.Candidate,
            RequestCandidateAssessmentReady ready => new RequestCandidate(
                ready.Details.ClientId,
                ready.Details.EnvironmentId,
                ready.Details.RoleId,
                ready.Details.Justification,
                ready.Details.IncidentId),
            _ => throw new InvalidOperationException(
                "The request candidate assessment is unsupported."),
        };

    private static bool MatchesCandidate(
        RequestCandidate expected,
        RequestCandidate actual) =>
        expected == actual;

    private static bool IsClarificationUnresolved(
        RequestCandidate candidate,
        RequestClarificationTarget target) =>
        target switch
        {
            RequestClarificationTarget.EnvironmentId =>
                candidate.EnvironmentId is null,
            RequestClarificationTarget.RequestedRoleId =>
                candidate.RequestedRoleId is null,
            RequestClarificationTarget.Justification =>
                candidate.Justification is null,
            RequestClarificationTarget.IncidentId => candidate.IncidentId is null,
            _ => throw new InvalidOperationException(
                "The clarification target is unsupported."),
        };

    private static RequestPreparationResult MapInterpretationFailure(
        RequestPreparationInterpretationFailure failure) =>
        failure switch
        {
            RequestPreparationInterpretationFailure.MalformedModelOutput => Failed(
                ApplicationFailureKind.DependencyFailure,
                MalformedModelOutputCode,
                "The request assistant returned an invalid response."),
            RequestPreparationInterpretationFailure.Timeout => Failed(
                ApplicationFailureKind.Timeout,
                ModelTimeoutCode,
                "Request preparation timed out."),
            RequestPreparationInterpretationFailure.Cancelled => Failed(
                ApplicationFailureKind.Cancelled,
                ModelCancelledCode,
                "Request preparation was cancelled."),
            RequestPreparationInterpretationFailure.Unavailable => Failed(
                ApplicationFailureKind.DependencyUnavailable,
                ModelUnavailableCode,
                "The request assistant is unavailable."),
            _ => throw new InvalidOperationException(
                "The preparation interpretation failure is unsupported."),
        };

    private static RequestPreparationResult Failed(
        ApplicationFailureKind kind,
        string code,
        string message) =>
        RequestPreparationResult.Failed(
            new ApplicationFailure(kind, code, message));
}
