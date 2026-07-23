using System.Security.Claims;
using System.Text.Json;
using GovernedAccess.Core.Application;
using GovernedAccess.Core.Domain;
using GovernedAccess.Web.Observability;
using GovernedAccess.Web.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GovernedAccess.Web.Controllers;

[ApiController]
[Route("api/requests")]
[Authorize]
public sealed class AccessRequestsController : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<RequestListResponse>> GetListAsync(
        [FromQuery] string? status,
        [FromServices] RequestQueryService queryService,
        CancellationToken cancellationToken)
    {
        RequestStatus? statusFilter = null;
        if (!string.IsNullOrWhiteSpace(status))
        {
            if (!Enum.TryParse<RequestStatus>(
                    status.Trim(),
                    ignoreCase: false,
                    out var parsedStatus)
                || !Enum.IsDefined(parsedStatus))
            {
                return new ApplicationFailure(
                        ApplicationFailureKind.InvalidInput,
                        "request_status_invalid",
                        "The request status filter is invalid.")
                    .ToProblemDetails(HttpContext);
            }

            statusFilter = parsedStatus;
        }

        var result = await queryService.ListAsync(
            User.FindFirstValue(ClaimTypes.NameIdentifier),
            statusFilter,
            cancellationToken);

        if (result.IsFailure)
        {
            return result.Failure!.ToProblemDetails(HttpContext);
        }

        return Ok(new RequestListResponse(
            result.Value.Select(ToListItemResponse).ToArray()));
    }

    [HttpGet("{requestId:guid}")]
    public async Task<ActionResult<RequestDetailResponse>> GetDetailAsync(
        Guid requestId,
        [FromServices] RequestQueryService queryService,
        CancellationToken cancellationToken)
    {
        var result = await queryService.GetDetailAsync(
            requestId,
            User.FindFirstValue(ClaimTypes.NameIdentifier),
            cancellationToken);

        if (result.IsFailure)
        {
            return result.Failure!.ToProblemDetails(HttpContext);
        }

        var detail = result.Value;
        return Ok(new RequestDetailResponse(
            detail.RequestId,
            detail.RequesterId,
            detail.ClientId,
            detail.EnvironmentId,
            detail.RequestedRoleId,
            detail.Justification,
            detail.IncidentId,
            detail.Status.ToString(),
            detail.CreatedAt,
            detail.LastModifiedAt,
            detail.AvailableActions,
            new RequestValidationResponse(
                detail.Validation.IsValid,
                detail.Validation.FieldErrors),
            detail.Decisions.Select(ToDecisionResponse).ToArray(),
            detail.ProvisioningOperation is null
                ? null
                : ToOperationResponse(detail.ProvisioningOperation),
            detail.Grant is null
                ? null
                : ToGrantResponse(detail.Grant),
            detail.AuditEvents.Select(ToAuditEventResponse).ToArray()));
    }

    [HttpPost]
    [Authorize(Roles = nameof(PrincipalKind.Requester))]
    [ServiceFilter(typeof(GovernedAccessAntiforgeryFilter))]
    public async Task<ActionResult<CreateAccessRequestResponse>> CreateAsync(
        CreateAccessRequestRequest request,
        [FromServices] RequestSubmissionService submissionService,
        CancellationToken cancellationToken)
    {
        var input = new RequestValidationInput(
            request.ClientId,
            request.EnvironmentId,
            request.RequestedRole,
            request.Justification,
            request.IncidentId);
        var outcome = await submissionService.SubmitAsync(
            User.FindFirstValue(ClaimTypes.NameIdentifier),
            input,
            HttpContext.GetCorrelationId(),
            cancellationToken);

        return outcome switch
        {
            RequestSubmitted submitted => Created(
                $"/api/requests/{submitted.Request.Id:D}",
                new CreateAccessRequestResponse(
                    submitted.Request.Id,
                    submitted.Request.Status.ToString(),
                    submitted.Request.CorrelationId)),
            RequestSubmissionValidationRejected rejected =>
                ProblemDetailsMapping.ToValidationProblemDetails(
                    "request_validation_failed",
                    "The access request contains invalid or stale values.",
                    rejected.ValidationErrors.Select(ToApiFieldError),
                    HttpContext),
            RequestSubmissionFailed failed => failed.Failure.ToProblemDetails(HttpContext),
            _ => throw new InvalidOperationException(
                "The request submission outcome is unsupported."),
        };
    }

    [HttpPost("{requestId:guid}/retry-provisioning")]
    [ServiceFilter(typeof(GovernedAccessAntiforgeryFilter))]
    public async Task<ActionResult<ProvisioningRetryResponse>> RetryProvisioningAsync(
        Guid requestId,
        [FromServices] AccessRequestWorkflowService workflowService,
        CancellationToken cancellationToken)
    {
        var result = await workflowService.RetryProvisioningAsync(
            requestId,
            User.FindFirstValue(ClaimTypes.NameIdentifier),
            HttpContext.GetCorrelationId(),
            cancellationToken);

        if (result.IsFailure)
        {
            return result.Failure!.ToProblemDetails(HttpContext);
        }

        var completed = result.Value;
        return Ok(new ProvisioningRetryResponse(
            completed.Request.Id,
            completed.Request.Status.ToString(),
            new RequestGrantResponse(
                completed.Grant.Id,
                completed.Grant.RequestId,
                completed.Grant.RequesterId,
                completed.Grant.EnvironmentId,
                completed.Grant.RoleId,
                completed.Grant.ActivatedAt,
                completed.Grant.ExpiresAt,
                completed.Grant.Outcome.ToString(),
                completed.Grant.CorrelationId,
                IsExpired: false)));
    }

    private static RequestListItemResponse ToListItemResponse(
        RequestListItemView item) =>
        new(
            item.RequestId,
            item.ClientId,
            item.EnvironmentId,
            item.RequesterId,
            item.Status.ToString(),
            item.LastModifiedAt,
            item.Actionable);

    private static ApprovalDecisionResponse ToDecisionResponse(
        ApprovalDecisionView decision) =>
        new(
            decision.DecisionId,
            decision.RequestId,
            decision.Stage.ToString(),
            decision.Decision.ToString(),
            decision.ApproverId,
            decision.ApprovedRoleId,
            decision.Comment,
            decision.DecidedAt,
            decision.CorrelationId);

    private static ProvisioningOperationResponse ToOperationResponse(
        ProvisioningOperationView operation) =>
        new(
            operation.RequestId,
            operation.EnvironmentId,
            operation.RoleId,
            operation.Status.ToString(),
            operation.AttemptCount,
            operation.LastOutcomeCode,
            operation.CreatedAt,
            operation.LastAttemptAt);

    private static RequestGrantResponse ToGrantResponse(AccessGrantView grant) =>
        new(
            grant.GrantId,
            grant.RequestId,
            grant.RequesterId,
            grant.EnvironmentId,
            grant.RoleId,
            grant.ActivatedAt,
            grant.ExpiresAt,
            grant.Outcome.ToString(),
            grant.CorrelationId,
            grant.IsExpired);

    private static RequestAuditEventResponse ToAuditEventResponse(
        AuditEventView auditEvent) =>
        new(
            auditEvent.EventId,
            auditEvent.RequestId,
            auditEvent.EventType.ToString(),
            auditEvent.ActorId,
            auditEvent.OccurredAt,
            auditEvent.CorrelationId,
            auditEvent.OutcomeCode,
            ParseDetails(auditEvent.DetailsJson));

    private static JsonElement ParseDetails(string detailsJson)
    {
        using var document = JsonDocument.Parse(detailsJson);
        return document.RootElement.Clone();
    }

    private static FieldValidationError ToApiFieldError(FieldValidationError error)
    {
        var field = error.Field switch
        {
            "requestedRoleId" => "requestedRole",
            _ => error.Field,
        };

        return new FieldValidationError(field, error.Code, error.Message);
    }
}

