using GovernedAccess.Core.Application;
using GovernedAccess.Web.Observability;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace GovernedAccess.Web.Endpoints;

public static class ProblemDetailsMapping
{
    public static ProblemHttpResult ToProblemDetails(
        this ApplicationFailure failure,
        HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(failure);
        ArgumentNullException.ThrowIfNull(context);

        var (status, title) = GetStatusAndTitle(failure.Kind);
        var problem = new ProblemDetails
        {
            Status = status,
            Title = title,
            Detail = failure.Message,
        };

        problem.Extensions["code"] = failure.Code;
        problem.Extensions["correlationId"] = GetCorrelationId(context);

        if (failure.FieldErrors.Count > 0)
        {
            problem.Extensions["fieldErrors"] = failure.FieldErrors;
        }

        if (failure.CurrentVersion is int currentVersion)
        {
            problem.Extensions["currentVersion"] = currentVersion;
        }

        return TypedResults.Problem(problem);
    }

    private static (int Status, string Title) GetStatusAndTitle(
        ApplicationFailureKind kind)
    {
        return kind switch
        {
            ApplicationFailureKind.Validation =>
                (StatusCodes.Status422UnprocessableEntity, "Validation failed."),
            ApplicationFailureKind.InvalidInput =>
                (StatusCodes.Status400BadRequest, "Invalid request."),
            ApplicationFailureKind.NotFound =>
                (StatusCodes.Status404NotFound, "Resource not found."),
            ApplicationFailureKind.Unauthenticated =>
                (StatusCodes.Status401Unauthorized, "Authentication required."),
            ApplicationFailureKind.Unauthorized =>
                (StatusCodes.Status403Forbidden, "Access denied."),
            ApplicationFailureKind.StaleVersion =>
                (StatusCodes.Status409Conflict, "Request version is stale."),
            ApplicationFailureKind.InvalidTransition =>
                (StatusCodes.Status409Conflict, "Workflow transition is invalid."),
            ApplicationFailureKind.ConcurrencyConflict =>
                (StatusCodes.Status409Conflict, "Concurrent update conflict."),
            ApplicationFailureKind.Timeout =>
                (StatusCodes.Status503ServiceUnavailable, "A dependency timed out."),
            ApplicationFailureKind.Cancelled =>
                (StatusCodes.Status499ClientClosedRequest, "Request cancelled."),
            ApplicationFailureKind.DependencyUnavailable =>
                (StatusCodes.Status503ServiceUnavailable, "A dependency is unavailable."),
            ApplicationFailureKind.DependencyFailure =>
                (StatusCodes.Status503ServiceUnavailable, "A dependency operation failed."),
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
        };
    }

    private static string GetCorrelationId(HttpContext context)
    {
        var correlationId = context.Response.Headers[CorrelationContext.HeaderName].ToString();
        return string.IsNullOrWhiteSpace(correlationId)
            ? context.TraceIdentifier
            : correlationId;
    }
}
