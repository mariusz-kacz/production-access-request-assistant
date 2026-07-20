using GovernedAccess.Core.Application;
using GovernedAccess.Core.Domain;
using GovernedAccess.Core.Ports;
using GovernedAccess.Web.Observability;
using GovernedAccess.Web.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GovernedAccess.Web.Controllers;

[ApiController]
[Route("api/request-drafts")]
[Authorize(Roles = nameof(PrincipalKind.Requester))]
[ServiceFilter(typeof(GovernedAccessAntiforgeryFilter))]
public sealed class RequestDraftsController : ControllerBase
{
    [HttpPost("prepare")]
    public async Task<ActionResult<PrepareRequestDraftResponse>> PrepareAsync(
        PrepareRequestDraftRequest request,
        [FromServices] IRequestDraftInterpreter interpreter,
        CancellationToken cancellationToken)
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
                HttpContext);
        }

        var interpretationRequest = new DraftInterpretationRequest(
            request.Intent,
            HttpContext.GetCorrelationId());
        var outcome = await interpreter.InterpretAsync(
            interpretationRequest,
            cancellationToken);

        return Ok(new PrepareRequestDraftResponse(outcome.Kind.ToString(), outcome.Draft));
    }
}

public sealed record PrepareRequestDraftRequest(string? Intent);

public sealed record PrepareRequestDraftResponse(
    string Outcome,
    AccessRequestDraft? Draft);
