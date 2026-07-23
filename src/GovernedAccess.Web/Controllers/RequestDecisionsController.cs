using System.Security.Claims;
using GovernedAccess.Core.Application;
using GovernedAccess.Core.Domain;
using GovernedAccess.Web.Observability;
using GovernedAccess.Web.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GovernedAccess.Web.Controllers;

[ApiController]
[Route("api/requests/{requestId:guid}")]
[Authorize]
[ServiceFilter(typeof(GovernedAccessAntiforgeryFilter))]
public sealed class RequestDecisionsController : ControllerBase
{
    [HttpPost("business-decisions")]
    public async Task<ActionResult<BusinessDecisionResponse>> RecordBusinessDecisionAsync(
        Guid requestId,
        BusinessDecisionRequest request,
        [FromServices] AccessRequestWorkflowService workflowService,
        CancellationToken cancellationToken)
    {
        var decision = request.Decision switch
        {
            "Approve" => ApprovalOutcome.Approved,
            "Reject" => ApprovalOutcome.Rejected,
            _ => (ApprovalOutcome?)null,
        };

        if (decision is null)
        {
            return new ApplicationFailure(
                    ApplicationFailureKind.InvalidInput,
                    "business_decision_invalid",
                    "The business decision must be Approve or Reject.")
                .ToProblemDetails(HttpContext);
        }

        var outcome = await workflowService.DecideBusinessAsync(
            requestId,
            User.FindFirstValue(ClaimTypes.NameIdentifier),
            decision.Value,
            request.Comment,
            HttpContext.GetCorrelationId(),
            cancellationToken);

        if (outcome.IsFailure)
        {
            return outcome.Failure!.ToProblemDetails(HttpContext);
        }

        var completed = outcome.Value;
        return Ok(new BusinessDecisionResponse(
            completed.Request.Id,
            completed.Request.Status.ToString(),
            completed.Decision.CorrelationId));
    }

    [HttpPost("devops-decisions")]
    public async Task<ActionResult<DevOpsDecisionResponse>> RecordDevOpsDecisionAsync(
        Guid requestId,
        DevOpsDecisionRequest request,
        [FromServices] AccessRequestWorkflowService workflowService,
        CancellationToken cancellationToken)
    {
        var decision = request.Decision switch
        {
            "Approve" => ApprovalOutcome.Approved,
            "Reject" => ApprovalOutcome.Rejected,
            _ => (ApprovalOutcome?)null,
        };

        if (decision is null)
        {
            return new ApplicationFailure(
                    ApplicationFailureKind.InvalidInput,
                    "devops_decision_invalid",
                    "The DevOps decision must be Approve or Reject.")
                .ToProblemDetails(HttpContext);
        }

        var outcome = await workflowService.DecideDevOpsAsync(
            requestId,
            User.FindFirstValue(ClaimTypes.NameIdentifier),
            decision.Value,
            request.Comment,
            HttpContext.GetCorrelationId(),
            cancellationToken);

        if (outcome.IsFailure)
        {
            return outcome.Failure!.ToProblemDetails(HttpContext);
        }

        var completed = outcome.Value;
        return Ok(new DevOpsDecisionResponse(
            completed.Request.Id,
            completed.Request.Status.ToString(),
            completed.Decision.CorrelationId,
            completed.Grant is null
                ? null
                : new DevOpsAccessGrantResponse(
                    completed.Grant.Id,
                    completed.Grant.EnvironmentId,
                    completed.Grant.RoleId,
                    completed.Grant.ActivatedAt,
                    completed.Grant.ExpiresAt)));
    }
}

public sealed record BusinessDecisionRequest(string? Decision, string? Comment);

public sealed record BusinessDecisionResponse(
    Guid RequestId,
    string Status,
    string CorrelationId);

/// <summary>
/// Restricted browser command. Approved scope, duration, and acting identity are
/// intentionally absent and are resolved from authenticated and persisted state.
/// </summary>
public sealed record DevOpsDecisionRequest(string? Decision, string? Comment);

public sealed record DevOpsDecisionResponse(
    Guid RequestId,
    string Status,
    string CorrelationId,
    DevOpsAccessGrantResponse? Grant);

public sealed record DevOpsAccessGrantResponse(
    Guid GrantId,
    string EnvironmentId,
    string RoleId,
    DateTimeOffset ActivatedAt,
    DateTimeOffset ExpiresAt);
