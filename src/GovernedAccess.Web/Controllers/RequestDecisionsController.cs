using System.Security.Claims;
using GovernedAccess.Core.Application;
using GovernedAccess.Core.Application.AccessRequests;
using GovernedAccess.Core.Domain.AccessRequests;
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
        var decision = ParseDecision(request.Decision);

        if (decision is null)
        {
            return new ApplicationFailure(
                    ApplicationFailureKind.InvalidInput,
                    "business_decision_invalid",
                    "The business decision must be Approve or Reject.")
                .ToProblemDetails(HttpContext);
        }

        var outcome = await workflowService.DecideAsync(
            ApprovalStage.Business,
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
        var decision = ParseDecision(request.Decision);

        if (decision is null)
        {
            return new ApplicationFailure(
                    ApplicationFailureKind.InvalidInput,
                    "devops_decision_invalid",
                    "The DevOps decision must be Approve or Reject.")
                .ToProblemDetails(HttpContext);
        }

        var outcome = await workflowService.DecideAsync(
            ApprovalStage.DevOps,
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
            completed.Provisioning is null
                ? null
                : new DevOpsAccessGrantResponse(
                    completed.Provisioning.Grant.Id,
                    completed.Provisioning.Grant.EnvironmentId,
                    completed.Provisioning.Grant.RoleId,
                    completed.Provisioning.Grant.ActivatedAt,
                    completed.Provisioning.Grant.ExpiresAt)));
    }

    private static ApprovalOutcome? ParseDecision(string? decision) =>
        decision switch
        {
            "Approve" => ApprovalOutcome.Approved,
            "Reject" => ApprovalOutcome.Rejected,
            _ => null,
        };
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