public sealed record CreateAccessRequestRequest(
    string? ClientId,
    string? EnvironmentId,
    string? RequestedRole,
    string? Justification,
    string? IncidentId);

public sealed record CreateAccessRequestResponse(
    Guid RequestId,
    string Status,
    string CorrelationId);

public sealed record RequestListResponse(
    IReadOnlyList<RequestListItemResponse> Items);

public sealed record RequestListItemResponse(
    Guid RequestId,
    string ClientId,
    string EnvironmentId,
    string RequesterId,
    string Status,
    DateTimeOffset LastModifiedAt,
    bool Actionable);

public sealed record RequestDetailResponse(
    Guid RequestId,
    string RequesterId,
    string ClientId,
    string EnvironmentId,
    string RequestedRoleId,
    string Justification,
    string? IncidentId,
    string Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset LastModifiedAt,
    IReadOnlyList<string> AvailableActions,
    RequestValidationResponse Validation,
    IReadOnlyList<ApprovalDecisionResponse> Decisions,
    ProvisioningOperationResponse? ProvisioningOperation,
    RequestGrantResponse? Grant,
    IReadOnlyList<RequestAuditEventResponse> AuditEvents);

public sealed record RequestValidationResponse(
    bool IsValid,
    IReadOnlyList<FieldValidationError> FieldErrors);

public sealed record ApprovalDecisionResponse(
    Guid DecisionId,
    Guid RequestId,
    string Stage,
    string Decision,
    string ApproverId,
    string? ApprovedRoleId,
    string? Comment,
    DateTimeOffset DecidedAt,
    string CorrelationId);

public sealed record ProvisioningOperationResponse(
    Guid RequestId,
    string EnvironmentId,
    string RoleId,
    string Status,
    int AttemptCount,
    string? LastOutcomeCode,
    DateTimeOffset CreatedAt,
    DateTimeOffset LastAttemptAt);

public sealed record RequestGrantResponse(
    Guid GrantId,
    Guid RequestId,
    string RequesterId,
    string EnvironmentId,
    string RoleId,
    DateTimeOffset ActivatedAt,
    DateTimeOffset ExpiresAt,
    string Outcome,
    string CorrelationId,
    bool IsExpired);

public sealed record RequestAuditEventResponse(
    Guid EventId,
    Guid RequestId,
    string EventType,
    string? ActorId,
    DateTimeOffset OccurredAt,
    string CorrelationId,
    string OutcomeCode,
    JsonElement Details);

public sealed record ProvisioningRetryResponse(
    Guid RequestId,
    string Status,
    RequestGrantResponse Grant);
