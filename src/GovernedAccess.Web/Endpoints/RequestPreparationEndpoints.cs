using System.Security.Claims;
using GovernedAccess.Core.Application;
using GovernedAccess.Core.Domain;
using GovernedAccess.Core.Ports;
using GovernedAccess.Web.Observability;

namespace GovernedAccess.Web.Endpoints;

public static class RequestPreparationEndpoints
{
    public static RouteGroupBuilder MapRequestPreparationEndpoints(
        this RouteGroupBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        endpoints
            .MapPost("/request-drafts/prepare", PrepareDraftAsync)
            .RequireAuthorization(policy =>
                policy.RequireRole(PrincipalKind.Requester.ToString()))
            .AddEndpointFilter(SessionEndpoints.ValidateAntiforgeryAsync);

        endpoints
            .MapPost("/requests", SubmitRequestAsync)
            .RequireAuthorization(policy =>
                policy.RequireRole(PrincipalKind.Requester.ToString()))
            .AddEndpointFilter(SessionEndpoints.ValidateAntiforgeryAsync);

        return endpoints;
    }

    private static async Task<IResult> PrepareDraftAsync(
        PrepareRequestDraftRequest request,
        HttpContext context,
        IRequestDraftInterpreter interpreter)
    {
        if (string.IsNullOrWhiteSpace(request.Intent))
        {
            return ProblemDetailsMapping.ToValidationProblemDetails(
                "request_intent_invalid",
                "Describe the temporary production access that is needed.",
                [
                    new FieldValidationError(
                        "intent",
                        "intent_required",
                        "An access request description is required."),
                ],
                context);
        }

        var interpretationRequest = new DraftInterpretationRequest(
            request.Intent,
            context.GetCorrelationId());
        var outcome = await interpreter.InterpretAsync(
            interpretationRequest,
            context.RequestAborted);

        return Results.Ok(
            new PrepareRequestDraftResponse(outcome.Kind.ToString(), outcome.Draft));
    }

    private static async Task<IResult> SubmitRequestAsync(
        CreateAccessRequestRequest request,
        HttpContext context,
        RequestSubmissionService submissionService)
    {
        var input = new RequestValidationInput(
            request.ClientId,
            request.EnvironmentId,
            request.RequestedRole,
            request.DurationMinutes,
            request.Justification,
            request.IncidentId);
        var outcome = await submissionService.SubmitAsync(
            context.User.FindFirstValue(ClaimTypes.NameIdentifier),
            input,
            context.GetCorrelationId(),
            context.RequestAborted);

        return outcome switch
        {
            RequestSubmitted submitted => Results.Created(
                $"/api/requests/{submitted.Request.Id:D}",
                new CreateAccessRequestResponse(
                    submitted.Request.Id,
                    submitted.Request.Version,
                    submitted.Request.Status.ToString(),
                    submitted.Request.CorrelationId)),
            RequestSubmissionValidationRejected rejected =>
                ProblemDetailsMapping.ToValidationProblemDetails(
                    "request_validation_failed",
                    "The access request contains invalid or stale values.",
                    rejected.ValidationErrors.Select(ToApiFieldError),
                    context),
            RequestSubmissionFailed failed => failed.Failure.ToProblemDetails(context),
            _ => throw new InvalidOperationException(
                "The request submission outcome is unsupported."),
        };
    }

    private static FieldValidationError ToApiFieldError(FieldValidationError error)
    {
        var field = error.Field switch
        {
            "requestedRoleId" => "requestedRole",
            "requestedDurationMinutes" => "durationMinutes",
            _ => error.Field,
        };

        return new FieldValidationError(field, error.Code, error.Message);
    }
}

public sealed record PrepareRequestDraftRequest(string? Intent);

public sealed record PrepareRequestDraftResponse(
    string Outcome,
    AccessRequestDraft? Draft);

public sealed record CreateAccessRequestRequest(
    string? ClientId,
    string? EnvironmentId,
    string? RequestedRole,
    int DurationMinutes,
    string? Justification,
    string? IncidentId);

public sealed record CreateAccessRequestResponse(
    Guid RequestId,
    int Version,
    string Status,
    string CorrelationId);
