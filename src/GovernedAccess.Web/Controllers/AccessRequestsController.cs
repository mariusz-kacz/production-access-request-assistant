using System.Security.Claims;
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
            request.DurationMinutes,
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

public sealed record CreateAccessRequestRequest(
    string? ClientId,
    string? EnvironmentId,
    string? RequestedRole,
    int DurationMinutes,
    string? Justification,
    string? IncidentId);

public sealed record CreateAccessRequestResponse(
    Guid RequestId,
    string Status,
    string CorrelationId);
